// True Flasher - Made by Adromedary
// LinuxFixer.cs - shrink a fake drive to its real size on Linux (sfdisk + mkfs).
// Same idea as DriveFixer.cs on Windows: one partition that ends inside the real flash,
// formatted FAT32 (<= 32 GB) or exFAT, labelled with its true size.

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TrueFlasher
{
    public static class LinuxFixer
    {
        const long MiB = 1L << 20;

        public static string FileSystemFor(long bytes) { return bytes / MiB <= 32000 ? "fat32" : "exfat"; }

        public static string DefaultLabel(long bytes)
        {
            if (bytes < 1000000000L) return "USB " + Math.Max(1, (long)(bytes / 1e6)) + "MB";
            return "USB " + (long)(bytes / 1e9) + "GB";
        }

        public static string CleanLabel(string label, long bytes)
        {
            int max = FileSystemFor(bytes) == "fat32" ? 11 : 15;
            StringBuilder sb = new StringBuilder();
            foreach (char c in (label ?? "").Trim())
                if (c >= 32 && c < 127 && "\"*/:<>?\\|+,.;=[]'`$".IndexOf(c) < 0) sb.Append(c);
            string s = sb.ToString().Trim();
            if (s.Length == 0) s = DefaultLabel(bytes);
            if (s.Length > max) s = s.Substring(0, max).Trim();
            return FileSystemFor(bytes) == "fat32" ? s.ToUpperInvariant() : s;
        }

        /// <summary>Tools the fix needs; returns null when all are present.</summary>
        public static string MissingTools(long bytes)
        {
            string mkfs = FileSystemFor(bytes) == "fat32" ? "mkfs.vfat" : "mkfs.exfat";
            string pkg = FileSystemFor(bytes) == "fat32" ? "dosfstools" : "exfatprogs";
            if (!LinuxDiskList.HasTool("sfdisk")) return "sfdisk (package: fdisk or util-linux)";
            if (!LinuxDiskList.HasTool(mkfs)) return mkfs + " (package: " + pkg + ")";
            return null;
        }

        public static string[] Commands(LinuxDiskInfo d, long bytes, string label)
        {
            label = CleanLabel(label, bytes);
            string part = d.PartitionPath(1);
            string mkfs = FileSystemFor(bytes) == "fat32"
                ? "mkfs.vfat -F 32 -n '" + label + "' " + part
                : "mkfs.exfat -L '" + label + "' " + part;
            return new string[]
            {
                "printf 'label: dos\\nstart=2048, size=" + (bytes / 512 - 2048) + ", type=" + (FileSystemFor(bytes) == "fat32" ? "c" : "7") + "\\n' | sudo sfdisk --wipe always " + d.Path,
                "sudo " + mkfs
            };
        }

        public static bool Run(LinuxDiskInfo d, long bytes, string label, out string output)
        {
            StringBuilder log = new StringBuilder();
            output = "";
            label = CleanLabel(label, bytes);

            // make sure the disk is still the one that was tested
            LinuxDiskInfo now = LinuxDiskList.Describe(d.Name, LinuxDiskList.SystemDisks(), File.ReadAllLines("/proc/mounts"), d.Bus == "loop");
            if (!now.Eligible || now.Size != d.Size)
            {
                output = d.Path + " is no longer the drive that was tested (unplugged or swapped?). Nothing was changed.";
                return false;
            }
            if (bytes < 16 * MiB || bytes > d.Size - MiB) { output = "Invalid size."; return false; }
            string missing = MissingTools(bytes);
            if (missing != null) { output = "Missing tool: " + missing + ". Install it and try again."; return false; }
            string um = LinuxDiskList.UnmountAll(now);
            if (um != null) { output = um; return false; }

            long sectors = bytes / 512 - 2048;           // partition starts at 1 MiB
            string type = FileSystemFor(bytes) == "fat32" ? "c" : "7";
            string table = "label: dos\nstart=2048, size=" + sectors + ", type=" + type + "\n";
            string o;
            int rc = LinuxDiskList.Run("sfdisk", "--wipe always --wipe-partitions always " + d.Path, table, out o);
            log.AppendLine("$ sfdisk " + d.Path); log.AppendLine(o);
            if (rc != 0) { output = log.ToString(); return false; }

            // wait for udev to create the partition node
            string part = d.PartitionPath(1);
            LinuxDiskList.Run("partx", "-u " + d.Path, out o);
            for (int i = 0; i < 50 && !File.Exists(part); i++) Thread.Sleep(100);
            if (!File.Exists(part))
            {
                LinuxDiskList.Run("partprobe", d.Path, out o);
                for (int i = 0; i < 50 && !File.Exists(part); i++) Thread.Sleep(100);
            }
            if (!File.Exists(part)) { log.AppendLine(part + " did not appear. Replug the drive and format it manually."); output = log.ToString(); return false; }

            if (FileSystemFor(bytes) == "fat32")
                rc = LinuxDiskList.Run("mkfs.vfat", "-F 32 -n \"" + label + "\" " + part, out o);
            else
            {
                rc = LinuxDiskList.Run("mkfs.exfat", "-L \"" + label + "\" " + part, out o);          // exfatprogs
                if (rc != 0) rc = LinuxDiskList.Run("mkfs.exfat", "-n \"" + label + "\" " + part, out o); // exfat-utils
            }
            log.AppendLine("$ mkfs " + part); log.AppendLine(o);
            output = log.ToString().Trim();
            return rc == 0;
        }
    }
}
