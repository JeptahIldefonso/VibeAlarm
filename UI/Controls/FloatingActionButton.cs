using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VibeAlarm.UI.Theming;

namespace VibeAlarm.UI.Controls
{
    /// <summary>
    /// Circular floating action button (the Tasks view's "New Task" affordance). Owner-drawn
    /// and double-buffered, with a circular hit-test <see cref="Control.Region"/> cut from a
    /// <see cref="GraphicsPath"/> ellipse — not a square button with rounded corners — so
    /// clicks outside the disc fall through to whatever is underneath.
    ///
    /// Performance contract (why this is not a Guna2Button):
    /// - the drop shadow AND the button face (disc + plus glyph) are each rasterized ONCE
    ///   into cached bitmaps and rebuilt only when the disc size or accent ink changes —
    ///   never per frame, never per hover;
    /// - hover/press feedback is a ~150ms scale lerp driven by a <see cref="Timer"/>: each
    ///   tick paints two cached bitmaps into the control's offscreen buffer. No layout
    ///   recalculation, no bitmap allocation, no parent invalidation during the animation;
    /// - the host parents this control to a NON-scrolling container; the FAB keeps itself
    ///   above list content via BringToFront (see MainForm.PositionTaskFab).
    /// </summary>
    public class FloatingActionButton : Control
    {
        private const int AnimationMs = 150;   // hover/press lerp duration (spec)
        private const int TickMs = 15;         // ~66fps — smooth at 0 layout cost
        private const float HoverScale = 1.06F;
        private const float PressScale = 0.94F;
        private const int ShadowPad = 8;       // soft-shadow extent around the disc
        private const int ShadowOffsetY = 3;   // light comes from above

        private readonly int discSize;
        private Bitmap? shadowCache;
        private Bitmap? faceCache;
        private bool cachesDirty = true;
        private Color ink;
        private float scale = 1F;
        private float scaleFrom = 1F;
        private float scaleTo = 1F;
        private long animStart;
        // Ambiguous between System.Windows.Forms.Timer and System.Threading.Timer (implicit
        // usings) — the WinForms timer is required: it pumps on the UI thread, which every
        // other GDI+ consumer here assumes.
        private readonly System.Windows.Forms.Timer animator = new() { Interval = TickMs };

        /// <param name="accent">Theme accent color — the disc fill; the plus glyph is always
        /// white on it. Changing themes later goes through <see cref="SetInk"/>.</param>
        /// <param name="discSize">Diameter of the visible disc in pixels.</param>
        public FloatingActionButton(Color accent, int discSize = 52)
        {
            this.discSize = discSize;
            ink = accent;
            AccessibleName = "Create a new task";
            AccessibleRole = AccessibleRole.PushButton;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            // The soft shadow has alpha — whatever is behind the control (the atmospheric
            // background) must show through it, never a solid BackColor rectangle. Assigned
            // AFTER SupportsTransparentBackColor, or the setter rejects it.
            BackColor = Color.Transparent;
            // The control rect is larger than the disc: room for the shadow's blur extent
            // plus the hover-scale growth, so the 1.06x lerp never clips.
            int headroom = (int)Math.Ceiling(discSize * (HoverScale - 1F) / 2F) + 1;
            int total = discSize + 2 * (ShadowPad + headroom);
            Size = new Size(total, total);
            animator.Tick += OnAnimatorTick;
            // OnResize only fires when the size actually CHANGES — cut the initial region here
            // so the hit test is circular from the first showing.
            ApplyCircularRegion();
        }

        /// <summary>Theme hook: swaps the accent ink. Cache regeneration happens once, on the
        /// next paint — passing the SAME color is a pure no-op (the spec: regenerate only when
        /// size or ink actually changes).</summary>
        public void SetInk(Color accent)
        {
            if (accent.ToArgb() == ink.ToArgb())
            {
                return;
            }
            ink = accent;
            cachesDirty = true;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyCircularRegion();
        }

        /// <summary>Circular hit-test region: an ellipse matching the disc (with 1px of
        /// anti-alias allowance). Everything outside stays clickable through the control's
        /// bounding square — the FAB never blocks the list below its disc.</summary>
        private void ApplyCircularRegion()
        {
            int d = discSize + 2;
            using GraphicsPath path = new();
            path.AddEllipse(Width / 2 - d / 2, Height / 2 - d / 2, d, d);
            Region?.Dispose();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (cachesDirty)
            {
                RebuildCaches();
                cachesDirty = false;
            }
            if (faceCache == null || shadowCache == null)
            {
                return;
            }

            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Shadow first (offset down), scaled with the face so hover grows both together.
            float shadowDim = (discSize + 2 * ShadowPad) * scale;
            RectangleF shadowRect = CenteredRect(shadowDim);
            shadowRect.Offset(0, ShadowOffsetY * scale);
            g.DrawImage(shadowCache, shadowRect);

            g.DrawImage(faceCache, CenteredRect(discSize * scale));

            // Focus ring (keyboard users get the same affordance mouse hover gives).
            if (Focused)
            {
                using Pen focusPen = new(Color.FromArgb(160, ink), 1.5F);
                RectangleF ring = CenteredRect((discSize + 8) * scale);
                g.DrawEllipse(focusPen, ring);
            }
        }

        private RectangleF CenteredRect(float diameter)
        {
            return new RectangleF(Width / 2F - diameter / 2F, Height / 2F - diameter / 2F, diameter, diameter);
        }

        // ---- Hover / press animation (scale lerp, paint-only) ----

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            StartAnimation();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            StartAnimation();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                StartAnimation();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                StartAnimation();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
        }

        private float TargetScale
        {
            get
            {
                if (MouseButtons == MouseButtons.Left && ClientRectangle.Contains(PointToClient(Cursor.Position)))
                {
                    return PressScale;
                }
                return ClientRectangle.Contains(PointToClient(Cursor.Position)) ? HoverScale : 1F;
            }
        }

        private void StartAnimation()
        {
            scaleFrom = scale;
            scaleTo = TargetScale;
            animStart = Environment.TickCount64;
            animator.Start();
        }

        private void OnAnimatorTick(object? sender, EventArgs e)
        {
            float t = Math.Min(1F, (Environment.TickCount64 - animStart) / (float)AnimationMs);
            // Ease-out cubic: immediate response, soft landing — no bounce.
            float k = 1F - (1F - t) * (1F - t) * (1F - t);
            scale = scaleFrom + (scaleTo - scaleFrom) * k;
            Invalidate(); // paint-only; layout is never touched during the lerp
            if (t >= 1F)
            {
                scale = scaleTo;
                animator.Stop();
            }
        }

        // ---- Cached bitmaps (rebuilt only on size/ink change) ----

        private void RebuildCaches()
        {
            shadowCache?.Dispose();
            faceCache?.Dispose();
            shadowCache = BuildShadow();
            faceCache = BuildFace();
        }

        /// <summary>Soft drop shadow: concentric discs ramping alpha toward the center — a
        /// cheap gaussian built once, never per frame.</summary>
        private Bitmap BuildShadow()
        {
            int dim = discSize + 2 * ShadowPad;
            Bitmap bmp = new(dim, dim);
            using Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int passes = 8;
            const int totalAlpha = 70;
            int ramp = passes * (passes + 1) / 2;
            for (int i = 1; i <= passes; i++)
            {
                int inflate = (int)Math.Round((passes - i + 1) * (ShadowPad / (double)passes));
                int alpha = totalAlpha * i / ramp;
                if (alpha <= 0)
                {
                    continue;
                }
                using SolidBrush brush = new(Color.FromArgb(alpha, 0, 0, 0));
                g.FillEllipse(brush,
                    ShadowPad - inflate, ShadowPad - inflate,
                    discSize + 2 * inflate, discSize + 2 * inflate);
            }
            return bmp;
        }

        /// <summary>The button face: accent disc + white plus glyph, pre-composited so a
        /// hover frame is a single scaled DrawImage.</summary>
        private Bitmap BuildFace()
        {
            Bitmap bmp = new(discSize, discSize);
            using Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush fill = new(ink);
            g.FillEllipse(fill, 0, 0, discSize - 1, discSize - 1);

            // IconSet.Render returns a SHARED cached bitmap — draw it, never dispose it.
            Bitmap glyph = IconSet.Render(IconKind.Plus, Color.White, (int)(discSize * 0.46F));
            int gx = (discSize - glyph.Width) / 2;
            int gy = (discSize - glyph.Height) / 2;
            g.DrawImage(glyph, gx, gy, glyph.Width, glyph.Height);
            return bmp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animator.Stop();
                animator.Dispose();
                shadowCache?.Dispose();
                faceCache?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
