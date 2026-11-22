// CatPet - Abu Khaleel Bamblast, a little black pixel cat that lives on your desktop.
//  Left-drag : carry him anywhere          Click        : pet him (hearts + purr)
//  Double-click : he hops                  Right-click  : menu (Feed, Journal, ...)
// Build with build.ps1 (uses the C# compiler that ships with Windows).
using System;
using System.Threading;
using System.Windows.Forms;

namespace CatPet
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--preview") { Art.SavePreview(args[1]); return; }

            bool created;
            using (Mutex m = new Mutex(true, "CatPet_SingleInstance", out created))
            {
                if (!created) return; // only one Abu at a time
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Cat());
            }
        }
    }
}
