import AppKit

// Meows come from the recording bundled in the app (Resources/*.wav);
// purr, crunch and thump are generated in code.
enum Sounds {
    static var muted = false
    static var lastPlay = 0
    static var cache: [String: Data] = [:]
    static var current: NSSound?
    static let rate = 22050

    static func play(_ name: String) {
        if muted { return }
        lastPlay = Clock.ms
        guard let data = load(name) else { return }
        current?.stop()
        current = NSSound(data: data)
        current?.play()
    }

    static func load(_ name: String) -> Data? {
        if let d = cache[name] { return d }
        var d: Data? = nil
        if let url = Bundle.main.url(forResource: name, withExtension: "wav") { d = try? Data(contentsOf: url) }
        if d == nil {
            switch name {
            case "purr": d = purr(1.8)
            case "crunch": d = crunch()
            case "thump": d = thump()
            default: d = nil
            }
        }
        if let d = d { cache[name] = d }
        return d
    }

    static func ease(_ t: Double) -> Double { 0.5 - 0.5 * cos(Double.pi * max(0, min(1, t))) }

    static func purr(_ dur: Double) -> Data {
        let n = Int(Double(rate) * dur)
        var buf = [Double](repeating: 0, count: n)
        var lp = 0.0, lp2 = 0.0
        var seed: UInt64 = 7
        for i in 0..<n {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            let noise = Double(seed >> 33) / Double(1 << 31) - 1
            let t = Double(i) / Double(rate), u = Double(i) / Double(n)
            lp += (noise - lp) * 0.03
            lp2 += (lp - lp2) * 0.05
            let am = pow(0.5 + 0.5 * sin(2 * Double.pi * 22 * t), 3)
            let tone = sin(2 * Double.pi * 55 * t) * 0.5 + sin(2 * Double.pi * 110 * t) * 0.25
            let breath = 0.75 + 0.25 * sin(2 * Double.pi * 0.8 * t)
            let env = ease(u * dur / 0.3) * ease((1 - u) * dur / 0.5)
            buf[i] = (lp2 * 6 + tone) * am * breath * env
        }
        return wav(buf, peak: 0.14)
    }

    static func crunch() -> Data {
        let n = Int(Double(rate) * 0.2), half = n / 2
        var buf = [Double](repeating: 0, count: n)
        var lp = 0.0
        var seed: UInt64 = 11
        for i in 0..<n {
            seed = seed &* 6364136223846793005 &+ 1442695040888963407
            let noise = Double(seed >> 33) / Double(1 << 31) - 1
            lp += (noise - lp) * 0.35
            let since = Double(i % half) / Double(rate)
            buf[i] = lp * exp(-since * 45) * (i < half ? 1.0 : 0.7)
        }
        return wav(buf, peak: 0.07)
    }

    static func thump() -> Data {
        let n = Int(Double(rate) * 0.16)
        var buf = [Double](repeating: 0, count: n)
        var ph = 0.0
        for i in 0..<n {
            let t = Double(i) / Double(rate)
            ph += 2 * Double.pi * (140 - 70 * Double(i) / Double(n)) / Double(rate)
            buf[i] = sin(ph) * exp(-t * 28) * min(1, t / 0.004)
        }
        return wav(buf, peak: 0.12)
    }

    static func wav(_ buf: [Double], peak: Double) -> Data {
        let mx = max(1e-9, buf.map { abs($0) }.max() ?? 1e-9)
        var d = Data()
        func u32(_ v: UInt32) { var x = v.littleEndian; withUnsafeBytes(of: &x) { d.append(contentsOf: $0) } }
        func u16(_ v: UInt16) { var x = v.littleEndian; withUnsafeBytes(of: &x) { d.append(contentsOf: $0) } }
        let bytes = UInt32(buf.count * 2)
        d.append(contentsOf: Array("RIFF".utf8)); u32(36 + bytes)
        d.append(contentsOf: Array("WAVEfmt ".utf8)); u32(16); u16(1); u16(1)
        u32(UInt32(rate)); u32(UInt32(rate * 2)); u16(2); u16(16)
        d.append(contentsOf: Array("data".utf8)); u32(bytes)
        for v in buf {
            let s = Int16(max(-32767, min(32767, v / mx * peak * 32767)))
            var x = s.littleEndian
            withUnsafeBytes(of: &x) { d.append(contentsOf: $0) }
        }
        return d
    }
}
