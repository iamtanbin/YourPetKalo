import AppKit

// Mood journal: how Abu feels right now and what his last 7 days looked like.
final class JournalWindow: NSWindow {
    let view: JournalView
    var timer: Timer?

    init(store: Store) {
        view = JournalView(store: store)
        super.init(contentRect: NSRect(x: 0, y: 0, width: 360, height: 450), styleMask: [.titled, .closable],
                   backing: .buffered, defer: false)
        title = store.name + "'s journal"
        contentView = view
        isReleasedWhenClosed = false
        level = .floating
        center()
        let t = Timer(timeInterval: 2, repeats: true) { [weak self] _ in self?.view.needsDisplay = true }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    override func close() {
        timer?.invalidate()
        super.close()
    }
}

final class JournalView: NSView {
    let st: Store
    init(store: Store) { st = store; super.init(frame: NSRect(x: 0, y: 0, width: 360, height: 450)) }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }

    let ink = Art.rgb(40, 34, 30), muted = Art.rgb(130, 120, 110), track = Art.rgb(232, 222, 205)

    func text(_ s: String, _ x: CGFloat, _ y: CGFloat, _ font: NSFont, _ color: NSColor) {
        (s as NSString).draw(at: NSPoint(x: x, y: y), withAttributes: [.font: font, .foregroundColor: color])
    }

    func bar(_ label: String, _ value: Double, _ color: NSColor, _ y: CGFloat) {
        let body = NSFont.systemFont(ofSize: 12)
        text(label, 18, y, body, ink)
        let bx: CGFloat = 108, bw: CGFloat = 360 - 36 - 90 - 40, bh: CGFloat = 12
        track.setFill(); NSBezierPath(roundedRect: NSRect(x: bx, y: y + 3, width: bw, height: bh), xRadius: 6, yRadius: 6).fill()
        let fill = bw * CGFloat(max(0, min(100, value))) / 100
        if fill > 0 {
            color.setFill()
            NSBezierPath(roundedRect: NSRect(x: bx, y: y + 3, width: max(fill, bh), height: bh), xRadius: 6, yRadius: 6).fill()
        }
        text("\(Int(value.rounded()))%", bx + bw + 6, y, body, ink)
    }

    var mood: String {
        if st.hunger > 75 { return "He is hungry. Right-click him and choose Feed." }
        if st.energy < 25 { return "He is sleepy and will nap soon." }
        if st.affection < 30 { return "He misses you. Give him a pet!" }
        if st.fun < 30 { return "He is bored and might get the zoomies." }
        return "He is happy and a little bit silly."
    }

    override func draw(_ dirtyRect: NSRect) {
        Art.rgb(250, 245, 236).setFill(); bounds.fill()
        let title = NSFont.boldSystemFont(ofSize: 17), head = NSFont.boldSystemFont(ofSize: 13), body = NSFont.systemFont(ofSize: 12)
        var y: CGFloat = 14
        text(st.name, 18, y, title, ink); y += 28
        text(mood, 18, y, body, muted); y += 30
        text("How he feels", 18, y, head, ink); y += 24
        bar("Full tummy", 100 - st.hunger, Art.rgb(232, 150, 60), y); y += 26
        bar("Energy", st.energy, Art.rgb(90, 160, 220), y); y += 26
        bar("Fun", st.fun, Art.rgb(120, 190, 110), y); y += 26
        bar("Love", st.affection, Art.rgb(240, 110, 140), y); y += 26
        let lv = st.level
        var bond = 100.0
        if lv < Store.levelXp.count {
            let from = Store.levelXp[lv - 1], to = Store.levelXp[lv]
            bond = 100.0 * Double(st.xp - from) / Double(to - from)
        }
        bar("Bond Lv \(lv)", bond, Art.rgb(160, 110, 210), y); y += 36

        text("Last 7 days", 18, y, head, ink); y += 24
        let colW = (360 - 36 - 70) / CGFloat(Store.kinds)
        for k in 0..<Store.kinds { text(Store.kindNames[k], 18 + 70 + CGFloat(k) * colW, y, body, muted) }
        y += 20
        let f = DateFormatter(); f.dateFormat = "EEE d"
        for i in 0..<7 {
            let d = Date().addingTimeInterval(Double(-i) * 86400)
            let c = st.journal[Store.dateKey(d)]
            let label = i == 0 ? "Today" : i == 1 ? "Yesterday" : f.string(from: d)
            text(label, 18, y, body, i == 0 ? ink : muted)
            for k in 0..<Store.kinds {
                text(c.map { String($0[k]) } ?? "-", 18 + 70 + CGFloat(k) * colW + 10, y, body, i == 0 ? ink : muted)
            }
            y += 20
        }
    }
}
