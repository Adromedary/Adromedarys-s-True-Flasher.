// True Flasher - Made by Adromedary
// WinDisk.cs - raw physical-disk access on Windows (unbuffered, write-through),
// USB disk discovery, system-disk protection and volume lock/dismount.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TrueFlasher
{
    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint FILE_FLAG_NO_BUFFERING = 0x20000000, FILE_FLAG_WRITE_THROUGH = 0x80000000;

        public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;
        public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x7405C;
        public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x700A0;
        public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x70140;
        public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x560000;
        public const uint FSCTL_LOCK_VOLUME = 0x90018;
        public const uint FSCTL_DISMOUNT_VOLUME = 0x90020;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa,
                                                        uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inSize,
                                                  byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int n, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int n, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFilePointerEx(SafeFileHandle h, long distance, out long newPos, uint method);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindFirstVolumeW(StringBuilder name, int len);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool FindNextVolumeW(IntPtr h, StringBuilder name, int len);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindVolumeClose(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool GetVolumePathNamesForVolumeNameW(string volume, char[] buf, int len, out int needed);
    }

    public class DiskInfo
    {
        public int Number;
        public string Vendor = "", Product = "", Serial = "", Bus = "";
        public long Size;
        public int SectorSize;
        public bool Removable, Eligible;
        public string Note = "";
        public List<string> Letters = new List<string>();

        public string Label
        {
            get
            {
                string name = (Vendor + " " + Product).Trim();
                if (name.Length == 0) name = "Unknown device";
                return "Disk " + Number + "  " + name + "  (" + Bus + ")";
            }
        }
    }

    public static class DiskList
    {
        static readonly Dictionary<int, string> BusNames = new Dictionary<int, string>
        {
            {1,"SCSI"},{2,"ATAPI"},{3,"ATA"},{4,"1394"},{5,"SSA"},{6,"FibreChannel"},{7,"USB"},{8,"RAID"},
            {9,"iSCSI"},{10,"SAS"},{11,"SATA"},{12,"SD"},{13,"MMC"},{14,"Virtual"},{15,"FileBackedVirtual"},
            {16,"Spaces"},{17,"NVMe"},{18,"SCM"},{19,"UFS"}
        };

        static SafeFileHandle Open(string path, uint access)
        {
            return Native.CreateFileW(path, access, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                                      IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        }

        /// <summary>Disk numbers that hold Windows itself - never offered, never touched.</summary>
        public static HashSet<int> SystemDisks()
        {
            HashSet<int> set = new HashSet<int>();
            string[] roots = { Path.GetPathRoot(Environment.SystemDirectory), Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                using (SafeFileHandle h = Open(@"\\.\" + root.TrimEnd('\\'), 0))
                {
                    if (h.IsInvalid) continue;
                    foreach (int d in Extents(h)) set.Add(d);
                }
            }
            return set;
        }

        public static List<int> Extents(SafeFileHandle volume)
        {
            List<int> disks = new List<int>();
            byte[] o = new byte[8 + 24 * 32];
            int ret;
            if (!Native.DeviceIoControl(volume, Native.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, o, o.Length, out ret, IntPtr.Zero))
                return disks;
            int n = BitConverter.ToInt32(o, 0);
            for (int i = 0; i < n && 8 + i * 24 + 4 <= ret; i++) disks.Add(BitConverter.ToInt32(o, 8 + i * 24));
            return disks;
        }

        static string AnsiAt(byte[] buf, int offset, int limit)
        {
            if (offset <= 0 || offset >= limit) return "";
            int end = offset;
            while (end < limit && buf[end] != 0) end++;
            return Encoding.ASCII.GetString(buf, offset, end - offset).Trim();
        }

        public static List<DiskInfo> All()
        {
            HashSet<int> sys = SystemDisks();
            Dictionary<int, List<string>> letters = LettersByDisk();
            List<DiskInfo> list = new List<DiskInfo>();
            for (int n = 0; n < 64; n++)
            {
                using (SafeFileHandle h = Open(@"\\.\PhysicalDrive" + n, Native.GENERIC_READ))
                {
                    if (h.IsInvalid) continue;
                    DiskInfo d = new DiskInfo();
                    d.Number = n;
                    int ret;

                    byte[] q = new byte[12];   // STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery }
                    byte[] o = new byte[1024];
                    int bus = 0;
                    if (Native.DeviceIoControl(h, Native.IOCTL_STORAGE_QUERY_PROPERTY, q, q.Length, o, o.Length, out ret, IntPtr.Zero) && ret >= 36)
                    {
                        d.Removable = o[10] != 0;
                        d.Vendor = AnsiAt(o, BitConverter.ToInt32(o, 12), ret);
                        d.Product = AnsiAt(o, BitConverter.ToInt32(o, 16), ret);
                        d.Serial = AnsiAt(o, BitConverter.ToInt32(o, 24), ret);
                        bus = BitConverter.ToInt32(o, 28);
                    }
                    string bn;
                    d.Bus = BusNames.TryGetValue(bus, out bn) ? bn : "bus " + bus;

                    byte[] len = new byte[8];
                    if (Native.DeviceIoControl(h, Native.IOCTL_DISK_GET_LENGTH_INFO, null, 0, len, 8, out ret, IntPtr.Zero))
                        d.Size = BitConverter.ToInt64(len, 0);
                    byte[] geo = new byte[256];
                    if (Native.DeviceIoControl(h, Native.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 0, geo, geo.Length, out ret, IntPtr.Zero))
                        d.SectorSize = BitConverter.ToInt32(geo, 20);

                    List<string> l;
                    if (letters.TryGetValue(n, out l)) d.Letters = l;

                    bool busOk = bus == 7 || bus == 12 || bus == 13;
                    if (sys.Contains(n)) d.Note = "system disk - protected";
                    else if (!busOk) d.Note = "not a USB/SD device - protected";
                    else if (d.Size <= 0) d.Note = "no media";
                    else if (d.SectorSize > Pattern.PageSize || (d.SectorSize > 0 && Pattern.PageSize % d.SectorSize != 0))
                        d.Note = "unsupported sector size " + d.SectorSize;
                    else d.Eligible = true;
                    list.Add(d);
                }
            }
            return list;
        }

        public static List<string> VolumesOnDisk(int disk, bool forLock)
        {
            List<string> result = new List<string>();
            StringBuilder sb = new StringBuilder(1024);
            IntPtr fh = Native.FindFirstVolumeW(sb, sb.Capacity);
            if (fh == new IntPtr(-1)) return result;
            try
            {
                do
                {
                    string vol = sb.ToString();                  // \\?\Volume{guid}\
                    using (SafeFileHandle h = Open(vol.TrimEnd('\\'), 0))
                    {
                        if (!h.IsInvalid && Extents(h).Contains(disk)) result.Add(vol);
                    }
                    sb.Length = 0;
                } while (Native.FindNextVolumeW(fh, sb, sb.Capacity));
            }
            finally { Native.FindVolumeClose(fh); }
            return result;
        }

        static Dictionary<int, List<string>> LettersByDisk()
        {
            Dictionary<int, List<string>> map = new Dictionary<int, List<string>>();
            StringBuilder sb = new StringBuilder(1024);
            IntPtr fh = Native.FindFirstVolumeW(sb, sb.Capacity);
            if (fh == new IntPtr(-1)) return map;
            try
            {
                do
                {
                    string vol = sb.ToString();
                    List<int> disks;
                    using (SafeFileHandle h = Open(vol.TrimEnd('\\'), 0))
                        disks = h.IsInvalid ? new List<int>() : Extents(h);
                    char[] buf = new char[512];
                    int need;
                    string paths = "";
                    if (Native.GetVolumePathNamesForVolumeNameW(vol, buf, buf.Length, out need))
                        paths = new string(buf).Split('\0')[0];
                    if (paths.Length > 0)
                        foreach (int d in disks)
                        {
                            List<string> l;
                            if (!map.TryGetValue(d, out l)) { l = new List<string>(); map[d] = l; }
                            l.Add(paths.TrimEnd('\\'));
                        }
                    sb.Length = 0;
                } while (Native.FindNextVolumeW(fh, sb, sb.Capacity));
            }
            finally { Native.FindVolumeClose(fh); }
            return map;
        }
    }

    /// <summary>The selected USB disk, opened raw with Windows caching disabled.</summary>
    public sealed class WinDisk : IBlockDevice, IDisposable
    {
        const int BufSize = 4 << 20;
        readonly SafeFileHandle h;
        readonly IntPtr buf;
        readonly long length;
        readonly List<SafeFileHandle> locks = new List<SafeFileHandle>();
        public readonly int Number;
        public readonly List<string> LockLog = new List<string>();

        public long Length { get { return length; } }
        public int MaxTransfer { get { return BufSize; } }

        public WinDisk(int number, long size)
        {
            Number = number;
            LockVolumes();
            h = Native.CreateFileW(@"\\.\PhysicalDrive" + number,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
                Native.FILE_FLAG_NO_BUFFERING | Native.FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
            if (h.IsInvalid)
                throw new IOException("Cannot open disk " + number + " for writing: " +
                                      new Win32Exception(Marshal.GetLastWin32Error()).Message);
            length = size / Pattern.PageSize * Pattern.PageSize;
            // Page-aligned buffer (VirtualAlloc) - required for unbuffered I/O.
            buf = Native.VirtualAlloc(IntPtr.Zero, new UIntPtr((uint)BufSize), 0x3000, 0x04);
            if (buf == IntPtr.Zero) throw new OutOfMemoryException("VirtualAlloc failed");
        }

        /// <summary>Lock and dismount every volume on the disk so Windows lets us write raw sectors.</summary>
        void LockVolumes()
        {
            foreach (string vol in DiskList.VolumesOnDisk(Number, true))
            {
                SafeFileHandle v = Native.CreateFileW(vol.TrimEnd('\\'), Native.GENERIC_READ | Native.GENERIC_WRITE,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (v.IsInvalid) { LockLog.Add("could not open " + vol); continue; }
                int ret;
                bool locked = Native.DeviceIoControl(v, Native.FSCTL_LOCK_VOLUME, null, 0, null, 0, out ret, IntPtr.Zero);
                bool dism = Native.DeviceIoControl(v, Native.FSCTL_DISMOUNT_VOLUME, null, 0, null, 0, out ret, IntPtr.Zero);
                LockLog.Add(vol + (locked ? " locked" : " lock failed") + (dism ? ", dismounted" : ", dismount failed"));
                locks.Add(v);   // keep the handle (and the lock) for the whole test
            }
        }

        /// <summary>Reads sector 0 and writes it back unchanged. Returns null if raw writes work, else the reason.</summary>
        public string CheckWritable()
        {
            byte[] b = new byte[Pattern.PageSize];
            try
            {
                Read(0, b, b.Length);
                Write(0, b, b.Length);
                return null;
            }
            catch (IOException ex)
            {
                return ex.Message + ". Likely causes: the controller has locked itself read-only (common after a fake " +
                       "has been overfilled), a program is using the drive, or a policy blocks writes to USB drives.";
            }
        }

        static bool IsGone(int code)
        {
            // file/path not found, invalid handle, not ready, device not connected, no such device, device removed
            return code == 2 || code == 3 || code == 6 || code == 21 || code == 55 || code == 433 ||
                   code == 1167 || code == 483 || code == 1110;
        }

        Exception Fail(string op, long offset)
        {
            int code = Marshal.GetLastWin32Error();
            string msg = op + " at " + Engine.Gb(offset) + ": " + new Win32Exception(code).Message + " (" + code + ")";
            if (IsGone(code)) return new DeviceGoneException(msg, code);
            return new MediaIOException(msg, code);
        }

        void Seek(long offset)
        {
            long pos;
            if (!Native.SetFilePointerEx(h, offset, out pos, 0)) throw Fail("seek", offset);
        }

        public void Read(long offset, byte[] data, int count)
        {
            Seek(offset);
            int n;
            if (!Native.ReadFile(h, buf, count, out n, IntPtr.Zero)) throw Fail("read", offset);
            if (n != count) throw new MediaIOException("short read at " + Engine.Gb(offset), 0);
            Marshal.Copy(buf, data, 0, count);
        }

        public void Write(long offset, byte[] data, int count)
        {
            Marshal.Copy(data, 0, buf, count);
            Seek(offset);
            int n;
            if (!Native.WriteFile(h, buf, count, out n, IntPtr.Zero)) throw Fail("write", offset);
            if (n != count) throw new MediaIOException("short write at " + Engine.Gb(offset), 0);
        }

        public void Dispose()
        {
            if (h != null && !h.IsInvalid)
            {
                int ret;
                Native.DeviceIoControl(h, Native.IOCTL_DISK_UPDATE_PROPERTIES, null, 0, null, 0, out ret, IntPtr.Zero);
                h.Dispose();
            }
            foreach (SafeFileHandle v in locks) v.Dispose();
            if (buf != IntPtr.Zero) Native.VirtualFree(buf, UIntPtr.Zero, 0x8000);
        }
    }
}
