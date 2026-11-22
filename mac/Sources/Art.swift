import AppKit

enum Expr: Int { case wide, surprised, cry, smug, drowsy, cheeky, happy, sleep, content, dizzy, beg, eat }

// Everything needed to draw one frame.
struct Look {
    var expr: Expr = .wide
    var dx = 0, dy = 0              // pupil offset (-3...3)
    var blink = false, zzz = false
    var feet = 0                    // 0 stand, 1 left paw up, 2 right paw up
    var bob = 0, hop = 0, lift = 0
    var ear = 0                     // 0 normal, 1 left folded, 2 right folded
    var paw = false, tongue = false, pawDy = 0
    var tail = 0                    // 0 hidden, 1 right, 2 left
    var stars = false, chomp = false
    var hat = 0, tangled = false
    var animMs = 0
    var bubble: String? = nil
    var hearts: [CGPoint] = []
    var notes: [CGPoint] = []

    func sig() -> Int {
        var h = Hasher()
        h.combine(expr.rawValue); h.combine(dx); h.combine(dy); h.combine(blink)
        h.combine(feet); h.combine(bob); h.combine(hop); h.combine(lift); h.combine(ear)
        h.combine(paw); h.combine(pawDy); h.combine(tongue); h.combine(chomp); h.combine(tail)
        h.combine(hat); h.combine(tangled)
        if expr == .cry { h.combine((animMs / 90) % 8) }
        if zzz { h.combine(animMs / 120) }
        if stars { h.combine(animMs / 80) }
        h.combine(bubble ?? "")
        for p in hearts { h.combine(p.x); h.combine(p.y) }
        for p in notes { h.combine(p.x); h.combine(p.y) }
        return h.finalize()
    }
}

// Pixel art. Every letter is one cell; a cell is drawn as a scale x scale square.
// Same art as the Windows version (src/Art.cs).
enum Art {
    static let CW = 56, CH = 49      // canvas in cells
    static let OX = 10, OY = 18      // where the body starts on the canvas
    static let BodyW = 36, BodyH = 29

    static let bodyHalf = [
        "......KK..........",
        ".....KKKK.........",
        ".....KPPKK........",
        "....KKPPPKK.......",
        "....KPPPPPKKKKKKKK",
        "....KPPPPPKKKKKKKK",
        "...KKPPPPKKKKKKKKK",
        "...KKKPPKKKKKKKKKK",
        "..KKKKKKKKKKKKKKKK",
        "..KKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        "KKKKKKKKKKKKKKKKKK",
        "KKKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        ".KKKKKKKKKKKKKKKKK",
        "..KKKKKKKKKKKKKKKK",
        "..KKKKKKKKKKKKKKKK",
        "...KKKKKKKKKKKKKKK",
        "....KKKKKKKKKKKKKK",
        ".....KKKKKKKKKKKKK",
    ]

    static let wideEye = [
        "...OOOOO...",
        ".OOOOOOOOO.",
        ".OOOOOOOOO.",
        "OOOOOOOOOOO",
        "OOOOOOOOOOO",
        "OOOOOOOOOOO",
        "OOOOOOOOOOO",
        ".OOOOOOOOO.",
        ".OOOOOOOOO.",
        "...OOOOO...",
    ]
    static let blinkEye = [".OOOOOOOOO."]
    static let pupil = [".KK.", "KKWK", "KKKK", ".KK."]
    static let smugEye = ["OOOWKKKKOO", "OOOKKKKKOO", ".OOOOKOOO.", ".OOOOOOO..", "...OOO...."]
    static let drowsyEye = [".OOOOKOOO.", ".OOOOOOO..", "...OOO...."]
    static let annoyedEye = ["OOOOOOKKKO", "OOOOOOKKKO", ".OOOOOOOO.", "...OOOO..."]
    static let xEye = ["OO.OO", ".OOO.", "..O..", ".OOO.", "OO.OO"]
    static let brow = ["BBBB......", "....BBBBB."]
    static let happyEye = ["...OOOOO...", ".OO.....OO.", "O.........O"]
    static let sleepEye = ["O.........O", ".OO.....OO.", "...OOOOO..."]
    static let tongueG = ["PPPP", "PPPP", ".PP."]
    static let cryMouth = ["PPPP", "PPPP", "PPPP"]
    static let yawnMouth = [".PP.", "PPPP", "PPPP", ".PP."]
    static let chompOpen = ["PPPP", "PPPP"]
    static let chompShut = [".PP."]
    static let smallO = ["PP", "PP"]
    static let wobbly = ["P.P.P", ".P.P."]
    static let wMouth = ["P.PP.P", ".P..P."]
    static let blush = ["PPP"]
    static let pawUp = [".KKK.", "KPKPK", "KKKKK", "KKPKK", "KKKKK", ".KKK."]
    static let tailR = ["......KK", ".....KKK", ".....KK.", "....KK..", "....KK..", "...KK...", "..KKK...", "KKKK....", "KKK....."]
    static let heart = [".HH.HH.", "HHHHHHH", "HHHHHHH", ".HHHHH.", "..HHH..", "...H..."]
    static let star = [".S.", "SSS", ".S."]
    static let note = ["..nnn", "..n.n", "..n..", "nnn..", "nnn.."]
    static let zBig = ["ZZZZ", "..Z.", ".Z..", "ZZZZ"]
    static let zSmall = ["ZZZ", ".Z.", "ZZZ"]

    // food bowl (14 x 6)
    static let bowlFull = ["...FfFFfFFf...", "..fFFfFFfFFf..", "CCCCCCCCCCCCCC", "CLLLLLLLLLLLLC", ".CCCCCCCCCCCC.", "..CCCCCCCCCC.."]
    static let bowlHalf = ["..............", "...fFFfFFfF...", "CCCCCCCCCCCCCC", "CLLLLLLLLLLLLC", ".CCCCCCCCCCCC.", "..CCCCCCCCCC.."]
    static let bowlEmpty = ["..............", "..............", "CCCCCCCCCCCCCC", "CLLLLLLLLLLLLC", ".CCCCCCCCCCCC.", "..CCCCCCCCCC.."]

    // hats
    static let hatNames = ["None", "Bow", "Flower", "Party hat", "Headphones", "Crown", "Santa hat", "Witch hat"]
    static let hatLevel = [0, 2, 3, 4, 6, 8, 0, 0]
    static let hatSanta = 6, hatWitch = 7, hatParty = 3
    static let hatBow = ["QQ.....QQ", "QQQQ.QQQQ", "QQQQqQQQQ", "QQQQ.QQQQ", "QQ.....QQ"]
    static let hatFlower = ["..NNN..", ".NNNNN.", "NNNYNNN", "NNYYYNN", "NNNYNNN", ".NNNNN.", "..NNN.."]
    static let hatPartyCone = ["....Y....", "...YYY...", "....R....", "...RRR...", "...YYY...", "..RRRRR..", "..YYYYY..", ".RRRRRRR.", ".YYYYYYY.", "RRRRRRRRR"]
    static let hatCrown = ["Y....Y....Y", "YY..YYY..YY", "YYYYYYYYYYY", "YRYYYbYYYRY", "YYYYYYYYYYY"]
    static let hatSantaCone = ["........NN..", ".......RNN..", "......RRR...", ".....RRRR...", "....RRRRR...", "...RRRRRRR..", ".NNNNNNNNNN.", ".NNNNNNNNNN."]
    static let hatWitchCone = [".......UU....", "......UUU....", ".....UUUU....", ".....UUUUU...", "....UUUUUU...", "....UYYYYU...", "...UUUUUUUU..", "UUUUUUUUUUUUU"]

    // toys
    static let yarnA = ["..QQQQ..", ".QqQQqQ.", "QQQqqQQQ", "QqQQQqQQ", "QQqQQQqQ", "QQQqqQQQ", ".QqQQqQ.", "..QQQQ.."]
    static let yarnB = rotate(yarnA)
    static let laserDot = [".xxx.", "xXXXx", "xXXXx", "xXXXx", ".xxx."]

    static func rotate(_ g: [String]) -> [String] {
        let rows = g.map { Array($0) }
        let h = rows.count, w = rows[0].count
        return (0..<w).map { x in String((0..<h).map { y in rows[h - 1 - y][x] }) }
    }

    static func rgb(_ r: Int, _ g: Int, _ b: Int) -> NSColor {
        NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1)
    }

    static let colors: [Character: NSColor] = [
        "K": rgb(16, 16, 16), "P": rgb(240, 128, 112), "O": rgb(247, 172, 52), "W": .white,
        "B": rgb(110, 50, 40), "T": rgb(190, 230, 255), "H": rgb(255, 95, 125), "Z": rgb(225, 225, 255),
        "S": rgb(255, 222, 90), "C": rgb(222, 78, 78), "L": rgb(255, 236, 236), "F": rgb(160, 98, 50),
        "f": rgb(210, 150, 90), "Q": rgb(255, 125, 170), "q": rgb(200, 70, 120), "N": rgb(250, 250, 250),
        "Y": rgb(250, 200, 60), "R": rgb(220, 60, 70), "U": rgb(130, 80, 175), "b": rgb(80, 140, 230),
        "M": rgb(150, 150, 165), "X": rgb(255, 30, 30), "x": rgb(255, 140, 140), "n": rgb(120, 215, 255),
    ]

    static var bodyCache: [Int: [String]] = [:]
    static func bodyRows(_ ear: Int) -> [String] {
        if let b = bodyCache[ear] { return b }
        var rows: [String] = []
        for (i, half) in bodyHalf.enumerated() {
            var left = half, right = String(half.reversed())
            if i < 2 && ear == 1 { left = String(repeating: ".", count: left.count) }
            if i < 2 && ear == 2 { right = String(repeating: ".", count: right.count) }
            rows.append(left + right)
        }
        bodyCache[ear] = rows
        return rows
    }

    // Draws a grid, merging runs of the same colour into one rectangle (fast).
    // Expects a flipped (top-left origin) view.
    static func grid(_ rows: [String], _ x: Int, _ y: Int, _ s: CGFloat, mirror: Bool = false) {
        for (r, row) in rows.enumerated() {
            let chars = Array(row)
            let w = chars.count
            var c = 0
            while c < w {
                let ch = chars[c]
                if ch == "." { c += 1; continue }
                var e = c + 1
                while e < w && chars[e] == ch { e += 1 }
                let x0 = mirror ? x + w - e : x + c
                (colors[ch] ?? .black).setFill()
                NSRect(x: CGFloat(x0) * s, y: CGFloat(y + r) * s, width: CGFloat(e - c) * s, height: s).fill()
                c = e
            }
        }
    }

    static func rect(_ ch: Character, _ x: Int, _ y: Int, _ w: Int, _ h: Int, _ s: CGFloat) {
        (colors[ch] ?? .black).setFill()
        NSRect(x: CGFloat(x) * s, y: CGFloat(y) * s, width: CGFloat(w) * s, height: CGFloat(h) * s).fill()
    }

    static func draw(_ L: Look, _ s: CGFloat) {
        NSGraphicsContext.current?.shouldAntialias = false
        let bx = OX, by = OY + L.bob + L.hop - L.lift
        let fy = OY + BodyH + L.hop

        if L.tail == 1 { grid(tailR, bx + 33, by + 15, s) }
        if L.tail == 2 { grid(tailR, bx - 5, by + 15, s, mirror: true) }

        rect("K", bx + 8, fy - 1 - L.lift, 5, (L.feet == 1 ? 2 : 3) + L.lift, s)
        rect("K", bx + 23, fy - 1 - L.lift, 5, (L.feet == 2 ? 2 : 3) + L.lift, s)

        grid(bodyRows(L.ear), bx, by, s)
        drawHat(L.hat, bx, by, s)

        switch L.expr {
        case .wide, .surprised, .cry, .beg:
            if L.blink && L.expr != .beg {
                grid(blinkEye, bx + 5, by + 15, s)
                grid(blinkEye, bx + 20, by + 15, s)
            } else {
                var dx = L.dx, dy = L.dy
                if L.expr == .surprised { dx = 0; dy = 2 }
                if L.expr == .cry || L.expr == .beg { dx = 0; dy = -2 }
                grid(wideEye, bx + 5, by + 10, s)
                grid(wideEye, bx + 20, by + 10, s)
                grid(pupil, bx + 8 + dx, by + 13 + dy, s)
                grid(pupil, bx + 23 + dx, by + 13 + dy, s)
            }
            if L.expr == .surprised { grid(smallO, bx + 17, by + 21, s) }
            if L.expr == .beg {
                rect("W", bx + 7, by + 16, 2, 1, s); rect("W", bx + 22, by + 16, 2, 1, s)
                rect("W", bx + 12, by + 17, 1, 1, s); rect("W", bx + 27, by + 17, 1, 1, s)
                grid(chompShut, bx + 16, by + 21, s)
            }
            if L.expr == .cry {
                rect("W", bx + 7, by + 17, 1, 1, s); rect("W", bx + 28, by + 17, 1, 1, s)
                grid(cryMouth, bx + 16, by + 20, s)
                let phase = (L.animMs / 90) % 8
                for k in stride(from: 0, to: 8, by: 4) {
                    let ty = 18 + (phase + k) % 8
                    if ty + 1 > BodyH - 2 { continue }
                    rect("T", bx + 6, by + ty, 1, 2, s); rect("T", bx + 29, by + ty, 1, 2, s)
                }
            }
        case .smug:
            grid(smugEye, bx + 5, by + 13, s); grid(smugEye, bx + 21, by + 13, s, mirror: true)
        case .drowsy:
            grid(drowsyEye, bx + 5, by + 15, s); grid(drowsyEye, bx + 21, by + 15, s, mirror: true)
            grid(yawnMouth, bx + 16, by + 20, s)
        case .cheeky:
            grid(brow, bx + 5, by + 12, s); grid(brow, bx + 21, by + 12, s, mirror: true)
            grid(annoyedEye, bx + 5, by + 14, s); grid(annoyedEye, bx + 21, by + 14, s)
            grid(tongueG, bx + 16, by + 20, s)
        case .happy:
            grid(happyEye, bx + 5, by + 13, s); grid(happyEye, bx + 20, by + 13, s)
            grid(blush, bx + 3, by + 17, s); grid(blush, bx + 30, by + 17, s)
            grid(wMouth, bx + 15, by + 19, s)
        case .content:
            grid(happyEye, bx + 5, by + 14, s); grid(happyEye, bx + 20, by + 14, s)
        case .eat:
            grid(happyEye, bx + 5, by + 14, s); grid(happyEye, bx + 20, by + 14, s)
            grid(L.chomp ? chompOpen : chompShut, bx + 16, by + 20, s)
        case .dizzy:
            grid(xEye, bx + 8, by + 13, s); grid(xEye, bx + 23, by + 13, s)
            grid(wobbly, bx + 16, by + 21, s)
        case .sleep:
            grid(sleepEye, bx + 5, by + 15, s); grid(sleepEye, bx + 20, by + 15, s)
        }

        if L.tangled {
            for i in 0..<3 {
                for x in 0..<BodyW {
                    let y = 9 + i * 6 + Int((sin(Double(x + i * 7) / 5.0) * 2).rounded())
                    rect(i == 1 ? "q" : "Q", bx + x, by + y, 1, 1, s)
                }
            }
        }
        if L.tongue { grid(tongueG, bx + 16, by + 20, s) }
        if L.paw { grid(pawUp, bx - 2, by + 18 + L.pawDy, s) }

        if L.stars {
            for i in 0..<3 {
                let a = Double(L.animMs) / 250.0 + Double(i) * 2.094
                grid(star, bx + 17 + Int((cos(a) * 13).rounded()), by + 2 + Int((sin(a) * 3).rounded()), s)
            }
        }
        for p in L.hearts { grid(heart, Int(p.x), Int(p.y), s) }
        for p in L.notes { grid(note, Int(p.x), Int(p.y), s) }
        if L.zzz {
            for i in 0..<3 {
                let t = (L.animMs + i * 1000) % 3000
                grid(i == 1 ? zBig : zSmall, OX + 32 + t * 6 / 3000, OY + 1 - t * 12 / 3000, s)
            }
        }
        if let b = L.bubble, !b.isEmpty { drawBubble(b, s) }
    }

    static func drawHat(_ hat: Int, _ bx: Int, _ by: Int, _ s: CGFloat) {
        switch hat {
        case 1: grid(hatBow, bx + 22, by + 2, s)
        case 2: grid(hatFlower, bx + 22, by + 3, s)
        case 3: grid(hatPartyCone, bx + 13, by - 5, s)
        case 4:
            rect("M", bx + 3, by + 3, 30, 1, s)
            rect("M", bx + 2, by + 4, 1, 2, s); rect("M", bx + 33, by + 4, 1, 2, s)
            rect("M", bx + 1, by + 6, 1, 6, s); rect("M", bx + 34, by + 6, 1, 6, s)
            rect("M", bx - 1, by + 11, 3, 7, s); rect("M", bx + 34, by + 11, 3, 7, s)
            rect("U", bx - 1, by + 12, 1, 5, s); rect("U", bx + 36, by + 12, 1, 5, s)
        case 5: grid(hatCrown, bx + 12, by, s)
        case 6: grid(hatSantaCone, bx + 12, by - 2, s)
        case 7: grid(hatWitchCone, bx + 11, by - 3, s)
        default: break
        }
    }

    static func drawBubble(_ text: String, _ s: CGFloat) {
        let font = NSFont.boldSystemFont(ofSize: 5.5 * s)
        let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: colors["K"]!]
        let ts = (text as NSString).size(withAttributes: attrs)
        let pad = 3 * s
        let maxW = CGFloat(CW) * s - 2 * s
        let w = min(maxW, ((ts.width + 2 * pad) / s).rounded(.up) * s + s)
        let h = ((ts.height + 2 * s) / s).rounded(.up) * s
        let x = (((CGFloat(CW) * s - w) / 2) / s).rounded(.down) * s
        let y = max(0, CGFloat(OY - 3) * s - h)
        NSColor.white.setFill(); NSRect(x: x + s, y: y + s, width: w - 2 * s, height: h - 2 * s).fill()
        colors["K"]!.setFill()
        NSRect(x: x + s, y: y, width: w - 2 * s, height: s).fill()
        NSRect(x: x + s, y: y + h - s, width: w - 2 * s, height: s).fill()
        NSRect(x: x, y: y + s, width: s, height: h - 2 * s).fill()
        NSRect(x: x + w - s, y: y + s, width: s, height: h - 2 * s).fill()
        let cx = ((x + w / 2) / s).rounded(.down) * s
        NSRect(x: cx - s, y: y + h, width: 2 * s, height: s).fill()
        NSRect(x: cx - s, y: y + h + s, width: s, height: s).fill()
        NSGraphicsContext.current?.shouldAntialias = true
        (text as NSString).draw(at: NSPoint(x: x + pad - s, y: y + s), withAttributes: attrs)
        NSGraphicsContext.current?.shouldAntialias = false
    }
}
