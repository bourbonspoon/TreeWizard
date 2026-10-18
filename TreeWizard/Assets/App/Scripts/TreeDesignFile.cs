using System;
using System.IO;
using UnityEngine;

namespace TreeWizard.App
{
    /// <summary>A saved tree design: its settings plus the preset that supplies its materials. Stored as JSON.</summary>
    [Serializable]
    public class TreeDesignFile
    {
        public const string Extension = ".treewizard";
        const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string preset;
        public TreeDefinition definition;

        public static void Save(string path, string preset, TreeDefinition definition)
        {
            var file = new TreeDesignFile { preset = preset, definition = definition };
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        public static TreeDesignFile Load(string path)
        {
            var file = JsonUtility.FromJson<TreeDesignFile>(File.ReadAllText(path));
            if (file?.definition == null)
                throw new InvalidDataException("Not a TreeWizard design file.");
            if (file.version > CurrentVersion)
                throw new InvalidDataException("This design was saved by a newer TreeWizard.");
            return file;
        }
    }
}
