// Turns a meow recording into soft, soothing sounds for CatPet.
// Used by make-sounds.ps1 (together with Mp3Decode.cs). Not part of CatPet.exe itself.
using System;
using System.IO;

public static class SoundTool
{
    // speed: 1 = original, < 1 = slower and lower, > 1 = quicker and higher.
    // peak: final loudness (1.0 = full volume).
    public static void Make(float[] x, int rate, double speed, double peak, string outPath)
    {
        // 1. trim silence
        int a = 0, b = x.Length - 1;
        while (a < x.Length && Math.Abs(x[a]) < 0.02) a++;
        while (b > a && Math.Abs(x[b]) < 0.004) b--;
        a = Math.Max(0, a - rate / 100);
        b = Math.Min(x.Length - 1, b + rate / 20);

        // 2. change speed / pitch (smooth interpolation)
        int n = (int)((b - a) / speed);
        double[] y = new double[n + (int)(rate * 0.45)]; // extra room for the echo tail
        for (int i = 0; i < n; i++)
        {
            double p = a + i * speed;
            int k = (int)p; double f = p - k;
            y[i] = x[k] * (1 - f) + (k + 1 < x.Length ? x[k + 1] : 0) * f;
        }

        // 3. warm it up: take the sharp highs off (two gentle low-pass passes ~3 kHz)
        double c = 1 - Math.Exp(-2 * Math.PI * 3000.0 / rate), s1 = 0, s2 = 0;
        for (int i = 0; i < n; i++) { s1 += c * (y[i] - s1); s2 += c * (s1 - s2); y[i] = s2; }

        // 4. soft fade in so it never starts with a click
        int fin = rate * 30 / 1000;
        for (int i = 0; i < fin && i < n; i++) y[i] *= 0.5 - 0.5 * Math.Cos(Math.PI * i / fin);

        // 5. a little cosy room echo (gentle, mostly dry)
        double[] wet = Reverb(y, rate);
        for (int i = 0; i < y.Length; i++) y[i] = y[i] * 0.85 + wet[i] * 0.22;

        // 6. long smooth fade-out over the tail
        int fout = (int)(rate * 0.35);
        for (int i = 0; i < fout; i++)
        {
            int j = y.Length - fout + i;
            y[j] *= 0.5 + 0.5 * Math.Cos(Math.PI * i / fout);
        }

        // 7. make it soft: set the loudest point to `peak`
        double max = 1e-9;
        foreach (double v in y) max = Math.Max(max, Math.Abs(v));
        short[] pcm = new short[y.Length];
        for (int i = 0; i < y.Length; i++) pcm[i] = (short)(y[i] / max * peak * 32767);
        WriteWav(outPath, pcm, rate);
    }

    static double[] Reverb(double[] x, int rate)
    {
        double[] outp = new double[x.Length];
        double[] combMs = { 29.7, 37.1, 41.1, 43.7 };
        foreach (double ms in combMs)
        {
            int d = (int)(rate * ms / 1000);
            double[] buf = new double[d];
            int idx = 0; double damp = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double o = buf[idx];
                damp += 0.4 * (o - damp);               // darker echoes
                buf[idx] = x[i] + damp * 0.72;
                idx = (idx + 1) % d;
                outp[i] += o * 0.25;
            }
        }
        double[] apMs = { 5.0, 1.7 };
        foreach (double ms in apMs)
        {
            int d = (int)(rate * ms / 1000);
            double[] buf = new double[d];
            int idx = 0;
            for (int i = 0; i < outp.Length; i++)
            {
                double bo = buf[idx], v = outp[i];
                double o = -v + bo;
                buf[idx] = v + bo * 0.5;
                idx = (idx + 1) % d;
                outp[i] = o;
            }
        }
        return outp;
    }

    static void WriteWav(string path, short[] s, int rate)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            int bytes = s.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (short v in s) w.Write(v);
        }
    }
}
