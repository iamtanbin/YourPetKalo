using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CatPet
{
    // One cat: its window, its body state machine, and the menu.
    class Cat : Form
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);

        enum Act { Idle, Walk, Held, Sleep, Trick, Eat, Fall }
        enum Trick { Stretch, Groom, Wiggle, Hunt, ChaseTail, Stare, Trip, Bonk, Forget, Beg, Tangled, Land, Jump, Dance }
        enum Goal { Wander, Bowl, Zoom, Yarn, Laser, Climb }
        enum SleepWhy { Nap, AwayFromPc, Forced }

        const int AwaySleepMs = 5 * 60 * 1000;    // you stop using the PC for 5 min -> he sleeps
        const int LonelyMs = 60 * 60 * 1000;      // no pets for 1 hour -> he cries
        const int BreakAfterMs = 60 * 60 * 1000;  // 1 hour of non-stop use -> break reminder
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Store st = new Store();
        readonly Look look = new Look();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Random rnd = new Random();
        readonly List<int[]> hearts = new List<int[]>(); // { x, bornMs }

        int scale = 2;

        Act act = Act.Idle;
        SleepWhy sleepWhy;
        Trick trick; int trickStart, trickLen;
        Goal goal;
        double pounceFromX, pounceFromY, pounceToX, pounceToY; bool pounceSet, huntMiss;
        int sleepStart, napUntil;
        bool hasEmote; Expr emote; int emoteUntil;
        string bubble; int bubbleUntil;
        double px, py, tx, ty;
        int lastTick, decideAt, nextBlink, blinkEnd, stepAt, hopStart, glanceAt, glanceDx, glanceDy;
        bool blinking, stepLeft;
        int lastPet, lastPetSound, nextLonely, activeSince, nextBreakNag, hoverStart, nextTrim, nextDayCheck, nextSave, nextHungerNag;
        bool hovering, hoverShown;
        int lastSig = int.MinValue;

        // goofs while walking
        bool walkClamped, hasTrip, hasForget; int tripAt, forgetAt, zoomLeft;
        int stareDx, stareDy;
        // what he did last, so petting can reward it
        string lastDeed; int lastDeedAt;

        // food
        Prop bowl; bool bowlEaten; int bowlCloseAt, eatStart, nextBite;

        // toys
        Yarn yarn; int yarnBats, yarnMaxBats, yarnChaseUntil, batCooldown, lastYarnEnd;
        Prop laser; int laserUntil, nextLaserPounce;
        int huntCrouch = 1300;

        // bond
        int lastPetXp, curHat;

        // gravity & windows
        double vx, vy, fallFromY;
        bool fallGoof;
        IntPtr standHwnd = IntPtr.Zero; Rectangle standRect;
        List<Surface> surfaces = new List<Surface>(); int nextScan;
        double jumpFromX, jumpFromY, jumpToX, jumpToY, jumpH; IntPtr jumpHwnd; bool jumpShort;
        Surface climbTo;
        double dragVx, dragVy;

        // fullscreen + music
        bool hidden; int nextFsCheck;
        int nextMeter, loudMs, quietMs, lastBeat, beatCount;
        float peakAvg;
        readonly List<int[]> notes = new List<int[]>(); // { x, bornMs }
        int nextNote;

        JournalForm journal;

        bool mouseDown, dragging, dizzy;
        Point downScreen, dragOff;
        double shake; int lastMoveMs;

        ToolStripMenuItem miStay, miFollow, miBreaks, miStartup, miMute, miYarn, miHat, miGravity;
        readonly List<ToolStripMenuItem> miSizes = new List<ToolStripMenuItem>();

        public Cat()
        {
            int now = Environment.TickCount;
            st.Load();
            scale = st.Scale;

            Text = st.Name;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 0, 255);
            TransparencyKey = BackColor;
            DoubleBuffered = true;
            ClientSize = new Size(Art.CW * scale, Art.CH * scale);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (double.IsNaN(st.X)) { px = wa.Right - Width - 120; py = wa.Bottom - Height; }
            else { px = st.X; py = st.Y; }
            ClampPos(ref px, ref py);
            Location = new Point((int)px, (int)py);

            BuildMenu();

            curHat = st.CurrentHat();
            hopStart = now - 100000; lastPetSound = now - 100000; lastDeedAt = now - 100000;
            lastPetXp = now - 100000; lastYarnEnd = now - 100000;
            lastTick = now; lastPet = now; nextLonely = now; activeSince = now; nextBreakNag = now;
            decideAt = now + 4000; nextBlink = now + 2500; nextTrim = now + 4000; glanceAt = now;
            nextDayCheck = now + 3000; nextSave = now + 60000; nextHungerNag = now + 20000;
            Say("Meow!", 2500, now);

            timer.Interval = 40;
            timer.Tick += OnTick;
            timer.Start();
        }

        // Tool window: hidden from Alt+Tab and the taskbar; never steals focus.
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        // ---------- helpers ----------
        static bool Due(int now, int t) { return unchecked(now - t) >= 0; }
        static int Since(int now, int t) { return unchecked(now - t); }
        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static bool IsNight() { int h = DateTime.Now.Hour; return h >= 22 || h < 5; }
        bool Roll(double chance) { return rnd.NextDouble() < chance; }
        bool Gravity { get { return st.Gravity && !st.Follow; } }
        double CatCx { get { return px + Width / 2.0; } }
        double FootY { get { return py + Height; } }
        Rectangle WorkArea { get { return Screen.FromPoint(new Point((int)CatCx, (int)FootY - 5)).WorkingArea; } }

        static int IdleMs()
        {
            LASTINPUTINFO l = new LASTINPUTINFO();
            l.cbSize = (uint)Marshal.SizeOf(l);
            if (!GetLastInputInfo(ref l)) return 0;
            return unchecked(Environment.TickCount - (int)l.dwTime);
        }

        Rectangle CatScreenRect()
        {
            return new Rectangle(Left + Art.OX * scale, Top + Art.OY * scale, Art.BodyW * scale, (Art.BodyH + 2) * scale);
        }

        void ClampPos(ref double x, ref double y)
        {
            Rectangle wa = Screen.FromPoint(new Point((int)x + Width / 2, (int)y + Height - 5)).WorkingArea;
            x = Clamp(x, wa.Left - Art.OX * scale, wa.Right - (Art.OX + Art.BodyW) * scale);
            y = Clamp(y, wa.Top - Art.OY * scale, wa.Bottom - Height);
        }

        void Say(string text, int ms, int now) { bubble = text; bubbleUntil = now + ms; }
        void SetEmote(Expr e, int ms, int now) { hasEmote = true; emote = e; emoteUntil = now + ms; }
        void Hop(int now) { hopStart = now; }
        void Goof() { st.Count(Store.Goofs); }

        // ---------- bond ----------
        void GainXp(int n, int now)
        {
            int before = st.Level;
            st.Xp += n;
            int after = st.Level;
            if (after <= before) return;
            // level up!
            for (int i = 0; i < 3; i++) hearts.Add(new int[] { Art.OX + 6 + i * 9, now - i * 250 });
            string unlocked = null;
            for (int h = 1; h < Art.HatLevel.Length; h++) if (Art.HatLevel[h] == after) unlocked = Art.HatNames[h];
            Say(unlocked != null ? "Lv " + after + "! New hat!" : "Bond level " + after + "!", 4000, now);
            SetEmote(Expr.Happy, 2500, now);
            Sounds.Play("meow");
            curHat = st.CurrentHat();
            SaveAll();
        }

        void StartSleep(SleepWhy why, int now)
        {
            act = Act.Sleep; sleepWhy = why; sleepStart = now;
            hasEmote = false; bubble = null;
            st.Count(Store.Naps);
        }

        void WakeUp(int now, string msg)
        {
            act = Act.Idle;
            SetEmote(Expr.Drowsy, 1200, now);
            decideAt = now + 4000;
            if (msg != null) Say(msg, 3000, now);
        }

        void StartTrick(Trick t, int len, int now)
        {
            act = Act.Trick; trick = t; trickStart = now; trickLen = len; pounceSet = false;
            hasEmote = false;
        }

        void EndDeed(int now, int restMin, int restMax)
        {
            act = Act.Idle;
            lastDeedAt = now;
            decideAt = now + rnd.Next(restMin, restMax);
        }

        void Pet(int now)
        {
            lastPet = now; nextLonely = now;
            st.Count(Store.Petted);
            st.Change(ref st.Affection, 6);
            st.Change(ref st.Fun, 2);
            if (Since(now, lastPetXp) > 3000) { lastPetXp = now; GainXp(1, now); }   // no XP for click-spamming
            if (act == Act.Trick && trick == Trick.Tangled) { act = Act.Idle; Say("Thanks!", 1500, now); }

            // learning: if you pet him right after he did something, he'll do it more often
            if (lastDeed != null && (act == Act.Trick || Since(now, lastDeedAt) < 5000)) { st.Reward(lastDeed); lastDeed = null; }

            if (act == Act.Sleep) { WakeUp(now, "Mrrp?"); Sounds.Play("mrrp"); return; }
            if (act == Act.Walk || act == Act.Trick) act = Act.Idle;
            SetEmote(Expr.Happy, 2200, now);
            hearts.Add(new int[] { Art.OX + 14 + rnd.Next(-8, 9), now });
            Sounds.Play(Since(now, lastPetSound) < 3000 ? "purr" : "meow");
            lastPetSound = now;
            if (rnd.Next(100) < 35) Say(rnd.Next(2) == 0 ? "Purr~" : "Nya!", 1800, now);
            decideAt = now + 4000;
        }

        // ---------- walking ----------
        void WalkTo(double x, double y, Goal g, int now)
        {
            if (Gravity && g != Goal.Yarn && g != Goal.Laser) y = py;   // with gravity he walks along the floor he's on
            double rx = x, ry = y;
            ClampPos(ref x, ref y);
            tx = x; ty = y;
            walkClamped = Math.Abs(rx - x) > 2;
            goal = g;
            act = Act.Walk;
            stepAt = now;
            hasTrip = false; hasForget = false;
        }

        void PickTarget(int now)
        {
            double x = px + rnd.Next(-300, 301);
            if (Gravity && standHwnd != IntPtr.Zero && !Roll(Brain.GoofWalkOff))
                x = Clamp(x + Width / 2.0, standRect.Left + 20 * scale, standRect.Right - 20 * scale) - Width / 2.0;  // stay on the window
            WalkTo(x, py + rnd.Next(-120, 121), Goal.Wander, now);
            if (Roll(Brain.GoofTrip)) { hasTrip = true; tripAt = now + rnd.Next(400, 1500); }
            else if (Roll(Brain.GoofForget)) { hasForget = true; forgetAt = now + rnd.Next(800, 2000); }
        }

        void StartZoomies(int now)
        {
            zoomLeft = rnd.Next(3, 7);
            st.Count(Store.Played);
            st.Change(ref st.Fun, 6);
            st.Change(ref st.Energy, -3);
            GainXp(3, now);
            NextZoom(now);
        }

        void NextZoom(int now) { WalkTo(px + rnd.Next(-350, 351), py + rnd.Next(-150, 151), Goal.Zoom, now); }

        void GoToBowl(int now)
        {
            if (bowl == null) return;
            double bx = bowl.Left + bowl.Width / 2.0 - Width / 2.0;
            double by = bowl.Top + bowl.Height - Height;
            if (Math.Abs(bx - px) + Math.Abs(by - py) < 3) { StartEat(now); return; }
            if (Gravity && Math.Abs(by - py) > 20) { StartJump(bx, by, IntPtr.Zero, false, now); return; }  // hop down to the bowl
            WalkTo(bx, by, Goal.Bowl, now);
        }

        // ---------- yarn ball ----------
        void ToggleYarn()
        {
            int now = Environment.TickCount;
            if (yarn != null) { RemoveYarn(); return; }
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            double x = Clamp(Left + Width / 2.0 + rnd.Next(-120, 121), wa.Left, wa.Right - 8 * scale);
            double y = Clamp(Top + Art.OY * scale, wa.Top, wa.Bottom - 8 * scale);
            yarn = new Yarn(scale, x, y);
            yarn.Kick(rnd.Next(-200, 201), -300);
            yarn.Show();
            miYarn.Text = "Put yarn away";
            if (act == Act.Sleep) WakeUp(now, "Yarn!!");
            else Say("Yarn!!", 1500, now);
        }

        void RemoveYarn()
        {
            if (yarn == null) return;
            yarn.Close(); yarn.Dispose(); yarn = null;
            miYarn.Text = "Yarn ball";
            if (act == Act.Walk && goal == Goal.Yarn) EndDeed(Environment.TickCount, 2000, 5000);
        }

        void StartYarnChase(int now)
        {
            yarnBats = 0; yarnMaxBats = rnd.Next(3, 8); yarnChaseUntil = now + 15000; batCooldown = now;
            st.Count(Store.Played);
            GainXp(3, now);
            act = Act.Walk; goal = Goal.Yarn; stepAt = now;
            hasTrip = false; hasForget = false; walkClamped = false;
        }

        void BatYarn(int now)
        {
            double catCx = px + Width / 2.0;
            int dir = yarn.CenterX >= catCx ? 1 : -1;
            yarn.Kick(dir * rnd.Next(350, 800) * scale / 2.0, -rnd.Next(250, 650) * scale / 2.0);
            Hop(now);
            yarnBats++;
            batCooldown = now + 600;
            st.Change(ref st.Fun, 3);
            if (Roll(0.3)) Sounds.Play("mrrp");

            if (Roll(Brain.GoofTangle))
            {
                StartTrick(Trick.Tangled, 3000, now);
                Say("Help?", 2500, now);
                Goof();
                lastYarnEnd = now;
            }
            else if (yarnBats >= yarnMaxBats || Due(now, yarnChaseUntil))
            {
                EndDeed(now, 3000, 7000);
                SetEmote(Expr.Smug, 2000, now);
                lastYarnEnd = now;
            }
        }

        // ---------- laser pointer ----------
        void StartLaser()
        {
            int now = Environment.TickCount;
            if (laser != null) { laserUntil = now + 60000; return; }
            laser = new Prop(Art.LaserDot, scale, true);
            PlaceLaser();
            laser.Show();
            laserUntil = now + 60000;
            nextLaserPounce = now + 600;
            if (act == Act.Sleep) WakeUp(now, null);
            if (act != Act.Held && act != Act.Eat) act = Act.Idle;
            Say("!!!", 1200, now);
            Hop(now);
        }

        void PlaceLaser()
        {
            Point c = Cursor.Position;
            Point p = new Point(c.X - laser.Width / 2, c.Y - laser.Height / 2);
            if (p != laser.Location) laser.Location = p;
        }

        void EndLaser(int now)
        {
            if (laser == null) return;
            laser.Close(); laser.Dispose(); laser = null;
            st.Count(Store.Played);
            st.Change(ref st.Fun, 15);
            st.Change(ref st.Energy, -5);
            GainXp(5, now);
            if (act == Act.Idle || act == Act.Walk || act == Act.Trick)
            {
                StartTrick(Trick.Forget, 2600, now);
                Say("Where did it go?!", 2400, now);
            }
        }

        // ---------- gravity ----------
        void StartFall(int now, string msg, bool goof)
        {
            if (act == Act.Sleep) { hasEmote = false; }
            act = Act.Fall;
            fallFromY = py;
            fallGoof = goof;
            standHwnd = IntPtr.Zero;
            if (vy > 0) vy = 0;
            if (msg != null) Say(msg, 1200, now);
            if (goof) Goof();
        }

        // Where would he land if his feet move from y0 down to y1?
        bool FindLanding(double y0, double y1, out Surface land)
        {
            Rectangle wa = WorkArea;
            land = new Surface { X1 = wa.Left, X2 = wa.Right, Y = wa.Bottom, Hwnd = IntPtr.Zero };
            bool found = y1 >= wa.Bottom;
            double cx = CatCx;
            foreach (Surface s in surfaces)
            {
                if (cx < s.X1 + 8 || cx > s.X2 - 8) continue;
                if (s.Y < y0 - 1 || s.Y > y1) continue;
                if (!found || s.Y < land.Y) { land = s; found = true; }
            }
            return found;
        }

        void Land(Surface s, int now)
        {
            py = s.Y - Height;
            vx = 0; vy = 0;
            standHwnd = s.Hwnd;
            if (s.Hwnd != IntPtr.Zero) Desktop.GetFrame(s.Hwnd, out standRect);
            Location = new Point((int)Math.Round(px), (int)Math.Round(py));
            double drop = py - fallFromY;
            act = Act.Idle;
            decideAt = now + rnd.Next(2500, 6000);
            if (drop > 40 * scale)
            {
                StartTrick(Trick.Land, 450, now);
                Sounds.Play("thump");
                if (drop > 250 * scale && Roll(0.3)) { SetEmote(Expr.Dizzy, 1600, now); Say("Whoa...", 1500, now); Goof(); }
            }
        }

        // Is something still holding him up? (window moved, closed, walked off the edge...)
        void CheckSupport(int now)
        {
            if (standHwnd != IntPtr.Zero)
            {
                Rectangle r;
                if (!Desktop.IsUsable(standHwnd) || !Desktop.GetFrame(standHwnd, out r))
                {
                    StartFall(now, act == Act.Sleep ? "Whoa!" : "!?", false);
                    return;
                }
                bool wasOn = Math.Abs(FootY - standRect.Top) <= 6;
                int ddx = r.Left - standRect.Left, ddy = r.Top - standRect.Top;
                standRect = r;
                if (wasOn && (ddx != 0 || ddy != 0))
                {
                    if (Math.Abs(ddx) + Math.Abs(ddy) > 120) { StartFall(now, "Whoa!", false); return; }  // yanked away
                    px += ddx; py += ddy; tx += ddx; ty += ddy;                                            // ride along
                    Location = new Point((int)Math.Round(px), (int)Math.Round(py));
                }
                if (Math.Abs(FootY - r.Top) > 6) standHwnd = IntPtr.Zero;   // not actually on it any more
                else if (CatCx < r.Left - 4 || CatCx > r.Right + 4)
                {
                    bool goof = act == Act.Walk && goal == Goal.Wander;
                    StartFall(now, goof ? "!?" : null, goof);   // walked off the edge
                    return;
                }
                else return;
            }
            if (FootY < WorkArea.Bottom - 2) StartFall(now, null, false);       // nothing under his feet
        }

        void StartJump(double x, double y, IntPtr hwnd, bool canGoof, int now)
        {
            jumpFromX = px; jumpFromY = py;
            jumpShort = canGoof && Roll(Brain.GoofShortJump);
            if (jumpShort) { x = px + (x - px) * 0.6; y = py + (y - py) * 0.6; }
            ClampPos(ref x, ref y);
            jumpToX = x; jumpToY = y; jumpHwnd = jumpShort ? IntPtr.Zero : hwnd;
            jumpH = 50 * scale + Math.Max(0, py - y) * 0.35;
            standHwnd = IntPtr.Zero;
            StartTrick(Trick.Jump, 650, now);
        }

        // Pick a window edge (or the floor) nearby and go jump onto it.
        bool TryClimb(int now)
        {
            List<Surface> options = new List<Surface>();
            double cx = CatCx, fy = FootY;
            foreach (Surface s in surfaces)
            {
                if (s.Hwnd == standHwnd) continue;
                double nearX = Clamp(cx, s.X1 + 25 * scale, s.X2 - 25 * scale);
                if (Math.Abs(nearX - cx) > 450 || Math.Abs(s.Y - fy) > 420 || Math.Abs(s.Y - fy) < 20) continue;
                options.Add(s);
            }
            if (standHwnd != IntPtr.Zero)
            {   // he could also hop down to the floor
                Rectangle wa = WorkArea;
                options.Add(new Surface { X1 = wa.Left, X2 = wa.Right, Y = wa.Bottom, Hwnd = IntPtr.Zero });
            }
            if (options.Count == 0) return false;
            climbTo = options[rnd.Next(options.Count)];
            double tcx = Clamp(cx, climbTo.X1 + 25 * scale, climbTo.X2 - 25 * scale);
            if (Math.Abs(tcx - cx) > 220)
            {   // walk closer first
                double standX = tcx - Math.Sign(tcx - cx) * 150 - Width / 2.0;
                WalkTo(standX, py, Goal.Climb, now);
            }
            else ClimbJump(now);
            return true;
        }

        void ClimbJump(int now)
        {
            double tcx = Clamp(CatCx, climbTo.X1 + 25 * scale, climbTo.X2 - 25 * scale);
            if (Math.Abs(tcx - CatCx) < 40) tcx += (tcx < (climbTo.X1 + climbTo.X2) / 2.0 ? 40 : -40);
            StartJump(tcx - Width / 2.0, climbTo.Y - Height, climbTo.Hwnd, true, now);
            st.Change(ref st.Fun, 2);
        }

        // ---------- fullscreen ----------
        void HideAll()
        {
            hidden = true;
            Hide();
            if (bowl != null) bowl.Hide();
            if (yarn != null) yarn.Hide();
            if (laser != null) { laser.Close(); laser.Dispose(); laser = null; }
            if (act == Act.Walk || act == Act.Trick) act = Act.Idle;
        }

        void ShowAll(int now)
        {
            hidden = false;
            Show();
            TopMost = true;
            if (bowl != null) bowl.Show();
            if (yarn != null) yarn.Show();
            lastSig = int.MinValue;
            lastTick = now;
        }

        // ---------- music ----------
        void ListenForMusic(int now)
        {
            if (!Due(now, nextMeter)) return;
            nextMeter = now + 150;
            float pk = Since(now, Sounds.LastPlay) < 2500 ? 0 : AudioMeter.Peak();
            peakAvg = peakAvg * 0.9f + pk * 0.1f;
            bool loud = pk > 0.03f;
            if (loud) { loudMs = Math.Min(loudMs + 150, 5000); quietMs = 0; }
            else { quietMs += 150; loudMs = Math.Max(0, loudMs - 300); }

            bool dancing = act == Act.Trick && trick == Trick.Dance;
            if (dancing)
            {
                if (pk > 0.05f && pk > peakAvg * 1.3f && Since(now, lastBeat) > 250) { lastBeat = now; beatCount++; }
                if (quietMs >= 3000)
                {   // music stopped
                    EndDeed(now, 3000, 7000);
                    st.Count(Store.Played);
                    GainXp(3, now);
                    if (Roll(0.4)) Say("That was fun!", 2000, now);
                    curHat = st.CurrentHat();
                }
            }
            else if (loudMs >= 2000 && act == Act.Idle && !hasEmote && laser == null && !mouseDown)
            {
                StartTrick(Trick.Dance, int.MaxValue / 4, now);
                beatCount = 0; lastBeat = now; nextNote = now;
                if (st.Hat == "auto" && st.HatUnlocked(4)) curHat = 4;   // headphones on!
                if (Roll(0.5)) Say("♪ Music! ♪", 1500, now);
            }
        }

        // ---------- the brain picks something to do ----------
        void DoDeed(Deed d, int now, double cursorDist)
        {
            lastDeed = d.ToString(); lastDeedAt = now;
            switch (d)
            {
                case Deed.Walk: if (!st.Stay) PickTarget(now); break;
                case Deed.Nap:
                    StartSleep(SleepWhy.Nap, now);
                    napUntil = now + (st.Energy < 30 ? rnd.Next(120000, 300000) : rnd.Next(30000, 90000));
                    break;
                case Deed.Groom: StartTrick(Trick.Groom, 3000, now); break;
                case Deed.Stretch: StartTrick(Trick.Stretch, 2700, now); break;
                case Deed.Wiggle: StartTrick(Trick.Wiggle, 1200, now); break;
                case Deed.Hunt: huntCrouch = 1300; StartTrick(Trick.Hunt, 1700, now); break;
                case Deed.Yarn: if (yarn != null && !st.Stay) StartYarnChase(now); break;
                case Deed.Climb: if (!(Gravity && !st.Stay && TryClimb(now))) SetEmote(Expr.Smug, 2000, now); break;
                case Deed.AskFood:
                    StartTrick(Trick.Beg, 4000, now);
                    Say(rnd.Next(2) == 0 ? "Food?" : st.ShortName + " hungry...", 3500, now);
                    Sounds.Play("sad");
                    break;
                case Deed.Zoomies: if (!st.Stay) StartZoomies(now); break;
                case Deed.Stare:
                    StartTrick(Trick.Stare, rnd.Next(3000, 5000), now);
                    stareDx = rnd.Next(2) == 0 ? -3 : 3; stareDy = rnd.Next(-3, 0);
                    break;
                case Deed.ChaseTail: StartTrick(Trick.ChaseTail, 2600, now); break;
                case Deed.Smug: SetEmote(Expr.Smug, rnd.Next(3000, 6000), now); break;
                case Deed.Cheeky: SetEmote(Expr.Cheeky, 2500, now); break;
                case Deed.Hop: Hop(now); break;
            }
        }

        // ---------- food ----------
        void Feed()
        {
            int now = Environment.TickCount;
            if (bowl != null) { Say("Already got food!", 2000, now); return; }
            if (act == Act.Sleep) WakeUp(now, "Food?!");

            bowl = new Prop(Art.BowlFull, scale);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int footY = Top + Height, centerX = Left + Width / 2;
            int side = centerX < wa.Left + wa.Width / 2 ? 1 : -1;
            int bx = centerX + side * 70 * scale - bowl.Width / 2;
            bx = (int)Clamp(bx, wa.Left, wa.Right - bowl.Width);
            int by = Gravity ? wa.Bottom - bowl.Height : (int)Clamp(footY - bowl.Height, wa.Top, wa.Bottom - bowl.Height);
            bowl.Location = new Point(bx, by);
            bowl.Show();
            bowlEaten = false;
            Say("Food!!", 1500, now);
            Sounds.Play("mrrp");
            if (act != Act.Held) GoToBowl(now);
        }

        void StartEat(int now)
        {
            act = Act.Eat; eatStart = now; nextBite = now + 200;
            hasEmote = false; bubble = null;
            px = tx; py = ty;
        }

        void FinishEat(int now)
        {
            st.Hunger = 0;
            st.Change(ref st.Fun, 5);
            st.Change(ref st.Affection, 5);
            st.Change(ref st.Energy, 5);
            st.Count(Store.Fed);
            GainXp(5, now);
            bowlEaten = true;
            bowlCloseAt = now + 1500;
            act = Act.Idle;
            SetEmote(Expr.Happy, 2200, now);
            Say("Yum!", 2000, now);
            hearts.Add(new int[] { Art.OX + 14, now });
            Sounds.Play("mrrp");
            decideAt = now + 5000;
            nextHungerNag = now + 10 * 60 * 1000;
        }

        // Good morning / lunch / dinner / bedtime, once per day each.
        void CheckTimeOfDay(int now, int idle)
        {
            curHat = st.CurrentHat();
            if (idle < 15000 && st.LastVisit != Store.Today) { st.LastVisit = Store.Today; GainXp(10, now); } // daily visit bonus
            if (act == Act.Sleep || act == Act.Held || act == Act.Eat || idle > 15000 || bubble != null) return;
            DateTime d = DateTime.Now;
            string today = d.ToString("yyyy-MM-dd");
            int hm = d.Hour * 60 + d.Minute;
            if (hm >= 5 * 60 && hm < 11 * 60 && st.Greet != today)
            {
                st.Greet = today; Say("Good morning!", 4000, now); SetEmote(Expr.Happy, 2500, now); Sounds.Play("meow");
            }
            else if (hm >= 12 * 60 + 30 && hm < 14 * 60 && st.Lunch != today)
            {
                st.Lunch = today; Say("Lunch time! Go eat~", 6000, now); SetEmote(Expr.Happy, 2500, now); Sounds.Play("meow");
            }
            else if (hm >= 19 * 60 + 30 && hm < 21 * 60 && st.Dinner != today)
            {
                st.Dinner = today; Say("Dinner time!", 6000, now); SetEmote(Expr.Happy, 2500, now); Sounds.Play("meow");
            }
            else if (hm >= 23 * 60 + 30 || hm < 4 * 60)
            {
                string night = d.AddHours(-5).ToString("yyyy-MM-dd");
                if (st.Late == night) return;
                st.Late = night; Say("It's late... sleep soon?", 6000, now); SetEmote(Expr.Drowsy, 2500, now);
            }
            else return;
            SaveAll();
        }

        // ---------- main loop ----------
        void OnTick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            double dt = Since(now, lastTick) / 1000.0;
            lastTick = now;
            if (dt < 0) dt = 0;

            // games / fullscreen video: get out of the way
            if (Due(now, nextFsCheck))
            {
                nextFsCheck = now + 1000;
                bool fs = Desktop.FullscreenActive();
                if (fs && !hidden) HideAll();
                else if (!fs && hidden) ShowAll(now);
            }
            if (hidden)
            {
                st.Tick(Math.Min(dt, 2.0) / 60.0, act == Act.Sleep);
                if (timer.Interval != 1000) timer.Interval = 1000;
                if (Due(now, nextSave)) { SaveAll(); nextSave = now + 60000; }
                return;
            }
            if (dt > 0.25) dt = 0.25;

            st.Tick(dt / 60.0, act == Act.Sleep);

            // window edges he could stand on (refreshed once a second, only when it matters)
            if (Gravity && act != Act.Sleep && Due(now, nextScan))
            {
                nextScan = now + 1000;
                surfaces = Desktop.Scan(Art.BodyW * scale / 2, SystemInformation.VirtualScreen.Top + (Art.BodyH + 4) * scale);
            }

            if (act != Act.Sleep && act != Act.Held) ListenForMusic(now);

            int idle = IdleMs();
            Point cur = Cursor.Position;
            Rectangle cat = CatScreenRect();

            if (hasEmote && Due(now, emoteUntil)) hasEmote = false;
            if (bubble != null && Due(now, bubbleUntil)) bubble = null;
            if (bowl != null && bowlEaten && Due(now, bowlCloseAt)) { bowl.Close(); bowl.Dispose(); bowl = null; }
            if (yarn != null) { if (yarn.PutAway) RemoveYarn(); else yarn.Step(dt); }
            if (laser != null) { PlaceLaser(); if (Due(now, laserUntil)) EndLaser(now); }

            if (Due(now, nextDayCheck)) { CheckTimeOfDay(now, idle); nextDayCheck = now + 20000; }

            // break reminder
            if (idle > 3 * 60 * 1000) activeSince = now;
            if (st.Breaks && act != Act.Sleep && Since(now, activeSince) > BreakAfterMs && Due(now, nextBreakNag))
            {
                Say("Take a break!", 8000, now);
                SetEmote(Expr.Cheeky, 3000, now);
                Sounds.Play("meow");
                nextBreakNag = now + 10 * 60 * 1000;
            }

            // sleeping / waking
            if ((act == Act.Idle || act == Act.Walk || act == Act.Trick) && idle > AwaySleepMs) StartSleep(SleepWhy.AwayFromPc, now);
            if (act == Act.Sleep)
            {
                if (sleepWhy == SleepWhy.AwayFromPc && idle < 1500) WakeUp(now, "Welcome back!");
                else if (sleepWhy == SleepWhy.Nap && Due(now, napUntil)) WakeUp(now, null);
            }

            // lonely -> cries until you pet him
            if ((act == Act.Idle || act == Act.Walk) && !hasEmote && idle < 30000
                && Since(now, lastPet) > LonelyMs && Due(now, nextLonely))
            {
                act = Act.Idle;
                SetEmote(Expr.Cry, 8000, now);
                Say("Pet me...", 4000, now);
                Sounds.Play("sad");
                nextLonely = now + 5 * 60 * 1000;
                decideAt = now + 9000;
            }

            // hover -> name and time
            bool over = cat.Contains(cur);
            if (over && !hovering) { hovering = true; hoverStart = now; hoverShown = false; }
            else if (!over) hovering = false;
            if (hovering && !hoverShown && !mouseDown && act != Act.Sleep && bubble == null && Since(now, hoverStart) > 900)
            {
                Say(st.ShortName + " · " + DateTime.Now.ToString("h:mm tt"), 2500, now);
                hoverShown = true;
            }

            bool crying = hasEmote && emote == Expr.Cry;
            double cdx = cur.X - (cat.X + cat.Width / 2.0), cdy = cur.Y - (cat.Y + cat.Height / 2.0);
            double cursorDist = Math.Sqrt(cdx * cdx + cdy * cdy);

            // food first!
            if (act == Act.Idle && !mouseDown && bowl != null && !bowlEaten) GoToBowl(now);

            // decide what to do next
            if (act == Act.Idle && !mouseDown && !crying)
            {
                if (laser != null)
                {   // laser mode: chase the red dot and pounce at it
                    if (cursorDist < 110 * scale / 2.0 && Due(now, nextLaserPounce))
                    {
                        huntCrouch = 300;
                        StartTrick(Trick.Hunt, 700, now);
                        nextLaserPounce = now + rnd.Next(700, 1500);
                    }
                    else if (cursorDist > 50 * scale / 2.0) { act = Act.Walk; goal = Goal.Laser; stepAt = now; hasTrip = false; hasForget = false; walkClamped = false; }
                }
                else if (yarn != null && yarn.Moving && !yarn.Held && !st.Stay && Since(now, lastYarnEnd) > 2500)
                    StartYarnChase(now);   // can't resist a rolling ball
                else if (st.Follow)
                {
                    double fx = cur.X - Width / 2.0, fy = cur.Y + 24 - Art.OY * scale;
                    ClampPos(ref fx, ref fy);
                    if (Math.Abs(fx - px) + Math.Abs(fy - py) > 80) WalkTo(fx, fy, Goal.Wander, now);
                }
                else if (st.Hunger > 75 && Due(now, nextHungerNag))
                {
                    DoDeed(Deed.AskFood, now, cursorDist);
                    nextHungerNag = now + 5 * 60 * 1000;
                }
                else if (Due(now, decideAt))
                {
                    decideAt = now + rnd.Next(4000, 10000);
                    DoDeed(Brain.Pick(st, rnd, st.Stay, cursorDist < 450 && cursorDist > 60, IsNight(), yarn != null,
                                      Gravity && (surfaces.Count > 0 || standHwnd != IntPtr.Zero)), now, cursorDist);
                }
            }

            // walking
            int tail = 0;
            if (act == Act.Walk)
            {
                if (st.Follow && goal == Goal.Wander)
                {
                    tx = cur.X - Width / 2.0; ty = cur.Y + 24 - Art.OY * scale;
                    ClampPos(ref tx, ref ty);
                }
                bool reachedYarn = false;
                if (goal == Goal.Yarn)
                {
                    if (yarn == null) { EndDeed(now, 2000, 5000); }
                    else
                    {   // stand just beside the ball, facing it
                        double catCx = px + Width / 2.0;
                        int side = yarn.CenterX > catCx ? -1 : 1;
                        tx = yarn.CenterX + side * 12 * scale - Width / 2.0;
                        ty = yarn.BottomY - Height;
                        ClampPos(ref tx, ref ty);
                        reachedYarn = Math.Abs(yarn.CenterX - catCx) < 24 * scale && Math.Abs(yarn.BottomY - (py + Height)) < 30 * scale;
                    }
                }
                if (goal == Goal.Laser)
                {
                    if (laser == null) { EndDeed(now, 2000, 5000); }
                    else
                    {
                        tx = cur.X - Width / 2.0; ty = cur.Y - (Art.OY + Art.BodyH - 4) * scale;
                        ClampPos(ref tx, ref ty);
                    }
                }
                if (Gravity && act == Act.Walk && (goal == Goal.Yarn || goal == Goal.Laser) && !reachedYarn)
                {   // with gravity: run along the floor, and leap when it's up (or down) there
                    double hdx = tx - px, vdy = ty - py;
                    if (Math.Abs(vdy) > 20 * scale && Math.Abs(hdx) < 90 * scale && (goal == Goal.Yarn || Due(now, nextLaserPounce)))
                    {
                        if (goal == Goal.Laser) nextLaserPounce = now + rnd.Next(700, 1500);
                        StartJump(tx, ty, IntPtr.Zero, false, now);
                    }
                    else ty = py;
                }
                double speed = goal == Goal.Zoom ? 230 : goal == Goal.Laser ? 200 : goal == Goal.Yarn ? 150
                             : goal == Goal.Bowl ? 75 : (st.Follow ? 110 : 45);
                speed *= scale / 2.0;
                double dx = tx - px, dy = ty - py;
                double d = Math.Sqrt(dx * dx + dy * dy), step = speed * dt;
                if (goal == Goal.Zoom || goal == Goal.Laser) tail = dx > 0 ? 2 : 1;

                if (act != Act.Walk) { }
                else if (goal == Goal.Yarn && reachedYarn)
                {
                    if (Due(now, batCooldown) && !yarn.Held) BatYarn(now);
                }
                else if (goal == Goal.Yarn && Due(now, yarnChaseUntil))
                {
                    EndDeed(now, 3000, 7000); lastYarnEnd = now;
                }
                else if (d <= step || (st.Follow && goal == Goal.Wander && d < 40))
                {
                    if (!(st.Follow && goal == Goal.Wander)) { px = tx; py = ty; }
                    if (goal == Goal.Yarn || goal == Goal.Laser) { if (goal == Goal.Laser) act = Act.Idle; }
                    else if (goal == Goal.Bowl && bowl != null) StartEat(now);
                    else if (goal == Goal.Climb) ClimbJump(now);
                    else if (goal == Goal.Zoom)
                    {
                        if (--zoomLeft > 0) NextZoom(now);
                        else
                        {
                            EndDeed(now, 3000, 7000);
                            SetEmote(Expr.Smug, 2000, now);
                            if (Roll(0.5)) Say("Huff~", 1500, now);
                        }
                    }
                    else if (walkClamped && Roll(Brain.GoofBonk))
                    {
                        StartTrick(Trick.Bonk, 1300, now);
                        Say("Ow!", 1300, now);
                        Goof();
                    }
                    else EndDeed(now, 2000, 7000);
                }
                else
                {
                    px += dx / d * step; py += dy / d * step;
                    if (hasTrip && Due(now, tripAt))
                    {
                        hasTrip = false;
                        StartTrick(Trick.Trip, 1400, now);
                        Say("!?", 1200, now);
                        Goof();
                    }
                    else if (hasForget && Due(now, forgetAt))
                    {
                        hasForget = false;
                        StartTrick(Trick.Forget, 2600, now);
                        Say("...why am I here?", 2400, now);
                        Goof();
                    }
                }

                Point np = new Point((int)Math.Round(px), (int)Math.Round(py));
                if (np != Location) Location = np;
                if (Due(now, stepAt)) { stepLeft = !stepLeft; stepAt = now + (goal == Goal.Zoom ? 80 : 170); }
            }

            // gravity
            if (act == Act.Fall)
            {
                double g = 2200 * scale / 2.0;
                vy = Math.Min(vy + g * dt, 2600);
                double y0 = FootY;
                px += vx * dt; py += vy * dt;
                Rectangle wa = WorkArea;
                if (px < wa.Left - Art.OX * scale) { px = wa.Left - Art.OX * scale; vx = Math.Abs(vx) * 0.5; }
                if (px > wa.Right - (Art.OX + Art.BodyW) * scale) { px = wa.Right - (Art.OX + Art.BodyW) * scale; vx = -Math.Abs(vx) * 0.5; }
                if (py < wa.Top - Art.OY * scale) { py = wa.Top - Art.OY * scale; vy = Math.Abs(vy) * 0.3; }
                Surface land;
                if (vy > 0 && FindLanding(y0, FootY, out land)) Land(land, now);
                else Location = new Point((int)Math.Round(px), (int)Math.Round(py));
            }
            else if (Gravity && act != Act.Held && !(act == Act.Trick && (trick == Trick.Jump || trick == Trick.Hunt)))
                CheckSupport(now);

            // blinking
            if (!blinking && Due(now, nextBlink)) { blinking = true; blinkEnd = now + 140; }
            else if (blinking && Due(now, blinkEnd)) { blinking = false; nextBlink = now + rnd.Next(2000, 6000); }

            // where the eyes look
            int ldx, ldy;
            double lookX = cur.X - (cat.X + cat.Width / 2.0), lookY = cur.Y - (cat.Y + 15 * scale);
            double vd = Math.Sqrt(lookX * lookX + lookY * lookY);
            if (yarn != null && (yarn.Moving || (act == Act.Walk && goal == Goal.Yarn)))
            {   // eyes glued to the ball
                double yx = yarn.CenterX - (cat.X + cat.Width / 2.0), yy = yarn.Y + yarn.Height / 2.0 - (cat.Y + 15 * scale);
                double yd = Math.Max(1, Math.Sqrt(yx * yx + yy * yy));
                ldx = (int)Math.Round(yx / yd * 3); ldy = (int)Math.Round(yy / yd * 3);
            }
            else if (act == Act.Walk && goal == Goal.Laser) { ldx = (int)Math.Round(lookX / Math.Max(1, vd) * 3); ldy = (int)Math.Round(lookY / Math.Max(1, vd) * 3); }
            else if (act == Act.Walk && !st.Follow) { ldx = tx > px ? 3 : -3; ldy = 0; }
            else if (vd < 600 && vd > 1) { ldx = (int)Math.Round(lookX / vd * 3); ldy = (int)Math.Round(lookY / vd * 3); }
            else
            {
                if (Due(now, glanceAt)) { glanceDx = rnd.Next(-2, 3); glanceDy = rnd.Next(-1, 2); glanceAt = now + rnd.Next(2000, 5000); }
                ldx = glanceDx; ldy = glanceDy;
            }

            // hearts float up
            look.Hearts.Clear();
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                int age = Since(now, hearts[i][1]);
                if (age > 1500) { hearts.RemoveAt(i); continue; }
                look.Hearts.Add(new Point(hearts[i][0], Art.OY + 2 - age * 14 / 1500));
            }

            // hop
            int hop = 0, hopAge = Since(now, hopStart);
            if (hopAge >= 0 && hopAge < 450) hop = -(int)Math.Round(Math.Sin(Math.PI * hopAge / 450.0) * 6);

            // default frame
            Expr ex;
            if (act == Act.Held) ex = dizzy ? Expr.Cry : Expr.Surprised;
            else if (act == Act.Fall) ex = Expr.Surprised;
            else if (act == Act.Sleep) ex = Since(now, sleepStart) < 2500 ? Expr.Drowsy : Expr.Sleep;
            else if (hasEmote) ex = emote;
            else if (act == Act.Idle && st.Hunger > 85) ex = Expr.Beg;
            else ex = Expr.Wide;

            int anim = now & int.MaxValue;
            int feet = act == Act.Walk ? (stepLeft ? 1 : 2) : 0;
            int bob = ex == Expr.Sleep ? (anim / 1200) % 2 : 0;
            int lift = 0, ear = 0, pawDy = 0;
            bool paw = false, tongue = false, chomp = false, blink = blinking;

            // eating
            if (act == Act.Eat)
            {
                int age = Since(now, eatStart);
                if (age < 4000)
                {
                    ex = Expr.Eat;
                    chomp = (age / 250) % 2 == 0;
                    bob = chomp ? 2 : 1;
                    if (Due(now, nextBite)) { Sounds.Play("crunch"); nextBite = now + 500; }
                    if (bowl != null) bowl.SetGrid(age < 1300 ? Art.BowlFull : age < 2600 ? Art.BowlHalf : Art.BowlEmpty);
                }
                else FinishEat(now);
            }

            // tricks
            if (act == Act.Trick)
            {
                int age = Since(now, trickStart);
                bool done = age >= trickLen;
                switch (trick)
                {
                    case Trick.Stretch:
                        if (age < 400) { lift = age * 4 / 400; ex = Expr.Content; }
                        else if (age < 1500) { lift = 4; ex = Expr.Content; }
                        else if (age < 1900) { lift = 4 - (age - 1500) * 4 / 400; ex = Expr.Content; }
                        else ex = Expr.Drowsy;
                        if (done) EndDeed(now, 3000, 7000);
                        break;
                    case Trick.Groom:
                        ex = Expr.Content; paw = true;
                        pawDy = (age / 220) % 2; tongue = pawDy == 0;
                        if (done) EndDeed(now, 3000, 7000);
                        break;
                    case Trick.Wiggle:
                        { int[] seq = { 1, 0, 2, 0 }; ear = seq[(age / 150) % 4]; }
                        if (done) EndDeed(now, 2000, 6000);
                        break;
                    case Trick.Hunt:
                        if (age < huntCrouch)
                        {   // crouch and wiggle, eyes locked on the cursor
                            bob = 2; feet = ((age / 110) % 2) + 1; ex = Expr.Wide;
                        }
                        else if (!done)
                        {   // pounce!
                            if (!pounceSet)
                            {
                                pounceFromX = px; pounceFromY = py;
                                double gx = cur.X - Width / 2.0, gy = cur.Y - (Art.OY + Art.BodyH) * scale;
                                double ddx = gx - px, ddy = gy - py, dd = Math.Sqrt(ddx * ddx + ddy * ddy);
                                if (dd > 300) { gx = px + ddx / dd * 300; gy = py + ddy / dd * 300; }
                                huntMiss = laser == null && Roll(Brain.GoofMiss);
                                if (huntMiss) { gx = px + (gx - px) * 0.55; gy = py + (gy - py) * 0.55; } // falls short
                                ClampPos(ref gx, ref gy);
                                pounceToX = gx; pounceToY = gy; pounceSet = true;
                            }
                            double t = (age - huntCrouch) / 400.0;
                            px = pounceFromX + (pounceToX - pounceFromX) * t;
                            py = pounceFromY + (pounceToY - pounceFromY) * t;
                            hop = -(int)Math.Round(Math.Sin(Math.PI * t) * 8);
                            ex = Expr.Wide;
                            Point np = new Point((int)Math.Round(px), (int)Math.Round(py));
                            if (np != Location) Location = np;
                        }
                        else
                        {
                            if (pounceSet) { px = pounceToX; py = pounceToY; }
                            Location = new Point((int)Math.Round(px), (int)Math.Round(py));
                            if (laser != null)
                            {   // the dot always gets away
                                act = Act.Idle;
                                if (Roll(0.25)) Say(rnd.Next(2) == 0 ? "Almost!" : "Grr!", 1000, now);
                                break;
                            }
                            st.Count(Store.Played);
                            st.Change(ref st.Fun, 8);
                            st.Change(ref st.Energy, -2);
                            GainXp(3, now);
                            EndDeed(now, 3000, 7000);
                            if (huntMiss)
                            {   // dumbo moment: faceplant
                                SetEmote(Expr.Dizzy, 1800, now);
                                Say("Meant that.", 2000, now);
                                Goof();
                            }
                            else
                            {
                                SetEmote(rnd.Next(2) == 0 ? Expr.Happy : Expr.Smug, 1800, now);
                                if (Roll(0.5)) Say("Got it!", 1800, now);
                                Sounds.Play("mrrp");
                            }
                        }
                        break;
                    case Trick.ChaseTail:
                        tail = ((age / 260) % 2) + 1;
                        ldx = tail == 1 ? 3 : -3; ldy = 2;
                        feet = ((age / 120) % 2) + 1;
                        if (age % 520 < 130) hop = -1;
                        if (done)
                        {
                            st.Count(Store.Played);
                            st.Change(ref st.Fun, 5);
                            GainXp(3, now);
                            EndDeed(now, 3000, 7000);
                            if (Roll(Brain.GoofDizzy)) { SetEmote(Expr.Dizzy, 1800, now); Say("Whoa...", 1600, now); Goof(); }
                            else SetEmote(Expr.Smug, 2000, now);
                        }
                        break;
                    case Trick.Stare:
                        ex = Expr.Wide; blink = false; ldx = stareDx; ldy = stareDy;
                        if (done)
                        {
                            if (Roll(Brain.GoofPanic))
                            {   // saw a ghost
                                Say("!!", 1200, now);
                                Hop(now);
                                Goof();
                                StartZoomies(now);
                            }
                            else EndDeed(now, 2000, 6000);
                        }
                        break;
                    case Trick.Trip:
                        if (age < 250) { ex = Expr.Surprised; hop = -2; }
                        else { ex = Expr.Dizzy; bob = 3; }
                        if (done) { act = Act.Walk; stepAt = now; }
                        break;
                    case Trick.Bonk:
                        ex = Expr.Dizzy;
                        if (age < 300) hop = age < 150 ? -2 : -1;
                        if (done) EndDeed(now, 3000, 7000);
                        break;
                    case Trick.Forget:
                        ex = Expr.Wide; ldx = (age / 600) % 2 == 0 ? -3 : 3; ldy = 0;
                        if (done) EndDeed(now, 2000, 5000);
                        break;
                    case Trick.Beg:
                        ex = Expr.Beg;
                        if (done) EndDeed(now, 4000, 8000);
                        break;
                    case Trick.Land:
                        bob = age < 200 ? 2 : 1;   // squish!
                        ex = hasEmote ? emote : Expr.Content;
                        if (done) act = Act.Idle;
                        break;
                    case Trick.Jump:
                        {
                            double t = Math.Min(1.0, age / 650.0);
                            px = jumpFromX + (jumpToX - jumpFromX) * t;
                            py = jumpFromY + (jumpToY - jumpFromY) * t - 4 * jumpH * t * (1 - t);
                            ex = t < 0.5 ? Expr.Wide : Expr.Surprised;
                            ldx = jumpToX > jumpFromX ? 3 : -3; ldy = t < 0.5 ? -2 : 2;
                            tail = jumpToX > jumpFromX ? 2 : 1;
                            Location = new Point((int)Math.Round(px), (int)Math.Round(py));
                            if (done)
                            {
                                act = Act.Idle;
                                decideAt = now + rnd.Next(2500, 6000);
                                if (jumpHwnd != IntPtr.Zero && Desktop.IsUsable(jumpHwnd))
                                {
                                    standHwnd = jumpHwnd;
                                    Desktop.GetFrame(jumpHwnd, out standRect);
                                    py = standRect.Top - Height;   // snap onto the edge
                                    Location = new Point((int)Math.Round(px), (int)Math.Round(py));
                                    Sounds.Play("thump");
                                    if (Roll(0.4)) SetEmote(Expr.Smug, 1800, now);
                                }
                                else if (jumpShort) { Say("Oops!", 1200, now); Goof(); }
                            }
                        }
                        break;
                    case Trick.Dance:
                        {
                            bool beats = Since(now, lastBeat) < 900;
                            int step = beats ? beatCount : age / 400;          // follow the beat, or a steady groove
                            bob = step % 2;
                            feet = step % 2 + 1;
                            int[] earSeq = { 0, 1, 0, 2 };
                            ear = earSeq[step % 4];
                            ex = (step / 4) % 2 == 0 ? Expr.Content : Expr.Smug;
                            if (step % 8 == 7 && Since(now, lastBeat) < 150) hop = -2;
                            ldx = step % 4 < 2 ? -2 : 2; ldy = 0;
                            if (Due(now, nextNote))
                            {
                                notes.Add(new int[] { rnd.Next(2) == 0 ? Art.OX - 3 + rnd.Next(4) : Art.OX + Art.BodyW - 1 + rnd.Next(4), now });
                                nextNote = now + rnd.Next(500, 900);
                            }
                        }
                        break;
                    case Trick.Tangled:
                        ex = (age / 700) % 2 == 0 ? Expr.Surprised : Expr.Cry;
                        feet = ((age / 200) % 2) + 1;   // wriggling
                        if (done) { EndDeed(now, 3000, 7000); Say("Free!", 1500, now); SetEmote(Expr.Smug, 1500, now); }
                        break;
                }
            }

            look.Expr = ex;
            look.Dx = Math.Max(-3, Math.Min(3, ldx));
            look.Dy = Math.Max(-3, Math.Min(3, ldy));
            look.Blink = blink;
            look.Feet = feet;
            look.Bob = bob;
            look.Hop = hop;
            look.Lift = lift;
            look.Ear = ear;
            look.Paw = paw; look.PawDy = pawDy; look.Tongue = tongue; look.Chomp = chomp;
            look.Notes.Clear();
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                int age = Since(now, notes[i][1]);
                if (age > 1600) { notes.RemoveAt(i); continue; }
                look.Notes.Add(new Point(notes[i][0] + (age / 400) % 2, Art.OY + 10 - age * 14 / 1600));
            }

            look.Tail = tail;
            look.Hat = curHat;
            look.Tangled = act == Act.Trick && trick == Trick.Tangled;
            look.Stars = ex == Expr.Dizzy;
            look.AnimMs = anim;
            look.Zzz = ex == Expr.Sleep;
            look.Bubble = bubble;

            int sig = look.Sig();
            if (sig != lastSig) { lastSig = sig; Invalidate(); }

            bool busy = act == Act.Walk || act == Act.Held || act == Act.Trick || act == Act.Eat || act == Act.Fall || notes.Count > 0
                     || hearts.Count > 0 || hop != 0 || ex == Expr.Cry || ex == Expr.Dizzy
                     || laser != null || (yarn != null && (yarn.Moving || yarn.VY != 0));
            // fast moves get smooth 25 fps; a calm stroll or dance only needs ~16 fps; resting ~8 fps
            bool fast = act == Act.Fall || act == Act.Held || (act == Act.Trick && (trick == Trick.Jump || trick == Trick.Hunt))
                     || (act == Act.Walk && goal != Goal.Wander && goal != Goal.Bowl) || laser != null || (yarn != null && yarn.Moving);
            int want = fast ? 40 : busy ? 60 : (act == Act.Sleep ? 150 : 120);
            if (timer.Interval != want) timer.Interval = want;

            if (Due(now, nextSave)) { SaveAll(); nextSave = now + 60000; }
            if (Due(now, nextTrim)) { TrimMemory(); nextTrim = now + 120000; }
        }

        // Hands unused memory back to Windows so the pet stays light.
        static void TrimMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Art.Draw(e.Graphics, scale, look);
        }

        // ---------- mouse ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mouseDown = true; dragging = false; dizzy = false; shake = 0;
                downScreen = Cursor.Position; dragOff = e.Location; lastMoveMs = Environment.TickCount;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mouseDown)
            {
                Point c = Cursor.Position;
                if (!dragging && (Math.Abs(c.X - downScreen.X) > 4 || Math.Abs(c.Y - downScreen.Y) > 4))
                {
                    dragging = true;
                    act = Act.Held;
                    bubble = null;
                    standHwnd = IntPtr.Zero;
                    dragVx = 0; dragVy = 0;
                }
                if (dragging)
                {
                    int now = Environment.TickCount;
                    int nx = c.X - dragOff.X, ny = c.Y - dragOff.Y;
                    double moved = Math.Abs(nx - Left) + Math.Abs(ny - Top);
                    double dtm = Math.Max(0, Since(now, lastMoveMs)) / 1000.0;
                    if (dtm > 0.005) { dragVx = 0.5 * dragVx + 0.5 * (nx - Left) / dtm; dragVy = 0.5 * dragVy + 0.5 * (ny - Top) / dtm; }
                    shake = shake * Math.Exp(-dtm * 2) + moved;
                    lastMoveMs = now;
                    if (shake > 2500) dizzy = true;
                    Location = new Point(nx, ny);
                    px = nx; py = ny;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                int now = Environment.TickCount;
                if (dragging)
                {
                    act = Act.Idle;
                    decideAt = now + 6000; // rest a bit where you dropped him
                    if (dizzy) { SetEmote(Expr.Cry, 6000, now); Say("So dizzy...", 3000, now); Sounds.Play("sad"); }
                    if (Gravity)
                    {   // let go in the air -> he falls; flick the mouse -> he gets thrown
                        bool flung = Since(now, lastMoveMs) < 120 && Math.Abs(dragVx) + Math.Abs(dragVy) > 600;
                        vx = flung ? Math.Max(-1500, Math.Min(1500, dragVx * 0.6)) : 0;
                        vy = flung ? Math.Max(-1500, Math.Min(1500, dragVy * 0.6)) : 0;
                        if (flung) { act = Act.Fall; fallFromY = py; standHwnd = IntPtr.Zero; Say("Wheee!", 1200, now); }
                    }
                    SaveAll();
                }
                else Pet(now);
                mouseDown = false; dragging = false; dizzy = false;
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && act != Act.Sleep) Hop(Environment.TickCount);
            base.OnMouseDoubleClick(e);
        }

        // ---------- menu ----------
        void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            ToolStripMenuItem miName = new ToolStripMenuItem(st.Name);
            miName.Enabled = false;
            miName.Font = new Font(menu.Font, FontStyle.Bold);

            ToolStripMenuItem miFeed = new ToolStripMenuItem("Feed");
            miFeed.Click += delegate { Feed(); };

            ToolStripMenuItem miJournal = new ToolStripMenuItem("Journal");
            miJournal.Click += delegate { OpenJournal(); };

            ToolStripMenuItem miToys = new ToolStripMenuItem("Toys");
            miYarn = new ToolStripMenuItem("Yarn ball");
            miYarn.Click += delegate { ToggleYarn(); };
            ToolStripMenuItem miLaser = new ToolStripMenuItem("Laser pointer (1 min)");
            miLaser.Click += delegate { StartLaser(); };
            miToys.DropDownItems.Add(miYarn);
            miToys.DropDownItems.Add(miLaser);

            miHat = new ToolStripMenuItem("Hat");
            miHat.DropDownItems.Add("(loading)");
            miHat.DropDownOpening += delegate { FillHatMenu(); };

            miStay = new ToolStripMenuItem("Stay here");
            miStay.Checked = st.Stay;
            miStay.Click += delegate
            {
                st.Stay = !st.Stay; miStay.Checked = st.Stay;
                if (st.Stay) { st.Follow = false; miFollow.Checked = false; if (act == Act.Walk && goal != Goal.Bowl) act = Act.Idle; }
                SaveAll();
            };

            miFollow = new ToolStripMenuItem("Follow my mouse");
            miFollow.Checked = st.Follow;
            miFollow.Click += delegate
            {
                st.Follow = !st.Follow; miFollow.Checked = st.Follow;
                if (st.Follow) { st.Stay = false; miStay.Checked = false; }
                if (act == Act.Walk && goal != Goal.Bowl) act = Act.Idle;
                SaveAll();
            };

            miGravity = new ToolStripMenuItem("Gravity (sits on windows)");
            miGravity.Checked = st.Gravity;
            miGravity.Click += delegate
            {
                st.Gravity = !st.Gravity; miGravity.Checked = st.Gravity;
                standHwnd = IntPtr.Zero;
                if (act == Act.Fall) { act = Act.Idle; vx = vy = 0; }
                SaveAll();
            };

            ToolStripMenuItem miNap = new ToolStripMenuItem("Take a nap");
            miNap.Click += delegate { StartSleep(SleepWhy.Forced, Environment.TickCount); };

            ToolStripMenuItem miSize = new ToolStripMenuItem("Size");
            string[] sizeNames = { "Small", "Medium", "Large" };
            for (int i = 0; i < 3; i++)
            {
                int sc = i + 2;
                ToolStripMenuItem it = new ToolStripMenuItem(sizeNames[i]);
                it.Checked = sc == scale;
                it.Click += delegate { SetScale(sc); };
                miSizes.Add(it);
                miSize.DropDownItems.Add(it);
            }

            miMute = new ToolStripMenuItem("Mute sounds");
            miMute.Checked = Sounds.Muted;
            miMute.Click += delegate { Sounds.Muted = !Sounds.Muted; miMute.Checked = Sounds.Muted; SaveAll(); };

            miBreaks = new ToolStripMenuItem("Break reminders");
            miBreaks.Checked = st.Breaks;
            miBreaks.Click += delegate { st.Breaks = !st.Breaks; miBreaks.Checked = st.Breaks; SaveAll(); };

            miStartup = new ToolStripMenuItem("Start with Windows");
            miStartup.Checked = IsStartup();
            miStartup.Click += delegate { SetStartup(!miStartup.Checked); miStartup.Checked = IsStartup(); };

            menu.Items.Add(miName);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miFeed);
            menu.Items.Add(miToys);
            menu.Items.Add(miHat);
            menu.Items.Add(miJournal);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miStay);
            menu.Items.Add(miFollow);
            menu.Items.Add(miGravity);
            menu.Items.Add(miNap);
            menu.Items.Add(miSize);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miMute);
            menu.Items.Add(miBreaks);
            menu.Items.Add(miStartup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Close(); });
            ContextMenuStrip = menu;
        }

        void FillHatMenu()
        {
            miHat.DropDownItems.Clear();
            AddHatItem("Auto (seasonal / best)", "auto", true);
            AddHatItem("None", "none", true);
            miHat.DropDownItems.Add(new ToolStripSeparator());
            for (int h = 1; h < Art.HatNames.Length; h++)
            {
                bool open = st.HatUnlocked(h);
                string label = Art.HatNames[h];
                if (h == Art.HatSanta) label += "  (December)";
                else if (h == Art.HatWitch) label += "  (Halloween)";
                else if (!open) label += "  (Lv " + Art.HatLevel[h] + ")";
                AddHatItem(label, h.ToString(), open);
            }
            miHat.DropDownItems.Add(new ToolStripSeparator());
            int lv = st.Level;
            string next = lv >= Store.LevelXp.Length ? "max level!" : (Store.LevelXp[lv] - st.Xp) + " XP to Lv " + (lv + 1);
            ToolStripMenuItem info = new ToolStripMenuItem("Bond Lv " + lv + "  ·  " + next);
            info.Enabled = false;
            miHat.DropDownItems.Add(info);
        }

        void AddHatItem(string label, string value, bool enabled)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(label);
            it.Enabled = enabled;
            it.Checked = st.Hat == value;
            it.Click += delegate
            {
                st.Hat = value;
                curHat = st.CurrentHat();
                lastSig = int.MinValue;
                SaveAll();
            };
            miHat.DropDownItems.Add(it);
        }

        void OpenJournal()
        {
            if (journal == null || journal.IsDisposed) { journal = new JournalForm(st); journal.Show(); }
            else journal.Activate();
        }

        void SetScale(int sc)
        {
            // keep his feet in the same spot while resizing
            double footX = px + Width / 2.0, footY = py + Height;
            scale = sc; st.Scale = sc;
            ClientSize = new Size(Art.CW * scale, Art.CH * scale);
            px = footX - Width / 2.0; py = footY - Height;
            ClampPos(ref px, ref py);
            Location = new Point((int)px, (int)py);
            for (int i = 0; i < miSizes.Count; i++) miSizes[i].Checked = (i + 2) == scale;
            if (bowl != null) { bowl.Close(); bowl.Dispose(); bowl = null; if (act == Act.Walk && goal == Goal.Bowl) act = Act.Idle; }
            RemoveYarn();
            if (laser != null) { laser.Close(); laser.Dispose(); laser = null; }
            if (act == Act.Walk && goal == Goal.Laser) act = Act.Idle;
            lastSig = int.MinValue;
            Invalidate();
            SaveAll();
        }

        static bool IsStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("CatPet") != null;
            }
            catch { return false; }
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("CatPet", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("CatPet", false);
                }
            }
            catch { }
        }

        void SaveAll()
        {
            st.X = Left; st.Y = Top;
            st.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            SaveAll();
            if (bowl != null) bowl.Close();
            if (yarn != null) yarn.Close();
            if (laser != null) laser.Close();
            if (journal != null && !journal.IsDisposed) journal.Close();
            base.OnFormClosed(e);
        }
    }
}
