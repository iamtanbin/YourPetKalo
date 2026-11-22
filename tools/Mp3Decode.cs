using System;
using System.IO;
using System.Runtime.InteropServices;

public static class Mp3Decode
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct MP3FMT
    {
        public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
        public ushort wID; public uint fdwFlags; public ushort nBlockSize, nFramesPerBlock, nCodecDelay;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct PCMFMT
    {
        public ushort wFormatTag, nChannels; public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ACMHDR
    {
        public uint cbStruct, fdwStatus; public IntPtr dwUser;
        public IntPtr pbSrc; public uint cbSrcLength, cbSrcLengthUsed; public IntPtr dwSrcUser;
        public IntPtr pbDst; public uint cbDstLength, cbDstLengthUsed; public IntPtr dwDstUser;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)] public uint[] reserved; // 15 on 64-bit Windows
    }
    [DllImport("msacm32.dll")] static extern int acmFormatSuggest(IntPtr had, ref MP3FMT src, ref PCMFMT dst, int cb, int flags);
    [DllImport("msacm32.dll")] static extern int acmStreamOpen(out IntPtr has, IntPtr had, ref MP3FMT src, ref PCMFMT dst, IntPtr filter, IntPtr cb, IntPtr inst, int flags);
    [DllImport("msacm32.dll")] static extern int acmStreamPrepareHeader(IntPtr has, IntPtr hdr, int flags);
    [DllImport("msacm32.dll")] static extern int acmStreamUnprepareHeader(IntPtr has, IntPtr hdr, int flags);
    [DllImport("msacm32.dll")] static extern int acmStreamConvert(IntPtr has, IntPtr hdr, int flags);
    [DllImport("msacm32.dll")] static extern int acmStreamClose(IntPtr has, int flags);

    static readonly int[] Br1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    static readonly int[] Br2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

    // Returns mono samples in [-1,1]; outputs sample rate.
    public static float[] Decode(string path, out int rate)
    {
        byte[] d = File.ReadAllBytes(path);
        int p = 0;
        if (d.Length > 10 && d[0] == 'I' && d[1] == 'D' && d[2] == '3')
            p = 10 + ((d[6] & 0x7f) << 21 | (d[7] & 0x7f) << 14 | (d[8] & 0x7f) << 7 | (d[9] & 0x7f));
        while (p < d.Length - 4 && !(d[p] == 0xFF && (d[p + 1] & 0xE0) == 0xE0)) p++;
        int ver = (d[p + 1] >> 3) & 3;               // 3 = MPEG1, 2 = MPEG2, 0 = MPEG2.5
        int bri = (d[p + 2] >> 4) & 15, sri = (d[p + 2] >> 2) & 3, mode = (d[p + 3] >> 6) & 3;
        int[] sr1 = { 44100, 48000, 32000 };
        int sr = sr1[sri]; if (ver == 2) sr /= 2; if (ver == 0) sr /= 4;
        int br = (ver == 3 ? Br1[bri] : Br2[bri]) * 1000;
        int ch = mode == 3 ? 1 : 2;

        MP3FMT mf = new MP3FMT();
        mf.wFormatTag = 0x55; mf.nChannels = (ushort)ch; mf.nSamplesPerSec = (uint)sr;
        mf.nAvgBytesPerSec = (uint)(br / 8); mf.nBlockAlign = 1; mf.cbSize = 12;
        mf.wID = 1; mf.fdwFlags = 2; mf.nBlockSize = (ushort)((ver == 3 ? 144 : 72) * br / sr); mf.nFramesPerBlock = 1; mf.nCodecDelay = 1393;
        PCMFMT pf = new PCMFMT();
        pf.wFormatTag = 1;
        int r = acmFormatSuggest(IntPtr.Zero, ref mf, ref pf, Marshal.SizeOf(pf), 0x10000 /*WFORMATTAG*/);
        if (r != 0) throw new Exception("acmFormatSuggest " + r);
        IntPtr has;
        r = acmStreamOpen(out has, IntPtr.Zero, ref mf, ref pf, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        if (r != 0) throw new Exception("acmStreamOpen " + r);

        MemoryStream outp = new MemoryStream();
        int srcChunk = 16384, dstChunk = 1 << 20;
        IntPtr src = Marshal.AllocHGlobal(srcChunk), dst = Marshal.AllocHGlobal(dstChunk);
        ACMHDR h = new ACMHDR();
        h.cbStruct = (uint)Marshal.SizeOf(typeof(ACMHDR)); h.pbSrc = src; h.cbSrcLength = (uint)srcChunk;
        h.pbDst = dst; h.cbDstLength = (uint)dstChunk; h.reserved = new uint[15];
        IntPtr hp = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ACMHDR)));
        Marshal.StructureToPtr(h, hp, false);
        r = acmStreamPrepareHeader(has, hp, 0);
        if (r != 0) throw new Exception("prepare " + r);
        int pos = p;
        while (pos < d.Length)
        {
            int n = Math.Min(srcChunk, d.Length - pos);
            Marshal.Copy(d, pos, src, n);
            h = (ACMHDR)Marshal.PtrToStructure(hp, typeof(ACMHDR));
            h.cbSrcLength = (uint)n;
            Marshal.StructureToPtr(h, hp, false);
            r = acmStreamConvert(has, hp, 0x4 /*BLOCKALIGN*/);
            if (r != 0) throw new Exception("convert " + r);
            h = (ACMHDR)Marshal.PtrToStructure(hp, typeof(ACMHDR));
            byte[] o = new byte[h.cbDstLengthUsed];
            Marshal.Copy(dst, o, 0, o.Length);
            outp.Write(o, 0, o.Length);
            if (h.cbSrcLengthUsed == 0) break;
            pos += (int)h.cbSrcLengthUsed;
        }
        h.cbSrcLength = (uint)srcChunk;
        Marshal.StructureToPtr(h, hp, false);
        acmStreamUnprepareHeader(has, hp, 0);
        acmStreamClose(has, 0);

        byte[] pcm = outp.ToArray();
        int pch = pf.nChannels, frames = pcm.Length / (2 * pch);
        float[] mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float s = 0;
            for (int c = 0; c < pch; c++) s += BitConverter.ToInt16(pcm, (i * pch + c) * 2) / 32768f;
            mono[i] = s / pch;
        }
        rate = (int)pf.nSamplesPerSec;
        return mono;
    }
}
