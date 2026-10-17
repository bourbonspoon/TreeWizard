using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TreeWizard.Fbx;

namespace TreeWizard.Editor.Tests
{
    /// <summary>
    /// Strict reader for binary FBX 7.4, used to check the writer's output byte layout: every
    /// record's end offset, property list length and null-record terminator, and the footer.
    /// </summary>
    static class FbxTestReader
    {
        public static List<FbxNode> Read(byte[] file)
        {
            var magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  ").Concat(new byte[] { 0x00, 0x1a, 0x00 }).ToArray();
            CollectionAssert.AreEqual(magic, file.Take(magic.Length).ToArray(), "header magic");

            using (var r = new BinaryReader(new MemoryStream(file)))
            {
                r.BaseStream.Position = magic.Length;
                Assert.AreEqual(FbxBinaryWriter.Version, r.ReadInt32(), "header version");

                var nodes = new List<FbxNode>();
                FbxNode node;
                while ((node = ReadNode(r)) != null)
                    nodes.Add(node);

                // Footer: id, 4 zero bytes, padding to 16, version, 120 zero bytes, 16-byte magic.
                r.ReadBytes(16);
                CollectionAssert.AreEqual(new byte[4], r.ReadBytes(4));
                var pad = (int)(16 - r.BaseStream.Position % 16);
                CollectionAssert.AreEqual(new byte[pad], r.ReadBytes(pad), "footer padding");
                Assert.AreEqual(FbxBinaryWriter.Version, r.ReadInt32(), "footer version");
                CollectionAssert.AreEqual(new byte[120], r.ReadBytes(120));
                Assert.AreEqual(16, r.ReadBytes(16).Length);
                Assert.AreEqual(r.BaseStream.Length, r.BaseStream.Position, "nothing after the footer");
                return nodes;
            }
        }

        static FbxNode ReadNode(BinaryReader r)
        {
            var start = r.BaseStream.Position;
            var end = r.ReadUInt32();
            var count = r.ReadUInt32();
            var length = r.ReadUInt32();
            var nameLength = r.ReadByte();
            if (end == 0)
            {
                Assert.AreEqual(0, count + length + nameLength, $"null record at {start}");
                return null;
            }

            var node = new FbxNode(Encoding.UTF8.GetString(r.ReadBytes(nameLength)));
            var propertiesStart = r.BaseStream.Position;
            for (var i = 0; i < count; i++)
                node.Properties.Add(ReadProperty(r));
            Assert.AreEqual(length, r.BaseStream.Position - propertiesStart, $"{node.Name}: property list length");

            if (r.BaseStream.Position < end)
            {
                FbxNode child;
                while ((child = ReadNode(r)) != null)
                    node.Children.Add(child);
            }
            Assert.AreEqual(end, r.BaseStream.Position, $"{node.Name}: end offset");
            return node;
        }

        static object ReadProperty(BinaryReader r)
        {
            var type = (char)r.ReadByte();
            switch (type)
            {
                case 'Y': return r.ReadInt16();
                case 'C': return r.ReadByte() != 0;
                case 'I': return r.ReadInt32();
                case 'F': return r.ReadSingle();
                case 'D': return r.ReadDouble();
                case 'L': return r.ReadInt64();
                case 'S': return Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                case 'R': return r.ReadBytes(r.ReadInt32());
                case 'f': return ReadArray(r, 4, br => br.ReadSingle());
                case 'd': return ReadArray(r, 8, br => br.ReadDouble());
                case 'l': return ReadArray(r, 8, br => br.ReadInt64());
                case 'i': return ReadArray(r, 4, br => br.ReadInt32());
                case 'b': return ReadArray(r, 1, br => br.ReadByte() != 0);
                default: throw new InvalidDataException($"Unknown property type '{type}'");
            }
        }

        static T[] ReadArray<T>(BinaryReader r, int elementSize, Func<BinaryReader, T> read)
        {
            var count = r.ReadInt32();
            var encoding = r.ReadInt32();
            var data = r.ReadBytes(r.ReadInt32());
            if (encoding == 1)
            {
                Assert.AreEqual(0x78, data[0], "zlib header");
                using (var inflate = new DeflateStream(new MemoryStream(data, 2, data.Length - 6), CompressionMode.Decompress))
                using (var raw = new MemoryStream())
                {
                    inflate.CopyTo(raw);
                    data = raw.ToArray();
                }
            }
            else
            {
                Assert.AreEqual(0, encoding, "array encoding");
            }
            Assert.AreEqual(count * elementSize, data.Length, "array byte length");

            using (var br = new BinaryReader(new MemoryStream(data)))
                return Enumerable.Range(0, count).Select(_ => read(br)).ToArray();
        }
    }
}
