using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GameInputLab.Core;

namespace GameInputLab.UI
{
    public static class UiTheme
    {
        private static float _fontScale = 1f;

        public static Color Background { get; private set; }
        public static Color Sidebar { get; private set; }
        public static Color Surface { get; private set; }
        public static Color SurfaceRaised { get; private set; }
        public static Color SurfaceHover { get; private set; }
        public static Color Border { get; private set; }
        public static Color BorderSoft { get; private set; }
        public static Color Text { get; private set; }
        public static Color TextMuted { get; private set; }
        public static Color TextFaint { get; private set; }
        public static Color Accent { get; private set; }
        public static Color AccentHover { get; private set; }
        public static Color AccentDark { get; private set; }
        public static Color Record { get; private set; }
        public static Color RecordHover { get; private set; }
        public static Color RecordDark { get; private set; }
        public static Color Warning { get; private set; }
        public static Color Blue { get; private set; }

        static UiTheme()
        {
            ApplySettings(new AppSettings());
        }

        public static void ApplySettings(AppSettings settings)
        {
            if (settings == null)
                settings = new AppSettings();
            settings.Normalize();

            Color background;
            Color accent;
            if (!AppSettings.TryParseColor(settings.BackgroundColor, out background))
                background = Color.FromArgb(247, 247, 248);
            if (!AppSettings.TryParseColor(settings.AccentColor, out accent))
                accent = Color.FromArgb(15, 139, 126);

            Background = background;
            Sidebar = Blend(background, Color.Black, 0.035f);
            Surface = Color.White;
            SurfaceRaised = Blend(background, Color.White, 0.55f);
            SurfaceHover = Blend(background, Color.Black, 0.07f);
            Border = Blend(background, Color.Black, 0.14f);
            BorderSoft = Blend(background, Color.Black, 0.065f);
            Text = Color.FromArgb(32, 33, 35);
            TextMuted = Color.FromArgb(95, 99, 104);
            TextFaint = Color.FromArgb(138, 143, 152);
            Accent = accent;
            AccentHover = Blend(accent, Color.Black, 0.15f);
            AccentDark = Blend(accent, Color.Black, 0.35f);
            Record = Color.FromArgb(217, 45, 32);
            RecordHover = Color.FromArgb(180, 35, 24);
            RecordDark = Color.FromArgb(180, 35, 24);
            Warning = Color.FromArgb(181, 71, 8);
            Blue = Color.FromArgb(23, 92, 211);
            _fontScale = Math.Max(0.75f, Math.Min(1.6f,
                settings.FontScalePercent / 100f));
        }

        public static Font Font(float size, bool bold)
        {
            return new Font("Microsoft YaHei UI", size * 1.95f * _fontScale,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Font Mono(float size, bool bold)
        {
            return new Font("Cascadia Mono", size * 1.90f * _fontScale,
                bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static float FontScale
        {
            get { return _fontScale; }
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1f, radius * 2f);
            if (diameter > bounds.Width)
            {
                diameter = bounds.Width;
            }
            if (diameter > bounds.Height)
            {
                diameter = bounds.Height;
            }

            RectangleF arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180f, 90f);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270f, 90f);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0f, 90f);
            arc.X = bounds.Left;
            path.AddArc(arc, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color first, Color second, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }
    }

    public class RoundedPanel : Panel
    {
        private int cornerRadius = 14;
        private Color borderColor = UiTheme.BorderSoft;
        private int borderWidth = 1;

        public int CornerRadius
        {
            get { return cornerRadius; }
            set { cornerRadius = Math.Max(0, value); UpdateRegion(); Invalidate(); }
        }

        public Color BorderColor
        {
            get { return borderColor; }
            set { borderColor = value; Invalidate(); }
        }

        public int BorderWidth
        {
            get { return borderWidth; }
            set { borderWidth = Math.Max(0, value); Invalidate(); }
        }

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Padding = new Padding(1);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0 || cornerRadius <= 0)
            {
                return;
            }

            using (GraphicsPath path = UiTheme.RoundedRect(
                new RectangleF(0f, 0f, Width, Height), cornerRadius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null)
                {
                    old.Dispose();
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The entire surface is painted in OnPaint to avoid flicker.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5f, 0.5f,
                Math.Max(1, Width - 1f), Math.Max(1, Height - 1f));
            using (GraphicsPath path = UiTheme.RoundedRect(rect, cornerRadius))
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
                if (borderWidth > 0)
                {
                    using (Pen pen = new Pen(borderColor, borderWidth))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }

            base.OnPaint(e);
        }
    }

    public enum ButtonRole
    {
        Primary,
        Secondary,
        Danger,
        Quiet
    }

    public class RoundedButton : Button
    {
        private ButtonRole role = ButtonRole.Secondary;
        private bool hovering;
        private bool pressing;
        private int cornerRadius = 10;

        public ButtonRole Role
        {
            get { return role; }
            set { role = value; Invalidate(); }
        }

        public int CornerRadius
        {
            get { return cornerRadius; }
            set { cornerRadius = Math.Max(0, value); Invalidate(); }
        }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = UiTheme.Text;
            Font = UiTheme.Font(10f, true);
            Cursor = Cursors.Hand;
            Height = 40;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            pressing = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            pressing = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            pressing = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            pevent.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color fill;
            Color border;
            Color text;
            GetColors(out fill, out border, out text);
            if (pressing)
            {
                fill = UiTheme.Blend(fill, Color.Black, 0.13f);
            }

            RectangleF rect = new RectangleF(0.5f, 0.5f,
                Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using (GraphicsPath path = UiTheme.RoundedRect(rect, cornerRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                pevent.Graphics.FillPath(brush, path);
                if (border.A > 0)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        pevent.Graphics.DrawPath(pen, path);
                    }
                }

                if (Focused && ShowFocusCues)
                {
                    RectangleF focusRect = new RectangleF(rect.X + 3f, rect.Y + 3f,
                        Math.Max(1f, rect.Width - 6f), Math.Max(1f, rect.Height - 6f));
                    using (GraphicsPath focusPath = UiTheme.RoundedRect(
                        focusRect, Math.Max(1f, cornerRadius - 3)))
                    using (Pen focusPen = new Pen(Color.FromArgb(125, text)))
                    {
                        focusPen.DashStyle = DashStyle.Dot;
                        pevent.Graphics.DrawPath(focusPen, focusPath);
                    }
                }
            }

            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void GetColors(out Color fill, out Color border, out Color text)
        {
            if (!Enabled)
            {
                fill = UiTheme.Surface;
                border = UiTheme.BorderSoft;
                text = UiTheme.TextFaint;
                return;
            }

            switch (role)
            {
                case ButtonRole.Primary:
                    fill = hovering ? Color.FromArgb(52, 53, 65) : UiTheme.Text;
                    border = Color.Transparent;
                    text = Color.White;
                    break;
                case ButtonRole.Danger:
                    fill = hovering ? UiTheme.RecordHover : UiTheme.Record;
                    border = Color.Transparent;
                    text = Color.White;
                    break;
                case ButtonRole.Quiet:
                    fill = hovering ? UiTheme.SurfaceHover : Color.Transparent;
                    border = Color.Transparent;
                    text = hovering ? UiTheme.Text : UiTheme.TextMuted;
                    break;
                default:
                    fill = hovering ? UiTheme.SurfaceHover : UiTheme.Surface;
                    border = hovering ? UiTheme.Border : UiTheme.BorderSoft;
                    text = UiTheme.Text;
                    break;
            }
        }
    }

    public enum RunState
    {
        Idle,
        Preparing,
        Recording,
        Playing,
        Error
    }

    public class StateBadge : Control
    {
        private RunState state = RunState.Idle;
        private readonly Timer pulseTimer;
        private float pulse;
        private int pulseDirection = 1;

        public RunState State
        {
            get { return state; }
            set { state = value; Invalidate(); }
        }

        public StateBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Height = 32;
            Width = 120;
            Font = UiTheme.Font(8.8f, true);
            pulseTimer = new Timer();
            pulseTimer.Interval = 55;
            pulseTimer.Tick += PulseTimerTick;
            pulseTimer.Start();
        }

        private void PulseTimerTick(object sender, EventArgs e)
        {
            if (state != RunState.Recording && state != RunState.Playing &&
                state != RunState.Preparing)
            {
                if (pulse != 0f)
                {
                    pulse = 0f;
                    Invalidate();
                }
                return;
            }

            pulse += 0.05f * pulseDirection;
            if (pulse >= 1f)
            {
                pulse = 1f;
                pulseDirection = -1;
            }
            else if (pulse <= 0f)
            {
                pulse = 0f;
                pulseDirection = 1;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color color;
            string label;
            switch (state)
            {
                case RunState.Recording:
                    color = UiTheme.Record;
                    label = "录制中";
                    break;
                case RunState.Playing:
                    color = UiTheme.Accent;
                    label = "回放中";
                    break;
                case RunState.Preparing:
                    color = UiTheme.Warning;
                    label = "准备中";
                    break;
                case RunState.Error:
                    color = UiTheme.Warning;
                    label = "需检查";
                    break;
                default:
                    color = UiTheme.TextMuted;
                    label = "待命";
                    break;
            }

            RectangleF rect = new RectangleF(0.5f, 0.5f,
                Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using (GraphicsPath path = UiTheme.RoundedRect(rect, Height / 2f))
            using (SolidBrush background = new SolidBrush(
                Color.FromArgb(38, color.R, color.G, color.B)))
            {
                e.Graphics.FillPath(background, path);
            }

            int dotSize = 7;
            if (state == RunState.Recording || state == RunState.Playing ||
                state == RunState.Preparing)
            {
                float halo = 7f + pulse * 4f;
                using (SolidBrush haloBrush = new SolidBrush(
                    Color.FromArgb((int)(34f - pulse * 18f), color)))
                {
                    e.Graphics.FillEllipse(haloBrush, 13f - halo / 2f,
                        Height / 2f - halo / 2f, halo, halo);
                }
            }
            using (SolidBrush dot = new SolidBrush(color))
            {
                e.Graphics.FillEllipse(dot, 13f, Height / 2f - dotSize / 2f,
                    dotSize, dotSize);
            }

            TextRenderer.DrawText(e.Graphics, label, Font,
                new Rectangle(25, 0, Width - 29, Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pulseTimer != null)
            {
                pulseTimer.Stop();
                pulseTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class KeyCap : Control
    {
        public KeyCap()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Font = UiTheme.Mono(9.2f, true);
            ForeColor = UiTheme.TextMuted;
            Height = 26;
            Width = 36;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Width = Math.Max(36, TextRenderer.MeasureText(Text, Font).Width + 17);
            base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5f, 0.5f,
                Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using (GraphicsPath path = UiTheme.RoundedRect(rect, 5f))
            using (SolidBrush brush = new SolidBrush(UiTheme.SurfaceRaised))
            using (Pen pen = new Pen(UiTheme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                ForeColor, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    public class UiNumeric : Control
    {
        private decimal minimum;
        private decimal maximum;
        private decimal increment;
        private decimal currentValue;
        private int decimalPlaces;
        private string editBuffer;
        private int hoverZone;
        private int pressedZone;

        public event EventHandler ValueChanged;

        public UiNumeric(decimal minimum, decimal maximum, decimal increment,
            decimal value, int decimalPlaces)
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            this.minimum = minimum;
            this.maximum = Math.Max(minimum, maximum);
            this.increment = Math.Max(0.0001m, increment);
            this.decimalPlaces = Math.Max(0, Math.Min(4, decimalPlaces));
            currentValue = ClampAndRound(value);
            editBuffer = null;
            hoverZone = 0;
            pressedZone = 0;
            Height = 34;
            TabStop = true;
            Font = UiTheme.Mono(9.4f, true);
            Cursor = Cursors.Hand;
        }

        public decimal Value
        {
            get { return currentValue; }
            set
            {
                decimal next = ClampAndRound(value);
                if (next == currentValue)
                    return;
                currentValue = next;
                OnValueChanged(EventArgs.Empty);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled
                ? (hoverZone == 1 || hoverZone == 2 ? UiTheme.SurfaceHover : UiTheme.SurfaceRaised)
                : Color.FromArgb(240, 243, 247);
            Color border = Focused ? UiTheme.Accent : UiTheme.Border;
            Color text = Enabled ? UiTheme.Text : UiTheme.TextFaint;
            using (GraphicsPath path = UiTheme.RoundedRect(bounds, 7f))
            using (SolidBrush background = new SolidBrush(fill))
            using (Pen outline = new Pen(border))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(outline, path);
            }

            int buttonWidth = 27;
            int dividerX = Math.Max(1, Width - buttonWidth - 1);
            using (Pen divider = new Pen(UiTheme.Border))
                e.Graphics.DrawLine(divider, dividerX, 3, dividerX, Height - 4);

            Rectangle valueRect = new Rectangle(8, 0, Math.Max(1, dividerX - 12), Height);
            string display = currentValue.ToString(
                "F" + decimalPlaces, System.Globalization.CultureInfo.CurrentCulture);
            TextRenderer.DrawText(e.Graphics, display, Font, valueRect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);

            DrawArrow(e.Graphics, new Rectangle(dividerX + 1, 1, buttonWidth, Height / 2 - 1),
                true, hoverZone == 1, pressedZone == 1);
            DrawArrow(e.Graphics, new Rectangle(dividerX + 1, Height / 2, buttonWidth, Height / 2 - 1),
                false, hoverZone == 2, pressedZone == 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int nextZone = GetZone(e.Location);
            if (nextZone != hoverZone)
            {
                hoverZone = nextZone;
                Cursor = nextZone == 0 ? Cursors.Hand : Cursors.SizeNS;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverZone = 0;
            pressedZone = 0;
            Cursor = Cursors.Hand;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                Focus();
                pressedZone = GetZone(e.Location);
                if (pressedZone == 1)
                    Step(1);
                else if (pressedZone == 2)
                    Step(-1);
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressedZone = 0;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Enabled)
            {
                Step(e.Delta > 0 ? 1 : -1);
                HandledMouseEventArgs handled = e as HandledMouseEventArgs;
                if (handled != null)
                    handled.Handled = true;
            }
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down ||
                keyData == Keys.PageUp || keyData == Keys.PageDown ||
                keyData == Keys.Home || keyData == Keys.End)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled)
            {
                if (e.KeyCode == Keys.Up)
                {
                    Step(1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    Step(-1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.PageUp)
                {
                    Step(10);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.PageDown)
                {
                    Step(-10);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Home)
                {
                    Value = minimum;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.End)
                {
                    Value = maximum;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Back && editBuffer != null)
                {
                    editBuffer = editBuffer.Length > 1
                        ? editBuffer.Substring(0, editBuffer.Length - 1)
                        : "0";
                    ApplyEditBuffer();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
                {
                    editBuffer = null;
                    Invalidate();
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (Enabled && (Char.IsDigit(e.KeyChar) ||
                e.KeyChar == '.' || e.KeyChar == ',' ||
                (e.KeyChar == '-' && minimum < 0)))
            {
                string character = e.KeyChar == ',' ? "." : e.KeyChar.ToString();
                if (editBuffer == null)
                    editBuffer = character == "-" ? "-" : String.Empty;
                editBuffer += character;
                ApplyEditBuffer();
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            editBuffer = null;
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            editBuffer = null;
            Invalidate();
            base.OnLostFocus(e);
        }

        private void ApplyEditBuffer()
        {
            decimal parsed;
            if (Decimal.TryParse(editBuffer,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out parsed))
            {
                Value = parsed;
            }
            Invalidate();
        }

        private void Step(int direction)
        {
            editBuffer = null;
            Value = currentValue + increment * direction;
        }

        private decimal ClampAndRound(decimal value)
        {
            value = Math.Max(minimum, Math.Min(maximum, value));
            return Decimal.Round(value, decimalPlaces, MidpointRounding.AwayFromZero);
        }

        private int GetZone(Point point)
        {
            if (point.X < Width - 28 || !Enabled)
                return 0;
            return point.Y <= Height / 2 ? 1 : 2;
        }

        private void OnValueChanged(EventArgs e)
        {
            EventHandler handler = ValueChanged;
            if (handler != null)
                handler(this, e);
        }

        private void DrawArrow(Graphics graphics, Rectangle bounds, bool up,
            bool hovered, bool pressed)
        {
            if (hovered || pressed)
            {
                using (SolidBrush background = new SolidBrush(
                    pressed ? UiTheme.AccentDark : UiTheme.SurfaceHover))
                {
                    graphics.FillRectangle(background, bounds);
                }
            }
            int centerX = bounds.Left + bounds.Width / 2;
            int centerY = bounds.Top + bounds.Height / 2;
            Point[] points = up
                ? new Point[]
                {
                    new Point(centerX - 4, centerY + 2),
                    new Point(centerX + 4, centerY + 2),
                    new Point(centerX, centerY - 3)
                }
                : new Point[]
                {
                    new Point(centerX - 4, centerY - 2),
                    new Point(centerX + 4, centerY - 2),
                    new Point(centerX, centerY + 3)
                };
            using (SolidBrush arrow = new SolidBrush(Enabled ? UiTheme.TextMuted : UiTheme.TextFaint))
                graphics.FillPolygon(arrow, points);
        }
    }

    public class TimelinePreview : Control
    {
        private List<RecordedEvent> events = new List<RecordedEvent>();
        private int screenWidth = 1;
        private int screenHeight = 1;
        private int screenLeft;
        private int screenTop;
        private int selectedIndex = -1;

        public TimelinePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9.1f, false);
        }

        public void SetEvents(IList<RecordedEvent> value, int width, int height,
            int left, int top)
        {
            events = new List<RecordedEvent>();
            if (value != null)
            {
                for (int i = 0; i < value.Count; i++)
                {
                    if (value[i] != null)
                    {
                        events.Add(value[i]);
                    }
                }
            }
            screenWidth = Math.Max(1, width);
            screenHeight = Math.Max(1, height);
            screenLeft = left;
            screenTop = top;
            selectedIndex = -1;
            Invalidate();
        }

        public void SelectEventIndex(int index)
        {
            selectedIndex = index;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle titleRect = new Rectangle(16, 10, Width - 32, 25);
            TextRenderer.DrawText(e.Graphics, "输入轨迹", UiTheme.Font(9.6f, true),
                titleRect, UiTheme.Text, TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int cameraEventCount = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == RecordedEventKind.MouseRelativeMove)
                    cameraEventCount++;
            }
            string countText = events.Count.ToString("N0") + " 事件";
            if (cameraEventCount > 0)
                countText += " · 镜头 " + cameraEventCount.ToString("N0");
            TextRenderer.DrawText(e.Graphics, countText, UiTheme.Mono(8.6f, false),
                titleRect, UiTheme.TextMuted, TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Rectangle plot = new Rectangle(16, 43, Math.Max(10, Width - 32),
                Math.Max(10, Height - 59));
            using (GraphicsPath path = UiTheme.RoundedRect(plot, 9f))
            using (SolidBrush brush = new SolidBrush(
                Color.FromArgb(248, 250, 252)))
            using (Pen border = new Pen(UiTheme.BorderSoft))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }

            float scale = Math.Min((plot.Width - 18f) / screenWidth,
                (plot.Height - 18f) / screenHeight);
            float drawWidth = screenWidth * scale;
            float drawHeight = screenHeight * scale;
            RectangleF screenRect = new RectangleF(
                plot.Left + (plot.Width - drawWidth) / 2f,
                plot.Top + (plot.Height - drawHeight) / 2f,
                drawWidth, drawHeight);

            using (GraphicsPath screenPath = UiTheme.RoundedRect(screenRect, 4f))
            using (SolidBrush screenBrush = new SolidBrush(
                Color.FromArgb(238, 242, 247)))
            using (Pen screenPen = new Pen(Color.FromArgb(196, 205, 217)))
            {
                e.Graphics.FillPath(screenBrush, screenPath);
                e.Graphics.DrawPath(screenPen, screenPath);
            }

            if (events.Count == 0)
            {
                Rectangle messageRect = Rectangle.Inflate(plot, -18, -10);
                TextRenderer.DrawText(e.Graphics, "完成一次录制后，这里会显示操作轨迹",
                    Font, messageRect, UiTheme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            int stride = Math.Max(1, events.Count / 5000);
            List<PointF> points = new List<PointF>();
            PointF last = PointF.Empty;
            bool hasLast = false;
            int hitCount = 0;
            for (int i = 0; i < events.Count; i += stride)
            {
                RecordedEvent item = events[i];
                if (item.Kind != RecordedEventKind.MouseMove &&
                    item.Kind != RecordedEventKind.MouseButtonDown &&
                    item.Kind != RecordedEventKind.MouseButtonUp &&
                    item.Kind != RecordedEventKind.MouseWheel)
                {
                    continue;
                }

                PointF point = new PointF(
                    screenRect.Left + (item.X - screenLeft) * scale,
                    screenRect.Top + (item.Y - screenTop) * scale);
                if (!screenRect.Contains(point))
                {
                    continue;
                }

                if (hasLast)
                {
                    float dx = point.X - last.X;
                    float dy = point.Y - last.Y;
                    if (dx * dx + dy * dy >= 1.2f)
                    {
                        points.Add(point);
                    }
                }
                else
                {
                    points.Add(point);
                }
                last = point;
                hasLast = true;

                if ((item.Kind == RecordedEventKind.MouseButtonDown ||
                     item.Kind == RecordedEventKind.MouseWheel) && hitCount < 260)
                {
                    using (SolidBrush hit = new SolidBrush(
                        item.Kind == RecordedEventKind.MouseWheel
                        ? Color.FromArgb(180, UiTheme.Warning)
                        : Color.FromArgb(200, UiTheme.Record)))
                    {
                        e.Graphics.FillEllipse(hit, point.X - 2.2f, point.Y - 2.2f, 4.4f, 4.4f);
                    }
                    hitCount++;
                }
            }

            if (points.Count > 1)
            {
                using (Pen pathPen = new Pen(Color.FromArgb(155, UiTheme.Accent), 1.1f))
                {
                    pathPen.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(pathPen, points.ToArray());
                }
            }
            else if (cameraEventCount > 0)
            {
                Rectangle messageRect = Rectangle.Inflate(plot, -18, -10);
                TextRenderer.DrawText(e.Graphics,
                    "已捕获相对镜头位移\n回放时使用相对鼠标模式",
                    Font, messageRect, UiTheme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            if (selectedIndex >= 0 && selectedIndex < events.Count)
            {
                RecordedEvent selected = events[selectedIndex];
                if (selected.Kind == RecordedEventKind.MouseRelativeMove)
                    return;
                PointF selectedPoint = new PointF(
                    screenRect.Left + (selected.X - screenLeft) * scale,
                    screenRect.Top + (selected.Y - screenTop) * scale);
                if (screenRect.Contains(selectedPoint))
                {
                    using (SolidBrush halo = new SolidBrush(Color.FromArgb(55, UiTheme.Accent)))
                    using (SolidBrush core = new SolidBrush(UiTheme.Text))
                    {
                        e.Graphics.FillEllipse(halo, selectedPoint.X - 7f,
                            selectedPoint.Y - 7f, 14f, 14f);
                        e.Graphics.FillEllipse(core, selectedPoint.X - 3f,
                            selectedPoint.Y - 3f, 6f, 6f);
                    }
                }
            }
        }
    }

    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    public static class UiToast
    {
        public static void Show(Form owner, string message, ToastKind kind)
        {
            if (owner == null || owner.IsDisposed)
            {
                return;
            }

            ToastBanner toast = new ToastBanner(message, kind);
            owner.Controls.Add(toast);
            toast.BringToFront();
            toast.ShowAnimated();
        }
    }

    internal class ToastBanner : Control
    {
        private readonly Timer timer;
        private int age;
        private float opacity = 0f;
        private readonly ToastKind kind;

        public ToastBanner(string message, ToastKind kind)
        {
            this.kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = UiTheme.Font(9.6f, true);
            Text = message;
            Height = 50;
            Width = Math.Min(480, Math.Max(280,
                TextRenderer.MeasureText(message, Font).Width + 62));
            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += TimerTick;
        }

        public void ShowAnimated()
        {
            Form owner = FindForm();
            if (owner != null)
            {
                Left = Math.Max(12, (owner.ClientSize.Width - Width) / 2);
                Top = 66;
            }
            timer.Start();
            Invalidate();
        }

        private void TimerTick(object sender, EventArgs e)
        {
            age += timer.Interval;
            if (age < 150)
            {
                opacity = age / 150f;
            }
            else if (age > 3000)
            {
                opacity = Math.Max(0f, 1f - (age - 3000f) / 350f);
                if (opacity <= 0f)
                {
                    timer.Stop();
                    Dispose();
                    return;
                }
            }
            else
            {
                opacity = 1f;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent;
            switch (kind)
            {
                case ToastKind.Success:
                    accent = UiTheme.Accent;
                    break;
                case ToastKind.Warning:
                    accent = UiTheme.Warning;
                    break;
                case ToastKind.Error:
                    accent = UiTheme.Record;
                    break;
                default:
                    accent = UiTheme.Blue;
                    break;
            }

            int alpha = (int)(235 * opacity);
            RectangleF rect = new RectangleF(0.5f, 0.5f,
                Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
            using (GraphicsPath path = UiTheme.RoundedRect(rect, 10f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha,
                UiTheme.SurfaceRaised.R, UiTheme.SurfaceRaised.G, UiTheme.SurfaceRaised.B)))
            using (Pen pen = new Pen(Color.FromArgb(alpha, accent)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }

            using (SolidBrush dot = new SolidBrush(Color.FromArgb(alpha, accent)))
            {
                e.Graphics.FillEllipse(dot, 17f, Height / 2f - 4f, 8f, 8f);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(34, 0, Width - 46, Height),
                Color.FromArgb(alpha, UiTheme.Text),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null)
            {
                timer.Stop();
                timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
