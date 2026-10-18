using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TreeWizard.App
{
    /// <summary>
    /// A saved tree design: its settings, the preset that supplies its materials, and the person's own
    /// texture files if they chose any. Stored as JSON.
    /// </summary>
    [Serializable]
    public class TreeDesignFile
    {
        public const string Extension = ".treewizard";
        const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string preset;
        public TreeDefinition definition;
        public TextureFiles textures = new TextureFiles();

        /// <summary>
        /// Image paths. Empty means the preset's texture. Files in the design's folder, or below it, are stored
        /// relative to it, so the folder can move as a whole; anything else is stored as a full path.
        /// </summary>
        [Serializable]
        public class TextureFiles
        {
            public string bark = "";
            [Tooltip("Empty: generated from the bark.")]
            public string barkNormal = "";
            public string[] leafSheets = Array.Empty<string>();
        }

        public static void Save(string path, string preset, TreeDefinition definition, TextureFiles textures)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            var file = new TreeDesignFile
            {
                preset = preset,
                definition = definition,
                textures = new TextureFiles
                {
                    bark = Relative(folder, textures.bark),
                    barkNormal = Relative(folder, textures.barkNormal),
                    leafSheets = textures.leafSheets.Select(p => Relative(folder, p)).ToArray(),
                },
            };
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        /// <summary>Reads a design. Its texture paths come back as full paths.</summary>
        public static TreeDesignFile Load(string path)
        {
            var file = JsonUtility.FromJson<TreeDesignFile>(File.ReadAllText(path));
            if (file?.definition == null)
                throw new InvalidDataException("Not a TreeWizard design file.");
            if (file.version > CurrentVersion)
                throw new InvalidDataException("This design was saved by a newer TreeWizard.");

            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            var textures = file.textures ?? new TextureFiles();
            file.textures = new TextureFiles
            {
                bark = Absolute(folder, textures.bark),
                barkNormal = Absolute(folder, textures.barkNormal),
                leafSheets = (textures.leafSheets ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Select(p => Absolute(folder, p)).ToArray(),
            };
            return file;
        }

        // Forward slashes, so a design saved on Windows opens on a Mac and the other way round.
        static string Relative(string folder, string file)
        {
            if (string.IsNullOrEmpty(file))
                return "";
            var full = Path.GetFullPath(file);
            var prefix = folder.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            var comparison = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return full.StartsWith(prefix, comparison) ? full.Substring(prefix.Length).Replace('\\', '/') : full;
        }

        static string Absolute(string folder, string file) =>
            string.IsNullOrEmpty(file) ? "" : Path.GetFullPath(Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar)));
    }
}
