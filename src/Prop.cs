using System;
using System.Drawing;
using System.Windows.Forms;

namespace CatPet
{
    // A tiny see-through window that shows one pixel-art object (food bowl, yarn ball, laser dot).
    public class Prop : Form
    {
        string[] grid;
        readonly int scale;
        readonly bool clickThrough;

        public Prop(string[] g, int scale, bool clickThrough = false)
        {
            this.scale = scale;
            this.clickThrough = clickThrough;
            grid = g;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 0, 255);
            TransparencyKey = BackColor;
            DoubleBuffered = true;
            ClientSize = new Size(g[0].Length * scale, g.Length * scale);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;                              // tool window
                if (clickThrough) cp.ExStyle |= 0x20 | 0x80000;  // clicks pass straight through
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetGrid(string[] g)
        {
            if (g == grid) return;
            grid = g;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Art.Paint(e.Graphics, grid, 0, 0, scale);
        }
    }

    // A ball of yarn with simple physics. Drag and fling it; double-click to put it away.
    public class Yarn : Prop
    {
        public double X, Y, VX, VY;
        public bool Held, PutAway;
        readonly int s;
        double spin, lastX, lastY;
        int lastMs;
        Point grab;

        public Yarn(int scale, double x, double y) : base(Art.YarnA, scale)
        {
            s = scale;
            X = x; Y = y;
            Location = new Point((int)x, (int)y);
        }

        public bool Moving { get { return Held || Math.Abs(VX) + Math.Abs(VY) > 150; } }
        public double CenterX { get { return X + Width / 2.0; } }
        public double BottomY { get { return Y + Height; } }

        public void Kick(double vx, double vy) { if (!Held) { VX = vx; VY = vy; } }

        public void Step(double dt)
        {
            if (Held) return;
            Rectangle wa = Screen.FromPoint(new Point((int)X + Width / 2, (int)Y + Height / 2)).WorkingArea;
            VY += 1800 * s / 2.0 * dt;                // gravity
            X += VX * dt; Y += VY * dt;
            double floor = wa.Bottom - Height;
            bool onFloor = false;
            if (Y >= floor)
            {
                Y = floor; onFloor = true;
                if (VY > 0) { VY = -VY * 0.5; if (Math.Abs(VY) < 60) VY = 0; }
            }
            if (Y < wa.Top) { Y = wa.Top; VY = Math.Abs(VY) * 0.5; }
            if (X < wa.Left) { X = wa.Left; VX = Math.Abs(VX) * 0.6; }
            if (X > wa.Right - Width) { X = wa.Right - Width; VX = -Math.Abs(VX) * 0.6; }
            if (onFloor) { VX *= Math.Pow(0.3, dt); if (Math.Abs(VX) < 4) VX = 0; }   // rolling friction

            spin += Math.Abs(VX * dt) + Math.Abs(VY * dt) * 0.3;
            SetGrid(((int)(spin / (6 * s))) % 2 == 0 ? Art.YarnA : Art.YarnB);
            Point np = new Point((int)Math.Round(X), (int)Math.Round(Y));
            if (np != Location) Location = np;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Held = true; grab = e.Location; VX = VY = 0;
                lastMs = Environment.TickCount; lastX = Left; lastY = Top;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Held)
            {
                Point c = Cursor.Position;
                double nx = c.X - grab.X, ny = c.Y - grab.Y;
                int now = Environment.TickCount;
                double dt = unchecked(now - lastMs) / 1000.0;
                if (dt > 0.005)
                {   // remember how fast you're moving it, for the throw
                    VX = 0.5 * VX + 0.5 * (nx - lastX) / dt;
                    VY = 0.5 * VY + 0.5 * (ny - lastY) / dt;
                    lastX = nx; lastY = ny; lastMs = now;
                }
                X = nx; Y = ny;
                Location = new Point((int)nx, (int)ny);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (Held && e.Button == MouseButtons.Left)
            {
                Held = false;
                if (unchecked(Environment.TickCount - lastMs) > 120) { VX = 0; VY = 0; }   // let go without throwing
                double max = 3000;
                VX = Math.Max(-max, Math.Min(max, VX));
                VY = Math.Max(-max, Math.Min(max, VY));
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            PutAway = true;
            base.OnMouseDoubleClick(e);
        }
    }
}
