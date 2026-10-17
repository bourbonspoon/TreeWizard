using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TreeWizard.Fbx
{
    /// <summary>
    /// Writes <see cref="FbxNode"/> trees as binary FBX 7.4, the version every current importer reads
    /// (Autodesk FBX SDK, Unity, Unreal, Blender, Godot).
    /// </summary>
    public static class FbxBinaryWriter
    {
        public const int Version = 7400;

        /// <summary>
        /// Importers check the file's footer against its FileId and CreationTime nodes. These three
        /// values are a known-good set, so documents should use them rather than real timestamps.
        /// </summary>
        public const string CreationTime = "1970-01-01 10:00:00:000";
        public static readonly byte[] FileId =
            { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 };
        static readonly byte[] FooterId =
            { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
        static readonly byte[] FooterMagic =
            { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };

        const int NullRecordLength = 13; // three uint32s and a zero name length, for versions below 7500
        const int CompressionThreshold = 128; // arrays shorter than this aren't worth deflating

        public static void Write(Stream output, IEnumerable<FbxNode> topLevel, bool compressArrays = true)
        {
            // Records store absolute end offsets, patched in after their children are written,
            // so build the file in memory where we can seek back.
            using (var buffer = new MemoryStream())
            using (var w = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                w.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  "));
                w.Write((byte)0x00);
                w.Write((byte)0x1a);
                w.Write((byte)0x00);
                w.Write((uint)Version);

                foreach (var node in topLevel)
                    WriteNode(w, node, compressArrays);
                w.Write(new byte[NullRecordLength]);

                w.Write(FooterId);
                w.Write(new byte[4]);
                var pad = (int)(16 - buffer.Position % 16); // align to 16; a full 16 when already aligned
                w.Write(new byte[pad]);
                w.Write((uint)Version);
                w.Write(new byte[120]);
                w.Write(FooterMagic);
                w.Flush();

                buffer.Position = 0;
                buffer.CopyTo(output);
            }
        }

        static void WriteNode(BinaryWriter w, FbxNode node, bool compress)
        {
            var stream = w.BaseStream;
            var start = stream.Position;
            w.Write(0u); // end offset, patched below
            w.Write((uint)node.Properties.Count);
            w.Write(0u); // property list length, patched below
            var name = Encoding.UTF8.GetBytes(node.Name);
            if (name.Length > byte.MaxValue)
                throw new ArgumentException($"FBX node name too long: {node.Name}");
            w.Write((byte)name.Length);
            w.Write(name);

            var propertiesStart = stream.Position;
            foreach (var p in node.Properties)
                WriteProperty(w, p, compress);
            var propertiesLength = stream.Position - propertiesStart;

            // Nested lists end with a null record. The FBX SDK also writes one for nodes with no
            // properties, and readers expect it there.
            if (node.Children.Count > 0 || node.Properties.Count == 0)
            {
                foreach (var child in node.Children)
                    WriteNode(w, child, compress);
                w.Write(new byte[NullRecordLength]);
            }

            var end = stream.Position;
            if (end > uint.MaxValue)
                throw new InvalidOperationException("FBX 7.4 files are limited to 4 GB.");
            stream.Position = start;
            w.Write((uint)end);
            stream.Position = start + 8;
            w.Write((uint)propertiesLength);
            stream.Position = end;
        }

        static void WriteProperty(BinaryWriter w, object value, bool compress)
        {
            switch (value)
            {
                case short v: w.Write((byte)'Y'); w.Write(v); break;
                case bool v: w.Write((byte)'C'); w.Write((byte)(v ? 1 : 0)); break;
                case int v: w.Write((byte)'I'); w.Write(v); break;
                case float v: w.Write((byte)'F'); w.Write(v); break;
                case double v: w.Write((byte)'D'); w.Write(v); break;
                case long v: w.Write((byte)'L'); w.Write(v); break;
                case string v:
                    var bytes = Encoding.UTF8.GetBytes(v);
                    w.Write((byte)'S');
                    w.Write((uint)bytes.Length);
                    w.Write(bytes);
                    break;
                case byte[] v:
                    w.Write((byte)'R');
                    w.Write((uint)v.Length);
                    w.Write(v);
                    break;
                case float[] v: WriteArray(w, 'f', v.Length, 4, compress, bw => { foreach (var x in v) bw.Write(x); }); break;
                case double[] v: WriteArray(w, 'd', v.Length, 8, compress, bw => { foreach (var x in v) bw.Write(x); }); break;
                case long[] v: WriteArray(w, 'l', v.Length, 8, compress, bw => { foreach (var x in v) bw.Write(x); }); break;
                case int[] v: WriteArray(w, 'i', v.Length, 4, compress, bw => { foreach (var x in v) bw.Write(x); }); break;
                case bool[] v: WriteArray(w, 'b', v.Length, 1, compress, bw => { foreach (var x in v) bw.Write((byte)(x ? 1 : 0)); }); break;
                default:
                    throw new ArgumentException($"Unsupported FBX property type: {value?.GetType().Name ?? "null"}");
            }
        }

        static void WriteArray(BinaryWriter w, char type, int count, int elementSize, bool compress, Action<BinaryWriter> writeElements)
        {
            byte[] raw;
            using (var ms = new MemoryStream(count * elementSize))
            using (var bw = new BinaryWriter(ms))
            {
                writeElements(bw);
                bw.Flush();
                raw = ms.ToArray();
            }

            var deflate = compress && count >= CompressionThreshold;
            var payload = deflate ? Zlib(raw) : raw;
            w.Write((byte)type);
            w.Write((uint)count);
            w.Write(deflate ? 1u : 0u);
            w.Write((uint)payload.Length);
            w.Write(payload);
        }

        /// <summary>zlib stream (RFC 1950): a two-byte header, raw deflate data, then an Adler-32 checksum.</summary>
        static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9c);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);

                uint a = 1, b = 0;
                foreach (var x in data)
                {
                    a = (a + x) % 65521;
                    b = (b + a) % 65521;
                }
                var adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }
    }
}
