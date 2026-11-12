// True Flasher - Made by Adromedary
// Device.cs - abstractions shared by the engine, the Windows disk layer and the tests.

using System;
using System.IO;

namespace TrueFlasher
{
    /// <summary>A raw block device. Offsets and counts are always multiples of 4096.</summary>
    public interface IBlockDevice
    {
        long Length { get; }
        int MaxTransfer { get; }
        void Read(long offset, byte[] buf, int count);
        void Write(long offset, byte[] buf, int count);
    }

    /// <summary>A read/write failed on the media (bad or unmapped area). The test continues.</summary>
    public class MediaIOException : IOException
    {
        public int Code;
        public MediaIOException(string msg, int code) : base(msg) { Code = code; }
    }

    /// <summary>The device disappeared (unplugged, controller crashed). The test must stop.</summary>
    public class DeviceGoneException : IOException
    {
        public int Code;
        public DeviceGoneException(string msg, int code) : base(msg) { Code = code; }
    }

    /// <summary>Console/progress hooks so the engine stays UI-agnostic (and testable).</summary>
    public interface IUi
    {
        void Line(string text);          // permanent line, also written to the log
        void Status(string text);        // transient progress line
        bool StopRequested();            // user pressed Q / Ctrl+C / Stop
        void Progress(int stage, double fraction, long bytes, double mbPerSec, string note); // for progress bars
    }
}
