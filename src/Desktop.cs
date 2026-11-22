using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CatPet
{
    // A flat edge Abu can stand on: the top of a window, or the bottom of the screen (Hwnd = zero).
    public struct Surface
    {
        public int X1, X2, Y;
        public IntPtr Hwnd;
    }

    // Looks at the desktop: open windows, fullscreen apps.
    public static class Desktop
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static bool IsShell(string cls)
        {
            return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        }

        static bool IsCloaked(IntPtr h)
        {
            int c;
            return DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out c, 4) == 0 && c != 0;
        }

        static bool IsMine(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == myPid;
        }

        // The visible frame of a window (without the invisible resize border).
        public static bool GetFrame(IntPtr h, out Rectangle r)
        {
            RECT rc;
            if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out rc, Marshal.SizeOf(typeof(RECT))) != 0
                && !GetWindowRect(h, out rc)) { r = Rectangle.Empty; return false; }
            r = Rectangle.FromLTRB(rc.L, rc.T, rc.R, rc.B);
            return true;
        }

        public static bool IsUsable(IntPtr h)
        {
            return IsWindow(h) && IsWindowVisible(h) && !IsIconic(h) && !IsCloaked(h);
        }

        // Top edges of open windows that are not hidden behind other windows.
        public static List<Surface> Scan(int minWidth, int minTopY)
        {
            List<IntPtr> wins = new List<IntPtr>();
            EnumWindows(delegate (IntPtr h, IntPtr l) { wins.Add(h); return true; }, IntPtr.Zero);

            List<Rectangle> above = new List<Rectangle>();
            List<Surface> result = new List<Surface>();
            foreach (IntPtr h in wins)                       // EnumWindows goes from top to bottom
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h) || IsMine(h)) continue;
                if (IsShell(ClassOf(h))) continue;
                Rectangle r;
                if (!GetFrame(h, out r) || r.Width < 120 || r.Height < 60) continue;

                // tool windows / no-activate / click-through windows are overlays (e.g. NVIDIA, game bars): ignore them
                if ((GetWindowLong(h, -20) & (0x80 | 0x08000000 | 0x20)) != 0) continue;
                if (GetWindowTextLength(h) > 0 && r.Top >= minTopY)
                {
                    List<int[]> segs = new List<int[]> { new[] { r.Left, r.Right } };
                    foreach (Rectangle a in above)
                        if (a.Top < r.Top && a.Bottom > r.Top) segs = Subtract(segs, a.Left, a.Right);
                    foreach (int[] s in segs)
                        if (s[1] - s[0] >= minWidth) result.Add(new Surface { X1 = s[0], X2 = s[1], Y = r.Top, Hwnd = h });
                }
                above.Add(r);
            }
            return result;
        }

        static List<int[]> Subtract(List<int[]> segs, int a, int b)
        {
            List<int[]> outp = new List<int[]>();
            foreach (int[] s in segs)
            {
                if (b <= s[0] || a >= s[1]) { outp.Add(s); continue; }
                if (a > s[0]) outp.Add(new[] { s[0], a });
                if (b < s[1]) outp.Add(new[] { b, s[1] });
            }
            return outp;
        }

        // True while a game, fullscreen video or presentation is in front.
        public static bool FullscreenActive()
        {
            int state;
            if (SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4)) return true;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || IsMine(fg) || IsShell(ClassOf(fg))) return false;
            if ((GetWindowLong(fg, -16) & 0x00C00000) == 0x00C00000) return false;  // has a title bar -> normal window
            RECT rc;
            if (!GetWindowRect(fg, out rc)) return false;
            Rectangle mon = Screen.FromHandle(fg).Bounds;
            return rc.L <= mon.Left && rc.T <= mon.Top && rc.R >= mon.Right && rc.B >= mon.Bottom;
        }
    }

    // How loud the PC's sound output is right now (0..1), using Windows Core Audio.
    public static class AudioMeter
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        static IAudioMeterInformation meter;
        static int nextInit, refreshAt;

        public static float Peak()
        {
            int now = Environment.TickCount;
            try
            {
                if (meter != null && unchecked(now - refreshAt) >= 0) Release();   // follow headphone / speaker changes
                if (meter == null)
                {
                    if (unchecked(now - nextInit) < 0) return 0;
                    nextInit = now + 30000;
                    Init();
                    refreshAt = now + 60000;
                    if (meter == null) return 0;
                }
                float p;
                if (meter.GetPeakValue(out p) != 0) { Release(); return 0; }
                return p;
            }
            catch { Release(); return 0; }
        }

        static void Init()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /*render*/, 1 /*multimedia*/, out dev) != 0 || dev == null) return;
            Guid iid = typeof(IAudioMeterInformation).GUID;
            object o;
            if (dev.Activate(ref iid, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out o) != 0 || o == null) return;
            meter = (IAudioMeterInformation)o;
            Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
        }

        static void Release()
        {
            if (meter != null) { try { Marshal.ReleaseComObject(meter); } catch { } }
            meter = null;
            nextInit = 0;
        }
    }
}
