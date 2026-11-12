// True Flasher - Made by Adromedary
// Engine.cs - the capacity test itself. Knows nothing about Windows; works on any IBlockDevice.
//
// Stage 1  Quick probe (about a minute)
//          Stamps the first 64 MiB, then single stamped pages spread across the whole claimed
//          size (power-of-two aligned grid + every power-of-two boundary), written from the top
//          down. Reading everything back reveals address wraparound (a high address landing on
//          a low one) or areas that silently return nothing. Fast, catches the common fakes.
//
// Stage 2  Fill with spot checks (time depends on the REAL size, not the claimed one)
//          Writes the drive from the start with self-identifying pages. After each checkpoint
//          it re-reads a sample of everything written so far: the oldest pages (recycling fakes
//          drop those first), every power-of-two boundary and ~1000 random pages. The moment
//          older data is missing, damaged or replaced, the drive has run out of real flash and
//          writing stops - no need to fill the fake 1 TB.
//
// Stage 3  Full read-back
//          Reads back everything written and counts the pages that survived intact.
//          That count IS the real capacity, measured rather than guessed.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TrueFlasher
{
    public class EngineConfig
    {
        public int ProbeCount = 1024;                 // grid probes across the claimed size
        public long WindowBytes = 64L << 20;          // stamped area at the start for stage 1
        public long CheckEvery = 1L << 30;            // stage 2 checkpoint spacing after the early ones
        public long[] EarlyChecks = { 16L << 20, 32L << 20, 64L << 20, 128L << 20, 256L << 20, 512L << 20 };
        public int SamplePages = 1024;                // random pages re-read at each checkpoint
        public long RecentExclude = 16L << 20;        // skip the newest data (may sit in drive cache)
        public int ChunkBytes = 4 << 20;              // sequential transfer size
        public long BinarySearchResolution = 1L << 20;
    }

    public class ProbeResult
    {
        public bool Ran, Aborted, DeviceGone;
        public int Probes, BadProbes, WindowBad;
        public long AliasBytes = -1;     // wraparound period, if found
        public long FirstBadBytes = -1;  // lowest probe that failed
        public long EstimateBytes = -1;  // stage 1 estimate of real capacity
        public string Behaviour = "";
        public double Seconds;
    }

    public class VerifyResult
    {
        public long Pages, Ok, Moved, Stale, Blank, Garbage, Unreadable;
        public long FirstBadPage = -1;
        public long AliasPages = -1;
        public int AliasVotes;
        public bool Aborted, DeviceGone;
        public double Seconds;
        public long Bad { get { return Pages - Ok; } }
    }

    public class FillResult
    {
        public bool Ran;
        public long WrittenBytes, LastCleanCheckBytes, LossDetectedAtBytes = -1;
        public bool ReachedEnd, UserStopped, WriteError, DeviceGone, OutOfFlash;
        public int ChecksFailed;
        public string ErrorText = "";
        public long FirstFailPage = -1;
        public PageState FirstFailState;
        public long FirstFailFoundPage = -1;
        public double WriteSeconds;
        public VerifyResult Verify;
    }

    public class Engine
    {
        const int P = Pattern.PageSize;

        readonly IBlockDevice dev;
        readonly IUi ui;
        readonly EngineConfig cfg;
        readonly long totalPages;
        readonly int chunkPages;
        readonly byte[] chunk;
        readonly ulong[] scratch;
        readonly byte[] onePage = new byte[P];
        readonly ulong[] pageScratch = new ulong[Pattern.Words];

        public long TotalBytes { get { return totalPages * P; } }

        public Engine(IBlockDevice device, IUi ui, EngineConfig config)
        {
            dev = device; this.ui = ui; cfg = config;
            totalPages = dev.Length / P;
            int cb = Math.Min(cfg.ChunkBytes, dev.MaxTransfer);
            chunkPages = Math.Max(1, cb / P);
            chunk = new byte[chunkPages * P];
            scratch = new ulong[chunkPages * Pattern.Words];
        }

        // ------------------------------------------------------------------ helpers

        PageState ReadPage(long page, ulong run, out long found)
        {
            found = -1;
            try { dev.Read(page * P, onePage, P); }
            catch (MediaIOException) { return PageState.Unreadable; }
            return Pattern.Check(onePage, 0, run, page, pageScratch, out found);
        }

        void WritePage(long page, ulong run)
        {
            Pattern.Fill(onePage, run, page, 1, pageScratch);
            dev.Write(page * P, onePage, P);
        }

        static void Vote(Dictionary<long, int> d, long key)
        {
            if (key <= 0) return;
            int v; d.TryGetValue(key, out v); d[key] = v + 1;
        }

        /// <summary>Wraparound period: the smallest distance seen; mod-N mirroring produces multiples of it.</summary>
        static long PickPeriod(Dictionary<long, int> d, out int votes)
        {
            long best = -1; votes = 0;
            foreach (KeyValuePair<long, int> kv in d)
                if (best < 0 || kv.Key < best) { best = kv.Key; votes = kv.Value; }
            return best;
        }

        public static string Gb(long bytes)
        {
            return string.Format("{0:N2} GB", bytes / 1e9);
        }

        // ------------------------------------------------------------------ verify

        /// <summary>Reads pages [first, first+count) and classifies every one of them.</summary>
        public VerifyResult VerifyRange(ulong run, long first, long count, string label, int stage, double lo, double hi)
        {
            VerifyResult v = new VerifyResult();
            Dictionary<long, int> alias = new Dictionary<long, int>();
            Stopwatch sw = Stopwatch.StartNew();
            long lastUi = -1000;
            long end = first + count;
            long p = first;
            try
            {
                while (p < end)
                {
                    int n = (int)Math.Min(chunkPages, end - p);
                    bool chunkOk = true;
                    try { dev.Read(p * P, chunk, n * P); }
                    catch (MediaIOException) { chunkOk = false; }

                    for (int i = 0; i < n; i++)
                    {
                        long pg = p + i;
                        long found;
                        PageState st;
                        if (chunkOk)
                            st = Pattern.Check(chunk, i * P, run, pg, pageScratch, out found);
                        else
                            st = ReadPage(pg, run, out found);   // isolate the unreadable pages
                        Tally(v, st, pg, found, alias);
                    }
                    p += n;

                    if (sw.ElapsedMilliseconds - lastUi >= 500)
                    {
                        lastUi = sw.ElapsedMilliseconds;
                        double done = (double)(p - first) / count;
                        double mbs = (p - first) * (double)P / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        double eta = mbs > 0 ? (end - p) * (double)P / 1e6 / mbs : 0;
                        ui.Status(string.Format("{0}  {1} / {2}  ({3:P1})  {4:F1} MB/s  bad: {5:N0} pages  ETA {6}",
                            label, Gb((p - first) * P), Gb(count * P), done, mbs, v.Bad, Eta(eta)));
                        ui.Progress(stage, lo + (hi - lo) * done, (p - first) * P, mbs,
                            (v.Bad == 0 ? "no bad pages" : Gb(v.Bad * P) + " bad") + " - ETA " + Eta(eta));
                    }
                    if (ui.StopRequested()) { v.Aborted = true; break; }
                }
            }
            catch (DeviceGoneException) { v.DeviceGone = true; }

            int votes;
            v.AliasPages = PickPeriod(alias, out votes);
            v.AliasVotes = votes;
            v.Seconds = sw.Elapsed.TotalSeconds;
            return v;
        }

        static void Tally(VerifyResult v, PageState st, long pg, long found, Dictionary<long, int> alias)
        {
            v.Pages++;
            switch (st)
            {
                case PageState.Ok: v.Ok++; return;
                case PageState.Moved: v.Moved++; Vote(alias, Math.Abs(found - pg)); break;
                case PageState.Stale: v.Stale++; break;
                case PageState.Blank: v.Blank++; break;
                case PageState.Garbage: v.Garbage++; break;
                default: v.Unreadable++; break;
            }
            if (v.FirstBadPage < 0) v.FirstBadPage = pg;
        }

        public static string Eta(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "--";
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return string.Format("{0}h {1:D2}m", (int)t.TotalHours, t.Minutes);
            return string.Format("{0}m {1:D2}s", t.Minutes, t.Seconds);
        }

        // ------------------------------------------------------------------ stage 1

        List<long> BuildProbes(long minPage)
        {
            SortedSet<long> s = new SortedSet<long>();
            long step = 1;
            while (step * cfg.ProbeCount < totalPages) step <<= 1;
            for (long p = step; p < totalPages; p += step) s.Add(p);
            for (long b = 1; b < totalPages; b <<= 1) { s.Add(b - 1); s.Add(b); }
            s.Add(totalPages - 1);
            List<long> list = new List<long>();
            foreach (long p in s) if (p >= minPage && p < totalPages) list.Add(p);
            return list;
        }

        public ProbeResult QuickProbe()
        {
            ProbeResult r = new ProbeResult();
            r.Ran = true;
            Stopwatch sw = Stopwatch.StartNew();
            ulong run = Pattern.NewRunId();
            long winPages = Math.Min(cfg.WindowBytes / P, totalPages / 8);
            Dictionary<long, int> alias = new Dictionary<long, int>();
            SortedSet<long> bad = new SortedSet<long>();
            List<long> probes = BuildProbes(winPages);
            r.Probes = probes.Count;

            try
            {
                // 1) stamp the low window sequentially
                for (long p = 0; p < winPages; )
                {
                    int n = (int)Math.Min(chunkPages, winPages - p);
                    Pattern.Fill(chunk, run, p, n, scratch);
                    dev.Write(p * P, chunk, n * P);
                    p += n;
                    ui.Status(string.Format("Stage 1  stamping start of drive  {0:P0}", (double)p / winPages));
                    ui.Progress(1, 0.05 * p / winPages, p * P, 0, "stamping the start of the drive");
                }

                // 2) probes, top-down: if high addresses mirror onto low ones, the low (later) write
                //    wins and the HIGH probe reads back foreign data -> failure shows where it belongs.
                for (int i = probes.Count - 1; i >= 0; i--)
                {
                    try { WritePage(probes[i], run); }
                    catch (MediaIOException) { bad.Add(probes[i]); }
                    if ((i & 15) == 0)
                    {
                        ui.Status(string.Format("Stage 1  writing probes  {0} / {1}", probes.Count - i, probes.Count));
                        ui.Progress(1, 0.05 + 0.55 * (probes.Count - i) / probes.Count, 0, 0,
                            "writing probe " + (probes.Count - i) + " of " + probes.Count);
                        if (ui.StopRequested()) { r.Aborted = true; break; }
                    }
                }

                if (!r.Aborted)
                {
                    // 3) read back the window: any foreign stamp here means a high address landed on it
                    VerifyResult w = VerifyRange(run, 0, winPages, "Stage 1  checking start", 1, 0.60, 0.70);
                    r.WindowBad = (int)w.Bad;
                    if (w.DeviceGone) throw new DeviceGoneException("device lost", 0);
                    ScanWindowForAliases(run, winPages, alias);

                    // 4) read back every probe
                    for (int i = 0; i < probes.Count; i++)
                    {
                        long p = probes[i];
                        if (bad.Contains(p)) continue;
                        long found;
                        PageState st = ReadPage(p, run, out found);
                        if (st != PageState.Ok)
                        {
                            bad.Add(p);
                            if (st == PageState.Moved) Vote(alias, Math.Abs(p - found));
                        }
                        if ((i & 63) == 0)
                        {
                            ui.Status(string.Format("Stage 1  reading probes  {0} / {1}", i, probes.Count));
                            ui.Progress(1, 0.70 + 0.28 * i / probes.Count, 0, 0, "reading probe " + i + " of " + probes.Count);
                        }
                    }
                }
            }
            catch (DeviceGoneException) { r.DeviceGone = true; }
            catch (MediaIOException ex)
            {
                r.Behaviour = "the start of the drive cannot be written (" + ex.Message + ")";
                r.Seconds = sw.Elapsed.TotalSeconds;
                return r;
            }

            r.BadProbes = bad.Count;
            if (bad.Count > 0) r.FirstBadBytes = bad.Min * P;

            int votes;
            long period = PickPeriod(alias, out votes);
            if (period > 0)
            {
                r.AliasBytes = period * P;
                r.EstimateBytes = r.AliasBytes;
                r.Behaviour = "wraparound: addresses above " + Gb(r.AliasBytes) + " mirror back onto the start";
            }
            else if (bad.Count > 0 && !r.DeviceGone && !r.Aborted)
            {
                // no mirroring seen: binary-search the edge between the last good and first bad probe
                long lo = winPages - 1, hi = bad.Min;
                foreach (long p in probes) if (p < hi && !bad.Contains(p)) lo = p;
                try
                {
                    long resPages = Math.Max(1, cfg.BinarySearchResolution / P);
                    while (hi - lo > resPages)
                    {
                        long mid = lo + (hi - lo) / 2;
                        bool good;
                        try
                        {
                            WritePage(mid, run);
                            long f;
                            good = ReadPage(mid, run, out f) == PageState.Ok;
                        }
                        catch (MediaIOException) { good = false; }
                        if (good) lo = mid; else hi = mid;
                        ui.Status("Stage 1  narrowing the edge  " + Gb(lo * P) + " .. " + Gb(hi * P));
                        ui.Progress(1, 0.99, 0, 0, "narrowing the edge: " + Gb(lo * P) + " .. " + Gb(hi * P));
                    }
                }
                catch (DeviceGoneException) { r.DeviceGone = true; }
                r.EstimateBytes = (lo + 1) * P;
                r.Behaviour = "data written above about " + Gb(r.EstimateBytes) + " is not kept";
            }
            else if (r.WindowBad > 0)
            {
                r.Behaviour = "the start of the drive did not keep its data";
            }
            else
            {
                r.Behaviour = "no wraparound found (recycling fakes are only caught by stage 2)";
            }
            r.Seconds = sw.Elapsed.TotalSeconds;
            return r;
        }

        void ScanWindowForAliases(ulong run, long winPages, Dictionary<long, int> alias)
        {
            // VerifyRange already tallied; re-scan cheaply only the window for foreign stamps.
            for (long p = 0; p < winPages; )
            {
                int n = (int)Math.Min(chunkPages, winPages - p);
                try { dev.Read(p * P, chunk, n * P); }
                catch (MediaIOException) { p += n; continue; }
                for (int i = 0; i < n; i++)
                {
                    long found;
                    PageState st = Pattern.Check(chunk, i * P, run, p + i, pageScratch, out found);
                    if (st == PageState.Moved) Vote(alias, Math.Abs(found - (p + i)));
                }
                p += n;
            }
        }

        // ------------------------------------------------------------------ stage 2 + 3

        long NextCheck(long written)
        {
            foreach (long e in cfg.EarlyChecks) if (e > written) return e;
            return (written / cfg.CheckEvery + 1) * cfg.CheckEvery;
        }

        class Sample
        {
            public int Random, RandomFail, FixedFail;
            public long FirstFailPage = -1, FoundPage = -1;
            public PageState State;
            public bool AnyFail { get { return RandomFail + FixedFail > 0; } }
        }

        /// <summary>Re-reads the oldest pages, every power-of-two boundary and randomCount random pages
        /// from everything written so far (minus the newest data, which may still sit in the drive cache).</summary>
        Sample SampleCheck(ulong run, long limit, Random rng, int randomCount)
        {
            Sample s = new Sample();
            SortedSet<long> fixedPick = new SortedSet<long>();
            for (long i = 0; i < 8 && i < limit; i++) fixedPick.Add(i);                          // oldest data
            for (long c = 1; c < 32 && c * chunkPages < limit; c++) fixedPick.Add(c * chunkPages); // early chunks
            for (long b = 1; b < limit; b <<= 1) { fixedPick.Add(b - 1); fixedPick.Add(b); }
            SortedSet<long> randPick = new SortedSet<long>();
            int k = (int)Math.Min(randomCount, limit);
            while (randPick.Count < k) randPick.Add((long)(rng.NextDouble() * limit));

            // read in ascending address order (kinder to cheap controllers)
            SortedSet<long> all = new SortedSet<long>(fixedPick);
            all.UnionWith(randPick);
            foreach (long p in all)
            {
                if (p >= limit) continue;
                long found;
                PageState st = ReadPage(p, run, out found);
                bool isRand = randPick.Contains(p);
                if (isRand) s.Random++;
                if (st == PageState.Ok) continue;
                if (isRand) s.RandomFail++; else s.FixedFail++;
                if (s.FirstFailPage < 0) { s.FirstFailPage = p; s.State = st; s.FoundPage = found; }
            }
            return s;
        }

        public FillResult FillTest(Action<string> onStageChange)
        {
            FillResult f = new FillResult();
            f.Ran = true;
            ulong run = Pattern.NewRunId();
            Random rng = new Random(unchecked((int)run ^ (int)(run >> 32)));
            Stopwatch sw = Stopwatch.StartNew();
            long page = 0, nextCheck = NextCheck(0);
            long lastUi = -1000;
            string lastCheck = "none yet";
            bool suspect = false;               // older data has gone missing at least once
            long prevNet = 0, prevWritten = 0;  // estimated intact bytes at the previous checkpoint

            try
            {
                while (page < totalPages)
                {
                    int n = (int)Math.Min(chunkPages, totalPages - page);
                    Pattern.Fill(chunk, run, page, n, scratch);
                    try { dev.Write(page * P, chunk, n * P); }
                    catch (MediaIOException ex)
                    {
                        f.WriteError = true;
                        f.ErrorText = "write failed at " + Gb(page * P) + ": " + ex.Message;
                        break;
                    }
                    page += n;
                    f.WrittenBytes = page * P;

                    if (sw.ElapsedMilliseconds - lastUi >= 500)
                    {
                        lastUi = sw.ElapsedMilliseconds;
                        double mbs = f.WrittenBytes / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        ui.Status(string.Format("Stage 2  writing  {0} of {1} claimed  {2:F1} MB/s  last check: {3}  [Q = stop]",
                            Gb(f.WrittenBytes), Gb(TotalBytes), mbs, lastCheck));
                        ui.Progress(2, (double)f.WrittenBytes / TotalBytes, f.WrittenBytes, mbs, lastCheck);
                    }

                    if (f.WrittenBytes >= nextCheck || page == totalPages)
                    {
                        ui.Status("Stage 2  spot-checking older data at " + Gb(f.WrittenBytes) + " ...");
                        long limit = page - cfg.RecentExclude / P;
                        if (limit < 1) limit = page;
                        Sample s = SampleCheck(run, limit, rng, suspect ? cfg.SamplePages * 4 : cfg.SamplePages);

                        // estimated intact data = written minus the fraction of random samples that failed
                        double failRate = s.Random > 0 ? (double)s.RandomFail / s.Random : 0;
                        long net = f.WrittenBytes - (long)(failRate * limit * P);

                        if (s.AnyFail)
                        {
                            f.ChecksFailed++;
                            if (f.FirstFailPage < 0)
                            {
                                f.FirstFailPage = s.FirstFailPage; f.FirstFailState = s.State; f.FirstFailFoundPage = s.FoundPage;
                                f.LossDetectedAtBytes = f.WrittenBytes;
                            }
                            if (suspect)
                            {
                                // A fake that is out of real flash loses about as much old data as it takes in,
                                // so the intact total stops growing. A drive with a bad spot keeps growing.
                                long gain = net - prevNet, delta = f.WrittenBytes - prevWritten;
                                if (gain < delta / 2) { f.OutOfFlash = true; break; }
                                lastCheck = "Bad spot - still OK at " + Gb(f.WrittenBytes);
                            }
                            else
                            {
                                suspect = true;
                                lastCheck = "Data loss at " + Gb(f.WrittenBytes) + " - confirming";
                            }
                        }
                        else
                        {
                            f.LastCleanCheckBytes = f.WrittenBytes;
                            lastCheck = "OK at " + Gb(f.WrittenBytes);
                        }
                        prevNet = net; prevWritten = f.WrittenBytes;
                        nextCheck = NextCheck(f.WrittenBytes);
                    }
                    if (ui.StopRequested()) { f.UserStopped = true; break; }
                }
            }
            catch (DeviceGoneException ex)
            {
                f.DeviceGone = true;
                f.ErrorText = ex.Message;
            }
            f.WriteSeconds = sw.Elapsed.TotalSeconds;
            f.ReachedEnd = page == totalPages && !f.OutOfFlash && !f.WriteError && !f.DeviceGone;

            if (!f.DeviceGone && page > 0)
            {
                if (onStageChange != null) onStageChange("Stage 3 - reading back all " + Gb(page * P) + " written and counting what survived...");
                f.Verify = VerifyRange(run, 0, page, "Stage 3  verifying", 3, 0, 1);
                if (f.Verify.DeviceGone) f.DeviceGone = true;
            }
            return f;
        }
    }
}
