using System;

namespace CatPet
{
    public enum Deed { Walk, Nap, Groom, Stretch, Wiggle, Hunt, AskFood, Zoomies, Stare, ChaseTail, Smug, Cheeky, Hop, Yarn, Climb }

    // Abu's tiny "dumbo brain": every few seconds he scores what he could do from his needs,
    // his mood and what you've rewarded him for, adds some randomness, and picks one.
    // The goofs (tripping, missing pounces, forgetting...) are rolled in Cat.cs when he acts.
    public static class Brain
    {
        public const double GoofTrip = 0.08;      // trips over nothing while walking
        public const double GoofForget = 0.06;    // forgets where he was going
        public const double GoofBonk = 0.5;       // bonks into the screen edge
        public const double GoofMiss = 0.3;       // misses the cursor pounce
        public const double GoofPanic = 0.4;      // stares at nothing... then panics
        public const double GoofDizzy = 0.35;     // gets dizzy from chasing his tail
        public const double GoofTangle = 0.12;    // gets wrapped up in the yarn
        public const double GoofShortJump = 0.1;  // jumps for a window and doesn't make it
        public const double GoofWalkOff = 0.2;    // walks right off the edge of a window

        static readonly int Count = Enum.GetValues(typeof(Deed)).Length;

        public static Deed Pick(Store st, Random rnd, bool stay, bool cursorNear, bool night, bool yarnOut, bool canClimb)
        {
            double hungry = st.Hunger, tired = 100 - st.Energy, bored = 100 - st.Fun;
            double[] w = new double[Count];
            w[(int)Deed.Walk] = stay ? 0 : 30;
            w[(int)Deed.Nap] = 3 + tired * 0.4 + (night ? 15 : 0);
            w[(int)Deed.Groom] = 8;
            w[(int)Deed.Stretch] = 6;
            w[(int)Deed.Wiggle] = 6;
            w[(int)Deed.Hunt] = (!stay && cursorNear) ? 8 + bored * 0.15 : 0;
            w[(int)Deed.AskFood] = hungry > 60 ? (hungry - 50) * 1.2 : 0;
            w[(int)Deed.Zoomies] = stay ? 0 : 1.5 + bored * 0.06 + (st.Energy > 70 ? 3 : 0) - (st.Energy < 25 ? 1.5 : 0);
            w[(int)Deed.Stare] = 3;
            w[(int)Deed.ChaseTail] = 2.5 + bored * 0.05;
            w[(int)Deed.Smug] = 8;
            w[(int)Deed.Cheeky] = 6;
            w[(int)Deed.Hop] = 3;
            w[(int)Deed.Yarn] = (yarnOut && !stay) ? 20 + bored * 0.3 : 0;
            w[(int)Deed.Climb] = (canClimb && !stay) ? 10 + bored * 0.05 : 0;

            double total = 0;
            for (int i = 0; i < Count; i++)
            {
                if (w[i] < 0) w[i] = 0;
                w[i] *= st.Like(((Deed)i).ToString()) * (0.7 + rnd.NextDouble() * 0.6);
                total += w[i];
            }
            double pick = rnd.NextDouble() * total;
            for (int i = 0; i < Count; i++)
            {
                pick -= w[i];
                if (pick <= 0) return (Deed)i;
            }
            return Deed.Smug;
        }
    }
}
