using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MSFamilyControl
{
    /// <summary>
    /// Vollbild-Hinweise nach Marcs Bildvorlagen (1672×941): „App gesperrt“, „Spiel gesperrt“, „Nachricht von deinen Eltern“.
    /// Die Knöpfe sind Teil des Bildes; darüber liegen klickbare Bereiche. Veränderliche Texte werden zur Laufzeit geschrieben.
    /// </summary>
    internal class OverlayForm : Form
    {
        const float IW = 1672f, IH = 941f;
        class Hot { public RectangleF R; public Action Act; public bool Primary; public bool Hover; }
        readonly Image img;
        readonly List<Hot> hots = new List<Hot>();
        readonly Action<Graphics, Func<float, float>, Func<float, float, PointF>> painter;
        readonly Timer keep;
        public string Kind;
        public bool Closing;

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        OverlayForm(string image, string kind, Action<Graphics, Func<float, float>, Func<float, float, PointF>> painter)
        {
            img = Theme.Img(image); Kind = kind; this.painter = painter;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds; BackColor = Color.FromArgb(4, 10, 26); KeyPreview = true; Icon = Theme.AppIcon; Text = "MS Family Control";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            keep = new Timer { Interval = 700 };
            keep.Tick += (s, e) => { if (!Visible) return; TopMost = true; if (Form.ActiveForm == null && OwnedForms.Length == 0) ForceFront(); };
            Shown += (s, e) => { ForceFront(); keep.Start(); };
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

        /// <summary>Holt das Fenster auch über ein laufendes (Vollbild-)Spiel nach vorne</summary>
        public void ForceFront()
        {
            try
            {
                keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); // Alt antippen hebt die Vordergrund-Sperre auf
                Native.SetForegroundWindow(Handle); Native.BringWindowToTop(Handle);
            }
            catch { }
            Activate();
        }

        float S { get { return Math.Max(Width / IW, Height / IH); } }
        float OX { get { return (Width - IW * S) / 2; } }
        float OY { get { return (Height - IH * S) / 2; } }
        RectangleF Map(RectangleF r) { return new RectangleF(OX + r.X * S, OY + r.Y * S, r.Width * S, r.Height * S); }

        void AddHot(float x0, float y0, float x1, float y1, bool primary, Action act) { hots.Add(new Hot { R = new RectangleF(x0, y0, x1 - x0, y1 - y0), Act = act, Primary = primary }); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Hq(g);
            g.Clear(BackColor);
            if (img != null) g.DrawImage(img, OX, OY, IW * S, IH * S);
            if (painter != null) painter(g, v => v * S, (x, y) => new PointF(OX + x * S, OY + y * S));
            foreach (var h in hots)
                if (h.Hover)
                    using (var p = Theme.Round(Map(h.R), 30 * S)) using (var b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(b, p);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool any = false;
            foreach (var h in hots) { bool o = Map(h.R).Contains(e.Location); if (o != h.Hover) { h.Hover = o; Invalidate(); } any |= o; }
            Cursor = any ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            foreach (var h in hots) if (Map(h.R).Contains(e.Location)) { h.Act(); return; }
            base.OnMouseClick(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { var p = hots.Find(h => h.Primary); if (p != null) p.Act(); }
            else if (e.KeyCode == Keys.Escape) { var p = hots.Find(h => !h.Primary); if (p != null) p.Act(); }
            if (e.Alt && e.KeyCode == Keys.F4) e.Handled = true;
            base.OnKeyDown(e);
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { keep.Stop(); base.OnFormClosing(e); }
        protected override void Dispose(bool d) { if (d) keep.Dispose(); base.Dispose(d); }

        public void CloseOverlay() { Closing = true; Close(); }

        static Font Px(float px, FontStyle st = FontStyle.Bold, string fam = "Segoe UI") { return new Font(fam, Math.Max(6f, px), st, GraphicsUnit.Pixel); }

        static void Glow(Graphics g, string text, Font f, Color c, RectangleF r, StringFormat sf)
        {
            using (var b = new SolidBrush(Color.FromArgb(70, c))) for (int i = 1; i <= 2; i++) { var rr = r; rr.Offset(0, 0); rr.Inflate(i, i); g.DrawString(text, f, b, rr, sf); }
            using (var b = new SolidBrush(c)) g.DrawString(text, f, b, r, sf);
        }

        /// <summary>„Diese App / Dieses Spiel ist nicht erlaubt“</summary>
        public static OverlayForm Blocked(string name, bool game, Action ask)
        {
            string label = name ?? "";
            OverlayForm f = null;
            f = new OverlayForm(game ? "game-blocked-template.jpg" : "app-blocked-template.jpg", "blocked", (g, sc, pt) =>
            {
                if (game)
                {
                    var p = pt(845, 524);
                    var r = new RectangleF(p.X, p.Y, sc(410), sc(82));
                    using (var fnt = Px(sc(25), FontStyle.Regular))
                    {
                        string t = "„" + label + "“ wurde durch deine Familienregeln blockiert.";
                        var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
                        Glow(g, t, fnt, Color.FromArgb(120, 240, 255), r, sf);
                    }
                }
                else
                {
                    var p = pt(735, 536);
                    using (var fnt = Px(sc(30)))
                        Glow(g, "„" + label + "“", fnt, Color.FromArgb(0, 233, 255), new RectangleF(p.X, p.Y, sc(500), sc(44)), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
            });
            if (game) { f.AddHot(365, 735, 890, 832, true, () => { f.CloseOverlay(); if (ask != null) ask(); }); f.AddHot(932, 740, 1306, 828, false, () => f.CloseOverlay()); }
            else { f.AddHot(390, 750, 900, 845, true, () => { f.CloseOverlay(); if (ask != null) ask(); }); f.AddHot(938, 750, 1288, 845, false, () => f.CloseOverlay()); }
            try { SystemSounds.Hand.Play(); } catch { }
            return f;
        }

        /// <summary>„Nachricht von deinen Eltern“ – erscheint auch über Spielen und dem Sperrbildschirm</summary>
        public static OverlayForm Message(string from, DateTime at, string text, Action read, Action later)
        {
            OverlayForm f = null;
            f = new OverlayForm("message-template.jpg", "message", (g, sc, pt) =>
            {
                var p = pt(745, 502);
                using (var fnt = Px(sc(25)))
                    g.DrawString("Von: " + from, fnt, Brushes.White, new RectangleF(p.X, p.Y, sc(330), sc(38)), new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                var pTime = pt(1104, 502);
                string when = (at.Date == DateTime.Today ? "Heute" : at.ToString("dd.MM.")) + " · " + at.ToString("HH:mm") + " Uhr";
                using (var fnt = Px(sc(19), FontStyle.Regular))
                using (var b = new SolidBrush(Color.FromArgb(225, 235, 250)))
                    g.DrawString(when, fnt, b, new RectangleF(pTime.X, pTime.Y, sc(174), sc(38)), new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap });
                var pm = pt(688, 558);
                var rect = new RectangleF(pm.X, pm.Y, sc(575), sc(78));
                float size = 30;
                Font tf = Px(sc(size), FontStyle.Regular);
                var sfm = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
                while (size > 15 && g.MeasureString(text, tf, (int)rect.Width).Height > rect.Height) { tf.Dispose(); size -= 1.5f; tf = Px(sc(size), FontStyle.Regular); }
                g.DrawString(text, tf, Brushes.White, rect, sfm);
                tf.Dispose();
            });
            f.AddHot(392, 735, 868, 828, true, () => { f.CloseOverlay(); if (read != null) read(); });
            f.AddHot(918, 742, 1283, 820, false, () => { f.CloseOverlay(); if (later != null) later(); });
            try { SystemSounds.Asterisk.Play(); } catch { }
            return f;
        }
    }
}
