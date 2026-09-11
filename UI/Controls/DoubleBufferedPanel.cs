using System;
using System.Windows.Forms;

namespace VibeAlarm.UI.Controls
{
    /// <summary>
    /// Containers that actually double-buffer.
    ///
    /// ControlStyles do NOT inherit: setting OptimizedDoubleBuffer on the Form (MainForm's
    /// constructor does) buys its children nothing, and SetStyle is protected, so a plain
    /// `new Panel()` cannot be fixed from outside without reflection.
    ///
    /// WHICH styles matter here is a measured question, not a stylistic one. UserPaint on an
    /// AutoScroll container defeats the OS scroll-blit path — WinForms can no longer move the
    /// existing pixels and re-expose one band, so every wheel tick recomposites the whole
    /// surface. Measured on this app's content host that made scrolling ~70% SLOWER. The
    /// buffer-only pair (OptimizedDoubleBuffer + AllPaintingInWmPaint) keeps the blit and
    /// still suppresses the WM_ERASEBKGND flash, so that is the default.
    /// </summary>
    internal static class BufferedStyles
    {
        /// <summary>Buffer + no-erase, WITHOUT UserPaint/ResizeRedraw: safe on scroll hosts.</summary>
        internal const ControlStyles Minimal =
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint;

        /// Adds UserPaint + ResizeRedraw. Correct for owner-drawn leaf controls, measurably
        /// wrong for scrolling containers — kept for reference, not used.
        internal const ControlStyles Full =
            Minimal | ControlStyles.UserPaint | ControlStyles.ResizeRedraw;
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(BufferedStyles.Minimal, true);
            UpdateStyles();
        }
    }

    /// <summary>The list hosts that actually hold task/calendar rows, so they repaint on every
    /// scroll tick and every hover.</summary>
    public class DoubleBufferedFlowPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowPanel()
        {
            SetStyle(BufferedStyles.Minimal, true);
            UpdateStyles();
        }
    }
}
