using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace CatPet
{
    // Cat sounds. Meows come from your recording (built into the exe by build.ps1);
    // the purr and crunch are generated in code.
    public static class Sounds
    {
        const int Rate = 22050;
        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        static SoundPlayer player;
        public static bool Muted;
        public static int LastPlay = Environment.TickCount - 100000;   // so his own meows don't count as "music"

        public static void Play(string name)
        {
            if (Muted) return;
            LastPlay = Environment.TickCount;
            try
            {
                byte[] wav;
                if (!cache.TryGetValue(name, out wav)) { wav = Make(name); cache[name] = wav; }
                if (player != null) { player.Stop(); player.Dispose(); }
                player = new SoundPlayer(new MemoryStream(wav));
                player.Play();
            }
            catch { }
        }

        static byte[] Make(string name)
        {
            byte[] rec = Embedded(name + ".wav");
            if (rec != null) return rec;

            switch (name)
            {
                case "mrrp": return Wav(Meow(0.26, 720, 860, 780, 7), 0.15);   // one tiny "mew"
                case "sad": return Wav(Meow(0.90, 700, 780, 520, 3), 0.15);    // long, sleepy "meeeow..."
                case "purr": return Purr(1.8);
                case "crunch": return Crunch();
                case "thump": return Thump();
                default:                                                       // "meow meow"
                    return Wav(Join(Meow(0.42, 650, 900, 620, 5), Silence(0.11), Meow(0.36, 700, 960, 680, 9)), 0.18);
            }
        }

        static byte[] Embedded(string resName)
        {
            try
            {
                using (Stream st = typeof(Sounds).Assembly.GetManifestResourceStream(resName))
                {
                    if (st == null) return null;
                    MemoryStream ms = new MemoryStream();
                    st.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Ease(double t) { return 0.5 - 0.5 * Math.Cos(Math.PI * Math.Max(0, Math.Min(1, t))); }

        // Mouth shape over one meow:  m -> ee -> aa -> ow
        static readonly double[] KT  = { 0.00, 0.12, 0.32, 0.62, 1.00 };
        static readonly double[] KF1 = {  300,  420,  850,  600,  450 };
        static readonly double[] KF2 = { 1100, 2300, 1600, 1000,  850 };
        static readonly double[] KF3 = { 2600, 3100, 2800, 2600, 2500 };
        static readonly double[] KA  = { 0.30, 0.85, 1.00, 0.80, 0.60 };

        static double Key(double[] v, double t)
        {
            for (int k = 0; k < KT.Length - 1; k++)
                if (t <= KT[k + 1]) return Lerp(v[k], v[k + 1], Ease((t - KT[k]) / (KT[k + 1] - KT[k])));
            return v[v.Length - 1];
        }

        static double Res(double f, double center, double width)
        {
            double x = (f - center) / width;
            return 1 / (1 + x * x);
        }

        // Fallback synthesized meow (only used if the recording isn't built in).
        static double[] Meow(double dur, double fa, double fb, double fc, int seed)
        {
            int n = (int)(Rate * dur);
            double[] buf = new double[n];
            Random r = new Random(seed);
            double ph = 0, br = 0, y = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double f0 = t < 0.4 ? Lerp(fa, fb, Ease(t / 0.4)) : Lerp(fb, fc, Ease((t - 0.4) / 0.6));
                f0 *= 1 + 0.01 * t * Math.Sin(2 * Math.PI * 5.5 * i / Rate);
                ph += 2 * Math.PI * f0 / Rate;
                double F1 = Key(KF1, t), F2 = Key(KF2, t), F3 = Key(KF3, t);
                double v = 0;
                for (int h = 1; h * f0 < 5000; h++)
                {
                    double fh = h * f0;
                    double g = Res(fh, F1, 300) + 0.7 * Res(fh, F2, 400) + 0.2 * Res(fh, F3, 500);
                    v += g / Math.Sqrt(h) * Math.Sin(h * ph);
                }
                br += ((r.NextDouble() * 2 - 1) - br) * 0.2;
                v += br * 0.06;
                y += 0.45 * (v - y);
                double env = Key(KA, t) * Ease(t * dur / 0.04) * (t > 0.6 ? Ease((1 - t) / 0.4) : 1);
                buf[i] = y * env;
            }
            return buf;
        }

        static double[] Silence(double dur) { return new double[(int)(Rate * dur)]; }

        static double[] Join(params double[][] parts)
        {
            List<double> all = new List<double>();
            foreach (double[] p in parts) all.AddRange(p);
            return all.ToArray();
        }

        // Quiet, cosy purr: low rumble pulsing ~22 times a second, softly filtered.
        static byte[] Purr(double dur)
        {
            int n = (int)(Rate * dur);
            double[] buf = new double[n];
            Random r = new Random(7);
            double lp = 0, lp2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, u = (double)i / n;
                lp += ((r.NextDouble() * 2 - 1) - lp) * 0.03;
                lp2 += (lp - lp2) * 0.05;
                double am = Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * 22 * t), 3);
                double tone = Math.Sin(2 * Math.PI * 55 * t) * 0.5 + Math.Sin(2 * Math.PI * 110 * t) * 0.25;
                double breath = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 0.8 * t);
                double env = Ease(u * dur / 0.3) * Ease((1 - u) * dur / 0.5);
                buf[i] = (lp2 * 6 + tone) * am * breath * env;
            }
            return Wav(buf, 0.14);
        }

        // Soft little "nom" crunch: two muffled clicks.
        static byte[] Crunch()
        {
            int n = (int)(Rate * 0.2);
            double[] buf = new double[n];
            Random r = new Random(11);
            double lp = 0;
            int half = n / 2;
            for (int i = 0; i < n; i++)
            {
                lp += ((r.NextDouble() * 2 - 1) - lp) * 0.35;
                double since = (i % half) / (double)Rate;
                buf[i] = lp * Math.Exp(-since * 45) * (i < half ? 1.0 : 0.7);
            }
            return Wav(buf, 0.07);
        }

        // Soft little landing "thup" (paws on the floor).
        static byte[] Thump()
        {
            int n = (int)(Rate * 0.16);
            double[] buf = new double[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                ph += 2 * Math.PI * (140 - 70 * i / (double)n) / Rate;
                buf[i] = Math.Sin(ph) * Math.Exp(-t * 28) * Math.Min(1, t / 0.004);
            }
            return Wav(buf, 0.12);
        }

        static byte[] Wav(double[] buf, double peak)
        {
            double max = 1e-9;
            foreach (double v in buf) max = Math.Max(max, Math.Abs(v));
            short[] s = new short[buf.Length];
            for (int i = 0; i < buf.Length; i++) s[i] = (short)(buf[i] / max * peak * 32767);
            return Wav(s);
        }

        static byte[] Wav(short[] samples)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            int bytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (short v in samples) w.Write(v);
            w.Flush();
            return ms.ToArray();
        }
    }
}
