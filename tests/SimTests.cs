// True Flasher - Made by Adromedary
// SimTests.cs - runs the engine against simulated fake drives (no hardware needed).
// Build & run (any OS with Mono or .NET Framework):
//   csc /out:SimTests.exe tests\SimTests.cs src\Pattern.cs src\Device.cs src\Engine.cs src\Report.cs
//   SimTests.exe

using System;
using System.Collections.Generic;
using TrueFlasher;

namespace TrueFlasher.Tests
{
    /// <summary>Sparse page store. Unwritten pages read as zeros.</summary>
    abstract class SimDevice : IBlockDevice
    {
        protected const int P = Pattern.PageSize;
        protected long claimed;
        public long Length { get { return claimed; } }
        public int MaxTransfer { get { return 4 << 20; } }
        protected abstract void WritePage(long page, byte[] src, int off);
        protected abstract bool ReadPage(long page, byte[] dst, int off); // false = leave zeros

        public void Write(long offset, byte[] buf, int count)
        {
            for (int i = 0; i < count / P; i++) WritePage(offset / P + i, buf, i * P);
        }
        public void Read(long offset, byte[] buf, int count)
        {
            for (int i = 0; i < count / P; i++)
                if (!ReadPage(offset / P + i, buf, i * P)) Array.Clear(buf, i * P, P);
        }
        protected static byte[] Copy(byte[] src, int off)
        {
            byte[] b = new byte[P]; Buffer.BlockCopy(src, off, b, 0, P); return b;
        }
    }

    /// <summary>Physical = real bytes. Logical page L lives at physical L mod real (classic fake).</summary>
    class WrapDevice : SimDevice
    {
        long realPages; Dictionary<long, byte[]> store = new Dictionary<long, byte[]>();
        public WrapDevice(long claimedBytes, long realBytes) { claimed = claimedBytes; realPages = realBytes / P; }
        protected override void WritePage(long pg, byte[] s, int o) { store[pg % realPages] = Copy(s, o); }
        protected override bool ReadPage(long pg, byte[] d, int o)
        {
            byte[] b; if (!store.TryGetValue(pg % realPages, out b)) return false;
            Buffer.BlockCopy(b, 0, d, o, P); return true;
        }
    }

    /// <summary>Dynamic mapping: any logical page can be stored until physical space runs out,
    /// then the oldest (FIFO) or a random stored page is silently evicted. This is the
    /// "it deletes random files to make room" behaviour.</summary>
    class RecycleDevice : SimDevice
    {
        long realPages; bool random; Random rng = new Random(7);
        Dictionary<long, byte[]> store = new Dictionary<long, byte[]>();
        LinkedList<long> order = new LinkedList<long>();
        Dictionary<long, LinkedListNode<long>> nodes = new Dictionary<long, LinkedListNode<long>>();
        List<long> keys = new List<long>(); Dictionary<long, int> keyIdx = new Dictionary<long, int>();
        public RecycleDevice(long claimedBytes, long realBytes, bool randomEvict)
        { claimed = claimedBytes; realPages = realBytes / P; random = randomEvict; }

        void Remove(long pg)
        {
            store.Remove(pg);
            LinkedListNode<long> n; if (nodes.TryGetValue(pg, out n)) { order.Remove(n); nodes.Remove(pg); }
            int i = keyIdx[pg]; long last = keys[keys.Count - 1]; keys[i] = last; keyIdx[last] = i;
            keys.RemoveAt(keys.Count - 1); keyIdx.Remove(pg);
        }
        protected override void WritePage(long pg, byte[] s, int o)
        {
            if (store.ContainsKey(pg)) Remove(pg);
            if (store.Count >= realPages)
            {
                long victim = random ? keys[rng.Next(keys.Count)] : order.First.Value;
                Remove(victim);
            }
            store[pg] = Copy(s, o);
            nodes[pg] = order.AddLast(pg);
            keyIdx[pg] = keys.Count; keys.Add(pg);
        }
        protected override bool ReadPage(long pg, byte[] d, int o)
        {
            byte[] b; if (!store.TryGetValue(pg, out b)) return false;
            Buffer.BlockCopy(b, 0, d, o, P); return true;
        }
    }

    /// <summary>Above the real size writes are discarded (blank), or rejected with an error.</summary>
    class EdgeDevice : SimDevice
    {
        long realPages; bool error; Dictionary<long, byte[]> store = new Dictionary<long, byte[]>();
        public EdgeDevice(long claimedBytes, long realBytes, bool throwOnWrite)
        { claimed = claimedBytes; realPages = realBytes / P; error = throwOnWrite; }
        protected override void WritePage(long pg, byte[] s, int o)
        {
            if (pg >= realPages) { if (error) throw new MediaIOException("I/O device error", 1117); return; }
            store[pg] = Copy(s, o);
        }
        protected override bool ReadPage(long pg, byte[] d, int o)
        {
            byte[] b; if (!store.TryGetValue(pg, out b)) return false;
            Buffer.BlockCopy(b, 0, d, o, P); return true;
        }
    }

    /// <summary>Genuine size, but a small region silently loses data (worn/bad flash).</summary>
    class BadSpotDevice : SimDevice
    {
        long badFrom, badTo; Dictionary<long, byte[]> store = new Dictionary<long, byte[]>();
        public BadSpotDevice(long size, long from, long to) { claimed = size; badFrom = from / P; badTo = to / P; }
        protected override void WritePage(long pg, byte[] s, int o) { if (pg < badFrom || pg >= badTo) store[pg] = Copy(s, o); }
        protected override bool ReadPage(long pg, byte[] d, int o)
        {
            byte[] b; if (!store.TryGetValue(pg, out b)) return false;
            Buffer.BlockCopy(b, 0, d, o, P); return true;
        }
    }

    class QuietUi : IUi
    {
        public int StopAfterStatusCalls = -1;
        int calls;
        public void Line(string t) { }
        public void Progress(int stage, double f, long b, double m, string n) { }
        public void Status(string t) { if (t.StartsWith("Stage 2  writing")) calls++; }
        public bool StopRequested()
        {
            if (StopAfterStatusCalls > 0 && calls >= StopAfterStatusCalls) { StopAfterStatusCalls = -1; return true; }
            return false;
        }
    }

    static class Program
    {
        const long MiB = 1L << 20, GiB = 1L << 30;
        static int failures = 0;

        static EngineConfig SmallConfig()
        {
            // Same algorithm, scaled down so a "claimed 4 GiB" drive tests in seconds.
            EngineConfig c = new EngineConfig();
            c.WindowBytes = 8 * MiB;
            c.CheckEvery = 64 * MiB;
            c.EarlyChecks = new long[] { 4 * MiB, 8 * MiB, 16 * MiB, 32 * MiB };
            c.RecentExclude = 1 * MiB;
            c.SamplePages = 512;
            return c;
        }

        static void Run(string name, IBlockDevice dev, long realBytes, bool expectFake)
        {
            Run(name, dev, realBytes, expectFake ? VerdictKind.Fake : VerdictKind.Genuine, new QuietUi());
        }

        static void Run(string name, IBlockDevice dev, long realBytes, VerdictKind expect, QuietUi ui)
        {
            bool expectFake = expect == VerdictKind.Fake;
            Engine e = new Engine(dev, ui, SmallConfig());
            DateTime t0 = DateTime.Now;
            ProbeResult pr = e.QuickProbe();
            FillResult fr = e.FillTest(null);
            Report rep = Report.Build("sim", dev.Length, pr, fr);

            long measured = rep.RealBytes;
            double err = realBytes > 0 ? Math.Abs(measured - realBytes) / (double)realBytes : 0;
            bool fakeOk = rep.Kind == expect;
            bool sizeOk = expect == VerdictKind.Partial || err <= 0.02;
            bool safeOk = !expectFake || (rep.SafeBytes > 0 && rep.SafeBytes <= realBytes);
            bool pass = fakeOk && sizeOk && safeOk;
            if (!pass) failures++;

            Console.WriteLine("{0}  {1,-34} real {2,9}  measured {3,9}  err {4,5:P1}  written {5,9}  stage1 est {6,9}  safe {7,9}  {8}  ({9:F1}s)",
                pass ? "PASS" : "FAIL", name, Engine.Gb(realBytes), Engine.Gb(measured), err,
                Engine.Gb(fr.WrittenBytes), pr.EstimateBytes > 0 ? Engine.Gb(pr.EstimateBytes) : "-",
                rep.SafeBytes > 0 ? Engine.Gb(rep.SafeBytes) : "-", rep.Verdict,
                (DateTime.Now - t0).TotalSeconds);
            GC.Collect();
            if (!pass)
                foreach (string l in rep.Lines) Console.WriteLine("        " + l);
        }

        static int Main()
        {
            long claim = 4 * GiB;
            Run("genuine 512 MiB", new EdgeDevice(512 * MiB, 512 * MiB, false), 512 * MiB, false);
            Run("wrap, real 256 MiB (pow2)", new WrapDevice(claim, 256 * MiB), 256 * MiB, true);
            Run("wrap, real 300 MiB (odd)", new WrapDevice(claim, 300 * MiB), 300 * MiB, true);
            Run("wrap, real 37 MiB (tiny)", new WrapDevice(claim, 37 * MiB), 37 * MiB, true);
            Run("recycle FIFO, real 200 MiB", new RecycleDevice(claim, 200 * MiB, false), 200 * MiB, true);
            Run("recycle RANDOM, real 200 MiB", new RecycleDevice(claim, 200 * MiB, true), 200 * MiB, true);
            Run("recycle RANDOM, real 700 MiB", new RecycleDevice(claim, 700 * MiB, true), 700 * MiB, true);
            Run("blank above 333 MiB", new EdgeDevice(claim, 333 * MiB, false), 333 * MiB, true);
            Run("write error above 250 MiB", new EdgeDevice(claim, 250 * MiB, true), 250 * MiB, true);
            Run("genuine 768 MiB, 3 MiB bad spot", new BadSpotDevice(768 * MiB, 500 * MiB, 503 * MiB), 768 * MiB - 3 * MiB, VerdictKind.Faulty, new QuietUi());
            QuietUi stopper = new QuietUi(); stopper.StopAfterStatusCalls = 1;
            Run("genuine 512 MiB, user stops early", new EdgeDevice(512 * MiB, 512 * MiB, false), 0, VerdictKind.Partial, stopper);
            Console.WriteLine(failures == 0 ? "\nALL TESTS PASSED" : "\n" + failures + " TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
