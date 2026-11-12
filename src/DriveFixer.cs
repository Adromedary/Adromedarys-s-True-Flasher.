// True Flasher - Made by Adromedary
// DriveFixer.cs - shrink a fake drive to its real size.
//
// A fake controller only loses data once more is stored than its real flash can hold.
// A partition that ends below the real capacity keeps every write inside working flash,
// so the drive becomes a reliable (smaller) drive. The firmware still reports the fake
// size to Windows - that cannot be changed without the factory tool - but the partition
// shows its true size in Explorer and is labelled with it (e.g. "USB 28GB").

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TrueFlasher
{
    public static class DriveFixer
    {
        const long MiB = 1L << 20;

        public static string FileSystemFor(long bytes)
        {
            return bytes / MiB <= 32000 ? "fat32" : "exfat";
        }

        public static string DefaultLabel(long bytes)
        {
            if (bytes < 1000000000L) return "USB " + Math.Max(1, (long)(bytes / 1e6)) + "MB";
            return "USB " + (long)(bytes / 1e9) + "GB";
        }

        /// <summary>FAT32 labels: 11 chars; exFAT: 15. Strip characters diskpart/FAT reject.</summary>
        public static string CleanLabel(string label, long bytes)
        {
            int max = FileSystemFor(bytes) == "fat32" ? 11 : 15;
            StringBuilder sb = new StringBuilder();
            foreach (char c in (label ?? "").Trim())
                if (c >= 32 && c < 127 && "\"*/:<>?\\|+,.;=[]".IndexOf(c) < 0) sb.Append(c);
            string s = sb.ToString().Trim();
            if (s.Length == 0) s = DefaultLabel(bytes);
            if (s.Length > max) s = s.Substring(0, max).Trim();
            return FileSystemFor(bytes) == "fat32" ? s.ToUpperInvariant() : s;
        }

        public static List<string> Script(int disk, long sizeBytes, string label)
        {
            label = CleanLabel(label, sizeBytes);
            List<string> l = new List<string>();
            l.Add("select disk " + disk);
            l.Add("clean");
            l.Add("create partition primary size=" + (sizeBytes / MiB));
            l.Add("format fs=" + FileSystemFor(sizeBytes) + " quick label=\"" + label + "\"");
            l.Add("assign");
            return l;
        }

        /// <summary>Runs diskpart. Re-checks that disk N is still the same USB drive first.</summary>
        public static bool Run(int disk, long expectedDiskSize, long sizeBytes, string label, out string output)
        {
            output = "";
            DiskInfo d = DiskList.All().Find(delegate(DiskInfo x) { return x.Number == disk; });
            if (d == null || !d.Eligible || d.Size != expectedDiskSize)
            {
                output = "Disk " + disk + " is no longer the drive that was tested (was it unplugged or swapped?). Nothing was changed.";
                return false;
            }
            if (sizeBytes < 16 * MiB || sizeBytes > d.Size)
            {
                output = "Invalid size.";
                return false;
            }

            string script = Path.Combine(Path.GetTempPath(), "trueflasher_fix_" + Guid.NewGuid().ToString("N") + ".txt");
            List<string> lines = Script(disk, sizeBytes, label);
            lines.Add("exit");
            File.WriteAllLines(script, lines.ToArray());
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("diskpart.exe", "/s \"" + script + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(10 * 60 * 1000);
                    output = o.Trim();
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = "Could not run diskpart: " + ex.Message;
                return false;
            }
            finally
            {
                try { File.Delete(script); } catch { }
            }
        }
    }
}
