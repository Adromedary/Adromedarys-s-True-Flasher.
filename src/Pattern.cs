// True Flasher - Made by Adromedary
// Pattern.cs - self-identifying 4 KiB page stamps.
//
// Every 4 KiB page written by True Flasher carries:
//   [0]  magic      "TRUFSH01"
//   [8]  run id     random 64-bit value, unique per test run
//   [16] page index the page's own address (offset / 4096)
//   [24] check      ~(page index XOR run id), protects the header
//   [32..4095]      pseudo-random fill derived from (run id, page index)
//
// When a page is read back we can tell exactly what happened to it:
// intact, moved (holds another address's data = wraparound/aliasing),
// stale (left over from an older run), blank (00/FF) or garbage.

using System;

namespace TrueFlasher
{
    public enum PageState { Ok, Moved, Stale, Blank, Garbage, Unreadable }

    public static class Pattern
    {
        public const int PageSize = 4096;
        public const int Words = PageSize / 8;
        public const ulong Magic = 0x3130485346555254UL; // "TRUFSH01" little-endian

        /// <summary>Fills pageCount consecutive pages into buf (starting at byte 0).</summary>
        public static void Fill(byte[] buf, ulong runId, long firstPage, int pageCount, ulong[] scratch)
        {
            for (int p = 0; p < pageCount; p++)
                FillWords(scratch, p * Words, runId, firstPage + p);
            Buffer.BlockCopy(scratch, 0, buf, 0, pageCount * PageSize);
        }

        static void FillWords(ulong[] w, int o, ulong runId, long page)
        {
            ulong pg = (ulong)page;
            w[o] = Magic;
            w[o + 1] = runId;
            w[o + 2] = pg;
            w[o + 3] = ~(pg ^ runId);
            ulong s = Seed(runId, page);
            int end = o + Words;
            for (int i = o + 4; i < end; i++)
            {
                s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
                w[i] = s * 0x2545F4914F6CDD1DUL;
            }
        }

        static ulong Seed(ulong runId, long page)
        {
            ulong z = runId + (ulong)page * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return z == 0 ? 1UL : z;
        }

        /// <summary>Classifies one page located at buf[off..off+4096).</summary>
        public static PageState Check(byte[] buf, int off, ulong runId, long expectedPage,
                                      ulong[] scratch, out long foundPage)
        {
            foundPage = -1;
            ulong m = BitConverter.ToUInt64(buf, off);
            ulong r = BitConverter.ToUInt64(buf, off + 8);
            ulong pg = BitConverter.ToUInt64(buf, off + 16);
            ulong chk = BitConverter.ToUInt64(buf, off + 24);

            if (m == Magic && chk == ~(pg ^ r))
            {
                foundPage = (long)pg;
                if (r != runId) return PageState.Stale;
                FillWords(scratch, 0, runId, (long)pg);
                for (int i = 4; i < Words; i++)
                    if (BitConverter.ToUInt64(buf, off + i * 8) != scratch[i])
                        return PageState.Garbage;
                return (long)pg == expectedPage ? PageState.Ok : PageState.Moved;
            }

            if (AllSame(buf, off, 0x00) || AllSame(buf, off, 0xFF)) return PageState.Blank;
            return PageState.Garbage;
        }

        static bool AllSame(byte[] b, int off, byte v)
        {
            for (int i = 0; i < PageSize; i++) if (b[off + i] != v) return false;
            return true;
        }

        public static ulong NewRunId()
        {
            byte[] g = Guid.NewGuid().ToByteArray();
            ulong id = BitConverter.ToUInt64(g, 0) ^ BitConverter.ToUInt64(g, 8) ^ (ulong)DateTime.UtcNow.Ticks;
            return id == 0 ? 0x5EEDUL : id;
        }
    }
}
