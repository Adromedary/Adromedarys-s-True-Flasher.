// True Flasher - Made by Adromedary
// LinuxMain.cs - terminal front end for Linux: arrow-key drive picker, live progress panel,
// colour report and one-step "fix" to the real size.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TrueFlasher
{
    static class Ansi
    {
        public static bool On = true;
        static string E(string code) { return On ? "\x1b[" + code + "m" : ""; }
        public static string Reset { get { return E("0"); } }
        public static string Bold { get { return E("1"); } }
        public static string Dim { get { return E("2"); } }
        public static string Red { get { return E("38;5;203"); } }
        public static string Green { get { return E("38;5;78"); } }
        public static string Yellow { get { return E("38;5;214"); } }
        public static string Blue { get { return E("38;5;111"); } }
        public static string Teal { get { return E("38;5;43"); } }
        public static string Indigo { get { return E("38;5;105"); } }
        public static string Gray { get { return E("38;5;245"); } }
        public static string HideCursor { get { return On ? "\x1b[?25l" : ""; } }
        public static string ShowCursor { get { return On ? "\x1b[?25h" : ""; } }
    }

    /// <summary>Live 4-line progress panel at the bottom of the terminal; log lines scroll above it.</summary>
    class TermUi : IUi
    {
        readonly StreamWriter log;
        readonly bool tty;
        readonly object gate = new object();
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool stop;
        int panelLines;
        long lastDraw = -1000;

        public readonly int[] Pills = new int[3];     // 0 waiting, 1 running, 2 done, 3 skipped
        public string Title = "";
        double frac;
        string caption = "", speed = "", note = "";
        public DateTime Started = DateTime.Now;
        public bool PanelEnabled;

        public TermUi(StreamWriter log, bool tty) { this.log = log; this.tty = tty; }

        public void RequestStop() { stop = true; }

        int Width
        {
            get { try { return Math.Max(50, Math.Min(140, Console.WindowWidth)) - 1; } catch { return 79; } }
        }

        public void Line(string text) { Line(text, ""); }

        public void Line(string text, string color)
        {
            lock (gate)
            {
                ClearPanel();
                Console.WriteLine(color + text + Ansi.Reset);
                if (log != null) { log.WriteLine(text); log.Flush(); }
                DrawPanel();
            }
        }

        public void Status(string text) { }   // the panel shows structured progress instead

        public void Progress(int stage, double fraction, long bytes, double mbs, string n)
        {
            lock (gate)
            {
                frac = Math.Max(0, Math.Min(1, fraction));
                speed = mbs > 0 ? mbs.ToString("F1") + " MB/s" : "";
                note = n ?? "";
                caption = stage == 2 ? Engine.Gb(bytes) + " written - stops by itself at the real size"
                        : stage == 3 ? Engine.Gb(bytes) + " verified" : "";
                if (clock.ElapsedMilliseconds - lastDraw >= 200) { ClearPanel(); DrawPanel(); }
            }
        }

        public void SetStage(int stage, string title)
        {
            lock (gate)
            {
                for (int i = 0; i < 3; i++)
                    if (i + 1 < stage && Pills[i] != 3) Pills[i] = 2;
                    else if (i + 1 == stage) Pills[i] = 1;
                Title = title; frac = 0; caption = ""; speed = ""; note = "";
                ClearPanel(); DrawPanel();
            }
        }

        public void EndPanel()
        {
            lock (gate) { ClearPanel(); PanelEnabled = false; Console.Write(Ansi.ShowCursor); }
        }

        public bool StopRequested()
        {
            try
            {
                while (tty && Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape) stop = true;
                }
            }
            catch { }
            if (!stop) return false;
            stop = false;
            Line("  (stop requested - finishing this stage)", Ansi.Yellow);
            return true;
        }

        void ClearPanel()
        {
            if (!tty || panelLines == 0) return;
            Console.Write("\x1b[" + panelLines + "A\r\x1b[J");
            panelLines = 0;
        }

        static string Fit(string s, int w) { return s.Length > w ? s.Substring(0, Math.Max(0, w - 1)) + "…" : s; }

        void DrawPanel()
        {
            if (!tty || !PanelEnabled) return;
            lastDraw = clock.ElapsedMilliseconds;
            int w = Width;
            StringBuilder sb = new StringBuilder();

            // 1) stage pills
            string[] names = { "Quick probe", "Fill & spot-check", "Verify" };
            sb.Append("  ");
            for (int i = 0; i < 3; i++)
            {
                switch (Pills[i])
                {
                    case 1: sb.Append(Ansi.Teal + Ansi.Bold + "▶ " + names[i] + Ansi.Reset); break;
                    case 2: sb.Append(Ansi.Indigo + "✓ " + names[i] + Ansi.Reset); break;
                    case 3: sb.Append(Ansi.Gray + Ansi.Dim + "- " + names[i] + " (skipped)" + Ansi.Reset); break;
                    default: sb.Append(Ansi.Gray + "· " + names[i] + Ansi.Reset); break;
                }
                if (i < 2) sb.Append(Ansi.Gray + "  ──  " + Ansi.Reset);
            }
            sb.Append('\n');

            // 2) bar
            int bw = Math.Max(10, Math.Min(40, w - 14));
            int filled = (int)Math.Round(frac * bw);
            sb.Append("  " + Ansi.Teal + new string('█', filled) + Ansi.Gray + new string('░', bw - filled) + Ansi.Reset);
            sb.Append(Ansi.Bold + " " + (frac * 100).ToString("0.0").PadLeft(5) + "%" + Ansi.Reset + '\n');

            // 3) caption + stats
            string elapsed = Engine.Eta((DateTime.Now - Started).TotalSeconds);
            List<string> parts = new List<string>();
            if (caption.Length > 0) parts.Add(caption);
            if (speed.Length > 0) parts.Add(speed);
            if (note.Length > 0) parts.Add(note);
            sb.Append("  " + Fit(parts.Count > 0 ? string.Join("  ·  ", parts.ToArray()) : "working...", w - 2) + '\n');

            // 4) title + elapsed + hint
            sb.Append("  " + Ansi.Gray + Fit(Title + "  ·  " + elapsed + "   [Q = stop this stage]", w - 2) + Ansi.Reset + '\n');

            Console.Write(sb.ToString());
            panelLines = 4;
        }
    }

    static class LinuxProgram
    {
        const string Version = "1.0.0";
        static TermUi activeUi;

        static void Usage()
        {
            Console.WriteLine("Usage: sudo trueflasher [options]");
            Console.WriteLine();
            Console.WriteLine("  --device /dev/sdX   test this drive (skips the picker; still asks to confirm)");
            Console.WriteLine("  --quick             stage 1 only: ~1 minute wraparound probe");
            Console.WriteLine("  --no-probe          skip stage 1, go straight to the fill test");
            Console.WriteLine("  --check-mb N        spot-check interval in MB during the fill (default 1024)");
            Console.WriteLine("  --list              list drives and exit");
            Console.WriteLine("  --fix-mb N          skip testing: partition the drive to N MB (its real size from an earlier test)");
            Console.WriteLine("  --label TEXT        volume label for the fix (default \"USB <size>GB\")");
            Console.WriteLine("  --any-bus           also allow non-USB drives and loop devices (system disk stays protected)");
            Console.WriteLine("  --no-color          plain output");
            Console.WriteLine("  --version           print the version");
            Console.WriteLine();
            Console.WriteLine("While running: Q (or Ctrl+C) stops the current stage. What was written is still verified.");
        }

        static int Main(string[] args)
        {
            string device = null, fixLabel = null;
            bool quick = false, noProbe = false, listOnly = false, anyBus = false;
            long fixMb = 0;
            EngineConfig cfg = new EngineConfig();
            if (Console.IsOutputRedirected || Environment.GetEnvironmentVariable("NO_COLOR") != null) Ansi.On = false;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a == "--device" && i + 1 < args.Length) device = args[++i];
                    else if (a == "--quick") quick = true;
                    else if (a == "--no-probe") noProbe = true;
                    else if (a == "--list") listOnly = true;
                    else if (a == "--any-bus") anyBus = true;
                    else if (a == "--no-color") Ansi.On = false;
                    else if (a == "--check-mb" && i + 1 < args.Length) cfg.CheckEvery = Math.Max(64, long.Parse(args[++i])) << 20;
                    else if (a == "--fix-mb" && i + 1 < args.Length) fixMb = long.Parse(args[++i]);
                    else if (a == "--label" && i + 1 < args.Length) fixLabel = args[++i];
                    else if (a == "--version") { Console.WriteLine("True Flasher " + Version + " (Linux) - Made by Adromedary"); return 0; }
                    else { Usage(); return a == "--help" || a == "-h" ? 0 : 2; }
                }
            }
            catch (FormatException) { Usage(); return 2; }

            AppDomain.CurrentDomain.ProcessExit += delegate { Console.Write(Ansi.ShowCursor); };
            DateTime lastCtrlC = DateTime.MinValue;
            Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e)
            {
                if (activeUi != null && (DateTime.Now - lastCtrlC).TotalSeconds > 2)
                {
                    e.Cancel = true; lastCtrlC = DateTime.Now; activeUi.RequestStop();
                }
                else Console.Write(Ansi.Reset + Ansi.ShowCursor + "\n");
            };

            Banner();
            if (LibC.geteuid() != 0)
            {
                Console.WriteLine(Ansi.Red + "True Flasher needs root for raw disk access. Run:  sudo " + Environment.GetCommandLineArgs()[0] + Ansi.Reset);
                return 1;
            }
            try { return Run(device, quick, noProbe, listOnly, anyBus, cfg, fixMb, fixLabel); }
            catch (Exception ex)
            {
                if (activeUi != null) activeUi.EndPanel();
                Console.WriteLine();
                Console.WriteLine(Ansi.Red + "Error: " + ex.Message + Ansi.Reset);
                return 1;
            }
            finally { Console.Write(Ansi.ShowCursor); }
        }

        static void Banner()
        {
            Console.WriteLine();
            Console.WriteLine("  " + Ansi.Teal + Ansi.Bold + "T R U E   F L A S H E R" + Ansi.Reset + Ansi.Gray + "   v" + Version + " for Linux" + Ansi.Reset);
            Console.WriteLine("  " + Ansi.Gray + "Real-capacity tester for fake USB drives  ·  Made by Adromedary" + Ansi.Reset);
            Console.WriteLine();
        }

        static string ShortNote(string note)
        {
            if (note.StartsWith("system")) return "\u2715 system disk";
            if (note.StartsWith("not a USB")) return "\u2715 internal";
            if (note.StartsWith("read-only")) return "\u2715 read-only";
            return "\u2715 " + note;
        }

        static string DriveLine(LinuxDiskInfo d)
        {
            string name = (d.Vendor + " " + d.Model).Trim();
            if (name.Length == 0) name = "Unknown device";
            string s = d.Path.PadRight(14) + Engine.Gb(d.Size).PadLeft(12) + "   " + (name.Length > 30 ? name.Substring(0, 30) : name).PadRight(30) + "  " + d.Bus;
            if (d.Mounts.Count > 0) s += "  [mounted]";
            return s;
        }

        static int Run(string device, bool quick, bool noProbe, bool listOnly, bool anyBus, EngineConfig cfg, long fixMb, string fixLabel)
        {
            List<LinuxDiskInfo> disks = LinuxDiskList.All(anyBus);
            Console.WriteLine(Ansi.Gray + "  DEVICE              CLAIMS   MODEL                           BUS" + Ansi.Reset);
            int width = 100;
            try { width = Math.Max(60, Console.WindowWidth) - 1; } catch { }
            foreach (LinuxDiskInfo d in disks)
            {
                string line = DriveLine(d) + (d.Eligible ? "" : "   " + ShortNote(d.Note));
                if (line.Length > width - 2) line = line.Substring(0, width - 3) + "\u2026";
                Console.WriteLine("  " + (d.Eligible ? "" : Ansi.Gray) + line + Ansi.Reset);
            }
            Console.WriteLine();
            if (listOnly) return 0;

            List<LinuxDiskInfo> eligible = disks.FindAll(delegate(LinuxDiskInfo d) { return d.Eligible; });
            LinuxDiskInfo target = null;
            if (device != null)
            {
                string real = LibC.RealPath(device) ?? device;
                target = disks.Find(delegate(LinuxDiskInfo d) { return d.Path == real; });
                if (target == null)
                {
                    LinuxDiskInfo any = LinuxDiskList.All(true).Find(delegate(LinuxDiskInfo d) { return d.Path == real; });
                    Console.WriteLine(Ansi.Red + device + (any != null ? " is not testable: " + any.Note + (any.Note.StartsWith("not a USB") ? " (use --any-bus to allow it)" : "") : " is not a whole-disk device (use e.g. /dev/sdb, not /dev/sdb1)") + Ansi.Reset);
                    return 1;
                }
                if (!target.Eligible) { Console.WriteLine(Ansi.Red + device + " cannot be tested: " + target.Note + Ansi.Reset); return 1; }
            }
            else
            {
                if (eligible.Count == 0)
                {
                    Console.WriteLine("No USB / SD drive found. Plug the drive in and run True Flasher again.");
                    return 1;
                }
                List<string> items = new List<string>();
                foreach (LinuxDiskInfo d in eligible) items.Add(DriveLine(d));
                int idx = Menu("Pick the drive to test:", items);
                if (idx < 0) { Console.WriteLine("Cancelled."); return 0; }
                target = eligible[idx];
            }

            Console.WriteLine();
            Console.WriteLine("  " + Ansi.Bold + target.Label + Ansi.Reset);
            Console.WriteLine("  claims " + Report.Size(target.Size));
            foreach (string m in target.Mounts) Console.WriteLine("  " + Ansi.Gray + "mounted: " + m + Ansi.Reset);
            Console.WriteLine();
            string question = fixMb > 0
                ? "Erase " + target.Path + " and partition it to " + fixMb.ToString("N0") + " MB?"
                : "Erase EVERYTHING on " + target.Path + " and test it?";
            if (!Confirm(question)) { Console.WriteLine("Cancelled. Nothing was written."); return 0; }

            if (fixMb > 0)
            {
                long bytes = fixMb << 20;
                return DoFix(target, bytes, fixLabel ?? LinuxFixer.DefaultLabel(bytes)) ? 0 : 1;
            }

            string um = LinuxDiskList.UnmountAll(target);
            if (um != null) { Console.WriteLine(Ansi.Red + um + Ansi.Reset); return 1; }

            string logPath = Path.Combine(Directory.GetCurrentDirectory(),
                "TrueFlasher_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + target.Name + ".log");
            Report rep;
            using (StreamWriter log = new StreamWriter(logPath, false, new UTF8Encoding(false)))
            {
                GiveToSudoUser(logPath);
                bool tty = !Console.IsOutputRedirected;
                TermUi ui = new TermUi(log, tty);
                activeUi = ui;
                log.WriteLine("True Flasher " + Version + " (Linux) - Made by Adromedary");
                log.WriteLine("Started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                log.WriteLine(target.Label + "  claims " + Report.Size(target.Size) + "  sector " + target.SectorSize);
                log.WriteLine();

                ProbeResult probe = null;
                FillResult fill = null;
                using (LinuxDisk dev = new LinuxDisk(target.Path, target.Size))
                {
                    string err = dev.CheckWritable();
                    if (err != null) { Console.WriteLine(Ansi.Red + "Cannot write to the drive: " + err + Ansi.Reset); return 1; }

                    Engine engine = new Engine(dev, ui, cfg);
                    ui.Started = DateTime.Now;
                    ui.PanelEnabled = true;
                    Console.Write(Ansi.HideCursor);
                    if (quick || noProbe) { if (quick) { ui.Pills[1] = 3; ui.Pills[2] = 3; } if (noProbe) ui.Pills[0] = 3; }

                    if (!noProbe)
                    {
                        ui.SetStage(1, "Stage 1 of 3 - quick probe across the full claimed size");
                        probe = engine.QuickProbe();
                        ui.Line("  Stage 1: " + probe.Behaviour + (probe.EstimateBytes > 0 ? "  (estimated real size " + Report.Size(probe.EstimateBytes) + ")" : ""),
                                probe.EstimateBytes > 0 ? Ansi.Red : Ansi.Gray);
                    }
                    if (!quick && (probe == null || !probe.DeviceGone))
                    {
                        ui.SetStage(2, "Stage 2 of 3 - filling with spot checks");
                        fill = engine.FillTest(delegate(string s) { ui.SetStage(3, "Stage 3 of 3 - verifying everything written"); });
                    }
                    ui.EndPanel();
                    activeUi = null;
                    rep = Report.Build(target.Label, target.Size, probe, fill);
                    PrintReport(ui, rep, DateTime.Now - ui.Started);
                }
            }

            if (rep.SafeBytes > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  This drive can be made reliable at its " + Ansi.Bold + "real size" + Ansi.Reset + ": one partition of " +
                    (rep.SafeBytes >> 20).ToString("N0") + " MB, labelled \"" + LinuxFixer.CleanLabel(LinuxFixer.DefaultLabel(rep.SafeBytes), rep.SafeBytes) + "\".");
                if (Confirm("Fix it now?"))
                    DoFix(target, rep.SafeBytes, LinuxFixer.DefaultLabel(rep.SafeBytes));
                else
                {
                    Console.WriteLine(Ansi.Gray + "  To do it later:  sudo trueflasher --device " + target.Path + " --fix-mb " + (rep.SafeBytes >> 20) + Ansi.Reset);
                }
            }
            else if (rep.Kind == VerdictKind.Genuine || rep.Kind == VerdictKind.Partial)
            {
                Console.WriteLine();
                Console.WriteLine(Ansi.Gray + "  The drive has no partition now. To format it:  sudo trueflasher --device " + target.Path +
                    " --fix-mb " + ((target.Size >> 20) - 8) + Ansi.Reset);
            }
            Console.WriteLine();
            Console.WriteLine(Ansi.Gray + "  Log saved to " + logPath + Ansi.Reset);
            return rep.IsFake ? 3 : 0;
        }

        static void PrintReport(TermUi ui, Report rep, TimeSpan took)
        {
            string c = rep.Kind == VerdictKind.Genuine ? Ansi.Green : rep.Kind == VerdictKind.Fake ? Ansi.Red
                     : rep.Kind == VerdictKind.Faulty ? Ansi.Yellow : Ansi.Blue;
            ui.Line("");
            ui.Line("  ╔" + new string('═', 26) + " TRUE FLASHER REPORT " + new string('═', 26) + "╗", c);
            foreach (string l in rep.Lines)
            {
                bool key = l.StartsWith("Verdict") || l.StartsWith("Real capacity");
                ui.Line("    " + l, key ? c + Ansi.Bold : "");
            }
            ui.Line("    Total time     : " + Engine.Eta(took.TotalSeconds));
            ui.Line("  ╚" + new string('═', 73) + "╝", c);
        }

        static bool DoFix(LinuxDiskInfo d, long bytes, string label)
        {
            string missing = LinuxFixer.MissingTools(bytes);
            if (missing != null)
            {
                Console.WriteLine(Ansi.Red + "  Missing tool: " + missing + Ansi.Reset);
                Console.WriteLine(Ansi.Gray + "  Install it, or run these yourself:" + Ansi.Reset);
                foreach (string cmd in LinuxFixer.Commands(d, bytes, label)) Console.WriteLine("    " + cmd);
                return false;
            }
            Console.WriteLine(Ansi.Teal + "  Partitioning and formatting..." + Ansi.Reset);
            string output;
            bool ok = LinuxFixer.Run(d, bytes, label, out output);
            foreach (string l in output.Split('\n')) if (l.Trim().Length > 0) Console.WriteLine(Ansi.Gray + "    " + l.TrimEnd() + Ansi.Reset);
            Console.WriteLine(ok
                ? Ansi.Green + Ansi.Bold + "  Done. " + d.PartitionPath(1) + " is now a reliable " + Engine.Gb(bytes) + " drive labelled \"" + LinuxFixer.CleanLabel(label, bytes) + "\"." + Ansi.Reset
                : Ansi.Red + "  Fixing failed - see the output above." + Ansi.Reset);
            if (ok) Console.WriteLine(Ansi.Gray + "  (Tools like lsblk will still show the fake total size - the rest of the disk is unallocated; leave it alone.)" + Ansi.Reset);
            return ok;
        }

        /// <summary>Arrow-key picker; falls back to a numbered prompt when stdin is not a terminal.</summary>
        static int Menu(string title, List<string> items)
        {
            Console.WriteLine("  " + Ansi.Bold + title + Ansi.Reset);
            bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
            if (!interactive)
            {
                for (int i = 0; i < items.Count; i++) Console.WriteLine("    " + (i + 1) + ") " + items[i]);
                Console.Write("  Number (Enter = cancel): ");
                int n;
                return int.TryParse((Console.ReadLine() ?? "").Trim(), out n) && n >= 1 && n <= items.Count ? n - 1 : -1;
            }
            int sel = 0;
            Console.Write(Ansi.HideCursor);
            try
            {
                while (true)
                {
                    for (int i = 0; i < items.Count; i++)
                        Console.WriteLine(i == sel ? "  " + Ansi.Teal + Ansi.Bold + "❯ " + items[i] + Ansi.Reset
                                                   : "    " + items[i]);
                    Console.WriteLine(Ansi.Gray + "  ↑/↓ choose   Enter select   Q cancel" + Ansi.Reset);
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    Console.Write("\x1b[" + (items.Count + 1) + "A\r\x1b[J");
                    if (k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K) sel = (sel + items.Count - 1) % items.Count;
                    else if (k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J) sel = (sel + 1) % items.Count;
                    else if (k.Key == ConsoleKey.Enter) { Console.WriteLine("  " + Ansi.Teal + "❯ " + items[sel] + Ansi.Reset); return sel; }
                    else if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape) return -1;
                    else if (k.KeyChar >= '1' && k.KeyChar <= '9' && k.KeyChar - '1' < items.Count) sel = k.KeyChar - '1';
                }
            }
            finally { Console.Write(Ansi.ShowCursor); }
        }

        /// <summary>One keypress: y = yes, anything else = no. Reads a line when input is piped.</summary>
        static bool Confirm(string question)
        {
            Console.Write("  " + Ansi.Yellow + Ansi.Bold + question + Ansi.Reset + " [y/N] ");
            bool yes;
            if (Console.IsInputRedirected)
            {
                string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                yes = a == "y" || a == "yes";
            }
            else
            {
                ConsoleKeyInfo k = Console.ReadKey(true);
                yes = k.Key == ConsoleKey.Y;
            }
            Console.WriteLine(yes ? Ansi.Green + "yes" + Ansi.Reset : Ansi.Gray + "no" + Ansi.Reset);
            return yes;
        }

        /// <summary>When run through sudo, hand the log file back to the real user.</summary>
        static void GiveToSudoUser(string path)
        {
            int uid, gid;
            if (int.TryParse(Environment.GetEnvironmentVariable("SUDO_UID"), out uid) &&
                int.TryParse(Environment.GetEnvironmentVariable("SUDO_GID"), out gid))
                LibC.chown(path, uid, gid);
        }
    }
}
