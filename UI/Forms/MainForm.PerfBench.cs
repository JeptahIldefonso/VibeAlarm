using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VibeAlarm.UI.Diagnostics;
using Appearance = VibeAlarm.UI.Theming.Appearance;

namespace VibeAlarm.UI.Forms
{
    /// <summary>
    /// Self-driving performance harness for the two reported lag paths. Runs only when
    /// VIBEALARM_PERF=1, and drives the SAME code the user's clicks drive — the sidebar
    /// handler body is literally `activeView = viewKey; RenderActiveView();`, reproduced
    /// verbatim in <see cref="SwitchViewAndPaint"/> — so these numbers are not a model of
    /// the hot path, they are the hot path.
    ///
    /// Handle telemetry is part of the measurement, not a bonus: the first baseline run died
    /// with Win32 error 1158 (ERROR_NO_MORE_USER_HANDLES) partway through, so "how many USER
    /// objects does one tab switch strand?" is a primary number here.
    /// </summary>
    public partial class MainForm
    {
        private static readonly int BenchRounds =
            int.TryParse(Environment.GetEnvironmentVariable("VIBEALARM_PERF_ROUNDS"), out int r) ? r : 4;
        private const int BenchScrollSteps = 20;

        private static readonly string[] BenchViews =
            { "Dashboard", "Tasks", "Calendar", "Ambient", "Settings" };

        /// <summary>GetGuiResources: uiFlags 0 = GDI object count, 1 = USER object count, both
        /// per-process and both capped at 10,000 by Windows. A control that is removed from its
        /// parent but never disposed keeps its USER handle forever.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        private static (uint Gdi, uint User) GuiResources()
        {
            IntPtr h = Process.GetCurrentProcess().Handle;
            return (GetGuiResources(h, 0), GetGuiResources(h, 1));
        }

        internal void RunPerfBenchmark()
        {
            (uint gdi0, uint user0) = GuiResources();
            PerfProbe.Line(string.Empty);
            PerfProbe.Line($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}  tasks={masterTaskList.Count}  rounds={BenchRounds} ---");
            PerfProbe.Line($"{PerfProbe.RunLabel,-24} handles at start                   GDI={gdi0,-6} USER={user0}");
            PerfProbe.Flush();

            try
            {
                // TWO warm-up passes, NOT measured. The first render of each view pays one-time
                // JIT, font realization and SVG rasterization costs, and charging those to the
                // tab-switch metric would report a cost no user pays twice in a session. One pass
                // is NOT enough: it left those costs in the first MEASURED round, which swamped
                // the medians and made every variant look identical (~1272 ms across the board).
                for (int warm = 0; warm < 2; warm++)
                {
                    foreach (string view in BenchViews)
                    {
                        SwitchViewAndPaint(view);
                    }
                }
                PerfProbe.ResetPhases(); // warm-up samples must not pollute the phase ranking

                MeasureTabSwitch();
                MeasureScrollPaint();
            }
            catch (Exception ex)
            {
                // A crash mid-run IS a result (the baseline died on handle exhaustion) — record
                // it next to the samples that did complete rather than losing the whole run.
                PerfProbe.Line($"{PerfProbe.RunLabel,-24} ABORTED: {ex.GetType().Name}: {ex.Message}");
            }

            (uint gdi1, uint user1) = GuiResources();
            PerfProbe.ReportPhases();
            PerfProbe.Line($"{PerfProbe.RunLabel,-24} handles at end                     GDI={gdi1,-6} USER={user1}");
            PerfProbe.Flush();
        }

        /// <summary>One tab click: swap the active view, rebuild, and block until the content
        /// host has actually painted. Refresh() = Invalidate(true) + Update(), so the stopwatch
        /// closes on pixels-on-screen, which is what "time to first paint" has to mean.</summary>
        private double SwitchViewAndPaint(string view)
        {
            Stopwatch sw = Stopwatch.StartNew();
            activeView = view;
            RenderActiveView();
            contentPanel.Refresh();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        private void MeasureTabSwitch()
        {
            Dictionary<string, List<double>> perView = new();
            List<double> all = new();
            foreach (string view in BenchViews)
            {
                perView[view] = new List<double>();
            }

            for (int round = 0; round < BenchRounds; round++)
            {
                foreach (string view in BenchViews)
                {
                    double ms = SwitchViewAndPaint(view);
                    perView[view].Add(ms);
                    all.Add(ms);
                }
                (uint gdi, uint user) = GuiResources();
                PerfProbe.Line($"{PerfProbe.RunLabel,-24} after round {round + 1} ({(round + 1) * BenchViews.Length,2} tab switches)   GDI={gdi,-6} USER={user}");
                PerfProbe.Flush(); // survive a handle-exhaustion crash with the data intact
            }

            foreach (string view in BenchViews)
            {
                PerfProbe.Report($"tab-switch -> {view}", perView[view]);
            }
            PerfProbe.Report("tab-switch ALL", all);
        }

        /// <summary>Guards against a false win: if a "faster paint" is really a view that stopped
        /// drawing, the control count collapses or the rendered surface goes flat. Rasterizes the
        /// live content host and reports descendant count plus distinct colours.</summary>
        private void ReportRenderSanity()
        {
            int descendants = CountDescendants(contentPanel);
            int distinct = 0;
            try
            {
                using Bitmap shot = new(Math.Max(1, contentPanel.Width), Math.Max(1, contentPanel.Height));
                contentPanel.DrawToBitmap(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                HashSet<int> colours = new();
                for (int y = 0; y < shot.Height; y += 4)
                {
                    for (int x = 0; x < shot.Width; x += 4)
                    {
                        colours.Add(shot.GetPixel(x, y).ToArgb());
                    }
                }
                distinct = colours.Count;
            }
            catch (Exception ex)
            {
                PerfProbe.Line($"{PerfProbe.RunLabel,-24} render-sanity FAILED: {ex.Message}");
            }
            string widths = "n/a";
            if (taskListPanel != null && !taskListPanel.IsDisposed)
            {
                List<int> rowWidths = taskListPanel.Controls.Cast<Control>().Select(c => c.Width).Distinct().OrderBy(w => w).ToList();
                widths = $"host={taskListPanel.ClientSize.Width} rows=[{string.Join(",", rowWidths)}]";
            }
            PerfProbe.Line($"{PerfProbe.RunLabel,-24} render sanity (Tasks view)         descendants={descendants,-6} distinctColours={distinct}  {widths}");
        }

        private static int CountDescendants(Control host)
        {
            int n = host.Controls.Count;
            foreach (Control c in host.Controls)
            {
                n += CountDescendants(c);
            }
            return n;
        }

        /// <summary>Scroll cost, measured two ways because they answer different questions:
        /// the INCREMENTAL number (Update paints only the region the scroll invalidated) is
        /// what the user feels dragging the wheel; the FULL number (Refresh repaints the whole
        /// host) is the double-buffering stress case and the one that moves when a container
        /// stops being unbuffered.</summary>
        private void MeasureScrollPaint()
        {
            SwitchViewAndPaint("Tasks");
            Application.DoEvents();

            int step = Math.Max(1, Appearance.TaskRowHeight);
            List<double> incremental = new();
            List<double> full = new();

            for (int i = 1; i <= BenchScrollSteps; i++)
            {
                contentPanel.AutoScrollPosition = new Point(0, step * i);
                Stopwatch sw = Stopwatch.StartNew();
                contentPanel.Update();   // paints only what the scroll invalidated
                sw.Stop();
                incremental.Add(sw.Elapsed.TotalMilliseconds);
            }

            contentPanel.AutoScrollPosition = new Point(0, 0);
            for (int i = 1; i <= BenchScrollSteps; i++)
            {
                contentPanel.AutoScrollPosition = new Point(0, step * i);
                Stopwatch sw = Stopwatch.StartNew();
                contentPanel.Refresh();  // full-surface repaint
                sw.Stop();
                full.Add(sw.Elapsed.TotalMilliseconds);
            }

            ReportRenderSanity();
            PerfProbe.Report($"scroll incremental x{BenchScrollSteps}", incremental);
            PerfProbe.Report($"scroll full-repaint x{BenchScrollSteps}", full);
            contentPanel.AutoScrollPosition = new Point(0, 0);
        }
    }
}
