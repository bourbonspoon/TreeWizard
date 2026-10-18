using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TreeWizard.UI
{
    public enum PlaceKind { Home, Desktop, Documents, Downloads, Pictures, Drive, Recent }

    public readonly struct Place
    {
        public readonly string Name;
        public readonly string Path;
        public readonly PlaceKind Kind;

        public Place(string name, string path, PlaceKind kind)
        {
            Name = name;
            Path = path;
            Kind = kind;
        }
    }

    /// <summary>
    /// The user's standard folders and the drives or volumes to show in the file browser's sidebar.
    /// Works around Mono on macOS and Linux, where SpecialFolder.MyDocuments is the home folder.
    /// </summary>
    public static class Places
    {
        static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static string Home
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return string.IsNullOrEmpty(home) ? Environment.GetEnvironmentVariable("HOME") ?? "/" : home;
            }
        }

        public static string Desktop => UserFolder(Environment.SpecialFolder.DesktopDirectory, "DESKTOP", "Desktop");
        public static string Documents => UserFolder(Environment.SpecialFolder.MyDocuments, "DOCUMENTS", "Documents");
        public static string Pictures => UserFolder(Environment.SpecialFolder.MyPictures, "PICTURES", "Pictures");
        public static string Downloads => IsWindows ? WindowsDownloads() : UserFolder(null, "DOWNLOAD", "Downloads");

        /// <summary>Home and the standard folders that exist on this machine.</summary>
        public static List<Place> Standard()
        {
            var places = new List<Place> { new Place("Home", Home, PlaceKind.Home) };
            AddIfExists(places, "Desktop", Desktop, PlaceKind.Desktop);
            AddIfExists(places, "Documents", Documents, PlaceKind.Documents);
            AddIfExists(places, "Downloads", Downloads, PlaceKind.Downloads);
            AddIfExists(places, "Pictures", Pictures, PlaceKind.Pictures);
            return places;
        }

        /// <summary>Windows drives that are ready, macOS volumes, or Linux mounts under /media and /mnt.</summary>
        public static List<Place> Drives()
        {
            var drives = new List<Place>();
            try
            {
                if (IsWindows)
                    AddWindowsDrives(drives);
                else if (IsMac)
                    AddMacVolumes(drives);
                else
                    AddLinuxMounts(drives);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return drives;
        }

        static void AddIfExists(List<Place> places, string name, string path, PlaceKind kind)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !SamePath(path, Home))
                places.Add(new Place(name, path, kind));
        }

        internal static bool SamePath(string a, string b) =>
            a != null && b != null && string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
                IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        // Windows: the OS knows where the folder is, even if it was moved. Linux: the XDG user-dirs file.
        // Otherwise the usual folder under home.
        static string UserFolder(Environment.SpecialFolder? special, string xdgName, string defaultName)
        {
            if (IsWindows && special.HasValue)
                return Environment.GetFolderPath(special.Value);
            if (!IsWindows && !IsMac && XdgUserDir(xdgName) is string xdg)
                return xdg;
            return Path.Combine(Home, defaultName);
        }

        static string XdgUserDir(string name)
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var file = Path.Combine(string.IsNullOrEmpty(config) ? Path.Combine(Home, ".config") : config, "user-dirs.dirs");
            if (!File.Exists(file))
                return null;
            var key = $"XDG_{name}_DIR=";
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.StartsWith(key))
                    continue;
                var value = line.Substring(key.Length).Trim().Trim('"').Replace("$HOME", Home);
                return value.Length > 0 ? value : null;
            }
            return null;
        }

        static void AddWindowsDrives(List<Place> drives)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.NoRootDirectory || !drive.IsReady)
                        continue;
                    var letter = drive.Name.TrimEnd('\\');
                    var label = string.IsNullOrEmpty(drive.VolumeLabel) ? DriveTypeName(drive.DriveType) : drive.VolumeLabel;
                    drives.Add(new Place($"{label} ({letter})", drive.Name, PlaceKind.Drive));
                }
                catch (IOException)
                {
                    // ejected or unreachable while we looked
                }
            }
        }

        static string DriveTypeName(DriveType type) => type switch
        {
            DriveType.Removable => "Removable Disk",
            DriveType.Network => "Network Drive",
            DriveType.CDRom => "CD Drive",
            _ => "Local Disk",
        };

        // /Volumes lists every mounted volume; the startup disk is a symlink back to /.
        static void AddMacVolumes(List<Place> drives)
        {
            foreach (var volume in new DirectoryInfo("/Volumes").GetDirectories())
            {
                if (volume.Name.StartsWith(".") || volume.Name.StartsWith("com.apple."))
                    continue;
                var isStartupDisk = (volume.Attributes & FileAttributes.ReparsePoint) != 0;
                drives.Add(new Place(volume.Name, isStartupDisk ? "/" : volume.FullName, PlaceKind.Drive));
            }
            drives.Sort((a, b) => a.Path == "/" ? -1 : b.Path == "/" ? 1 : FolderListing.NaturalCompare(a.Name, b.Name));
        }

        static void AddLinuxMounts(List<Place> drives)
        {
            drives.Add(new Place("Computer", "/", PlaceKind.Drive));
            var user = Environment.UserName;
            foreach (var parent in new[] { $"/media/{user}", $"/run/media/{user}", "/mnt" })
            {
                if (!Directory.Exists(parent))
                    continue;
                foreach (var mount in Directory.GetDirectories(parent))
                    drives.Add(new Place(Path.GetFileName(mount), mount, PlaceKind.Drive));
            }
        }

        static string WindowsDownloads()
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
            try
            {
                if (SHGetKnownFolderPath(ref downloads, 0, IntPtr.Zero, out var pointer) == 0)
                {
                    var path = Marshal.PtrToStringUni(pointer);
                    Marshal.FreeCoTaskMem(pointer);
                    return path;
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }
            return Path.Combine(Home, "Downloads");
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
    }
}
