using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MSFamilyControl
{
    /// <summary>
    /// Vollbild-Sperre: „Ätsch Bätsch – Zeit is um!“, „Zeit fürs Bett“, „Pause!“ …
    /// Herunterfahren, Abmelden und „Mehr Zeit anfragen“ bleiben immer möglich. Strg+Alt+Entf wird nicht blockiert.
    /// </summary>
    internal class LockForm : Form, IBackdrop, ILockScreen
    {
        public bool AllowClose { get; set; }
        string customTitle = "", customText = "";
        public void SetCustom(string title, string text) { customTitle = title ?? ""; customText = text ?? ""; Rebuild(); }
        readonly bool primary;
        readonly Agent agent;
        Bitmap backdrop;
        string reason = "time", until = "", requestLine = "", child = "";
        Color requestColor = Theme.Muted;
        readonly Timer keepFront;
        NeonButton btnRequest, btnShutdown, btnLogoff, btnParent;
        static readonly Random Rnd = new Random();
        string funLine;
        static readonly string[] FunTime = {
            "Deine PC-Zeit für heute ist leider aufgebraucht.",
            "Game over – zumindest für heute! 🎮",
            "Der Bildschirm braucht jetzt auch mal Pause.",
            "Zeit für echte Abenteuer da draußen! 🌳",
            "Akku leer – deiner, nicht der vom PC. 🔋",
        };

        public Bitmap Backdrop { get { return backdrop; } }

        public LockForm(Agent agent, Screen screen, bool primary)
        {
            this.agent = agent; this.primary = primary;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds; BackColor = Theme.Bg; Icon = Theme.AppIcon; Text = "MS Family Control – gesperrt"; KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            funLine = FunTime[Rnd.Next(FunTime.Length)];
            if (primary)
            {
                btnRequest = new NeonButton("Mehr Zeit anfragen") { Glyph = "🙋 ", Primary = true, C1 = Theme.Cyan, C2 = Theme.Pink };
                btnShutdown = new NeonButton("Herunterfahren") { Glyph = "⏻ ", C1 = Theme.Violet };
                btnLogoff = new NeonButton("Abmelden") { Subtle = true, C1 = Theme.Cyan, Font = Theme.F(11f, FontStyle.Bold) };
                btnParent = new NeonButton("Eltern") { Glyph = "🔑 ", Subtle = true, C1 = Theme.Gold, Font = Theme.F(10f, FontStyle.Bold) };
                btnRequest.Click += (s, e) => agent.ShowRequestDialog(this);
                btnShutdown.Click += (s, e) => agent.Shutdown(this);
                btnLogoff.Click += (s, e) => agent.Logoff(this);
                btnParent.Click += (s, e) => agent.ParentDialog(this);
                Controls.AddRange(new Control[] { btnRequest, btnShutdown, btnLogoff, btnParent });
            }
            keepFront = new Timer { Interval = 600 };
            keepFront.Tick += (s, e) => KeepInFront();
            keepFront.Start();
            Resize += (s, e) => Rebuild();
            Rebuild();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/; return cp; }
        }

        void KeepInFront()
        {
            if (!Visible) return;
            if (agent != null && agent.DialogOpen) return;
            TopMost = true;
            if (primary && Form.ActiveForm == null)
            {
                Activate(); try { Native.SetForegroundWindow(Handle); } catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason != CloseReason.WindowsShutDown) { e.Cancel = true; return; }
            keepFront.Stop();
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.F4) e.Handled = true;
            base.OnKeyDown(e);
        }

        public void SetState(string reason, string until, string child, string requestLine, Color requestColor)
        {
            bool rebuild = reason != this.reason || until != this.until || child != this.child;
            this.reason = reason; this.until = until; this.child = child;
            if (this.requestLine != requestLine || this.requestColor != requestColor) { this.requestLine = requestLine; this.requestColor = requestColor; Invalidate(); }
            if (btnRequest != null) btnRequest.Text = reason == "bedtime" || reason == "early" || reason == "school" || reason == "manual" ? "Ausnahme anfragen" : "Mehr Zeit anfragen";
            if (rebuild) Rebuild();
        }

        float S { get { return Math.Min(Width / 1672f, Height / 941f); } }

        void Rebuild()
        {
            if (Width < 50 || Height < 50) return;
            float s = S;
            var old = backdrop;
            backdrop = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(backdrop))
            {
                Theme.Hq(g);
                bool night = reason == "bedtime";
                Theme.DrawCover(g, Theme.Img(night ? "bed-scenery.jpg" : "lock-scenery.jpg"), ClientRectangle);
                using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(b, ClientRectangle);
                var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

                // Kopfzeile
                var ico = Theme.AppIcon.ToBitmap();
                float hx = Width / 2f - 230 * s, hy = 22 * s;
                g.DrawImage(Theme.Img("icon-128.png") ?? ico, hx, hy, 74 * s, 74 * s);
                using (var f1 = Theme.F(30 * s, FontStyle.Bold)) using (var br = new SolidBrush(Color.White)) g.DrawString("MS Family Control", f1, br, hx + 88 * s, hy + 2 * s);
                using (var f2 = Theme.F(14 * s)) using (var br = new SolidBrush(Color.FromArgb(220, 230, 255))) g.DrawString("Familien-PC-Schutz & Bildschirmzeit", f2, br, hx + 92 * s, hy + 50 * s);
                using (var pen = new Pen(Color.FromArgb(150, Theme.Cyan), 2 * s)) { g.DrawLine(pen, Width / 2f - 640 * s, 72 * s, hx - 30 * s, 72 * s); g.DrawLine(pen, hx + 520 * s, 72 * s, Width / 2f + 640 * s, 72 * s); }

                // Titel
                string t1, t2, emo, sub, cardTitle, cardLine, hint;
                Image robot = Theme.Img(night ? "sleeping-robot.png" : "awake-robot.png");
                switch (reason)
                {
                    case "bedtime": t1 = "Zeit fürs "; t2 = "Bett"; emo = "😴"; sub = "Die Schlafenszeit hat begonnen – der PC wird jetzt gesperrt."; cardTitle = "Gute Nacht" + (child != "" ? ", " + child : "") + "!"; cardLine = "Bettzeit seit " + until + " Uhr"; hint = "🌙 Gute Nacht und bis morgen!"; break;
                    case "early": t1 = "Guten Morgen! "; t2 = "Noch nicht"; emo = "☀"; sub = "Der PC ist heute erst ab " + until + " Uhr freigegeben."; cardTitle = "PC gesperrt"; cardLine = "Freigabe ab " + until + " Uhr"; hint = "Bis gleich – frag deine Eltern, wenn es dringend ist."; break;
                    case "school": t1 = "Ab in die "; t2 = "Schule!"; emo = "📚"; sub = "Während der Unterrichtszeit ist der PC gesperrt."; cardTitle = "Schulzeit"; cardLine = "PC frei ab " + until + " Uhr"; hint = "Für Hausaufgaben oder Lernen kannst du eine Ausnahme anfragen."; break;
                    case "manual": t1 = "Pause! "; t2 = "PC gesperrt"; emo = "✋"; sub = "Deine Eltern haben den PC gerade gesperrt."; cardTitle = "PC gesperrt"; cardLine = until != "" ? "Gesperrt bis " + until + " Uhr" : "Von deinen Eltern gesperrt"; hint = "Wenn du etwas Wichtiges machen musst, frag nach einer Ausnahme."; break;
                    default: t1 = "Ätsch Bätsch – "; t2 = "Zeit is um!"; emo = "🤣"; sub = funLine; cardTitle = "PC gesperrt"; cardLine = "Tägliche Bildschirmzeit erreicht"; hint = "Morgen geht's weiter – frag deine Eltern, wenn du mehr Zeit brauchst."; break;
                }
                if (customTitle != "")
                {
                    var tt = customTitle.Trim(); int sp = tt.LastIndexOf(' ');
                    if (sp > 0) { t1 = tt.Substring(0, sp + 1); t2 = tt.Substring(sp + 1); } else { t1 = ""; t2 = tt; }
                    emo = "";
                }
                if (customText != "") sub = customText;
                {
                    var meas = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
                    float fsz = 64 * s;
                    Font ft = null, fe = null; SizeF w1 = SizeF.Empty, w2 = SizeF.Empty, we = SizeF.Empty; float total = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        if (ft != null) { ft.Dispose(); fe.Dispose(); }
                        ft = Theme.F(fsz, FontStyle.Bold | FontStyle.Italic, "Segoe UI Black"); fe = Theme.Emoji(fsz * 0.8f);
                        w1 = g.MeasureString(t1, ft, 4000, meas); w2 = g.MeasureString(t2, ft, 4000, meas); we = g.MeasureString(emo, fe);
                        total = w1.Width + w2.Width + we.Width + 14 * s;
                        if (total <= Width * 0.94f) break;
                        fsz *= 0.9f;
                    }
                    float x = (Width - total) / 2, y = 108 * s + (64 * s - fsz) * 0.6f;
                    Theme.GlowText(g, t1, ft, Color.White, new RectangleF(x, y, w1.Width + 60 * s, w1.Height * 1.3f), meas, 4);
                    Theme.GlowText(g, t2, ft, Theme.Cyan, new RectangleF(x + w1.Width, y, w2.Width + 60 * s, w2.Height * 1.3f), meas, 8);
                    using (var b = new SolidBrush(Theme.Gold)) g.DrawString(emo, fe, b, x + w1.Width + w2.Width + 14 * s, y + 6 * s);
                    ft.Dispose(); fe.Dispose();
                }
                using (var fs = Theme.F(21 * s)) using (var br = new SolidBrush(Color.White)) g.DrawString(sub, fs, br, new RectangleF(0, 210 * s, Width, 40 * s), center);

                // Karte
                var card = new RectangleF(Width / 2f - 450 * s, 438 * s, 900 * s, 222 * s);
                if (robot != null)
                {
                    float rs = 300 * s;
                    if (night) g.DrawImage(robot, card.X + 10 * s, card.Y - 40 * s, rs * .95f, rs * .95f);
                    else g.DrawImage(robot, Width / 2f - rs / 2, 238 * s, rs, rs);
                }
                using (var path = Theme.Round(card, 34 * s))
                {
                    using (var b = new LinearGradientBrush(card, Color.FromArgb(night ? 120 : 190, 10, 40, 70), Color.FromArgb(night ? 120 : 190, 30, 20, 70), 0f)) g.FillPath(b, path);
                    Theme.Glow(g, path, Theme.Cyan, 2.5f * s, 6);
                }
                if (night && robot != null) g.DrawImage(robot, card.X + 10 * s, card.Y - 40 * s, 285 * s, 285 * s);
                float lx = night ? card.X + 330 * s : card.X + 200 * s;
                if (!night)
                {
                    var circ = new RectangleF(card.X + 60 * s, card.Y + 36 * s, 150 * s, 150 * s);
                    using (var pen = new Pen(Color.FromArgb(200, Theme.Cyan), 12 * s)) g.DrawArc(pen, circ, -60, 300);
                    using (var pen = new Pen(Color.FromArgb(60, Theme.Cyan), 12 * s)) g.DrawArc(pen, circ, 240, 60);
                    using (var fl = Theme.Emoji(48 * s)) using (var b = new SolidBrush(Theme.Cyan)) g.DrawString("🔒", fl, b, circ, center);
                    using (var pen = new Pen(Color.FromArgb(120, Theme.Cyan), 2 * s)) g.DrawLine(pen, card.X + 255 * s, card.Y + 40 * s, card.X + 255 * s, card.Bottom - 40 * s);
                    lx = card.X + 300 * s;
                }
                {
                    float cs = 48 * s, avail = card.Right - lx - 30 * s;
                    var nowrap = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
                    var fc = Theme.F(cs, FontStyle.Bold);
                    while (g.MeasureString(cardTitle, fc, 4000, nowrap).Width > avail && cs > 18 * s) { fc.Dispose(); cs *= 0.9f; fc = Theme.F(cs, FontStyle.Bold); }
                    Theme.GlowText(g, cardTitle, fc, Color.White, new RectangleF(lx, card.Y + 36 * s + (48 * s - cs) * 0.5f, avail + 20 * s, 90 * s), nowrap, 4);
                    fc.Dispose();
                }
                using (var fl = Theme.F(22 * s)) using (var b = new SolidBrush(Theme.Cyan)) g.DrawString("📅  " + cardLine, fl, b, lx, card.Y + 128 * s);

                // Hinweis
                using (var fh = Theme.F(17 * s)) using (var b = new SolidBrush(Color.FromArgb(230, 236, 255))) g.DrawString(hint, fh, b, new RectangleF(0, 684 * s, Width, 34 * s), center);
                // Fuß
                using (var ff = Theme.F(14 * s)) using (var b = new SolidBrush(Color.FromArgb(220, 230, 255))) g.DrawString("🛡  Nur Eltern können die Sperre aufheben.", ff, b, new RectangleF(0, 885 * s, Width, 30 * s), center);
            }
            if (old != null) old.Dispose();
            if (primary)
            {
                int bw = (int)(330 * s), bh = (int)(78 * s), y = (int)(752 * s);
                btnRequest.Font = Theme.F(19 * s, FontStyle.Bold); btnShutdown.Font = Theme.F(19 * s, FontStyle.Bold);
                btnLogoff.Font = Theme.F(13 * s, FontStyle.Bold); btnParent.Font = Theme.F(12 * s, FontStyle.Bold);
                btnRequest.Bounds = new Rectangle(Width / 2 - bw - (int)(14 * s), y, bw, bh);
                btnShutdown.Bounds = new Rectangle(Width / 2 + (int)(14 * s), y, bw, bh);
                btnLogoff.Bounds = new Rectangle(Width / 2 - (int)(90 * s), y + bh + (int)(4 * s), (int)(180 * s), (int)(46 * s));
                btnParent.Bounds = new Rectangle(Width - (int)(170 * s), Height - (int)(64 * s), (int)(150 * s), (int)(46 * s));
                foreach (Control c in Controls) c.Invalidate();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (backdrop != null) g.DrawImageUnscaled(backdrop, 0, 0);
            Theme.Hq(g);
            float s = S;
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            if (requestLine != "")
                using (var f = Theme.F(16 * s, FontStyle.Bold)) using (var b = new SolidBrush(requestColor)) g.DrawString(requestLine, f, b, new RectangleF(0, 715 * s, Width, 32 * s), center);
            using (var f = Theme.F(13 * s)) using (var b = new SolidBrush(Color.FromArgb(170, 200, 220, 255))) g.DrawString(DateTime.Now.ToString("HH:mm"), f, b, 24 * s, Height - 44 * s);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { keepFront.Dispose(); if (backdrop != null) backdrop.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
