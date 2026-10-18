using System;
using System.Linq;

namespace TreeWizard.UI
{
    /// <summary>
    /// A named set of file extensions, such as "Images" with .png and .jpg, shown in the file browser's filter list.
    /// A filter with no extensions matches every file.
    /// </summary>
    public sealed class FileFilter
    {
        public static FileFilter All => new FileFilter("All files");

        public string Name { get; }

        /// <summary>Lower case, each with its leading dot.</summary>
        public string[] Extensions { get; }

        public FileFilter(string name, params string[] extensions)
        {
            Name = name;
            Extensions = extensions.Select(Normalize).ToArray();
        }

        public bool MatchesAll => Extensions.Length == 0;

        /// <summary>Added to saved file names that don't already match. Empty for <see cref="MatchesAll"/>.</summary>
        public string DefaultExtension => MatchesAll ? "" : Extensions[0];

        public bool Matches(string fileName)
        {
            if (MatchesAll)
                return true;
            foreach (var extension in Extensions)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>For example "Images (*.png, *.jpg)".</summary>
        public override string ToString() =>
            MatchesAll ? Name : $"{Name} ({string.Join(", ", Extensions.Select(e => "*" + e))})";

        static string Normalize(string extension)
        {
            extension = extension.Trim().ToLowerInvariant();
            return extension.StartsWith(".") ? extension : "." + extension;
        }
    }
}
