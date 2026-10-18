using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TreeWizard.UI
{
    public readonly struct FileEntry
    {
        public readonly string Path;
        public readonly string Name;
        public readonly bool IsFolder;
        /// <summary>In bytes; -1 for folders.</summary>
        public readonly long Size;
        public readonly DateTime Modified;

        public FileEntry(string path, string name, bool isFolder, long size, DateTime modified)
        {
            Path = path;
            Name = name;
            IsFolder = isFolder;
            Size = size;
            Modified = modified;
        }
    }

    public enum FileSortColumn { Name, Modified, Size }

    /// <summary>File system helpers behind the file browser: listing, sorting, path parsing and name checks.</summary>
    public static class FolderListing
    {
        // The union of what Windows, macOS and Linux reject, so saved files stay portable.
        static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// Folders, then files that match <paramref name="filter"/>, sorted by name. Hidden and system entries
        /// (dot files, and anything the OS marks hidden) are left out unless <paramref name="showHidden"/>.
        /// Throws if the folder itself can't be read.
        /// </summary>
        public static List<FileEntry> List(string folder, FileFilter filter, bool includeFiles = true, bool showHidden = false)
        {
            var entries = new List<FileEntry>();
            foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                try
                {
                    if (!showHidden && IsHidden(info))
                        continue;
                    if (info is DirectoryInfo)
                        entries.Add(new FileEntry(info.FullName, info.Name, true, -1, info.LastWriteTime));
                    else if (includeFiles && filter.Matches(info.Name))
                        entries.Add(new FileEntry(info.FullName, info.Name, false, ((FileInfo)info).Length, info.LastWriteTime));
                }
                catch (IOException)
                {
                    // broken links and entries deleted mid-listing
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            Sort(entries, FileSortColumn.Name, true);
            return entries;
        }

        static bool IsHidden(FileSystemInfo info) =>
            info.Name.StartsWith(".") || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

        /// <summary>Folders always come first; ties fall back to the name.</summary>
        public static void Sort(List<FileEntry> entries, FileSortColumn column, bool ascending)
        {
            entries.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder)
                    return a.IsFolder ? -1 : 1;
                var result = column switch
                {
                    FileSortColumn.Modified => a.Modified.CompareTo(b.Modified),
                    FileSortColumn.Size => a.Size.CompareTo(b.Size),
                    _ => 0,
                };
                if (result == 0)
                    result = NaturalCompare(a.Name, b.Name);
                return ascending ? result : -result;
            });
        }

        /// <summary>Case-insensitive, with runs of digits compared as numbers, so "tree2" sorts before "tree10".</summary>
        public static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int startA = i, startB = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                        i++;
                    while (j < b.Length && char.IsDigit(b[j]))
                        j++;
                    var numberA = a.Substring(startA, i - startA).TrimStart('0');
                    var numberB = b.Substring(startB, j - startB).TrimStart('0');
                    if (numberA.Length != numberB.Length)
                        return numberA.Length.CompareTo(numberB.Length);
                    var digits = string.CompareOrdinal(numberA, numberB);
                    if (digits != 0)
                        return digits;
                }
                else
                {
                    var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (c != 0)
                        return c;
                    i++;
                    j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary>Why <paramref name="name"/> can't be used as a file or folder name, or null if it can.</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Enter a name.";
            if (name.IndexOfAny(InvalidNameChars) >= 0 || name.Any(char.IsControl))
                return "Names can't contain \\ / : * ? \" < > |";
            if (name == "." || name == "..")
                return "That name is reserved.";
            if (name.EndsWith(".") || name.EndsWith(" "))
                return "Names can't end with a dot or a space.";
            var stem = name.Split('.')[0].Trim();
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
                return $"\"{stem}\" is reserved on Windows.";
            return null;
        }

        /// <summary>
        /// Turns what someone typed into a full path: absolute paths stay as they are, "~" means the home folder,
        /// anything else is relative to <paramref name="folder"/>. Throws <see cref="ArgumentException"/> for
        /// malformed paths.
        /// </summary>
        public static string Resolve(string folder, string typed)
        {
            typed = typed.Trim();
            if (typed == "~")
                typed = Places.Home;
            else if (typed.StartsWith("~/") || typed.StartsWith("~\\"))
                typed = System.IO.Path.Combine(Places.Home, typed.Substring(2));
            try
            {
                return System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(typed) ? typed : System.IO.Path.Combine(folder, typed));
            }
            catch (NotSupportedException e)
            {
                throw new ArgumentException(e.Message, e);
            }
        }

        /// <summary>Each folder from the root down to <paramref name="folder"/>, as (label, full path).</summary>
        public static List<(string label, string path)> Breadcrumbs(string folder)
        {
            var crumbs = new List<(string, string)>();
            for (var dir = new DirectoryInfo(System.IO.Path.GetFullPath(folder)); dir != null; dir = dir.Parent)
                crumbs.Add((dir.Parent == null ? RootLabel(dir.FullName) : dir.Name, dir.FullName));
            crumbs.Reverse();
            return crumbs;
        }

        /// <summary>"C:" for a Windows drive root, "/" on macOS and Linux.</summary>
        static string RootLabel(string root)
        {
            var trimmed = root.TrimEnd('\\', '/');
            return trimmed.Length > 0 ? trimmed : root;
        }

        /// <summary>"New Folder", or "New Folder 2" and so on if that already exists.</summary>
        public static string UniqueName(string folder, string name)
        {
            var candidate = name;
            for (var i = 2; Directory.Exists(System.IO.Path.Combine(folder, candidate)) || File.Exists(System.IO.Path.Combine(folder, candidate)); i++)
                candidate = $"{name} {i}";
            return candidate;
        }

        /// <summary>For example "840 bytes", "12 KB", "3.4 MB".</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0)
                return "";
            if (bytes < 1000)
                return $"{bytes} bytes";
            string[] units = { "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = -1;
            do
            {
                size /= 1000.0;
                unit++;
            } while (size >= 1000.0 && unit < units.Length - 1);
            return size < 10 ? $"{size:0.#} {units[unit]}" : $"{size:0} {units[unit]}";
        }
    }
}
