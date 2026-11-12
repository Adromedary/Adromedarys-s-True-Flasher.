// True Flasher - Made by Adromedary
// LinuxDisk.cs - raw block-device access on Linux (O_DIRECT | O_SYNC | O_EXCL),
// USB/SD drive discovery through sysfs, system-disk protection and unmounting.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TrueFlasher
{
    static class LibC
    {
        [DllImport("libc", SetLastError = true)] public static extern int open(string path, int flags, int mode);
        [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] public static extern IntPtr pread(int fd, IntPtr buf, UIntPtr count, long offset);
        [DllImport("libc", SetLastError = true)] public static extern IntPtr pwrite(int fd, IntPtr buf, UIntPtr count, long offset);
        [DllImport("libc", SetLastError = true)] public static extern int fsync(int fd);
        [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, ulong request, IntPtr arg);
        [DllImport("libc")] public static extern uint geteuid();
        [DllImport("libc", SetLastError = true)] public static extern int chown(string path, int uid, int gid);
        [DllImport("libc", SetLastError = true)] static extern IntPtr realpath(string path, IntPtr resolved);
        [DllImport("libc")] static extern void free(IntPtr p);
        [DllImport("libc")] static extern IntPtr strerror(int errnum);

        public const int O_RDONLY = 0, O_RDWR = 2, O_EXCL = 0x80, O_SYNC = 0x101000;
        public const ulong BLKFLSBUF = 0x1261, BLKRRPART = 0x125F;

        [DllImport("libc")] static extern int uname(byte[] buf);

        /// <summary>CPU name from uname(2), e.g. "x86_64", "aarch64".</summary>
        public static string Machine()
        {
            byte[] b = new byte[2048];
            if (uname(b) != 0) return "";
            // struct utsname: sysname, nodename, release, version, machine - 65 bytes each on Linux
            int off = 65 * 4, end = off;
            while (end < off + 65 && b[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(b, off, end - off);
        }

        /// <summary>O_DIRECT differs by architecture (x86/x64: 040000, ARM/ARM64: 0200000).</summary>
        public static int O_DIRECT
        {
            get
            {
                string m = Machine();
                return m.StartsWith("arm") || m.StartsWith("aarch64") ? 0x10000 : 0x4000;
            }
        }

        public static string RealPath(string path)
        {
            IntPtr p = realpath(path, IntPtr.Zero);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringAnsi(p); } finally { free(p); }
        }

        public static string Error(int errno)
        {
            try { return Marshal.PtrToStringAnsi(strerror(errno)); } catch { return "errno " + errno; }
        }
    }

    public class LinuxDiskInfo
    {
        public string Name = "";            // sdb, mmcblk0
        public string Path = "";            // /dev/sdb
        public string Vendor = "", Model = "", Bus = "";
        public long Size;
        public int SectorSize = 512;
        public bool Removable, Eligible;
        public string Note = "";
        public List<string> Partitions = new List<string>();     // /dev/sdb1 ...
        public List<string> Mounts = new List<string>();         // "/dev/sdb1 on /media/x"

        public string Label
        {
            get
            {
                string n = (Vendor + " " + Model).Trim();
                if (n.Length == 0) n = "Unknown device";
                return Path + "  " + n + "  (" + Bus + ")";
            }
        }

        public string PartitionPath(int n)
        {
            return Path + (char.IsDigit(Name[Name.Length - 1]) ? "p" : "") + n;
        }
    }

    public static class LinuxDiskList
    {
        static string Read(string path)
        {
            try { return File.ReadAllText(path).Trim(); } catch { return ""; }
        }

        static long ReadLong(string path)
        {
            long v; return long.TryParse(Read(path), out v) ? v : 0;
        }

        /// <summary>Maps a /dev path (partition, dm, by-uuid link...) to the top-level disks under it.</summary>
        public static HashSet<string> DisksOf(string dev)
        {
            HashSet<string> result = new HashSet<string>();
            string real = dev.StartsWith("/dev/") ? (LibC.RealPath(dev) ?? dev) : dev;
            string name = System.IO.Path.GetFileName(real);
            Walk(name, result, 0);
            return result;
        }

        static void Walk(string name, HashSet<string> result, int depth)
        {
            if (depth > 8 || name.Length == 0) return;
            string cls = "/sys/class/block/" + name;
            if (!Directory.Exists(cls)) return;
            // partition? -> parent disk
            if (File.Exists(cls + "/partition"))
            {
                string rp = LibC.RealPath(cls);
                if (rp != null) Walk(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(rp)), result, depth + 1);
                return;
            }
            // device-mapper / md: follow slaves (LVM, LUKS, RAID)
            string slaves = cls + "/slaves";
            if (Directory.Exists(slaves) && Directory.GetFileSystemEntries(slaves).Length > 0)
            {
                foreach (string s in Directory.GetFileSystemEntries(slaves)) Walk(System.IO.Path.GetFileName(s), result, depth + 1);
                return;
            }
            result.Add(name);
        }

        /// <summary>Disks that hold /, /boot, /usr, /home, /var or active swap. Never touched.</summary>
        public static HashSet<string> SystemDisks()
        {
            HashSet<string> set = new HashSet<string>();
            string[] critical = { "/", "/boot", "/boot/efi", "/efi", "/usr", "/home", "/var" };
            foreach (string line in ReadLines("/proc/mounts"))
            {
                string[] f = line.Split(' ');
                if (f.Length < 2 || !f[0].StartsWith("/dev/")) continue;
                if (Array.IndexOf(critical, Unescape(f[1])) >= 0) set.UnionWith(DisksOf(f[0]));
            }
            foreach (string line in ReadLines("/proc/swaps"))
            {
                string[] f = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length > 0 && f[0].StartsWith("/dev/")) set.UnionWith(DisksOf(f[0]));
            }
            return set;
        }

        static string[] ReadLines(string p)
        {
            try { return File.ReadAllLines(p); } catch { return new string[0]; }
        }

        static string Unescape(string s) { return s.Replace("\\040", " ").Replace("\\011", "\t"); }

        public static List<LinuxDiskInfo> All(bool allowAnyBus)
        {
            HashSet<string> sys = SystemDisks();
            string[] mounts = ReadLines("/proc/mounts");
            List<LinuxDiskInfo> list = new List<LinuxDiskInfo>();
            string[] names;
            try { names = Directory.GetFileSystemEntries("/sys/block"); } catch { names = new string[0]; }
            Array.Sort(names);
            foreach (string full in names)
            {
                string n = System.IO.Path.GetFileName(full);
                if (n.StartsWith("ram") || n.StartsWith("zram") || n.StartsWith("sr") || n.StartsWith("fd") ||
                    n.StartsWith("md") || n.StartsWith("dm-")) continue;
                bool isLoop = n.StartsWith("loop");
                if (isLoop && !allowAnyBus) continue;
                LinuxDiskInfo d = Describe(n, sys, mounts, allowAnyBus);
                if (isLoop && d.Size == 0) continue;
                list.Add(d);
            }
            return list;
        }

        public static LinuxDiskInfo Describe(string n, HashSet<string> sys, string[] mounts, bool allowAnyBus)
        {
            string b = "/sys/block/" + n;
            LinuxDiskInfo d = new LinuxDiskInfo();
            d.Name = n;
            d.Path = "/dev/" + n;
            d.Size = ReadLong(b + "/size") * 512;
            int lbs = (int)ReadLong(b + "/queue/logical_block_size");
            if (lbs > 0) d.SectorSize = lbs;
            d.Removable = Read(b + "/removable") == "1";
            d.Vendor = Read(b + "/device/vendor");
            d.Model = Read(b + "/device/model");
            if (d.Model.Length == 0) d.Model = Read(b + "/device/name");   // mmc
            string real = LibC.RealPath(b) ?? "";
            if (real.Contains("/usb")) d.Bus = "USB";
            else if (n.StartsWith("mmcblk")) d.Bus = "SD/MMC";
            else if (n.StartsWith("nvme")) d.Bus = "NVMe";
            else if (n.StartsWith("loop")) d.Bus = "loop";
            else if (real.Contains("/ata")) d.Bus = "SATA";
            else if (real.Contains("/virtio")) d.Bus = "virtio";
            else d.Bus = "other";

            try
            {
                foreach (string e in Directory.GetFileSystemEntries(b))
                {
                    string pn = System.IO.Path.GetFileName(e);
                    if (pn.StartsWith(n) && File.Exists(e + "/partition")) d.Partitions.Add("/dev/" + pn);
                }
            }
            catch { }
            d.Partitions.Sort();

            foreach (string line in mounts)
            {
                string[] f = line.Split(' ');
                if (f.Length < 2 || !f[0].StartsWith("/dev/")) continue;
                if (DisksOf(f[0]).Contains(n)) d.Mounts.Add(f[0] + " on " + Unescape(f[1]));
            }

            bool busOk = d.Bus == "USB" || d.Bus == "SD/MMC" || allowAnyBus;
            if (sys.Contains(n)) d.Note = "system disk - protected";
            else if (!busOk) d.Note = "not a USB/SD device - protected";
            else if (d.Size <= 0) d.Note = "no media";
            else if (d.SectorSize > Pattern.PageSize || Pattern.PageSize % d.SectorSize != 0) d.Note = "unsupported sector size " + d.SectorSize;
            else if (ReadLong(b + "/ro") == 1) d.Note = "read-only (write-protected or locked controller)";
            else d.Eligible = true;
            return d;
        }

        /// <summary>Unmounts every mounted filesystem on the disk. Returns null on success, else the error.</summary>
        public static string UnmountAll(LinuxDiskInfo d)
        {
            List<string> devs = new List<string>();
            foreach (string m in d.Mounts) devs.Add(m.Substring(0, m.IndexOf(' ')));
            devs.Reverse();
            foreach (string dev in devs)
            {
                string output;
                if (Run("umount", dev, out output) != 0)
                    return "could not unmount " + dev + ": " + output.Trim();
            }
            return null;
        }

        public static int Run(string file, string args, out string output)
        {
            return Run(file, args, null, out output);
        }

        public static int Run(string file, string args, string stdin, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = stdin != null;
                using (Process p = Process.Start(psi))
                {
                    if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    output = (o + e).Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = file + ": " + ex.Message;
                return -1;
            }
        }

        public static bool HasTool(string name)
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/sbin:/usr/bin:/sbin:/bin").Split(':'))
                if (dir.Length > 0 && File.Exists(System.IO.Path.Combine(dir, name))) return true;
            foreach (string dir in new string[] { "/usr/sbin", "/sbin", "/usr/bin", "/bin" })
                if (File.Exists(System.IO.Path.Combine(dir, name))) return true;
            return false;
        }
    }

    /// <summary>The selected drive, opened exclusively with the page cache bypassed.</summary>
    public sealed class LinuxDisk : IBlockDevice, IDisposable
    {
        const int BufSize = 4 << 20;
        const int Align = 4096;
        readonly int fd;
        readonly IntPtr raw, buf;
        readonly long length;

        public long Length { get { return length; } }
        public int MaxTransfer { get { return BufSize; } }

        public LinuxDisk(string path, long size)
        {
            fd = LibC.open(path, LibC.O_RDWR | LibC.O_DIRECT | LibC.O_SYNC | LibC.O_EXCL, 0);
            if (fd < 0)
            {
                int e = Marshal.GetLastWin32Error();
                throw new IOException("cannot open " + path + ": " + LibC.Error(e) +
                    (e == 16 ? " (it is still in use - close file managers and programs using it)" : ""));
            }
            length = size / Pattern.PageSize * Pattern.PageSize;
            raw = Marshal.AllocHGlobal(BufSize + Align);
            buf = new IntPtr((raw.ToInt64() + Align - 1) & ~(long)(Align - 1));
        }

        static bool IsGone(int e)
        {
            // ENXIO, ENODEV, ENOMEDIUM, ESHUTDOWN, EBADF
            return e == 6 || e == 19 || e == 123 || e == 108 || e == 9;
        }

        Exception Fail(string op, long offset)
        {
            int e = Marshal.GetLastWin32Error();
            string msg = op + " at " + Engine.Gb(offset) + ": " + LibC.Error(e) + " (errno " + e + ")";
            if (IsGone(e)) return new DeviceGoneException(msg, e);
            return new MediaIOException(msg, e);
        }

        public void Read(long offset, byte[] data, int count)
        {
            long n = LibC.pread(fd, buf, new UIntPtr((uint)count), offset).ToInt64();
            if (n < 0) throw Fail("read", offset);
            if (n != count) throw new MediaIOException("short read at " + Engine.Gb(offset), 0);
            Marshal.Copy(buf, data, 0, count);
        }

        public void Write(long offset, byte[] data, int count)
        {
            Marshal.Copy(data, 0, buf, count);
            long n = LibC.pwrite(fd, buf, new UIntPtr((uint)count), offset).ToInt64();
            if (n < 0) throw Fail("write", offset);
            if (n != count) throw new MediaIOException("short write at " + Engine.Gb(offset), 0);
        }

        /// <summary>Reads the first 4 KiB and writes it back unchanged. Null if raw writes work.</summary>
        public string CheckWritable()
        {
            byte[] b = new byte[Pattern.PageSize];
            try { Read(0, b, b.Length); Write(0, b, b.Length); return null; }
            catch (IOException ex)
            {
                return ex.Message + ". The controller may have locked itself read-only (common after a fake has been overfilled).";
            }
        }

        public void Dispose()
        {
            if (fd >= 0)
            {
                LibC.fsync(fd);
                LibC.ioctl(fd, LibC.BLKRRPART, IntPtr.Zero);   // let the kernel re-read the (now wiped) partition table
                LibC.close(fd);
            }
            if (raw != IntPtr.Zero) Marshal.FreeHGlobal(raw);
        }
    }
}
