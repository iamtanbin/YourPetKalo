using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CatPet
{
    // Mood journal: how Abu feels right now and what his last 7 days looked like.
    class JournalForm : Form
    {
        readonly Store st;
        readonly Timer refresh = new Timer();
        static readonly Color Bg = Color.FromArgb(250, 245, 236);
        static readonly Color Ink = Color.FromArgb(40, 34, 30);
        static readonly Color Muted = Color.FromArgb(130, 120, 110);
        static readonly Color Track = Color.FromArgb(232, 222, 205);

        public JournalForm(Store st)
        {
            this.st = st;
            Text = st.Name + "'s journal";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 450);
            BackColor = Bg;
            DoubleBuffered = true;
            TopMost = true;
            refresh.Interval = 2000;
            refresh.Tick += delegate { Invalidate(); };
            refresh.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refresh.Stop();
            refresh.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (Font title = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Font head = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (Font body = new Font("Segoe UI", 9f))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush muted = new SolidBrush(Muted))
            {
                int x = 18, y = 14, w = ClientSize.Width - 36;
                g.DrawString(st.Name, title, ink, x, y);
                y += 28;
                g.DrawString(Mood(), body, muted, x, y);
                y += 30;

                g.DrawString("How he feels", head, ink, x, y);
                y += 24;
                Bar(g, body, ink, "Full tummy", 100 - st.Hunger, Color.FromArgb(232, 150, 60), x, y, w); y += 26;
                Bar(g, body, ink, "Energy", st.Energy, Color.FromArgb(90, 160, 220), x, y, w); y += 26;
                Bar(g, body, ink, "Fun", st.Fun, Color.FromArgb(120, 190, 110), x, y, w); y += 26;
                Bar(g, body, ink, "Love", st.Affection, Color.FromArgb(240, 110, 140), x, y, w); y += 26;
                int lv = st.Level;
                double bond = 100;
                if (lv < Store.LevelXp.Length)
                {
                    int from = Store.LevelXp[lv - 1], to = Store.LevelXp[lv];
                    bond = 100.0 * (st.Xp - from) / (to - from);
                }
                Bar(g, body, ink, "Bond Lv " + lv, bond, Color.FromArgb(160, 110, 210), x, y, w); y += 36;

                g.DrawString("Last 7 days", head, ink, x, y);
                y += 24;
                int col0 = x, colW = (w - 70) / Store.Kinds;
                for (int k = 0; k < Store.Kinds; k++)
                    g.DrawString(Store.KindNames[k], body, muted, col0 + 70 + k * colW, y);
                y += 20;
                for (int i = 0; i < 7; i++)
                {
                    DateTime d = DateTime.Now.Date.AddDays(-i);
                    string key = d.ToString("yyyy-MM-dd");
                    int[] c;
                    st.Journal.TryGetValue(key, out c);
                    string label = i == 0 ? "Today" : i == 1 ? "Yesterday" : d.ToString("ddd d");
                    g.DrawString(label, body, i == 0 ? ink : muted, col0, y);
                    for (int k = 0; k < Store.Kinds; k++)
                        g.DrawString(c == null ? "-" : c[k].ToString(), body, i == 0 ? ink : muted, col0 + 70 + k * colW + 10, y);
                    y += 20;
                }
            }
        }

        string Mood()
        {
            if (st.Hunger > 75) return "He is hungry. Right-click him and choose Feed.";
            if (st.Energy < 25) return "He is sleepy and will nap soon.";
            if (st.Affection < 30) return "He misses you. Give him a pet!";
            if (st.Fun < 30) return "He is bored and might get the zoomies.";
            return "He is happy and a little bit silly.";
        }

        static void Bar(Graphics g, Font f, Brush ink, string label, double value, Color color, int x, int y, int w)
        {
            g.DrawString(label, f, ink, x, y);
            int bx = x + 90, bw = w - 90 - 40, bh = 12, by = y + 3;
            using (SolidBrush t = new SolidBrush(Track)) FillRound(g, t, bx, by, bw, bh);
            int fill = (int)(bw * Math.Max(0, Math.Min(100, value)) / 100);
            if (fill > 0) using (SolidBrush c = new SolidBrush(color)) FillRound(g, c, bx, by, Math.Max(fill, bh), bh);
            g.DrawString(((int)Math.Round(value)) + "%", f, ink, bx + bw + 6, y);
        }

        static void FillRound(Graphics g, Brush b, int x, int y, int w, int h)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                int r = h;
                p.AddArc(x, y, r, r, 90, 180);
                p.AddArc(x + w - r, y, r, r, 270, 180);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }
    }
}
