using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MSFamilyControl
{
    internal interface ILockScreen
    {
        bool AllowClose { get; set; }
        void SetState(string reason, string until, string child, string requestLine, Color requestColor);
    }

    /// <summary>Aufgelöstes Design für ein Ereignis (vom Server pro Kind konfiguriert)</summary>
    internal class ThemeSpec
    {
        public string Theme = "anime", Event = "", Title = "", Text = "";
        public Color? Accent; public float Scale = 1f; public bool Custom;

        public static ThemeSpec From(Dictionary<string, object> theme, string ev)
        {
            var t = new ThemeSpec { Event = ev };
            if (theme == null) return t;
            var e = theme.Obj("events") != null ? theme.Obj("events").Obj(ev) : null;
            t.Theme = e != null ? e.Str("theme", theme.Str("default", "anime")) : theme.Str("default", "anime");
            if (e != null) { t.Title = e.Str("title"); t.Text = e.Str("text"); t.Custom = e.Bool("custom"); }
            var a = theme.Str("accent");
            if (a.Length == 7 && a[0] == '#') try { t.Accent = ColorTranslator.FromHtml(a); } catch { }
            t.Scale = Math.Max(0.8f, Math.Min(1.6f, theme.Int("scale", 100) / 100f));
            return t;
        }

        public string Fill(string s, string name, string until, string child)
        {
            return (s ?? "").Replace("{name}", name ?? "").Replace("{until}", until ?? "").Replace("{child}", child ?? "");
        }
    }

    /// <summary>
    /// Vollbild-Oberfläche für die Designs Army, Anime-Charaktere, Neon und Family.
    /// Texte, Knöpfe und Status sind echte, per Tastatur bedienbare Elemente – Bilder dienen nur als Illustration.
    /// </summary>
    internal class ThemeScreen : Form, ILockScreen
    {
        class Btn { public string Text, Glyph; public bool Primary, Small; public Action Act; public RectangleF R; public bool Hover; }
        class Pal { public Color Bg1, Bg2, Accent, Accent2, Text, Muted, Box, BoxBorder, BtnText; public string TitleFont = "Segoe UI Black"; public bool Upper, Italic, Light; }

        const float DW = 1600f, DH = 900f;
        readonly ThemeSpec spec;
        readonly Pal pal;
        readonly List<Btn> btns = new List<Btn>();
        int focus;
        readonly bool lockMode, primary;
        public bool AllowClose { get; set; }
        string title, text, info = "", extraLine = "", badge = "", header;
        Color extraColor;
        Image art; bool artContain;
        string glyph = "🔒";
        Bitmap cache;
        readonly Timer keep, anim;
        float fade;
        public Func<bool> DialogOpen;

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        public ThemeScreen(ThemeSpec spec, Screen screen, bool lockMode, bool primary)
        {
            this.spec = spec; this.lockMode = lockMode; this.primary = primary;
            pal = Palette(spec);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds; BackColor = pal.Bg1; KeyPreview = true; Icon = Theme.AppIcon; Text = "MS Family Control";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            keep = new Timer { Interval = 650 };
            keep.Tick += (s, e) => { if (!Visible) return; TopMost = true; if (this.primary && Form.ActiveForm == null && OwnedForms.Length == 0 && (DialogOpen == null || !DialogOpen())) ForceFront(); };
            anim = new Timer { Interval = 16 };
            anim.Tick += (s, e) => { fade = Math.Min(1f, fade + 0.07f); Opacity = 0.35 + 0.65 * fade; if (fade >= 1f) anim.Stop(); };
            Shown += (s, e) => { keep.Start(); if (this.primary) ForceFront(); };
            Resize += (s, e) => Rebuild();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

        public void StartFade(bool animate) { if (animate) { fade = 0; Opacity = 0.35; anim.Start(); } }

        public void ForceFront()
        {
            try { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); Native.SetForegroundWindow(Handle); Native.BringWindowToTop(Handle); } catch { }
            Activate();
        }

        // ------------------------------------------------------------------ Paletten
        static Pal Palette(ThemeSpec s)
        {
            Pal p;
            switch (s.Theme)
            {
                case "army":
                    p = new Pal { Bg1 = Color.FromArgb(21, 25, 30), Bg2 = Color.FromArgb(38, 42, 46), Accent = Color.FromArgb(246, 184, 0), Accent2 = Color.FromArgb(237, 83, 57), Text = Color.FromArgb(244, 241, 232), Muted = Color.FromArgb(190, 186, 176), Box = Color.FromArgb(200, 28, 31, 35), BoxBorder = Color.FromArgb(246, 140, 0), BtnText = Color.FromArgb(25, 20, 10), Upper = true, Italic = true, TitleFont = "Impact" };
                    break;
                case "manga":
                    p = new Pal { Bg1 = Color.FromArgb(18, 10, 42), Bg2 = Color.FromArgb(52, 14, 70), Accent = Color.FromArgb(33, 230, 255), Accent2 = Color.FromArgb(255, 45, 111), Text = Color.White, Muted = Color.FromArgb(214, 200, 240), Box = Color.FromArgb(190, 30, 10, 40), BoxBorder = Color.FromArgb(255, 45, 111), BtnText = Color.White, Upper = true, Italic = true, TitleFont = "Impact" };
                    break;
                case "family":
                    p = new Pal { Bg1 = Color.FromArgb(231, 246, 255), Bg2 = Color.FromArgb(255, 247, 228), Accent = Color.FromArgb(20, 105, 216), Accent2 = Color.FromArgb(34, 186, 148), Text = Color.FromArgb(22, 34, 61), Muted = Color.FromArgb(70, 86, 120), Box = Color.FromArgb(250, 255, 255, 255), BoxBorder = Color.FromArgb(190, 214, 240), BtnText = Color.White, Light = true, TitleFont = "Segoe UI Black" };
                    break;
                default: // neon
                    p = new Pal { Bg1 = Color.FromArgb(7, 18, 34), Bg2 = Color.FromArgb(22, 12, 48), Accent = Color.FromArgb(0, 214, 255), Accent2 = Color.FromArgb(171, 49, 245), Text = Color.FromArgb(234, 246, 255), Muted = Color.FromArgb(160, 185, 215), Box = Color.FromArgb(170, 8, 26, 50), BoxBorder = Color.FromArgb(0, 214, 255), BtnText = Color.FromArgb(5, 15, 30), TitleFont = "Segoe UI Semibold" };
                    break;
            }
            if (s.Accent.HasValue) { p.Accent = s.Accent.Value; p.BoxBorder = s.Accent.Value; }
            return p;
        }

        // ------------------------------------------------------------------ Inhalte
        /// <summary>Konfiguriert den Inhalt je Ereignis</summary>
        public void Setup(string title, string text, string glyph, string header, string artKey)
        {
            this.title = title; this.text = text; this.glyph = glyph; this.header = header;
            art = null; artContain = false;
            if (spec.Theme == "army") art = Theme.Img("army-access.jpg");
            else if (spec.Theme == "manga") art = Theme.Img(artKey == "girl" ? "army-game.jpg" : "army-app.jpg");
            else if (spec.Theme == "family") { art = Theme.Img(artKey == "sleep" ? "sleeping-robot.png" : "awake-robot.png"); artContain = true; }
            Rebuild();
        }
        public void SetInfo(string info) { this.info = info ?? ""; Rebuild(); }
        public void SetBadge(string b) { badge = b ?? ""; Rebuild(); }
        public void AddButton(string text, string glyph, bool primary, Action act, bool small = false) { btns.Add(new Btn { Text = text, Glyph = glyph, Primary = primary, Act = act, Small = small }); Rebuild(); }
        public void ClearButtons() { btns.Clear(); }
        public void SetExtra(string line, Color c) { if (line != extraLine || c != extraColor) { extraLine = line ?? ""; extraColor = c; Invalidate(); } }

        // ILockScreen
        public void SetState(string reason, string until, string child, string requestLine, Color requestColor) { SetExtra(requestLine, requestColor); }

        float S { get { return Math.Min(Width / DW, Height / DH); } }
        float OX { get { return (Width - DW * S) / 2; } }
        float OY { get { return (Height - DH * S) / 2; } }
        RectangleF R(float x, float y, float w, float h) { return new RectangleF(OX + x * S, OY + y * S, w * S, h * S); }
        Font Fnt(float px, FontStyle st, string fam = "Segoe UI") { return new Font(fam, Math.Max(6f, px * S * spec.Scale), st, GraphicsUnit.Pixel); }

        // ------------------------------------------------------------------ Zeichnen
        void Rebuild()
        {
            if (Width < 50 || Height < 50 || title == null) return;
            var old = cache;
            cache = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(cache)) { Theme.Hq(g); DrawStatic(g); }
            if (old != null) old.Dispose();
            Invalidate();
        }

        void DrawStatic(Graphics g)
        {
            var full = new Rectangle(0, 0, Width, Height);
            using (var b = new LinearGradientBrush(full, pal.Bg1, pal.Bg2, 35f)) g.FillRectangle(b, full);
            bool neon = spec.Theme == "neon", fam = spec.Theme == "family", army = spec.Theme == "army";

            // Hintergrund-Deko
            if (fam)
            {
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) { g.FillEllipse(b, R(1150, -120, 600, 420)); g.FillEllipse(b, R(-200, 620, 700, 420)); }
                using (var b = new SolidBrush(Color.FromArgb(40, pal.Accent2))) g.FillEllipse(b, R(1300, 640, 420, 320));
                using (var b = new SolidBrush(Color.FromArgb(28, pal.Accent))) g.FillEllipse(b, R(560, -160, 520, 360));
            }
            else
            {
                using (var pen = new Pen(Color.FromArgb(neon ? 26 : 18, pal.Accent), 1))
                    for (float x = 0; x < DW; x += 40) g.DrawLine(pen, R(x, 0, 0, 0).Location, R(x, DH, 0, 0).Location);
                using (var pen = new Pen(Color.FromArgb(neon ? 18 : 12, pal.Accent), 1))
                    for (float y = 0; y < DH; y += 40) g.DrawLine(pen, R(0, y, 0, 0).Location, R(DW, y, 0, 0).Location);
            }
            if (army)
            {   // Warnstreifen oben/unten
                foreach (var y in new[] { 0f, DH - 26 })
                    using (var clip = new Region(R(0, y, DW, 26)))
                    {
                        g.Clip = clip;
                        using (var b = new SolidBrush(Color.FromArgb(230, 246, 140, 0))) g.FillRectangle(b, R(0, y, DW, 26));
                        using (var b = new SolidBrush(Color.FromArgb(240, 20, 20, 20)))
                            for (float x = -40; x < DW; x += 60) { var pts = new[] { R(x, y + 26, 0, 0).Location, R(x + 26, y, 0, 0).Location, R(x + 50, y, 0, 0).Location, R(x + 24, y + 26, 0, 0).Location }; g.FillPolygon(b, pts); }
                        g.ResetClip();
                    }
            }

            // Bild links
            float rightX = neon ? 0 : 690;
            if (art != null && !neon)
            {
                var area = R(0, 0, 660, DH);
                if (artContain)
                {
                    float sz = Math.Min(area.Width * 0.92f, area.Height * 0.8f);
                    g.DrawImage(art, area.X + (area.Width - sz) / 2, area.Y + area.Height * 0.14f, sz, sz);
                }
                else
                {
                    var st = g.Save();
                    g.SetClip(area);
                    float sc = Math.Max(area.Width / art.Width, area.Height / art.Height);
                    g.DrawImage(art, area.X + (area.Width - art.Width * sc) / 2, area.Y, art.Width * sc, art.Height * sc);
                    g.Restore(st);
                    var fadeR = R(420, 0, 245, DH);
                    using (var b = new LinearGradientBrush(new RectangleF(fadeR.X - 1, fadeR.Y, fadeR.Width + 2, fadeR.Height), Color.FromArgb(0, pal.Bg1), pal.Bg1, 0f)) g.FillRectangle(b, fadeR);
                    using (var pen = new Pen(Color.FromArgb(180, pal.BoxBorder), 3 * S)) g.DrawLine(pen, R(664, 60, 0, 0).Location, R(664, DH - 60, 0, 0).Location);
                }
            }

            var left = new StringFormat { Alignment = neon ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center };
            float colX = neon ? 260 : rightX, colW = neon ? 1080 : DW - rightX - 70;

            // Kopfzeile
            var ico = Theme.Img("icon-128.png");
            float hx = neon ? DW / 2 - 170 : colX;
            if (ico != null) g.DrawImage(ico, R(hx, 38, 64, 64));
            using (var f = Fnt(30, FontStyle.Bold)) using (var b = new SolidBrush(pal.Text)) g.DrawString("MS Family Control", f, b, R(hx + 76, 36, 600, 40).Location);
            using (var f = Fnt(15, FontStyle.Bold)) using (var b = new SolidBrush(pal.Accent)) g.DrawString((header ?? "").ToUpperInvariant(), f, b, R(hx + 78, 76, 600, 26).Location);

            // Neon: großes Symbol
            float ty = 150;
            if (neon)
            {
                var c = R(DW / 2 - 95, 118, 190, 190);
                for (int i = 6; i >= 1; i--) using (var pen = new Pen(Color.FromArgb(16, pal.Accent2), i * 6 * S)) g.DrawEllipse(pen, c);
                using (var pen = new Pen(pal.Accent, 5 * S)) g.DrawEllipse(pen, c);
                using (var f = Theme.Emoji(78 * S)) using (var b = new SolidBrush(pal.Accent)) g.DrawString(glyph, f, b, c, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                ty = 386;
            }
            if (badge != "")
            {
                var br = R(neon ? DW / 2 - 110 : colX, neon ? 322 : ty - 46, 220, 40);
                using (var p = Theme.Round(br, 20 * S)) using (var b = new SolidBrush(Color.FromArgb(235, 220, 38, 70))) g.FillPath(b, p);
                using (var f = Fnt(20, FontStyle.Bold)) g.DrawString(badge, f, Brushes.White, br, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }

            // Titel (automatisch verkleinert, letztes Wort in Akzentfarbe)
            string t = pal.Upper ? (title ?? "").ToUpperInvariant() : (title ?? "");
            float titleH = neon ? 136 : 270;
            float size = neon ? 76 : 104;
            Font tf = null; var tr = R(colX, ty, colW, titleH);
            var tsf = new StringFormat(left) { LineAlignment = StringAlignment.Far, Trimming = StringTrimming.Word };
            var style = FontStyle.Bold | (pal.Italic ? FontStyle.Italic : FontStyle.Regular);
            for (int i = 0; i < 14; i++)
            {
                if (tf != null) tf.Dispose();
                tf = Fnt(size, style, pal.TitleFont);
                var m = g.MeasureString(t, tf, (int)tr.Width, tsf);
                if (m.Height <= tr.Height && LongestWord(g, t, tf) <= tr.Width) break;
                size *= 0.9f;
            }
            DrawTitle(g, t, tf, tr, tsf);
            tf.Dispose();

            // Infobox
            var box = neon ? R(330, 530, 940, 150) : R(colX, 450, colW, 160);
            using (var p = Theme.Round(box, (pal.Light ? 28 : 18) * S))
            {
                using (var b = new SolidBrush(pal.Box)) g.FillPath(b, p);
                if (pal.Light) using (var pen = new Pen(pal.BoxBorder, 2 * S)) g.DrawPath(pen, p);
                else Theme.Glow(g, p, pal.BoxBorder, 2.5f * S, 4);
            }
            float tx = box.X + 30 * S;
            if (!neon)
            {
                var ic = new RectangleF(box.X + 22 * S, box.Y + (box.Height - 86 * S) / 2, 86 * S, 86 * S);
                using (var f = Theme.Emoji(46 * S)) using (var b = new SolidBrush(pal.Light ? pal.Accent : pal.Accent)) g.DrawString(glyph, f, b, ic, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                tx = ic.Right + 18 * S;
            }
            var textR = new RectangleF(tx, box.Y + 14 * S, box.Right - tx - 26 * S, box.Height - 28 * S);
            if (info != "")
            {
                using (var f = Fnt(19, FontStyle.Bold)) using (var b = new SolidBrush(pal.Accent)) g.DrawString(info, f, b, new RectangleF(textR.X, textR.Y, textR.Width, 30 * S), new StringFormat(left) { LineAlignment = StringAlignment.Near });
                textR = new RectangleF(textR.X, textR.Y + 32 * S, textR.Width, textR.Height - 32 * S);
            }
            float ts = 32; Font bf = Fnt(ts, FontStyle.Regular);
            while (ts > 14 && g.MeasureString(text ?? "", bf, (int)textR.Width).Height > textR.Height) { bf.Dispose(); ts -= 1.5f; bf = Fnt(ts, FontStyle.Regular); }
            using (var b = new SolidBrush(pal.Text)) g.DrawString(text ?? "", bf, b, textR, new StringFormat(left) { Trimming = StringTrimming.EllipsisWord });
            bf.Dispose();

            // Knöpfe
            var big = btns.FindAll(x => !x.Small); var small = btns.FindAll(x => x.Small);
            float by = neon ? 720 : 655, bw = neon ? 400 : (colW - 24) / Math.Max(1, big.Count);
            float bx = neon ? DW / 2 - (big.Count * 400 + (big.Count - 1) * 24) / 2f : colX;
            for (int i = 0; i < big.Count; i++) big[i].R = new RectangleF(bx + i * (bw + 24), by, bw, 88);
            float sw = 210, sx = neon ? DW / 2 - (small.Count * sw + (small.Count - 1) * 16) / 2f : colX;
            for (int i = 0; i < small.Count; i++) small[i].R = new RectangleF(sx + i * (sw + 16), by + 104, sw, 52);

            // Fuß
            using (var f = Fnt(17, FontStyle.Regular)) using (var b = new SolidBrush(pal.Muted))
                g.DrawString(lockMode || spec.Event != "parent_message" ? "🛡  Nur Eltern können die Sperre aufheben." : "🛡  Nachrichten von deinen Eltern werden immer angezeigt.", f, b, R(neon ? 0 : colX, DH - 66, neon ? DW : colW, 30), new StringFormat(left));
        }

        float LongestWord(Graphics g, string t, Font f)
        {
            float m = 0; foreach (var w in t.Split(' ')) m = Math.Max(m, g.MeasureString(w, f).Width); return m;
        }

        void DrawTitle(Graphics g, string t, Font f, RectangleF r, StringFormat sf)
        {
            // letztes Wort hervorheben: Text in zwei Läufen zeichnen (Grundfarbe, dann Akzent über das letzte Wort per Pfad-Clip)
            using (var path = new GraphicsPath())
            {
                path.AddString(t, f.FontFamily, (int)f.Style, f.Size, r, sf);
                if (!pal.Light) for (int i = 5; i >= 1; i--) using (var pen = new Pen(Color.FromArgb(14, pal.Accent), i * 2.4f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var b = new SolidBrush(pal.Text)) g.FillPath(b, path);
                int idx = t.TrimEnd().LastIndexOf(' ');
                if (idx > 0)
                {
                    var ranges = new[] { new CharacterRange(idx + 1, t.Length - idx - 1) };
                    var sf2 = new StringFormat(sf); sf2.SetMeasurableCharacterRanges(ranges);
                    var regs = g.MeasureCharacterRanges(t, f, r, sf2);
                    if (regs.Length > 0)
                    {
                        var st = g.Save(); g.SetClip(regs[0], CombineMode.Replace);
                        var hc = spec.Theme == "army" || spec.Theme == "manga" ? pal.Accent2 : pal.Accent;
                        using (var b = new SolidBrush(hc)) g.FillPath(b, path);
                        g.Restore(st);
                    }
                }
                if (pal.Upper) using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.5f)) g.DrawPath(pen, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (cache != null) g.DrawImageUnscaled(cache, 0, 0);
            Theme.Hq(g);
            for (int i = 0; i < btns.Count; i++) DrawBtn(g, btns[i], i == focus);
            if (extraLine != "")
            {
                bool neon = spec.Theme == "neon";
                using (var f = Fnt(20, FontStyle.Bold)) using (var b = new SolidBrush(extraColor))
                    g.DrawString(extraLine, f, b, R(neon ? 0 : 690, 612, neon ? DW : DW - 760, 36), new StringFormat { Alignment = neon ? StringAlignment.Center : StringAlignment.Near, LineAlignment = StringAlignment.Center });
            }
            if (lockMode) using (var f = Fnt(15, FontStyle.Regular)) using (var b = new SolidBrush(pal.Muted)) g.DrawString(DateTime.Now.ToString("HH:mm"), f, b, R(24, DH - 50, 100, 30).Location);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        void DrawBtn(Graphics g, Btn b, bool focused)
        {
            var r = R(b.R.X, b.R.Y, b.R.Width, b.R.Height);
            GraphicsPath path;
            if (spec.Theme == "army" || spec.Theme == "manga")
            {   // abgeschrägte Ecken
                float c = 18 * S; path = new GraphicsPath();
                path.AddPolygon(new[] { new PointF(r.X + c, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, r.Y + c) });
            }
            else path = Theme.Round(r, (spec.Theme == "family" ? r.Height / 2 : 18 * S));
            using (path)
            {
                Color fill = b.Primary ? pal.Accent : (pal.Light ? Color.White : Color.FromArgb(150, 12, 18, 30));
                if (spec.Theme == "manga" && b.Primary) fill = pal.Accent2;
                if (b.Hover) fill = ControlPaint.Light(fill, 0.15f);
                using (var br = new SolidBrush(fill)) g.FillPath(br, path);
                if (!b.Primary || focused)
                {
                    var bc = b.Primary ? Color.White : pal.Accent;
                    if (pal.Light) using (var pen = new Pen(bc, (focused ? 3.5f : 2f) * S)) g.DrawPath(pen, path);
                    else Theme.Glow(g, path, bc, (focused ? 3f : 2f) * S, focused ? 5 : 3);
                }
                var tc = b.Primary ? (spec.Theme == "manga" ? Color.White : pal.BtnText) : (pal.Light ? pal.Accent : pal.Text);
                string label = pal.Upper ? b.Text.ToUpperInvariant() : b.Text;
                using (var f = Fnt(b.Small ? 19 : 30, FontStyle.Bold))
                using (var ef = Theme.Emoji((b.Small ? 17 : 26) * S * spec.Scale))
                using (var br = new SolidBrush(tc))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                    if (string.IsNullOrEmpty(b.Glyph)) g.DrawString(label, f, br, r, sf);
                    else
                    {
                        var gw = g.MeasureString(b.Glyph, ef).Width; var tw = Math.Min(r.Width - gw - 30 * S, g.MeasureString(label, f).Width);
                        float x = r.X + (r.Width - gw - tw - 8 * S) / 2;
                        g.DrawString(b.Glyph, ef, br, new RectangleF(x, r.Y, gw, r.Height), sf);
                        g.DrawString(label, f, br, new RectangleF(x + gw + 8 * S, r.Y, tw + 4, r.Height), sf);
                    }
                }
            }
        }

        Btn Hit(Point p) { foreach (var b in btns) if (R(b.R.X, b.R.Y, b.R.Width, b.R.Height).Contains(p)) return b; return null; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = Hit(e.Location); bool ch = false;
            foreach (var b in btns) { bool v = b == h; if (b.Hover != v) { b.Hover = v; ch = true; } }
            Cursor = h != null ? Cursors.Hand : Cursors.Default; if (ch) Invalidate();
        }
        protected override void OnMouseClick(MouseEventArgs e) { var h = Hit(e.Location); if (h != null) h.Act(); }
        protected override bool ProcessDialogKey(Keys k)
        {
            if (btns.Count == 0) return base.ProcessDialogKey(k);
            if (k == Keys.Tab || k == Keys.Right || k == Keys.Down) { focus = (focus + 1) % btns.Count; Invalidate(); return true; }
            if (k == (Keys.Tab | Keys.Shift) || k == Keys.Left || k == Keys.Up) { focus = (focus - 1 + btns.Count) % btns.Count; Invalidate(); return true; }
            if (k == Keys.Enter || k == Keys.Space) { btns[focus].Act(); return true; }
            if (k == Keys.Escape && !lockMode) { var s = btns.Find(x => !x.Primary); if (s != null) s.Act(); return true; }
            if (k == (Keys.Alt | Keys.F4)) return true;
            return base.ProcessDialogKey(k);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (lockMode && !AllowClose && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; return; }
            keep.Stop(); anim.Stop(); base.OnFormClosing(e);
        }
        protected override void Dispose(bool d) { if (d) { keep.Dispose(); anim.Dispose(); if (cache != null) cache.Dispose(); } base.Dispose(d); }
        public void CloseScreen() { AllowClose = true; Close(); }
    }
}
