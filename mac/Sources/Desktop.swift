import AppKit

// Milliseconds since boot (never wraps, unlike Windows' tick count).
enum Clock {
    static var ms: Int { Int(ProcessInfo.processInfo.systemUptime * 1000) }
}

// A flat edge Abu can stand on: the top of a window, or the floor (wid = 0).
struct Surface {
    var x1: Double, x2: Double, y: Double
    var wid: CGWindowID
}

// All positions in the app use "top-left" screen coordinates (like Windows): y grows downward from the
// top of the main screen. macOS windows use bottom-left, so we convert here.
enum Screen {
    static var primaryHeight: Double { Double(NSScreen.screens.first?.frame.height ?? 900) }

    static func toTopLeft(_ r: NSRect) -> CGRect {
        CGRect(x: r.minX, y: primaryHeight - r.maxY, width: r.width, height: r.height)
    }

    static func cursor() -> CGPoint {
        let m = NSEvent.mouseLocation
        return CGPoint(x: m.x, y: primaryHeight - m.y)
    }

    static func screen(at p: CGPoint) -> NSScreen? {
        for s in NSScreen.screens where toTopLeft(s.frame).contains(p) { return s }
        return NSScreen.main ?? NSScreen.screens.first
    }

    // Usable area (without menu bar and Dock), top-left coordinates.
    static func workArea(at p: CGPoint) -> CGRect {
        guard let s = screen(at: p) else { return CGRect(x: 0, y: 0, width: 1440, height: 900) }
        return toTopLeft(s.visibleFrame)
    }

    static func setTopLeft(_ w: NSWindow, _ x: Double, _ y: Double) {
        let o = NSPoint(x: x.rounded(), y: (primaryHeight - y - Double(w.frame.height)).rounded())
        if w.frame.origin != o { w.setFrameOrigin(o) }
    }

    static func topLeft(_ w: NSWindow) -> CGPoint {
        CGPoint(x: w.frame.minX, y: primaryHeight - w.frame.maxY)
    }
}

enum Desktop {
    static let myPid = ProcessInfo.processInfo.processIdentifier

    static func windowList(_ opts: CGWindowListOption, _ wid: CGWindowID = kCGNullWindowID) -> [[String: Any]] {
        (CGWindowListCopyWindowInfo(opts, wid) as? [[String: Any]]) ?? []
    }

    static func bounds(_ w: [String: Any]) -> CGRect? {
        guard let d = w[kCGWindowBounds as String] as? NSDictionary else { return nil }
        return CGRect(dictionaryRepresentation: d as CFDictionary)
    }

    // Top edges of normal windows that aren't hidden behind other windows. (Window positions don't need
    // any special permission; we never read window titles or contents.)
    static func scan(minWidth: Double, minTopY: Double) -> [Surface] {
        var above: [CGRect] = []
        var out: [Surface] = []
        for w in windowList([.optionOnScreenOnly, .excludeDesktopElements]) {   // front to back
            guard (w[kCGWindowLayer as String] as? Int) == 0 else { continue }
            if (w[kCGWindowOwnerPID as String] as? Int32) == myPid { continue }
            if let a = w[kCGWindowAlpha as String] as? Double, a < 0.1 { continue }
            guard let r = bounds(w), r.width >= 120, r.height >= 60 else { continue }
            let wid = (w[kCGWindowNumber as String] as? NSNumber)?.uint32Value ?? 0
            if Double(r.minY) >= minTopY {
                var segs: [(Double, Double)] = [(Double(r.minX), Double(r.maxX))]
                for a in above where a.minY < r.minY && a.maxY > r.minY {
                    segs = subtract(segs, Double(a.minX), Double(a.maxX))
                }
                for s in segs where s.1 - s.0 >= minWidth {
                    out.append(Surface(x1: s.0, x2: s.1, y: Double(r.minY), wid: wid))
                }
            }
            above.append(r)
        }
        return out
    }

    static func subtract(_ segs: [(Double, Double)], _ a: Double, _ b: Double) -> [(Double, Double)] {
        var out: [(Double, Double)] = []
        for s in segs {
            if b <= s.0 || a >= s.1 { out.append(s); continue }
            if a > s.0 { out.append((s.0, a)) }
            if b < s.1 { out.append((b, s.1)) }
        }
        return out
    }

    // Current frame of one window, or nil if it's closed / minimized / on another Space.
    static func frame(_ wid: CGWindowID) -> CGRect? {
        guard let w = windowList([.optionIncludingWindow], wid).first,
              (w[kCGWindowIsOnscreen as String] as? Bool) == true else { return nil }
        return bounds(w)
    }

    // True when the front app covers a whole screen (games, borderless fullscreen video).
    // Native macOS fullscreen apps live on their own Space, where Abu doesn't appear anyway.
    static func fullscreenActive() -> Bool {
        guard let front = NSWorkspace.shared.frontmostApplication?.processIdentifier, front != myPid else { return false }
        let screens = NSScreen.screens.map { Screen.toTopLeft($0.frame) }
        for w in windowList([.optionOnScreenOnly, .excludeDesktopElements]) {
            guard (w[kCGWindowOwnerPID as String] as? Int32) == front,
                  (w[kCGWindowLayer as String] as? Int) == 0, let r = bounds(w) else { continue }
            for s in screens where r.minX <= s.minX && r.minY <= s.minY && r.maxX >= s.maxX && r.maxY >= s.maxY { return true }
        }
        return false
    }

    // Seconds since the last keyboard / mouse input anywhere.
    static func idleSeconds() -> Double {
        guard let any = CGEventType(rawValue: ~0) else { return 0 }
        return CGEventSource.secondsSinceLastEventType(.combinedSessionState, eventType: any)
    }
}
