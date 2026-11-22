import AppKit

// A borderless, see-through, always-on-top window that never steals focus.
class PetPanel: NSPanel {
    init(size: NSSize, clickThrough: Bool = false) {
        super.init(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = .floating
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        ignoresMouseEvents = clickThrough
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
    }
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

// Draws one pixel-art grid (food bowl, yarn ball, laser dot).
class PixelView: NSView {
    var grid: [String] { didSet { needsDisplay = true } }
    let scale: CGFloat
    init(grid: [String], scale: CGFloat) {
        self.grid = grid; self.scale = scale
        super.init(frame: NSRect(x: 0, y: 0, width: CGFloat(grid[0].count) * scale, height: CGFloat(grid.count) * scale))
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) { Art.grid(grid, 0, 0, scale) }
}

class Prop {
    let window: PetPanel
    let view: PixelView
    init(grid: [String], scale: Int, clickThrough: Bool = false) {
        view = PixelView(grid: grid, scale: CGFloat(scale))
        window = PetPanel(size: view.frame.size, clickThrough: clickThrough)
        window.contentView = view
    }
    var width: Double { Double(window.frame.width) }
    var height: Double { Double(window.frame.height) }
    var topLeft: CGPoint { Screen.topLeft(window) }
    func move(_ x: Double, _ y: Double) { Screen.setTopLeft(window, x, y) }
    func show() { window.orderFrontRegardless() }
    func hide() { window.orderOut(nil) }
    func close() { window.orderOut(nil); window.close() }
    func setGrid(_ g: [String]) { if g != view.grid { view.grid = g } }
}

// A ball of yarn with simple physics. Drag and fling it; double-click to put it away.
final class Yarn: Prop {
    var x: Double, y: Double, vx = 0.0, vy = 0.0
    var held = false, putAway = false
    let s: Double
    var spin = 0.0, lastX = 0.0, lastY = 0.0, lastMs = 0
    var grab = CGPoint.zero

    init(scale: Int, x: Double, y: Double) {
        self.x = x; self.y = y; s = Double(scale)
        super.init(grid: Art.yarnA, scale: scale)
        let v = YarnView(grid: Art.yarnA, scale: CGFloat(scale))
        window.contentView = v
        v.yarn = self
        move(x, y)
    }

    var moving: Bool { held || abs(vx) + abs(vy) > 150 }
    var centerX: Double { x + width / 2 }
    var bottomY: Double { y + height }

    func kick(_ kx: Double, _ ky: Double) { if !held { vx = kx; vy = ky } }

    func step(_ dt: Double) {
        if held { return }
        let wa = Screen.workArea(at: CGPoint(x: x + width / 2, y: y + height / 2))
        vy += 1800 * s / 2 * dt
        x += vx * dt; y += vy * dt
        let floor = Double(wa.maxY) - height
        var onFloor = false
        if y >= floor { y = floor; onFloor = true; if vy > 0 { vy = -vy * 0.5; if abs(vy) < 60 { vy = 0 } } }
        if y < Double(wa.minY) { y = Double(wa.minY); vy = abs(vy) * 0.5 }
        if x < Double(wa.minX) { x = Double(wa.minX); vx = abs(vx) * 0.6 }
        if x > Double(wa.maxX) - width { x = Double(wa.maxX) - width; vx = -abs(vx) * 0.6 }
        if onFloor { vx *= pow(0.3, dt); if abs(vx) < 4 { vx = 0 } }
        spin += abs(vx * dt) + abs(vy * dt) * 0.3
        (window.contentView as? PixelView)?.grid = Int(spin / (6 * s)) % 2 == 0 ? Art.yarnA : Art.yarnB
        move(x, y)
    }

    func mouseDown(_ e: NSEvent) {
        if e.clickCount >= 2 { putAway = true; return }
        let c = Screen.cursor()
        held = true; vx = 0; vy = 0
        grab = CGPoint(x: Double(c.x) - x, y: Double(c.y) - y)
        lastMs = Clock.ms; lastX = x; lastY = y
    }

    func mouseDragged() {
        guard held else { return }
        let c = Screen.cursor()
        let nx = Double(c.x - grab.x), ny = Double(c.y - grab.y)
        let now = Clock.ms
        let dt = Double(now - lastMs) / 1000
        if dt > 0.005 {
            vx = 0.5 * vx + 0.5 * (nx - lastX) / dt
            vy = 0.5 * vy + 0.5 * (ny - lastY) / dt
            lastX = nx; lastY = ny; lastMs = now
        }
        x = nx; y = ny
        move(x, y)
    }

    func mouseUp() {
        guard held else { return }
        held = false
        if Clock.ms - lastMs > 120 { vx = 0; vy = 0 }
        vx = max(-3000, min(3000, vx)); vy = max(-3000, min(3000, vy))
    }
}

final class YarnView: PixelView {
    weak var yarn: Yarn?
    override func mouseDown(with event: NSEvent) { yarn?.mouseDown(event) }
    override func mouseDragged(with event: NSEvent) { yarn?.mouseDragged() }
    override func mouseUp(with event: NSEvent) { yarn?.mouseUp() }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
