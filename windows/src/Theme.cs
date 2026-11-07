using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MSFamilyControl
{
    /// <summary>Farben, Schriften, Bilder im Cyberpunk-Stil des Control Centers</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(6, 10, 28), Panel = Color.FromArgb(11, 20, 48), Panel2 = Color.FromArgb(16, 26, 59),
            Cyan = Color.FromArgb(0, 233, 255), Pink = Color.FromArgb(242, 62, 233), Violet = Color.FromArgb(163, 108, 255), Gold = Color.FromArgb(255, 211, 107),
            Green = Color.FromArgb(28, 255, 114), Red = Color.FromArgb(255, 68, 126), Text = Color.FromArgb(241, 244, 255), Muted = Color.FromArgb(157, 174, 211), Border = Color.FromArgb(38, 58, 112);

        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        public static Image Img(string name)
        {
            lock (cache)
            {
                Image i;
                if (cache.TryGetValue(name, out i)) return i;
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (s == null) return null;
                using (var ms = new MemoryStream()) { s.CopyTo(ms); i = Image.FromStream(new MemoryStream(ms.ToArray())); }
                cache[name] = i; return i;
            }
        }
        public static byte[] Bytes(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) return null;
            using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }
        static Icon icon;
        public static Icon AppIcon
        {
            get
            {
                if (icon != null) return icon;
                var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
                icon = s != null ? new Icon(s) : SystemIcons.Shield;
                return icon;
            }
        }

        public static Font F(float size, FontStyle st = FontStyle.Regular, string family = "Segoe UI") { return new Font(family, size, st, GraphicsUnit.Point); }
        public static Font Emoji(float size) { return new Font("Segoe UI Emoji", size, FontStyle.Regular, GraphicsUnit.Point); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = Math.Max(1, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }

        public static void Hq(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; g.CompositingQuality = CompositingQuality.HighQuality;
        }

        /// <summary>Leuchtender Rahmen (mehrere halbtransparente Striche)</summary>
        public static void Glow(Graphics g, GraphicsPath path, Color c, float width = 2f, int layers = 5)
        {
            for (int i = layers; i >= 1; i--)
                using (var pen = new Pen(Color.FromArgb(18, c), width + i * 3)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, path); }
            using (var pen = new Pen(c, width)) g.DrawPath(pen, path);
        }

        public static void GlowText(Graphics g, string text, Font f, Color c, RectangleF r, StringFormat sf, int glow = 6)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(text, f.FontFamily, (int)f.Style, g.DpiY * f.SizeInPoints / 72f, r, sf);
                for (int i = glow; i >= 1; i--) using (var pen = new Pen(Color.FromArgb(14, c), i * 2.2f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var b = new SolidBrush(Color.White)) g.FillPath(b, path);
                if (c != Color.White) using (var b = new SolidBrush(Color.FromArgb(235, c))) g.FillPath(b, path);
            }
        }

        public static void DrawCover(Graphics g, Image img, Rectangle dest)
        {
            if (img == null) { using (var b = new SolidBrush(Bg)) g.FillRectangle(b, dest); return; }
            float s = Math.Max(dest.Width / (float)img.Width, dest.Height / (float)img.Height);
            float w = img.Width * s, h = img.Height * s;
            g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h);
        }

        public static void StyleForm(Form f, string title)
        {
            f.Text = title; f.BackColor = Bg; f.ForeColor = Text; f.Font = F(10f); f.Icon = AppIcon;
            f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false; f.StartPosition = FormStartPosition.CenterScreen;
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }

        public static Label Lbl(string text, float size = 10f, FontStyle st = FontStyle.Regular, Color? c = null)
        {
            return new Label { Text = text, AutoSize = true, Font = F(size, st), ForeColor = c ?? Text, BackColor = Color.Transparent };
        }

        public static TextBox Box(bool password = false)
        {
            return new TextBox { BackColor = Color.FromArgb(10, 22, 51), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = F(12f), UseSystemPasswordChar = password };
        }
    }

    internal interface IBackdrop { Bitmap Backdrop { get; } }

    /// <summary>Neon-Button wie im Webinterface</summary>
    internal class NeonButton : Control
    {
        public Color C1 = Theme.Cyan, C2 = Theme.Violet;
        public bool Primary, Subtle;
        public string Glyph = "";
        bool hover, down;
        public NeonButton(string text)
        {
            Text = text; Font = Theme.F(12f, FontStyle.Bold); ForeColor = Color.White; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(220, 48); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var bd = Parent as IBackdrop;
            if (bd != null && bd.Backdrop != null) e.Graphics.DrawImage(bd.Backdrop, new Rectangle(0, 0, Width, Height), new Rectangle(Left, Top, Width, Height), GraphicsUnit.Pixel);
            else using (var b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Hq(g);
            var r = new RectangleF(6, 5, Width - 12, Height - 10);
            float rad = Math.Min(14, r.Height / 2);
            using (var path = Theme.Round(r, rad))
            {
                if (Subtle)
                {
                    using (var b = new SolidBrush(Color.FromArgb(hover ? 150 : 110, 16, 26, 59))) g.FillPath(b, path);
                    using (var pen = new Pen(Color.FromArgb(hover || Focused ? 255 : 160, C1), 1.5f)) g.DrawPath(pen, path);
                }
                else
                {
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(Primary ? 255 : 170, C1), Color.FromArgb(Primary ? 255 : 170, C2), 0f))
                    {
                        if (!Primary) { var cb = new ColorBlend { Colors = new[] { Color.FromArgb(200, 40, 20, 110), Color.FromArgb(200, 70, 30, 150) }, Positions = new[] { 0f, 1f } }; b.InterpolationColors = cb; }
                        g.FillPath(b, path);
                    }
                    if (down) using (var b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillPath(b, path);
                    else if (hover) using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillPath(b, path);
                    Theme.Glow(g, path, Primary ? C2 : C1, Focused ? 2.5f : 1.6f, hover ? 6 : 4);
                }
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Color tc = Primary && !Subtle ? Color.FromArgb(12, 11, 38) : ForeColor;
            if (Glyph != "")
            {
                var sz = g.MeasureString(Text, Font);
                using (var ef = Theme.Emoji(Font.Size))
                {
                    var gs = g.MeasureString(Glyph, ef);
                    float gap = Font.Size * 0.35f, total = gs.Width + gap + sz.Width, x = (Width - total) / 2;
                    using (var b = new SolidBrush(tc)) { g.DrawString(Glyph, ef, b, new RectangleF(x, 0, gs.Width, Height), sf); g.DrawString(Text, Font, b, new RectangleF(x + gs.Width + gap - 2, 0, sz.Width + 4, Height), sf); }
                }
            }
            else using (var b = new SolidBrush(tc)) g.DrawString(Text, Font, b, new RectangleF(0, 0, Width, Height), sf);
        }
    }

    /// <summary>Auswahl-Chip (z. B. 15/30/60 Minuten)</summary>
    internal class Chip : CheckBox
    {
        public string Value;
        public Chip(string text, string value)
        {
            Text = text; Value = value; Appearance = Appearance.Button; AutoSize = false; Size = new Size(120, 40); Font = Theme.F(10.5f, FontStyle.Bold);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderColor = Theme.Border; FlatAppearance.CheckedBackColor = Color.FromArgb(0, 109, 152);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 40, 80); BackColor = Color.FromArgb(12, 24, 55); ForeColor = Color.White; TextAlign = ContentAlignment.MiddleCenter; Cursor = Cursors.Hand;
            CheckedChanged += (s, e) => { FlatAppearance.BorderColor = Checked ? Theme.Cyan : Theme.Border; };
        }
    }
}
