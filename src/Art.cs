using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CatPet
{
    public enum Expr { Wide, Surprised, Cry, Smug, Drowsy, Cheeky, Happy, Sleep, Content, Dizzy, Beg, Eat }

    // Everything needed to draw one frame.
    public class Look
    {
        public Expr Expr;
        public int Dx, Dy;        // pupil offset (-3..3)
        public bool Blink, Zzz;
        public int Feet;          // 0 stand, 1 left paw up, 2 right paw up
        public int Bob, Hop;      // body offsets in cells (Hop < 0 = in the air, feet move too)
        public int Lift;          // body raised while paws stay on the ground (stretch)
        public int Ear;           // 0 normal, 1 left ear folded, 2 right ear folded
        public bool Paw, Tongue;  // grooming
        public int PawDy;
        public int Tail;          // 0 hidden, 1 sticking out right, 2 left
        public bool Stars;        // dizzy stars circling the head
        public bool Chomp;        // eating: mouth open
        public int Hat;           // index into Art.HatNames (0 = none)
        public bool Tangled;      // wrapped in yarn
        public int AnimMs;        // drives tears, stars and Zzz
        public string Bubble;
        public List<Point> Hearts = new List<Point>();
        public List<Point> Notes = new List<Point>();   // floating music notes

        public int Sig()
        {
            unchecked
            {
                int h = (int)Expr;
                h = h * 31 + Dx; h = h * 31 + Dy; h = h * 31 + (Blink ? 1 : 0);
                h = h * 31 + Feet; h = h * 31 + Bob; h = h * 31 + Hop; h = h * 31 + Lift;
                h = h * 31 + Ear; h = h * 31 + (Paw ? 1 : 0) + PawDy * 2 + (Tongue ? 4 : 0) + (Chomp ? 8 : 0);
                h = h * 31 + Tail; h = h * 31 + Hat; h = h * 31 + (Tangled ? 1 : 0);
                if (Expr == Expr.Cry) h = h * 31 + (AnimMs / 90) % 8;
                if (Zzz) h = h * 31 + AnimMs / 120;
                if (Stars) h = h * 31 + AnimMs / 80;
                h = h * 31 + (Bubble == null ? 0 : Bubble.GetHashCode());
                foreach (Point p in Hearts) h = h * 31 + p.X * 1000 + p.Y;
                foreach (Point p in Notes) h = h * 37 + p.X * 1000 + p.Y;
                return h;
            }
        }
    }

    // Pixel art. Every letter is one "cell"; a cell is drawn as a Scale x Scale square.
    public static class Art
    {
        public const int CW = 56, CH = 49;   // canvas in cells (room above the cat for bubbles / Zzz / hearts)
        public const int OX = 10, OY = 18;   // where the cat's body starts on the canvas
        public const int BodyW = 36, BodyH = 29;

        // Left half of the body; the right half is mirrored automatically.
        static readonly string[] BodyHalf = {
            "......KK..........",
            ".....KKKK.........",
            ".....KPPKK........",
            "....KKPPPKK.......",
            "....KPPPPPKKKKKKKK",
            "....KPPPPPKKKKKKKK",
            "...KKPPPPKKKKKKKKK",
            "...KKKPPKKKKKKKKKK",
            "..KKKKKKKKKKKKKKKK",
            "..KKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            "KKKKKKKKKKKKKKKKKK",
            "KKKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            ".KKKKKKKKKKKKKKKKK",
            "..KKKKKKKKKKKKKKKK",
            "..KKKKKKKKKKKKKKKK",
            "...KKKKKKKKKKKKKKK",
            "....KKKKKKKKKKKKKK",
            ".....KKKKKKKKKKKKK",
        };

        static readonly string[] WideEye = {
            "...OOOOO...",
            ".OOOOOOOOO.",
            ".OOOOOOOOO.",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            "OOOOOOOOOOO",
            ".OOOOOOOOO.",
            ".OOOOOOOOO.",
            "...OOOOO...",
        };
        static readonly string[] BlinkEye = { ".OOOOOOOOO." };
        static readonly string[] Pupil = { ".KK.", "KKWK", "KKKK", ".KK." };
        static readonly string[] SmugEye = {      // half-lidded, from reference 1
            "OOOWKKKKOO",
            "OOOKKKKKOO",
            ".OOOOKOOO.",
            ".OOOOOOO..",
            "...OOO....",
        };
        static readonly string[] DrowsyEye = { SmugEye[2], SmugEye[3], SmugEye[4] };
        static readonly string[] AnnoyedEye = {   // side-eye, from reference 4
            "OOOOOOKKKO",
            "OOOOOOKKKO",
            ".OOOOOOOO.",
            "...OOOO...",
        };
        static readonly string[] XEye = { "OO.OO", ".OOO.", "..O..", ".OOO.", "OO.OO" };
        static readonly string[] Brow = { "BBBB......", "....BBBBB." };
        static readonly string[] HappyEye = { "...OOOOO...", ".OO.....OO.", "O.........O" };
        static readonly string[] SleepEye = { "O.........O", ".OO.....OO.", "...OOOOO..." };
        static readonly string[] Tongue = { "PPPP", "PPPP", ".PP." };
        static readonly string[] CryMouth = { "PPPP", "PPPP", "PPPP" };
        static readonly string[] YawnMouth = { ".PP.", "PPPP", "PPPP", ".PP." };
        static readonly string[] ChompOpen = { "PPPP", "PPPP" };
        static readonly string[] ChompShut = { ".PP." };
        static readonly string[] SmallO = { "PP", "PP" };
        static readonly string[] Wobbly = { "P.P.P", ".P.P." };
        static readonly string[] WMouth = { "P.PP.P", ".P..P." };
        static readonly string[] Blush = { "PPP" };
        static readonly string[] PawUp = { ".KKK.", "KPKPK", "KKKKK", "KKPKK", "KKKKK", ".KKK." };
        static readonly string[] TailR = {        // sticks out to the right, curly tip
            "......KK",
            ".....KKK",
            ".....KK.",
            "....KK..",
            "....KK..",
            "...KK...",
            "..KKK...",
            "KKKK....",
            "KKK.....",
        };
        static readonly string[] Heart = { ".HH.HH.", "HHHHHHH", "HHHHHHH", ".HHHHH.", "..HHH..", "...H..." };
        static readonly string[] Star = { ".S.", "SSS", ".S." };
        static readonly string[] Note = { "..nnn", "..n.n", "..n..", "nnn..", "nnn.." };
        static readonly string[] ZBig = { "ZZZZ", "..Z.", ".Z..", "ZZZZ" };
        static readonly string[] ZSmall = { "ZZZ", ".Z.", "ZZZ" };

        // Food bowl (14 x 6), shown in its own little window.
        public static readonly string[] BowlFull = {
            "...FfFFfFFf...",
            "..fFFfFFfFFf..",
            "CCCCCCCCCCCCCC",
            "CLLLLLLLLLLLLC",
            ".CCCCCCCCCCCC.",
            "..CCCCCCCCCC..",
        };
        public static readonly string[] BowlHalf = {
            "..............",
            "...fFFfFFfF...",
            BowlFull[2], BowlFull[3], BowlFull[4], BowlFull[5],
        };
        public static readonly string[] BowlEmpty = {
            "..............",
            "..............",
            BowlFull[2], BowlFull[3], BowlFull[4], BowlFull[5],
        };

        // ---- hats (drawn on the head, between the ears) ----
        public static readonly string[] HatNames = { "None", "Bow", "Flower", "Party hat", "Headphones", "Crown", "Santa hat", "Witch hat" };
        public static readonly int[] HatLevel = { 0, 2, 3, 4, 6, 8, 0, 0 };   // bond level needed (seasonal ones are free)
        public const int HatSanta = 6, HatWitch = 7, HatParty = 3;

        static readonly string[] HatBow = {
            "QQ.....QQ",
            "QQQQ.QQQQ",
            "QQQQqQQQQ",
            "QQQQ.QQQQ",
            "QQ.....QQ",
        };
        static readonly string[] HatFlower = {
            "..NNN..",
            ".NNNNN.",
            "NNNYNNN",
            "NNYYYNN",
            "NNNYNNN",
            ".NNNNN.",
            "..NNN..",
        };
        static readonly string[] HatPartyCone = {
            "....Y....",
            "...YYY...",
            "....R....",
            "...RRR...",
            "...YYY...",
            "..RRRRR..",
            "..YYYYY..",
            ".RRRRRRR.",
            ".YYYYYYY.",
            "RRRRRRRRR",
        };
        static readonly string[] HatCrown = {
            "Y....Y....Y",
            "YY..YYY..YY",
            "YYYYYYYYYYY",
            "YRYYYbYYYRY",
            "YYYYYYYYYYY",
        };
        static readonly string[] HatSantaCone = {
            "........NN..",
            ".......RNN..",
            "......RRR...",
            ".....RRRR...",
            "....RRRRR...",
            "...RRRRRRR..",
            ".NNNNNNNNNN.",
            ".NNNNNNNNNN.",
        };
        static readonly string[] HatWitchCone = {
            ".......UU....",
            "......UUU....",
            ".....UUUU....",
            ".....UUUUU...",
            "....UUUUUU...",
            "....UYYYYU...",
            "...UUUUUUUU..",
            "UUUUUUUUUUUUU",
        };

        // ---- toys ----
        public static readonly string[] YarnA = {
            "..QQQQ..",
            ".QqQQqQ.",
            "QQQqqQQQ",
            "QqQQQqQQ",
            "QQqQQQqQ",
            "QQQqqQQQ",
            ".QqQQqQ.",
            "..QQQQ..",
        };
        public static readonly string[] YarnB = Rotate(YarnA);
        public static readonly string[] LaserDot = { ".xxx.", "xXXXx", "xXXXx", "xXXXx", ".xxx." };

        static string[] Rotate(string[] g)
        {
            int h = g.Length, w = g[0].Length;
            string[] r = new string[w];
            for (int x = 0; x < w; x++)
            {
                char[] row = new char[h];
                for (int y = 0; y < h; y++) row[y] = g[h - 1 - y][x];
                r[x] = new string(row);
            }
            return r;
        }

        static readonly Dictionary<char, SolidBrush> brushes = new Dictionary<char, SolidBrush> {
            { 'Q', new SolidBrush(Color.FromArgb(255, 125, 170)) }, // pink (bow, yarn)
            { 'q', new SolidBrush(Color.FromArgb(200, 70, 120)) },  // dark pink
            { 'N', new SolidBrush(Color.FromArgb(250, 250, 250)) }, // white (flower, santa trim)
            { 'Y', new SolidBrush(Color.FromArgb(250, 200, 60)) },  // gold / yellow
            { 'R', new SolidBrush(Color.FromArgb(220, 60, 70)) },   // red
            { 'U', new SolidBrush(Color.FromArgb(130, 80, 175)) },  // purple
            { 'b', new SolidBrush(Color.FromArgb(80, 140, 230)) },  // blue gem
            { 'M', new SolidBrush(Color.FromArgb(150, 150, 165)) }, // headphones
            { 'X', new SolidBrush(Color.FromArgb(255, 30, 30)) },   // laser
            { 'x', new SolidBrush(Color.FromArgb(255, 140, 140)) }, // laser glow
            { 'n', new SolidBrush(Color.FromArgb(120, 215, 255)) }, // music notes
            { 'K', new SolidBrush(Color.FromArgb(16, 16, 16)) },    // fur
            { 'P', new SolidBrush(Color.FromArgb(240, 128, 112)) }, // ears / tongue / toe beans
            { 'O', new SolidBrush(Color.FromArgb(247, 172, 52)) },  // eyes
            { 'W', new SolidBrush(Color.White) },                   // eye shine
            { 'B', new SolidBrush(Color.FromArgb(110, 50, 40)) },   // brow
            { 'T', new SolidBrush(Color.FromArgb(190, 230, 255)) }, // tears
            { 'H', new SolidBrush(Color.FromArgb(255, 95, 125)) },  // hearts
            { 'Z', new SolidBrush(Color.FromArgb(225, 225, 255)) }, // Zzz
            { 'S', new SolidBrush(Color.FromArgb(255, 222, 90)) },  // dizzy stars
            { 'C', new SolidBrush(Color.FromArgb(222, 78, 78)) },   // bowl
            { 'L', new SolidBrush(Color.FromArgb(255, 236, 236)) }, // bowl stripe
            { 'F', new SolidBrush(Color.FromArgb(160, 98, 50)) },   // kibble
            { 'f', new SolidBrush(Color.FromArgb(210, 150, 90)) },  // kibble light
        };

        static readonly Bitmap[] bodyBmp = new Bitmap[3]; // per ear pose
        static int bodyScale;
        static Font font;
        static int fontScale;

        static string[] BodyRows(int ear)
        {
            string[] rows = new string[BodyHalf.Length];
            for (int i = 0; i < BodyHalf.Length; i++)
            {
                char[] r = BodyHalf[i].ToCharArray();
                Array.Reverse(r);
                string left = BodyHalf[i], right = new string(r);
                if (i < 2 && ear == 1) left = new string('.', left.Length);   // fold the ear down
                if (i < 2 && ear == 2) right = new string('.', right.Length);
                rows[i] = left + right;
            }
            return rows;
        }

        public static void Paint(Graphics g, string[] rows, int x, int y, int s) { Grid(g, rows, x, y, s, false); }

        static void Grid(Graphics g, string[] rows, int x, int y, int s, bool mirror)
        {
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    char ch = row[c];
                    if (ch == '.') continue;
                    int cx = mirror ? x + row.Length - 1 - c : x + c;
                    g.FillRectangle(brushes[ch], cx * s, (y + r) * s, s, s);
                }
            }
        }

        static void Rect(Graphics g, char ch, int x, int y, int w, int h, int s)
        {
            g.FillRectangle(brushes[ch], x * s, y * s, w * s, h * s);
        }

        static Bitmap BodyBitmap(int s, int ear)
        {
            if (bodyScale != s)
            {
                for (int i = 0; i < 3; i++) if (bodyBmp[i] != null) { bodyBmp[i].Dispose(); bodyBmp[i] = null; }
                bodyScale = s;
            }
            if (bodyBmp[ear] == null)
            {
                Bitmap b = new Bitmap(BodyW * s, BodyH * s, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    Grid(g, BodyRows(ear), 0, 0, s, false);
                }
                bodyBmp[ear] = b;
            }
            return bodyBmp[ear];
        }

        public static void Draw(Graphics g, int s, Look L)
        {
            int bx = OX, by = OY + L.Bob + L.Hop - L.Lift;
            int fy = OY + BodyH + L.Hop;

            // tail (behind the body)
            if (L.Tail == 1) Grid(g, TailR, bx + 33, by + 15, s, false);
            if (L.Tail == 2) Grid(g, TailR, bx - 5, by + 15, s, true);

            // paws (they stay on the ground while the body lifts for a stretch)
            Rect(g, 'K', bx + 8, fy - 1 - L.Lift, 5, (L.Feet == 1 ? 2 : 3) + L.Lift, s);
            Rect(g, 'K', bx + 23, fy - 1 - L.Lift, 5, (L.Feet == 2 ? 2 : 3) + L.Lift, s);

            g.DrawImageUnscaled(BodyBitmap(s, L.Ear), bx * s, by * s);

            DrawHat(g, L.Hat, bx, by, s);

            switch (L.Expr)
            {
                case Expr.Wide:
                case Expr.Surprised:
                case Expr.Cry:
                case Expr.Beg:
                    if (L.Blink && L.Expr != Expr.Beg)
                    {
                        Grid(g, BlinkEye, bx + 5, by + 15, s, false);
                        Grid(g, BlinkEye, bx + 20, by + 15, s, false);
                    }
                    else
                    {
                        int dx = L.Dx, dy = L.Dy;
                        if (L.Expr == Expr.Surprised) { dx = 0; dy = 2; }
                        if (L.Expr == Expr.Cry || L.Expr == Expr.Beg) { dx = 0; dy = -2; }
                        Grid(g, WideEye, bx + 5, by + 10, s, false);
                        Grid(g, WideEye, bx + 20, by + 10, s, false);
                        Grid(g, Pupil, bx + 5 + 3 + dx, by + 10 + 3 + dy, s, false);
                        Grid(g, Pupil, bx + 20 + 3 + dx, by + 10 + 3 + dy, s, false);
                    }
                    if (L.Expr == Expr.Surprised) Grid(g, SmallO, bx + 17, by + 21, s, false);
                    if (L.Expr == Expr.Beg)
                    {   // big shiny "please" eyes
                        Rect(g, 'W', bx + 7, by + 16, 2, 1, s);
                        Rect(g, 'W', bx + 22, by + 16, 2, 1, s);
                        Rect(g, 'W', bx + 12, by + 17, 1, 1, s);
                        Rect(g, 'W', bx + 27, by + 17, 1, 1, s);
                        Grid(g, ChompShut, bx + 16, by + 21, s, false);
                    }
                    if (L.Expr == Expr.Cry)
                    {
                        Rect(g, 'W', bx + 7, by + 17, 1, 1, s);
                        Rect(g, 'W', bx + 28, by + 17, 1, 1, s);
                        Grid(g, CryMouth, bx + 16, by + 20, s, false);
                        int phase = (L.AnimMs / 90) % 8;
                        for (int k = 0; k < 8; k += 4)
                        {
                            int ty = 18 + (phase + k) % 8;
                            if (ty + 1 > BodyH - 2) continue;
                            Rect(g, 'T', bx + 6, by + ty, 1, 2, s);
                            Rect(g, 'T', bx + 29, by + ty, 1, 2, s);
                        }
                    }
                    break;
                case Expr.Smug:
                    Grid(g, SmugEye, bx + 5, by + 13, s, false);
                    Grid(g, SmugEye, bx + 21, by + 13, s, true);
                    break;
                case Expr.Drowsy:
                    Grid(g, DrowsyEye, bx + 5, by + 15, s, false);
                    Grid(g, DrowsyEye, bx + 21, by + 15, s, true);
                    Grid(g, YawnMouth, bx + 16, by + 20, s, false);
                    break;
                case Expr.Cheeky:
                    Grid(g, Brow, bx + 5, by + 12, s, false);
                    Grid(g, Brow, bx + 21, by + 12, s, true);
                    Grid(g, AnnoyedEye, bx + 5, by + 14, s, false);
                    Grid(g, AnnoyedEye, bx + 21, by + 14, s, false);
                    Grid(g, Tongue, bx + 16, by + 20, s, false);
                    break;
                case Expr.Happy:
                    Grid(g, HappyEye, bx + 5, by + 13, s, false);
                    Grid(g, HappyEye, bx + 20, by + 13, s, false);
                    Grid(g, Blush, bx + 3, by + 17, s, false);
                    Grid(g, Blush, bx + 30, by + 17, s, false);
                    Grid(g, WMouth, bx + 15, by + 19, s, false);
                    break;
                case Expr.Content:
                    Grid(g, HappyEye, bx + 5, by + 14, s, false);
                    Grid(g, HappyEye, bx + 20, by + 14, s, false);
                    break;
                case Expr.Eat:
                    Grid(g, HappyEye, bx + 5, by + 14, s, false);
                    Grid(g, HappyEye, bx + 20, by + 14, s, false);
                    Grid(g, L.Chomp ? ChompOpen : ChompShut, bx + 16, by + 20, s, false);
                    break;
                case Expr.Dizzy:
                    Grid(g, XEye, bx + 8, by + 13, s, false);
                    Grid(g, XEye, bx + 23, by + 13, s, false);
                    Grid(g, Wobbly, bx + 16, by + 21, s, false);
                    break;
                case Expr.Sleep:
                    Grid(g, SleepEye, bx + 5, by + 15, s, false);
                    Grid(g, SleepEye, bx + 20, by + 15, s, false);
                    break;
            }

            if (L.Tangled)
            {   // wrapped up in yarn strands
                for (int i = 0; i < 3; i++)
                    for (int x = 0; x < BodyW; x++)
                    {
                        int y = 9 + i * 6 + (int)Math.Round(Math.Sin((x + i * 7) / 5.0) * 2);
                        Rect(g, i == 1 ? 'q' : 'Q', bx + x, by + y, 1, 1, s);
                    }
            }

            if (L.Tongue) Grid(g, Tongue, bx + 16, by + 20, s, false);
            if (L.Paw) Grid(g, PawUp, bx - 2, by + 18 + L.PawDy, s, false);

            if (L.Stars)
            {
                for (int i = 0; i < 3; i++)
                {
                    double a = L.AnimMs / 250.0 + i * 2.094;
                    int sx = bx + 17 + (int)Math.Round(Math.Cos(a) * 13);
                    int sy = by + 2 + (int)Math.Round(Math.Sin(a) * 3);
                    Grid(g, Star, sx, sy, s, false);
                }
            }

            foreach (Point p in L.Hearts) Grid(g, Heart, p.X, p.Y, s, false);
            foreach (Point p in L.Notes) Grid(g, Note, p.X, p.Y, s, false);

            if (L.Zzz)
            {
                for (int i = 0; i < 3; i++)
                {
                    int t = (L.AnimMs + i * 1000) % 3000;
                    Grid(g, i == 1 ? ZBig : ZSmall, OX + 32 + t * 6 / 3000, OY + 1 - t * 12 / 3000, s, false);
                }
            }

            if (!string.IsNullOrEmpty(L.Bubble)) DrawBubble(g, s, L.Bubble);
        }

        static void DrawHat(Graphics g, int hat, int bx, int by, int s)
        {
            switch (hat)
            {
                case 1: Grid(g, HatBow, bx + 22, by + 2, s, false); break;
                case 2: Grid(g, HatFlower, bx + 22, by + 3, s, false); break;
                case 3: Grid(g, HatPartyCone, bx + 13, by - 5, s, false); break;
                case 4: // headphones: band over the head + ear cups
                    Rect(g, 'M', bx + 3, by + 3, 30, 1, s);
                    Rect(g, 'M', bx + 2, by + 4, 1, 2, s);
                    Rect(g, 'M', bx + 33, by + 4, 1, 2, s);
                    Rect(g, 'M', bx + 1, by + 6, 1, 6, s);
                    Rect(g, 'M', bx + 34, by + 6, 1, 6, s);
                    Rect(g, 'M', bx - 1, by + 11, 3, 7, s);
                    Rect(g, 'M', bx + 34, by + 11, 3, 7, s);
                    Rect(g, 'U', bx - 1, by + 12, 1, 5, s);
                    Rect(g, 'U', bx + 36, by + 12, 1, 5, s);
                    break;
                case 5: Grid(g, HatCrown, bx + 12, by, s, false); break;
                case 6: Grid(g, HatSantaCone, bx + 12, by - 2, s, false); break;
                case 7: Grid(g, HatWitchCone, bx + 11, by - 3, s, false); break;
            }
        }

        static void DrawBubble(Graphics g, int s, string text)
        {
            if (font == null || fontScale != s)
            {
                if (font != null) font.Dispose();
                font = new Font("Segoe UI", 5.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
                fontScale = s;
            }
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit; // crisp, no colour fringe
            SizeF ts = g.MeasureString(text, font);
            int pad = 3 * s;
            int w = ((int)ts.Width + 2 * pad) / s * s + s;
            int h = ((int)ts.Height + 2 * s) / s * s;
            int maxW = CW * s - 2 * s;
            if (w > maxW) w = maxW;
            int x = (CW * s - w) / 2 / s * s;
            int y = (OY - 3) * s - h;
            if (y < 0) y = 0;

            Brush K = brushes['K'], W = brushes['W'];
            g.FillRectangle(W, x + s, y + s, w - 2 * s, h - 2 * s);
            g.FillRectangle(K, x + s, y, w - 2 * s, s);
            g.FillRectangle(K, x + s, y + h - s, w - 2 * s, s);
            g.FillRectangle(K, x, y + s, s, h - 2 * s);
            g.FillRectangle(K, x + w - s, y + s, s, h - 2 * s);
            int cx = x + w / 2 / s * s;
            g.FillRectangle(K, cx - s, y + h, 2 * s, s);
            g.FillRectangle(K, cx - s, y + h + s, s, s);
            g.DrawString(text, font, K, x + pad - s, y + s);
        }

        public static void SavePreview(string path)
        {
            int s = 3, cw = CW * s, ch = CH * s, label = 22, cols = 5;
            string[] names = { "normal (looks at mouse)", "walking", "smug", "cheeky", "petted",
                               "crying", "yawning", "sleeping", "picked up", "break reminder",
                               "stretching", "grooming", "ear wiggle", "hunting cursor", "good morning",
                               "eating", "begging for food", "dizzy (goof!)", "chasing his tail", "zoomies",
                               "bow (Lv 2)", "flower (Lv 3)", "party hat (Lv 4)", "headphones (Lv 6)", "crown (Lv 8)",
                               "santa hat (December)", "witch hat (Halloween)", "tangled in yarn", "batting the yarn", "laser hunt",
                               "dancing to music", "falling!", "landing (squish)", "jumping onto a window", "music + headphones" };
            int n = names.Length;
            Look[] looks = new Look[n];
            for (int i = 0; i < n; i++) looks[i] = new Look();
            looks[0].Dx = -2; looks[0].Dy = 1;
            looks[1].Dx = 3; looks[1].Feet = 1;
            looks[2].Expr = Expr.Smug;
            looks[3].Expr = Expr.Cheeky;
            looks[4].Expr = Expr.Happy; looks[4].Hearts.Add(new Point(OX + 8, OY - 9)); looks[4].Hearts.Add(new Point(OX + 22, OY - 4));
            looks[5].Expr = Expr.Cry; looks[5].AnimMs = 180;
            looks[6].Expr = Expr.Drowsy;
            looks[7].Expr = Expr.Sleep; looks[7].Zzz = true; looks[7].AnimMs = 700; looks[7].Bob = 1;
            looks[8].Expr = Expr.Surprised; looks[8].Hop = -3;
            looks[9].Bubble = "Take a break!";
            looks[10].Expr = Expr.Content; looks[10].Lift = 4;
            looks[11].Expr = Expr.Content; looks[11].Paw = true; looks[11].Tongue = true;
            looks[12].Ear = 1; looks[12].Dx = 1;
            looks[13].Bob = 2; looks[13].Feet = 2; looks[13].Dx = 3; looks[13].Dy = -1;
            looks[14].Expr = Expr.Happy; looks[14].Bubble = "Good morning!";
            looks[15].Expr = Expr.Eat; looks[15].Chomp = true; looks[15].Bob = 2;
            looks[16].Expr = Expr.Beg; looks[16].Bubble = "Food?";
            looks[17].Expr = Expr.Dizzy; looks[17].Stars = true; looks[17].AnimMs = 300; looks[17].Bubble = "Meant that.";
            looks[18].Tail = 1; looks[18].Dx = 3; looks[18].Dy = 1; looks[18].Feet = 2;
            looks[19].Tail = 2; looks[19].Dx = 3; looks[19].Feet = 1;
            for (int i = 0; i < 7; i++) { looks[20 + i].Hat = i + 1; looks[20 + i].Dx = -1; looks[20 + i].Dy = 1; }
            looks[22].Expr = Expr.Happy;
            looks[27].Tangled = true; looks[27].Expr = Expr.Surprised; looks[27].Bubble = "Help?";
            looks[28].Dx = 3; looks[28].Dy = 2; looks[28].Hop = -2; looks[28].Feet = 2;
            looks[29].Bob = 2; looks[29].Feet = 1; looks[29].Dx = 3; looks[29].Dy = 3;
            looks[30].Expr = Expr.Content; looks[30].Ear = 1; looks[30].Bob = 1; looks[30].Feet = 2;
            looks[30].Notes.Add(new Point(OX - 2, OY + 2)); looks[30].Notes.Add(new Point(OX + 37, OY - 4));
            looks[31].Expr = Expr.Surprised; looks[31].Bubble = "!?";
            looks[32].Expr = Expr.Content; looks[32].Bob = 2;
            looks[33].Dx = 3; looks[33].Dy = -2; looks[33].Hop = -5; looks[33].Tail = 2;
            looks[34].Expr = Expr.Smug; looks[34].Hat = 4; looks[34].Ear = 2; looks[34].Bob = 1;
            looks[34].Notes.Add(new Point(OX + 38, OY + 4)); looks[34].Notes.Add(new Point(OX - 4, OY - 6));

            int rows = (n + cols - 1) / cols;
            using (Bitmap sheet = new Bitmap(cols * cw, rows * (ch + label)))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font lf = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(Color.FromArgb(245, 236, 220));
                for (int i = 0; i < n; i++)
                {
                    int ox = (i % cols) * cw, oy = (i / cols) * (ch + label);
                    g.TranslateTransform(ox, oy);
                    Draw(g, s, looks[i]);
                    if (i == 15) Paint(g, BowlHalf, OX + 11, OY + BodyH - 4, s);
                    if (i == 28) Paint(g, YarnB, OX + 40, OY + 12, s);
                    if (i == 29) Paint(g, LaserDot, OX + 41, OY + BodyH - 3, s);
                    g.ResetTransform();
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    SizeF sz = g.MeasureString(names[i], lf);
                    g.DrawString(names[i], lf, Brushes.DimGray, ox + (cw - sz.Width) / 2, oy + ch + 3);
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }
}
