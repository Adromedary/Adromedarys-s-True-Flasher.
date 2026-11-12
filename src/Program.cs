// True Flasher - Made by Adromedary
// Program.cs - console front end: pick the USB drive, confirm, run the stages, print the report.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;

[assembly: AssemblyTitle("True Flasher")]
[assembly: AssemblyDescription("Finds the real capacity of fake USB flash drives")]
[assembly: AssemblyCompany("Adromedary")]
[assembly: AssemblyProduct("True Flasher")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Adromedary - MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace TrueFlasher
{
    class ConsoleUi : IUi
    {
        readonly StreamWriter log;
        volatile bool stop;
        int lastLen;

        public ConsoleUi(StreamWriter log)
        {
            this.log = log;
            Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e) { e.Cancel = true; stop = true; };
        }

        public void Line(string text) { Line(text, Console.ForegroundColor); }

        public void Line(string text, ConsoleColor color)
        {
            ClearStatus();
            ConsoleColor old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = old;
            if (log != null) { log.WriteLine(text); log.Flush(); }
        }

        public void Progress(int stage, double fraction, long bytes, double mbPerSec, string note) { }

        public void Status(string text)
        {
            int w = 119;
            try { w = Math.Max(40, Console.WindowWidth - 1); } catch { }
            if (text.Length > w) text = text.Substring(0, w);
            Console.Write("\r" + text.PadRight(Math.Max(lastLen, text.Length)));
            lastLen = text.Length;
        }

        public void ClearStatus()
        {
            if (lastLen > 0) { Console.Write("\r" + new string(' ', lastLen) + "\r"); lastLen = 0; }
        }

        /// <summary>True once per Q / Ctrl+C press (consumed), so one press stops one stage.</summary>
        public bool StopRequested()
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    ConsoleKeyInfo k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Escape) stop = true;
                }
            }
            catch (InvalidOperationException) { }
            if (!stop) return false;
            stop = false;
            return true;
        }
    }

    static class Program
    {
        const string Version = "1.0.0";

        static void Usage()
        {
            Console.WriteLine("Usage: TrueFlasherCLI.exe [options]");
            Console.WriteLine();
            Console.WriteLine("  --disk N       test physical disk N (still asks you to type ERASE)");
            Console.WriteLine("  --quick        stage 1 only: ~1 minute wraparound probe, no full fill");
            Console.WriteLine("  --no-probe     skip stage 1, go straight to the fill test");
            Console.WriteLine("  --check-mb N   spot-check interval in MB during the fill (default 1024)");
            Console.WriteLine("  --list         list disks and exit");
            Console.WriteLine("  --fix-mb N     skip testing: partition the drive to N MB (its real size, from an earlier test)");
            Console.WriteLine("  --label TEXT   volume label for --fix-mb (default \"USB <size>GB\")");
            Console.WriteLine();
            Console.WriteLine("While running: press Q (or Ctrl+C) to stop the current stage.");
        }

        static int Main(string[] args)
        {
            int disk = -1; bool quick = false, noProbe = false, listOnly = false;
            long fixMb = 0; string fixLabel = null;
            EngineConfig cfg = new EngineConfig();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--disk" && i + 1 < args.Length) disk = int.Parse(args[++i]);
                else if (a == "--quick") quick = true;
                else if (a == "--no-probe") noProbe = true;
                else if (a == "--list") listOnly = true;
                else if (a == "--fix-mb" && i + 1 < args.Length) fixMb = long.Parse(args[++i]);
                else if (a == "--label" && i + 1 < args.Length) fixLabel = args[++i];
                else if (a == "--check-mb" && i + 1 < args.Length) cfg.CheckEvery = Math.Max(64, long.Parse(args[++i])) << 20;
                else { Usage(); return 2; }
            }

            try { Console.Title = "True Flasher " + Version + " - Made by Adromedary"; } catch { }
            Banner();

            if (!IsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("True Flasher needs administrator rights for raw disk access.");
                Console.WriteLine("Right-click TrueFlasher.exe and choose \"Run as administrator\".");
                Console.ResetColor();
                Pause();
                return 1;
            }

            int rc;
            try { rc = Run(disk, quick, noProbe, listOnly, cfg, fixMb, fixLabel); }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: " + ex.Message);
                Console.ResetColor();
                rc = 1;
            }
            Pause();
            return rc;
        }

        static int Run(int diskArg, bool quick, bool noProbe, bool listOnly, EngineConfig cfg, long fixMb, string fixLabel)
        {
            List<DiskInfo> disks = DiskList.All();
            Console.WriteLine("  #   Size                         Device                                   Status");
            Console.WriteLine("  --  ---------------------------  ---------------------------------------  ----------------");
            foreach (DiskInfo d in disks)
            {
                string name = (d.Vendor + " " + d.Product).Trim();
                if (d.Letters.Count > 0) name += "  [" + string.Join(" ", d.Letters.ToArray()) + "]";
                if (name.Length > 39) name = name.Substring(0, 39);
                Console.ForegroundColor = d.Eligible ? ConsoleColor.White : ConsoleColor.DarkGray;
                Console.WriteLine("  {0,-2}  {1,-27}  {2,-39}  {3}", d.Number, Report.Size(d.Size), name,
                    d.Eligible ? d.Bus + " - can be tested" : d.Note);
            }
            Console.ResetColor();
            Console.WriteLine();
            if (listOnly) return 0;

            List<DiskInfo> eligible = disks.FindAll(delegate(DiskInfo d) { return d.Eligible; });
            if (eligible.Count == 0)
            {
                Console.WriteLine("No USB / SD drive found. Plug the drive in and run True Flasher again.");
                return 1;
            }

            DiskInfo target = null;
            while (target == null)
            {
                string input;
                if (diskArg >= 0) { input = diskArg.ToString(); diskArg = -1; }
                else
                {
                    Console.Write("Enter the disk number to test (or press Enter to quit): ");
                    input = (Console.ReadLine() ?? "").Trim();
                    if (input.Length == 0) return 0;
                }
                int n;
                if (!int.TryParse(input, out n)) continue;
                target = disks.Find(delegate(DiskInfo d) { return d.Number == n; });
                if (target == null) { Console.WriteLine("No disk " + n + "."); continue; }
                if (!target.Eligible) { Console.WriteLine("Disk " + n + " cannot be tested: " + target.Note + "."); target = null; }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Selected : " + target.Label);
            Console.WriteLine("  Claims   : " + Report.Size(target.Size));
            if (target.Letters.Count > 0) Console.WriteLine("  Mounted  : " + string.Join(" ", target.Letters.ToArray()));
            Console.WriteLine();
            Console.WriteLine("  EVERYTHING on this drive will be ERASED. Copy off anything you need first.");
            Console.ResetColor();
            Console.Write("  Type ERASE to continue: ");
            if ((Console.ReadLine() ?? "").Trim() != "ERASE") { Console.WriteLine("Cancelled. Nothing was written."); return 0; }

            if (fixMb > 0)
            {
                long bytes = fixMb << 20;
                return DoFix(new ConsoleUi(null), target, bytes, fixLabel ?? DriveFixer.DefaultLabel(bytes)) ? 0 : 1;
            }

            string logPath = LogPath(target);
            Report rep;
            using (StreamWriter log = new StreamWriter(logPath, false, new UTF8Encoding(false)))
            {
                ConsoleUi ui = new ConsoleUi(log);
                log.WriteLine("True Flasher " + Version + " - Made by Adromedary");
                log.WriteLine("Started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                log.WriteLine(target.Label + "  serial " + target.Serial + "  sector " + target.SectorSize);
                log.WriteLine("Claimed " + Report.Size(target.Size));
                log.WriteLine();

                using (WinDisk dev = new WinDisk(target.Number, target.Size))
                {
                    foreach (string l in dev.LockLog) log.WriteLine("volume: " + l);
                    if (!Preflight(dev, ui)) return 1;

                    Engine engine = new Engine(dev, ui, cfg);
                    DateTime t0 = DateTime.Now;
                    ProbeResult probe = null;
                    FillResult fill = null;

                    if (!noProbe)
                    {
                        ui.Line("Stage 1 - quick probe across the full claimed size...", ConsoleColor.Cyan);
                        probe = engine.QuickProbe();
                        ui.Line("  " + (probe.EstimateBytes > 0
                            ? "Suspicious: " + probe.Behaviour + ".  Estimated real size " + Report.Size(probe.EstimateBytes)
                            : probe.Behaviour) + string.Format("  ({0:F0}s)", probe.Seconds),
                            probe.EstimateBytes > 0 ? ConsoleColor.Red : ConsoleColor.Gray);
                        if (probe.DeviceGone) ui.Line("  The drive disconnected during the probe.", ConsoleColor.Red);
                    }

                    if (!quick && (probe == null || !probe.DeviceGone))
                    {
                        ui.Line("Stage 2 - filling the drive with spot checks (stops as soon as data goes missing).", ConsoleColor.Cyan);
                        ui.Line("  Press Q to stop early; what was written so far is still verified.", ConsoleColor.DarkGray);
                        fill = engine.FillTest(delegate(string s) { ui.Line(s, ConsoleColor.Cyan); });
                    }

                    rep = Report.Build(target.Label, target.Size, probe, fill);
                    PrintReport(ui, rep, target, DateTime.Now - t0);
                }   // disk handle + volume locks released here

                if (rep.SafeBytes > 0)
                {
                    Console.WriteLine();
                    Console.Write("Fix the drive now (partition to " + (rep.SafeBytes >> 20).ToString("N0") + " MB, label \"" +
                                  DriveFixer.CleanLabel(DriveFixer.DefaultLabel(rep.SafeBytes), rep.SafeBytes) + "\")? [y/N] ");
                    if ((Console.ReadLine() ?? "").Trim().ToLowerInvariant().StartsWith("y"))
                        DoFix(ui, target, rep.SafeBytes, DriveFixer.DefaultLabel(rep.SafeBytes));
                }
                ui.Line("");
                ui.Line("Log saved to: " + logPath, ConsoleColor.DarkGray);
                return rep.IsFake ? 3 : 0;
            }
        }

        static bool Preflight(WinDisk dev, ConsoleUi ui)
        {
            string err = dev.CheckWritable();
            if (err == null) return true;
            ui.Line("Cannot write to the drive: " + err, ConsoleColor.Red);
            return false;
        }

        static void PrintReport(ConsoleUi ui, Report rep, DiskInfo d, TimeSpan took)
        {
            ConsoleColor c = rep.Kind == VerdictKind.Genuine ? ConsoleColor.Green
                           : rep.Kind == VerdictKind.Fake ? ConsoleColor.Red
                           : rep.Kind == VerdictKind.Faulty ? ConsoleColor.Yellow : ConsoleColor.Cyan;
            ui.Line("");
            ui.Line("========================== TRUE FLASHER REPORT ==========================", c);
            foreach (string l in rep.Lines)
                ui.Line(l, l.StartsWith("Verdict") || l.StartsWith("Real capacity") ? c : ConsoleColor.Gray);
            ui.Line("Total time     : " + Engine.Eta(took.TotalSeconds), ConsoleColor.Gray);
            ui.Line("==========================================================================", c);

            if (rep.SafeBytes > 0)
            {
                ui.Line("");
                ui.Line("This drive can be made reliable at its REAL size: a partition of " + (rep.SafeBytes >> 20).ToString("N0") +
                        " MB keeps every file inside working flash.", ConsoleColor.White);
                ui.Line("Manual diskpart commands (check the disk number with 'list disk' first!):", ConsoleColor.DarkGray);
                foreach (string l in DriveFixer.Script(d.Number, rep.SafeBytes, DriveFixer.DefaultLabel(rep.SafeBytes)))
                    ui.Line("  " + l);
            }
            else if (rep.Kind == VerdictKind.Genuine || rep.Kind == VerdictKind.Partial)
            {
                ui.Line("");
                ui.Line("The drive now has no partition. Format it in Disk Management, or with diskpart:", ConsoleColor.White);
                ui.Line("  select disk " + d.Number + " / clean / create partition primary / format fs=exfat quick / assign");
            }
        }

        static bool DoFix(ConsoleUi ui, DiskInfo d, long bytes, string label)
        {
            ui.Line("Partitioning and formatting...", ConsoleColor.Cyan);
            string output;
            bool ok = DriveFixer.Run(d.Number, d.Size, bytes, label, out output);
            ui.Line(output, ConsoleColor.DarkGray);
            ui.Line(ok ? "Done. The drive is now a reliable " + Engine.Gb(bytes) + " drive labelled \"" +
                         DriveFixer.CleanLabel(label, bytes) + "\"."
                       : "Fixing failed - see the diskpart output above.", ok ? ConsoleColor.Green : ConsoleColor.Red);
            return ok;
        }

        static string LogPath(DiskInfo d)
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(dir, "TrueFlasher_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_disk" + d.Number + ".log");
        }

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static void Banner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("  T R U E   F L A S H E R   v" + Version);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  Real-capacity tester for fake USB drives  -  Made by Adromedary");
            Console.ResetColor();
            Console.WriteLine();
        }

        static void Pause()
        {
            Console.WriteLine();
            Console.Write("Press Enter to exit...");
            try { Console.ReadLine(); } catch { }
        }
    }
}
