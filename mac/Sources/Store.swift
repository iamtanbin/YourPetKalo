import Foundation

// Everything Abu remembers: settings, needs, likes, bond and journal.
// Saved in ~/Library/Application Support/YourPetKalo/ (same key=value format as the Windows version).
final class Store {
    static let dir: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("YourPetKalo", isDirectory: true)
    }()
    var settingsFile: URL { Store.dir.appendingPathComponent("settings.ini") }
    var journalFile: URL { Store.dir.appendingPathComponent("journal.txt") }

    var scale = 2
    var stay = false, follow = false, breaks = true, gravity = true
    var greet = "", lunch = "", dinner = "", late = ""
    var name = "Abu Khaleel Bamblast"
    var x: Double? = nil, y: Double? = nil

    var hunger = 30.0, energy = 80.0, fun = 60.0, affection = 60.0
    var likes: [String: Double] = [:]

    var xp = 0
    var hat = "auto"
    var birthday = "", lastVisit = ""
    static let levelXp = [0, 20, 50, 90, 140, 200, 280, 380, 500, 650]

    static let fed = 0, petted = 1, naps = 2, played = 3, goofs = 4, kinds = 5
    static let kindNames = ["Fed", "Petted", "Naps", "Played", "Goofs"]
    var journal: [String: [Int]] = [:]

    var shortName: String {
        let n = name.trimmingCharacters(in: .whitespaces)
        return n.split(separator: " ").first.map(String.init) ?? n
    }

    static func clamp(_ v: Double) -> Double { max(0, min(100, v)) }

    func tick(minutes m: Double, sleeping: Bool) {
        hunger = Store.clamp(hunger + 0.25 * m)
        energy = Store.clamp(energy + (sleeping ? 2.0 : -0.2) * m)
        fun = Store.clamp(fun - 0.3 * m)
        affection = Store.clamp(affection - 0.5 * m)
    }

    var level: Int {
        var lv = 1
        for i in 1..<Store.levelXp.count where xp >= Store.levelXp[i] { lv = i + 1 }
        return lv
    }

    func hatUnlocked(_ h: Int) -> Bool { h >= 0 && h < Art.hatLevel.count && level >= Art.hatLevel[h] }

    func currentHat() -> Int {
        if hat == "none" { return 0 }
        if hat == "auto" {
            let c = Calendar.current.dateComponents([.month, .day], from: Date())
            let m = c.month ?? 1, d = c.day ?? 1
            if m == 12 { return Art.hatSanta }
            if (m == 10 && d >= 24) || (m == 11 && d == 1) { return Art.hatWitch }
            if birthday.count == 10 && birthday.suffix(5) == String(format: "%02d-%02d", m, d) && birthday != Store.today { return Art.hatParty }
            for h in stride(from: 5, through: 1, by: -1) where hatUnlocked(h) { return h }
            return 0
        }
        if let n = Int(hat), hatUnlocked(n) { return n }
        return 0
    }

    func like(_ action: String) -> Double { likes[action] ?? 1.0 }
    func reward(_ action: String) { likes[action] = min(3.0, like(action) * 1.1 + 0.05) }

    static var today: String { dateKey(Date()) }
    static func dateKey(_ d: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; f.locale = Locale(identifier: "en_US_POSIX")
        return f.string(from: d)
    }

    func count(_ kind: Int) {
        var c = journal[Store.today] ?? Array(repeating: 0, count: Store.kinds)
        c[kind] += 1
        journal[Store.today] = c
    }

    func load() {
        var savedAt: Double = 0
        if let text = try? String(contentsOf: settingsFile, encoding: .utf8) {
            for line in text.split(separator: "\n") {
                guard let eq = line.firstIndex(of: "=") else { continue }
                let k = line[..<eq].trimmingCharacters(in: .whitespaces)
                let v = line[line.index(after: eq)...].trimmingCharacters(in: .whitespaces)
                switch k {
                case "scale": if let n = Int(v), (2...4).contains(n) { scale = n }
                case "stay": stay = v == "1"
                case "follow": follow = v == "1"
                case "breaks": breaks = v == "1"
                case "gravity": gravity = v == "1"
                case "mute": Sounds.muted = v == "1"
                case "greet": greet = v
                case "lunch": lunch = v
                case "dinner": dinner = v
                case "late": late = v
                case "name": if !v.isEmpty { name = v }
                case "x": x = Double(v)
                case "y": y = Double(v)
                case "hunger": if let d = Double(v) { hunger = Store.clamp(d) }
                case "energy": if let d = Double(v) { energy = Store.clamp(d) }
                case "fun": if let d = Double(v) { fun = Store.clamp(d) }
                case "affection": if let d = Double(v) { affection = Store.clamp(d) }
                case "saved": savedAt = Double(v) ?? 0
                case "xp": xp = max(0, Int(v) ?? 0)
                case "hat": if !v.isEmpty { hat = v }
                case "birthday": birthday = v
                case "lastvisit": lastVisit = v
                default:
                    if k.hasPrefix("like_"), let d = Double(v) { likes[String(k.dropFirst(5))] = d }
                }
            }
            if (x == nil) != (y == nil) { x = nil; y = nil }
        }
        if savedAt > 0 {   // catch up on the time the app was closed (capped)
            let away = max(0, min((Date().timeIntervalSince1970 - savedAt) / 60, 8 * 60))
            if hunger < 80 { hunger = min(80, hunger + 0.25 * away) }
            energy = Store.clamp(energy + away)
            fun = max(20, fun - 0.3 * away)
            affection = max(20, affection - 0.5 * away)
        }
        if birthday.count != 10 { birthday = Store.today }
        if let text = try? String(contentsOf: journalFile, encoding: .utf8) {
            for line in text.split(separator: "\n") {
                let p = line.split(separator: " ")
                if p.count < 1 + Store.kinds { continue }
                journal[String(p[0])] = (1...Store.kinds).map { Int(p[$0]) ?? 0 }
            }
        }
    }

    func save() {
        try? FileManager.default.createDirectory(at: Store.dir, withIntermediateDirectories: true)
        var lines = [
            "name=\(name)", "scale=\(scale)", "stay=\(stay ? 1 : 0)", "follow=\(follow ? 1 : 0)",
            "breaks=\(breaks ? 1 : 0)", "gravity=\(gravity ? 1 : 0)", "mute=\(Sounds.muted ? 1 : 0)",
            "greet=\(greet)", "lunch=\(lunch)", "dinner=\(dinner)", "late=\(late)",
            "x=\(x.map { String(Int($0)) } ?? "")", "y=\(y.map { String(Int($0)) } ?? "")",
            String(format: "hunger=%.1f", hunger), String(format: "energy=%.1f", energy),
            String(format: "fun=%.1f", fun), String(format: "affection=%.1f", affection),
            "saved=\(Int(Date().timeIntervalSince1970))", "xp=\(xp)", "hat=\(hat)",
            "birthday=\(birthday)", "lastvisit=\(lastVisit)",
        ]
        for (k, v) in likes { lines.append(String(format: "like_%@=%.3f", k, v)) }
        try? lines.joined(separator: "\n").write(to: settingsFile, atomically: true, encoding: .utf8)

        let cutoff = Store.dateKey(Date().addingTimeInterval(-30 * 86400))
        let j = journal.keys.sorted().filter { $0 >= cutoff }.map { k in
            k + " " + journal[k]!.map(String.init).joined(separator: " ")
        }
        try? j.joined(separator: "\n").write(to: journalFile, atomically: true, encoding: .utf8)
    }
}
