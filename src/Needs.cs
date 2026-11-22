using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CatPet
{
    // Everything Abu remembers: settings, needs, what he likes, and his daily journal.
    // Saved in %APPDATA%\CatPet\settings.ini and journal.txt.
    public class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CatPet");
        static readonly string SettingsFile = Path.Combine(Dir, "settings.ini");
        static readonly string JournalFile = Path.Combine(Dir, "journal.txt");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // settings
        public int Scale = 2;
        public bool Stay, Follow, Breaks = true, Gravity = true;
        public string Greet = "", Lunch = "", Dinner = "", Late = "";
        public string Name = "Abu Khaleel Bamblast";
        public double X = double.NaN, Y = double.NaN;

        // needs, 0..100 (Hunger: 0 = full, 100 = starving)
        public double Hunger = 30, Energy = 80, Fun = 60, Affection = 60;

        // bond (friendship) and hats
        public int Xp;
        public string Hat = "auto";          // "auto", "none" or a hat number (see Art.HatNames)
        public string Birthday = "";         // first day he lived here (yyyy-MM-dd)
        public string LastVisit = "";        // for the daily visit bonus
        public static readonly int[] LevelXp = { 0, 20, 50, 90, 140, 200, 280, 380, 500, 650 };

        public int Level
        {
            get
            {
                int lv = 1;
                for (int i = 1; i < LevelXp.Length; i++) if (Xp >= LevelXp[i]) lv = i + 1;
                return lv;
            }
        }

        public bool HatUnlocked(int hat) { return hat >= 0 && hat < Art.HatLevel.Length && Level >= Art.HatLevel[hat]; }

        // The hat he wears right now.
        public int CurrentHat()
        {
            if (Hat == "none") return 0;
            if (Hat == "auto")
            {
                DateTime d = DateTime.Now;
                if (d.Month == 12) return Art.HatSanta;
                if ((d.Month == 10 && d.Day >= 24) || (d.Month == 11 && d.Day == 1)) return Art.HatWitch;
                if (Birthday.Length == 10 && Birthday.Substring(5) == d.ToString("MM-dd") && Birthday != Today) return Art.HatParty;
                for (int h = 5; h >= 1; h--) if (HatUnlocked(h)) return h;
                return 0;
            }
            int n;
            if (int.TryParse(Hat, out n) && HatUnlocked(n)) return n;
            return 0;
        }

        // learned likes per action (1 = normal, up to 3)
        public readonly Dictionary<string, double> Likes = new Dictionary<string, double>();

        // journal: date -> counts
        public const int Fed = 0, Petted = 1, Naps = 2, Played = 3, Goofs = 4, Kinds = 5;
        public static readonly string[] KindNames = { "Fed", "Petted", "Naps", "Played", "Goofs" };
        public readonly SortedDictionary<string, int[]> Journal = new SortedDictionary<string, int[]>();

        public string ShortName
        {
            get { string n = Name.Trim(); int sp = n.IndexOf(' '); return sp > 0 ? n.Substring(0, sp) : n; }
        }

        static double C(double v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }

        // Called every frame with the elapsed minutes.
        public void Tick(double minutes, bool sleeping)
        {
            Hunger = C(Hunger + 0.25 * minutes);                      // starving after ~6-7 hours
            Energy = C(Energy + (sleeping ? 2.0 : -0.2) * minutes);   // tired after ~8 hours awake
            Fun = C(Fun - 0.3 * minutes);
            Affection = C(Affection - 0.5 * minutes);
        }

        public void Change(ref double need, double by) { need = C(need + by); }

        public double Like(string action)
        {
            double v;
            return Likes.TryGetValue(action, out v) ? v : 1.0;
        }

        public void Reward(string action) { Likes[action] = Math.Min(3.0, Like(action) * 1.1 + 0.05); }

        public static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd"); } }

        public int[] Day(string date)
        {
            int[] c;
            if (!Journal.TryGetValue(date, out c)) { c = new int[Kinds]; Journal[date] = c; }
            return c;
        }

        public void Count(int kind) { Day(Today)[kind]++; }

        public void Load()
        {
            try
            {
                long savedAt = 0;
                if (File.Exists(SettingsFile))
                {
                    foreach (string line in File.ReadAllLines(SettingsFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        int n; double d; long l;
                        if (k == "scale" && int.TryParse(v, out n) && n >= 2 && n <= 4) Scale = n;
                        else if (k == "stay") Stay = v == "1";
                        else if (k == "follow") Follow = v == "1";
                        else if (k == "breaks") Breaks = v == "1";
                        else if (k == "gravity") Gravity = v == "1";
                        else if (k == "mute") Sounds.Muted = v == "1";
                        else if (k == "greet") Greet = v;
                        else if (k == "lunch") Lunch = v;
                        else if (k == "dinner") Dinner = v;
                        else if (k == "late") Late = v;
                        else if (k == "name" && v.Length > 0) Name = v;
                        else if (k == "x" && int.TryParse(v, out n)) X = n;
                        else if (k == "y" && int.TryParse(v, out n)) Y = n;
                        else if (k == "hunger" && double.TryParse(v, NumberStyles.Float, Inv, out d)) Hunger = C(d);
                        else if (k == "energy" && double.TryParse(v, NumberStyles.Float, Inv, out d)) Energy = C(d);
                        else if (k == "fun" && double.TryParse(v, NumberStyles.Float, Inv, out d)) Fun = C(d);
                        else if (k == "affection" && double.TryParse(v, NumberStyles.Float, Inv, out d)) Affection = C(d);
                        else if (k == "saved" && long.TryParse(v, out l)) savedAt = l;
                        else if (k == "xp" && int.TryParse(v, out n)) Xp = Math.Max(0, n);
                        else if (k == "hat" && v.Length > 0) Hat = v;
                        else if (k == "birthday") Birthday = v;
                        else if (k == "lastvisit") LastVisit = v;
                        else if (k.StartsWith("like_") && double.TryParse(v, NumberStyles.Float, Inv, out d)) Likes[k.Substring(5)] = d;
                    }
                    if (double.IsNaN(X) != double.IsNaN(Y)) { X = double.NaN; Y = double.NaN; }
                }

                // Catch up on the time CatPet was closed (capped so he isn't starving every morning).
                if (savedAt > 0)
                {
                    double away = (DateTime.UtcNow.Ticks - savedAt) / (double)TimeSpan.TicksPerMinute;
                    away = Math.Max(0, Math.Min(away, 8 * 60));
                    if (Hunger < 80) Hunger = Math.Min(80, Hunger + 0.25 * away);
                    Energy = C(Energy + 1.0 * away);       // he napped while you were away
                    Fun = Math.Max(20, Fun - 0.3 * away);
                    Affection = Math.Max(20, Affection - 0.5 * away);
                }

                if (Birthday.Length != 10) Birthday = Today;

                if (File.Exists(JournalFile))
                {
                    foreach (string line in File.ReadAllLines(JournalFile))
                    {
                        string[] p = line.Split(' ');
                        if (p.Length < 1 + Kinds) continue;
                        int[] c = new int[Kinds];
                        for (int i = 0; i < Kinds; i++) int.TryParse(p[i + 1], out c[i]);
                        Journal[p[0]] = c;
                    }
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string> {
                    "name=" + Name,
                    "scale=" + Scale,
                    "stay=" + (Stay ? 1 : 0),
                    "follow=" + (Follow ? 1 : 0),
                    "breaks=" + (Breaks ? 1 : 0),
                    "gravity=" + (Gravity ? 1 : 0),
                    "mute=" + (Sounds.Muted ? 1 : 0),
                    "greet=" + Greet, "lunch=" + Lunch, "dinner=" + Dinner, "late=" + Late,
                    "x=" + (double.IsNaN(X) ? "" : ((int)X).ToString()),
                    "y=" + (double.IsNaN(Y) ? "" : ((int)Y).ToString()),
                    "hunger=" + Hunger.ToString("0.0", Inv),
                    "energy=" + Energy.ToString("0.0", Inv),
                    "fun=" + Fun.ToString("0.0", Inv),
                    "affection=" + Affection.ToString("0.0", Inv),
                    "saved=" + DateTime.UtcNow.Ticks,
                    "xp=" + Xp,
                    "hat=" + Hat,
                    "birthday=" + Birthday,
                    "lastvisit=" + LastVisit,
                };
                foreach (KeyValuePair<string, double> kv in Likes) lines.Add("like_" + kv.Key + "=" + kv.Value.ToString("0.000", Inv));
                File.WriteAllLines(SettingsFile, lines.ToArray());

                // keep the last 30 days of journal
                List<string> j = new List<string>();
                string cutoff = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
                foreach (KeyValuePair<string, int[]> kv in Journal)
                {
                    if (string.CompareOrdinal(kv.Key, cutoff) < 0) continue;
                    j.Add(kv.Key + " " + string.Join(" ", Array.ConvertAll(kv.Value, v => v.ToString())));
                }
                File.WriteAllLines(JournalFile, j.ToArray());
            }
            catch { }
        }
    }
}
