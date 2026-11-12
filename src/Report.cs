// True Flasher - Made by Adromedary
// Report.cs - turns raw test results into a verdict a human can act on.

using System;
using System.Collections.Generic;

namespace TrueFlasher
{
    public enum VerdictKind { Genuine, Fake, Faulty, Partial, Unknown }

    public class Report
    {
        const int P = Pattern.PageSize;
        const long MiB = 1L << 20;

        public VerdictKind Kind = VerdictKind.Unknown;
        public string Verdict = "";
        public string How = "";
        public long RealBytes = -1;     // measured capacity that holds data
        public long SafeBytes = -1;     // recommended partition size
        public bool IsFake { get { return Kind == VerdictKind.Fake; } }
        public List<string> Lines = new List<string>();

        public static string Size(long bytes)
        {
            if (bytes < 0) return "-";
            return string.Format("{0:N2} GB ({1:N2} GiB)", bytes / 1e9, bytes / (double)(1L << 30));
        }

        static long Safe(long realBytes, double factor)
        {
            long s = (long)(realBytes * factor) / MiB * MiB;
            return s > 0 ? s : -1;
        }

        public static Report Build(string driveLabel, long claimed, ProbeResult probe, FillResult fill)
        {
            Report r = new Report();
            VerifyResult v = fill != null ? fill.Verify : null;

            if (fill != null && fill.Ran && v != null && v.Pages > 0)
            {
                long okB = v.Ok * P;
                long checkedB = v.Pages * P;
                bool loss = fill.LossDetectedAtBytes >= 0 || v.Bad > 0 || fill.WriteError;

                if (!loss)
                {
                    if (fill.ReachedEnd && !v.Aborted && !v.DeviceGone)
                    {
                        r.Kind = VerdictKind.Genuine;
                        r.Verdict = "GENUINE - every byte of the claimed size was written and verified";
                        r.RealBytes = okB;
                    }
                    else
                    {
                        r.Kind = VerdictKind.Partial;
                        r.Verdict = "NO FAKE BEHAVIOUR FOUND in the part tested (test was stopped early)";
                        r.RealBytes = okB;
                        r.How = "At least " + Size(okB) + " is real. Run the full test to confirm the rest.";
                    }
                }
                else
                {
                    r.RealBytes = okB;
                    bool ranOut = fill.OutOfFlash || fill.WriteError;
                    r.Kind = ranOut && okB < claimed * 0.9 ? VerdictKind.Fake : VerdictKind.Faulty;
                    r.Verdict = r.Kind == VerdictKind.Fake
                        ? "FAKE - the drive does NOT hold what it claims"
                        : "FAULTY - capacity looks real but some data was lost or damaged";

                    long edge = v.FirstBadPage >= 0 ? v.FirstBadPage * P : okB;
                    if (r.Kind == VerdictKind.Faulty)
                        r.How = Engine.Gb(v.Bad * P) + " (" + v.Bad.ToString("N0") + " pages) did not hold data, first at " + Engine.Gb(edge) +
                                ". New data kept being stored, so this is damaged flash, not a faked size.";
                    else if (v.Moved > 0 && v.Moved * 2 >= v.Bad && v.AliasPages > 0)
                        r.How = "Wraparound: addresses past " + Engine.Gb(v.AliasPages * P) +
                                " loop back to the start and overwrite older data.";
                    else if (fill.WriteError && v.Bad == 0)
                        r.How = "The drive refuses writes beyond " + Engine.Gb(fill.WrittenBytes) + ".";
                    else if (v.FirstBadPage >= 0 && edge < checkedB / 10)
                        r.How = "Recycling: when its real flash is full it silently throws away older data to make room.";
                    else if (v.Blank * 2 >= v.Bad)
                        r.How = "Data written beyond about " + Engine.Gb(edge) + " is silently discarded (reads back empty).";
                    else
                        r.How = "Data beyond about " + Engine.Gb(edge) + " comes back corrupted.";

                    if (r.Kind == VerdictKind.Fake) r.SafeBytes = Safe(okB, 0.96);
                    if (v.Aborted || v.DeviceGone)
                        r.How += " (read-back was not completed, so the figure is a lower bound)";
                }
            }
            else if (fill != null && fill.Ran && fill.DeviceGone)
            {
                r.Kind = VerdictKind.Fake;
                r.Verdict = "FAKE (very likely) - the drive disconnected after " + Engine.Gb(fill.WrittenBytes) + " was written";
                r.RealBytes = fill.LastCleanCheckBytes;
                r.How = "Fake controllers often crash when pushed past their real flash. The last clean check was at " +
                        Engine.Gb(fill.LastCleanCheckBytes) + ".";
                r.SafeBytes = Safe(fill.LastCleanCheckBytes, 0.90);
            }
            else if (probe != null && probe.Ran)
            {
                if (probe.EstimateBytes > 0)
                {
                    r.Kind = VerdictKind.Fake;
                    r.Verdict = "FAKE - found by the quick probe";
                    r.RealBytes = probe.EstimateBytes;
                    r.How = Cap(probe.Behaviour) + ". Run the full test for an exact figure.";
                    r.SafeBytes = Safe(probe.EstimateBytes, 0.90);
                }
                else
                {
                    r.Kind = VerdictKind.Partial;
                    r.Verdict = "QUICK PROBE FOUND NOTHING - this does not prove the drive is genuine";
                    r.How = "Recycling fakes pass the quick probe. Run the full test.";
                }
            }

            // ---------------- body
            r.Lines.Add("Drive          : " + driveLabel);
            r.Lines.Add("Claimed size   : " + Size(claimed));
            r.Lines.Add("Verdict        : " + r.Verdict);
            if (r.RealBytes >= 0)
                r.Lines.Add("Real capacity  : " + Size(r.RealBytes) +
                    (claimed > 0 && r.Kind == VerdictKind.Fake ? string.Format("  = {0:P1} of the claim", r.RealBytes / (double)claimed) : ""));
            if (r.How.Length > 0) r.Lines.Add("Behaviour      : " + r.How);
            if (r.SafeBytes > 0) r.Lines.Add("Safe size      : " + (r.SafeBytes / MiB).ToString("N0") + " MB  (partition size to use)");
            r.Lines.Add("");

            if (probe != null && probe.Ran)
                r.Lines.Add(string.Format("Stage 1 probe  : {0} probes, {1} failed, {2:F0}s. {3}",
                    probe.Probes, probe.BadProbes, probe.Seconds, Cap(probe.Behaviour)));
            if (fill != null && fill.Ran)
            {
                string s2 = "Stage 2 fill   : wrote " + Engine.Gb(fill.WrittenBytes);
                if (fill.WriteSeconds > 0) s2 += string.Format(" at {0:F1} MB/s", fill.WrittenBytes / 1e6 / fill.WriteSeconds);
                s2 += " in " + Engine.Eta(fill.WriteSeconds);
                if (fill.OutOfFlash)
                    s2 += ". Ran out of real flash: older data went missing after " + Engine.Gb(fill.LossDetectedAtBytes) +
                          " (first lost page at " + Engine.Gb(fill.FirstFailPage * P) + ", " + fill.FirstFailState + ")";
                else if (fill.LossDetectedAtBytes >= 0)
                    s2 += ". Lost data found from " + Engine.Gb(fill.LossDetectedAtBytes) + " on, but new data kept being stored (bad area, not a fake edge)";
                else if (fill.ReachedEnd) s2 += ". Reached the end of the drive";
                else if (fill.UserStopped) s2 += ". Stopped by user";
                if (fill.WriteError) s2 += ". " + fill.ErrorText;
                if (fill.DeviceGone) s2 += ". Device disconnected: " + fill.ErrorText;
                r.Lines.Add(s2);
            }
            if (v != null)
            {
                r.Lines.Add(string.Format("Stage 3 verify : {0} read{1} in {2}{3}",
                    Engine.Gb(v.Pages * P),
                    v.Seconds > 0 ? string.Format(" at {0:F1} MB/s", v.Pages * (double)P / 1e6 / v.Seconds) : "",
                    Engine.Eta(v.Seconds), v.Aborted ? " (stopped early)" : ""));
                r.Lines.Add(string.Format("  intact {0}  | moved {1}  blank {2}  stale {3}  corrupt {4}  unreadable {5}",
                    Engine.Gb(v.Ok * P), Engine.Gb(v.Moved * P), Engine.Gb(v.Blank * P),
                    Engine.Gb(v.Stale * P), Engine.Gb(v.Garbage * P), Engine.Gb(v.Unreadable * P)));
            }
            return r;
        }

        static string Cap(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
