import AppKit
import ServiceManagement

// The view that draws Abu and passes mouse events to the cat.
final class CatView: NSView {
    weak var cat: Cat?
    var look = Look()
    var scale: CGFloat = 2
    override var isFlipped: Bool { true }
    override func draw(_ dirtyRect: NSRect) { Art.draw(look, scale) }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with e: NSEvent) { cat?.mouseDown(e) }
    override func mouseDragged(with e: NSEvent) { cat?.mouseDragged() }
    override func mouseUp(with e: NSEvent) { cat?.mouseUp() }
    override func rightMouseDown(with e: NSEvent) { cat?.showMenu(e, in: self) }
}

// A menu item that runs a closure.
final class ActionItem: NSMenuItem {
    var handler: (() -> Void)?
    convenience init(_ title: String, checked: Bool = false, enabled: Bool = true, _ handler: (() -> Void)? = nil) {
        self.init(title: title, action: handler == nil ? nil : #selector(ActionItem.fire), keyEquivalent: "")
        self.handler = handler
        target = self
        state = checked ? .on : .off
        isEnabled = enabled && handler != nil
    }
    @objc func fire() { handler?() }
}

final class Cat {
    enum Act { case idle, walk, held, sleep, trick, eat, fall }
    enum Trick { case stretch, groom, wiggle, hunt, chaseTail, stare, trip, bonk, forget, beg, tangled, land, jump }
    enum Goal { case wander, bowl, zoom, yarn, laser, climb }
    enum SleepWhy { case nap, away, forced }

    let awaySleepMs = 5 * 60 * 1000, lonelyMs = 60 * 60 * 1000, breakAfterMs = 60 * 60 * 1000

    let st: Store
    var look = Look()
    let window: PetPanel
    let view = CatView()
    var timer: Timer?
    var interval = 0

    var scale = 2
    var act = Act.idle
    var sleepWhy = SleepWhy.nap
    var trick = Trick.stretch, trickStart = 0, trickLen = 0
    var goal = Goal.wander
    var pounceFromX = 0.0, pounceFromY = 0.0, pounceToX = 0.0, pounceToY = 0.0, pounceSet = false, huntMiss = false
    var sleepStart = 0, napUntil = 0
    var hasEmote = false, emote = Expr.wide, emoteUntil = 0
    var bubble: String? = nil, bubbleUntil = 0
    var px = 0.0, py = 0.0, tx = 0.0, ty = 0.0
    var lastTick = 0, decideAt = 0, nextBlink = 0, blinkEnd = 0, stepAt = 0, hopStart = -100000, glanceAt = 0, glanceDx = 0, glanceDy = 0
    var blinking = false, stepLeft = false
    var lastPet = 0, lastPetSound = -100000, nextLonely = 0, activeSince = 0, nextBreakNag = 0, hoverStart = 0
    var nextDayCheck = 0, nextSave = 0, nextHungerNag = 0
    var hovering = false, hoverShown = false
    var lastSig = Int.min

    var walkClamped = false, hasTrip = false, hasForget = false, tripAt = 0, forgetAt = 0, zoomLeft = 0
    var stareDx = 0, stareDy = 0
    var lastDeed: String? = nil, lastDeedAt = -100000

    var bowl: Prop? = nil, bowlEaten = false, bowlCloseAt = 0, eatStart = 0, nextBite = 0
    var yarn: Yarn? = nil, yarnBats = 0, yarnMaxBats = 0, yarnChaseUntil = 0, batCooldown = 0, lastYarnEnd = -100000
    var laser: Prop? = nil, laserUntil = 0, nextLaserPounce = 0
    var huntCrouch = 1300
    var lastPetXp = -100000, curHat = 0

    var vx = 0.0, vy = 0.0, fallFromY = 0.0
    var standWid: CGWindowID = 0, standRect = CGRect.zero
    var surfaces: [Surface] = [], nextScan = 0
    var jumpFromX = 0.0, jumpFromY = 0.0, jumpToX = 0.0, jumpToY = 0.0, jumpH = 0.0, jumpWid: CGWindowID = 0, jumpShort = false
    var climbTo = Surface(x1: 0, x2: 0, y: 0, wid: 0)
    var dragVx = 0.0, dragVy = 0.0

    var hidden = false, nextFsCheck = 0
    var mouseIsDown = false, dragging = false, dizzy = false
    var downScreen = CGPoint.zero, dragOff = CGPoint.zero
    var shake = 0.0, lastMoveMs = 0
    var journal: JournalWindow? = nil

    var W: Double { Double(Art.CW * scale) }
    var H: Double { Double(Art.CH * scale) }
    var s: Double { Double(scale) }
    var gravity: Bool { st.gravity && !st.follow }
    var catCx: Double { px + W / 2 }
    var footY: Double { py + H }
    var workArea: CGRect { Screen.workArea(at: CGPoint(x: catCx, y: footY - 5)) }

    init() {
        let store = Store()
        store.load()
        st = store
        scale = store.scale
        window = PetPanel(size: NSSize(width: Art.CW * store.scale, height: Art.CH * store.scale))
        view.frame = NSRect(x: 0, y: 0, width: Art.CW * scale, height: Art.CH * scale)
        view.scale = CGFloat(scale)
        view.cat = self
        window.contentView = view

        let wa = Screen.workArea(at: Screen.cursor())
        if let x = st.x, let y = st.y { px = x; py = y } else { px = Double(wa.maxX) - W - 120; py = Double(wa.maxY) - H }
        clampPos(&px, &py)
        Screen.setTopLeft(window, px, py)
        window.orderFrontRegardless()

        let now = Clock.ms
        curHat = st.currentHat()
        lastTick = now; lastPet = now; nextLonely = now; activeSince = now; nextBreakNag = now
        decideAt = now + 4000; nextBlink = now + 2500; glanceAt = now
        nextDayCheck = now + 3000; nextSave = now + 60000; nextHungerNag = now + 20000
        say("Meow!", 2500, now)
        setInterval(40)
    }

    // ---------- helpers ----------
    func setInterval(_ ms: Int) {
        if ms == interval { return }
        interval = ms
        timer?.invalidate()
        let t = Timer(timeInterval: Double(ms) / 1000, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func clamp(_ v: Double, _ lo: Double, _ hi: Double) -> Double { v < lo ? lo : (v > hi ? hi : v) }
    func roll(_ chance: Double) -> Bool { Double.random(in: 0..<1) < chance }
    func rnd(_ a: Int, _ b: Int) -> Int { Int.random(in: a..<b) }
    func isNight() -> Bool { let h = Calendar.current.component(.hour, from: Date()); return h >= 22 || h < 5 }

    func catRect() -> CGRect {
        CGRect(x: px + Double(Art.OX) * s, y: py + Double(Art.OY) * s, width: Double(Art.BodyW) * s, height: Double(Art.BodyH + 2) * s)
    }

    func clampPos(_ x: inout Double, _ y: inout Double) {
        let wa = Screen.workArea(at: CGPoint(x: x + W / 2, y: y + H - 5))
        x = clamp(x, Double(wa.minX) - Double(Art.OX) * s, Double(wa.maxX) - Double(Art.OX + Art.BodyW) * s)
        y = clamp(y, Double(wa.minY) - Double(Art.OY) * s, Double(wa.maxY) - H)
    }

    func moveWindow() { Screen.setTopLeft(window, px, py) }
    func say(_ t: String, _ ms: Int, _ now: Int) { bubble = t; bubbleUntil = now + ms }
    func setEmote(_ e: Expr, _ ms: Int, _ now: Int) { hasEmote = true; emote = e; emoteUntil = now + ms }
    func hop(_ now: Int) { hopStart = now }
    func goof() { st.count(Store.goofs) }

    func gainXp(_ n: Int, _ now: Int) {
        let before = st.level
        st.xp += n
        let after = st.level
        if after <= before { return }
        for i in 0..<3 { hearts.append((Art.OX + 6 + i * 9, now - i * 250)) }
        let unlocked = Art.hatLevel.contains(after)
        say(unlocked ? "Lv \(after)! New hat!" : "Bond level \(after)!", 4000, now)
        setEmote(.happy, 2500, now)
        Sounds.play("meow")
        curHat = st.currentHat()
        saveAll()
    }

    var hearts: [(Int, Int)] = []

    func startSleep(_ why: SleepWhy, _ now: Int) {
        act = .sleep; sleepWhy = why; sleepStart = now
        hasEmote = false; bubble = nil
        st.count(Store.naps)
    }

    func wakeUp(_ now: Int, _ msg: String?) {
        act = .idle
        setEmote(.drowsy, 1200, now)
        decideAt = now + 4000
        if let m = msg { say(m, 3000, now) }
    }

    func startTrick(_ t: Trick, _ len: Int, _ now: Int) {
        act = .trick; trick = t; trickStart = now; trickLen = len; pounceSet = false
        hasEmote = false
    }

    func endDeed(_ now: Int, _ a: Int, _ b: Int) {
        act = .idle
        lastDeedAt = now
        decideAt = now + rnd(a, b)
    }

    func pet(_ now: Int) {
        lastPet = now; nextLonely = now
        st.count(Store.petted)
        st.affection = Store.clamp(st.affection + 6)
        st.fun = Store.clamp(st.fun + 2)
        if now - lastPetXp > 3000 { lastPetXp = now; gainXp(1, now) }
        if act == .trick && trick == .tangled { act = .idle; say("Thanks!", 1500, now) }
        if let d = lastDeed, act == .trick || now - lastDeedAt < 5000 { st.reward(d); lastDeed = nil }

        if act == .sleep { wakeUp(now, "Mrrp?"); Sounds.play("mrrp"); return }
        if act == .walk || act == .trick { act = .idle }
        setEmote(.happy, 2200, now)
        hearts.append((Art.OX + 14 + rnd(-8, 9), now))
        Sounds.play(now - lastPetSound < 3000 ? "purr" : "meow")
        lastPetSound = now
        if roll(0.35) { say(roll(0.5) ? "Purr~" : "Nya!", 1800, now) }
        decideAt = now + 4000
    }

    // ---------- walking ----------
    func walkTo(_ x0: Double, _ y0: Double, _ g: Goal, _ now: Int) {
        var x = x0, y = (gravity && g != .yarn && g != .laser) ? py : y0
        let rx = x
        clampPos(&x, &y)
        tx = x; ty = y
        walkClamped = abs(rx - x) > 2
        goal = g; act = .walk; stepAt = now
        hasTrip = false; hasForget = false
    }

    func pickTarget(_ now: Int) {
        var x = px + Double(rnd(-300, 301))
        if gravity && standWid != 0 && !roll(Brain.goofWalkOff) {
            x = clamp(x + W / 2, Double(standRect.minX) + 20 * s, Double(standRect.maxX) - 20 * s) - W / 2
        }
        walkTo(x, py + Double(rnd(-120, 121)), .wander, now)
        if roll(Brain.goofTrip) { hasTrip = true; tripAt = now + rnd(400, 1500) }
        else if roll(Brain.goofForget) { hasForget = true; forgetAt = now + rnd(800, 2000) }
    }

    func startZoomies(_ now: Int) {
        zoomLeft = rnd(3, 7)
        st.count(Store.played)
        st.fun = Store.clamp(st.fun + 6); st.energy = Store.clamp(st.energy - 3)
        gainXp(3, now)
        nextZoom(now)
    }

    func nextZoom(_ now: Int) { walkTo(px + Double(rnd(-350, 351)), py + Double(rnd(-150, 151)), .zoom, now) }

    func goToBowl(_ now: Int) {
        guard let b = bowl else { return }
        let tl = b.topLeft
        let bx = Double(tl.x) + b.width / 2 - W / 2
        let by = Double(tl.y) + b.height - H
        if abs(bx - px) + abs(by - py) < 3 { startEat(now); return }
        if gravity && abs(by - py) > 20 { startJump(bx, by, 0, false, now); return }
        walkTo(bx, by, .bowl, now)
    }

    // ---------- food ----------
    func feed() {
        let now = Clock.ms
        if bowl != nil { say("Already got food!", 2000, now); return }
        if act == .sleep { wakeUp(now, "Food?!") }
        let b = Prop(grid: Art.bowlFull, scale: scale)
        let wa = workArea
        let side: Double = catCx < Double(wa.midX) ? 1 : -1
        let bx = clamp(catCx + side * 70 * s - b.width / 2, Double(wa.minX), Double(wa.maxX) - b.width)
        let by = gravity ? Double(wa.maxY) - b.height : clamp(footY - b.height, Double(wa.minY), Double(wa.maxY) - b.height)
        b.move(bx, by)
        b.show()
        bowl = b; bowlEaten = false
        say("Food!!", 1500, now)
        Sounds.play("mrrp")
        if act != .held { goToBowl(now) }
    }

    func startEat(_ now: Int) {
        act = .eat; eatStart = now; nextBite = now + 200
        hasEmote = false; bubble = nil
        px = tx; py = ty
    }

    func finishEat(_ now: Int) {
        st.hunger = 0
        st.fun = Store.clamp(st.fun + 5); st.affection = Store.clamp(st.affection + 5); st.energy = Store.clamp(st.energy + 5)
        st.count(Store.fed)
        gainXp(5, now)
        bowlEaten = true; bowlCloseAt = now + 1500
        act = .idle
        setEmote(.happy, 2200, now)
        say("Yum!", 2000, now)
        hearts.append((Art.OX + 14, now))
        Sounds.play("mrrp")
        decideAt = now + 5000
        nextHungerNag = now + 10 * 60 * 1000
    }

    // ---------- toys ----------
    func toggleYarn() {
        let now = Clock.ms
        if yarn != nil { removeYarn(); return }
        let wa = workArea
        let x = clamp(catCx + Double(rnd(-120, 121)), Double(wa.minX), Double(wa.maxX) - 8 * s)
        let y = clamp(py + Double(Art.OY) * s, Double(wa.minY), Double(wa.maxY) - 8 * s)
        let yn = Yarn(scale: scale, x: x, y: y)
        yn.kick(Double(rnd(-200, 201)), -300)
        yn.show()
        yarn = yn
        if act == .sleep { wakeUp(now, "Yarn!!") } else { say("Yarn!!", 1500, now) }
    }

    func removeYarn() {
        guard let y = yarn else { return }
        y.close(); yarn = nil
        if act == .walk && goal == .yarn { endDeed(Clock.ms, 2000, 5000) }
    }

    func startYarnChase(_ now: Int) {
        yarnBats = 0; yarnMaxBats = rnd(3, 8); yarnChaseUntil = now + 15000; batCooldown = now
        st.count(Store.played)
        gainXp(3, now)
        act = .walk; goal = .yarn; stepAt = now
        hasTrip = false; hasForget = false; walkClamped = false
    }

    func batYarn(_ now: Int) {
        guard let y = yarn else { return }
        let dir: Double = y.centerX >= catCx ? 1 : -1
        y.kick(dir * Double(rnd(350, 800)) * s / 2, -Double(rnd(250, 650)) * s / 2)
        hop(now)
        yarnBats += 1
        batCooldown = now + 600
        st.fun = Store.clamp(st.fun + 3)
        if roll(0.3) { Sounds.play("mrrp") }
        if roll(Brain.goofTangle) {
            startTrick(.tangled, 3000, now); say("Help?", 2500, now); goof(); lastYarnEnd = now
        } else if yarnBats >= yarnMaxBats || now >= yarnChaseUntil {
            endDeed(now, 3000, 7000); setEmote(.smug, 2000, now); lastYarnEnd = now
        }
    }

    func startLaser() {
        let now = Clock.ms
        if laser != nil { laserUntil = now + 60000; return }
        let l = Prop(grid: Art.laserDot, scale: scale, clickThrough: true)
        laser = l
        placeLaser()
        l.show()
        laserUntil = now + 60000
        nextLaserPounce = now + 600
        if act == .sleep { wakeUp(now, nil) }
        if act != .held && act != .eat { act = .idle }
        say("!!!", 1200, now)
        hop(now)
    }

    func placeLaser() {
        guard let l = laser else { return }
        let c = Screen.cursor()
        l.move(Double(c.x) - l.width / 2, Double(c.y) - l.height / 2)
    }

    func endLaser(_ now: Int) {
        guard let l = laser else { return }
        l.close(); laser = nil
        st.count(Store.played)
        st.fun = Store.clamp(st.fun + 15); st.energy = Store.clamp(st.energy - 5)
        gainXp(5, now)
        if act == .idle || act == .walk || act == .trick {
            startTrick(.forget, 2600, now); say("Where did it go?!", 2400, now)
        }
    }

    // ---------- gravity ----------
    func startFall(_ now: Int, _ msg: String?, _ isGoof: Bool) {
        if act == .sleep { hasEmote = false }
        act = .fall
        fallFromY = py
        standWid = 0
        if vy > 0 { vy = 0 }
        if let m = msg { say(m, 1200, now) }
        if isGoof { goof() }
    }

    func findLanding(_ y0: Double, _ y1: Double) -> Surface? {
        let wa = workArea
        var land: Surface? = y1 >= Double(wa.maxY) ? Surface(x1: Double(wa.minX), x2: Double(wa.maxX), y: Double(wa.maxY), wid: 0) : nil
        for sf in surfaces {
            if catCx < sf.x1 + 8 || catCx > sf.x2 - 8 { continue }
            if sf.y < y0 - 1 || sf.y > y1 { continue }
            if land == nil || sf.y < land!.y { land = sf }
        }
        return land
    }

    func land(_ sf: Surface, _ now: Int) {
        py = sf.y - H
        vx = 0; vy = 0
        standWid = sf.wid
        if sf.wid != 0, let r = Desktop.frame(sf.wid) { standRect = r }
        moveWindow()
        let drop = py - fallFromY
        act = .idle
        decideAt = now + rnd(2500, 6000)
        if drop > 40 * s {
            startTrick(.land, 450, now)
            Sounds.play("thump")
            if drop > 250 * s && roll(0.3) { setEmote(.dizzy, 1600, now); say("Whoa...", 1500, now); goof() }
        }
    }

    func checkSupport(_ now: Int) {
        if standWid != 0 {
            guard let r = Desktop.frame(standWid) else { startFall(now, act == .sleep ? "Whoa!" : "!?", false); return }
            let wasOn = abs(footY - Double(standRect.minY)) <= 6
            let ddx = Double(r.minX - standRect.minX), ddy = Double(r.minY - standRect.minY)
            standRect = r
            if wasOn && (ddx != 0 || ddy != 0) {
                if abs(ddx) + abs(ddy) > 120 { startFall(now, "Whoa!", false); return }
                px += ddx; py += ddy; tx += ddx; ty += ddy
                moveWindow()
            }
            if abs(footY - Double(r.minY)) > 6 { standWid = 0 }
            else if catCx < Double(r.minX) - 4 || catCx > Double(r.maxX) + 4 {
                let g = act == .walk && goal == .wander
                startFall(now, g ? "!?" : nil, g); return
            } else { return }
        }
        if footY < Double(workArea.maxY) - 2 { startFall(now, nil, false) }
    }

    func startJump(_ x0: Double, _ y0: Double, _ wid: CGWindowID, _ canGoof: Bool, _ now: Int) {
        var x = x0, y = y0
        jumpFromX = px; jumpFromY = py
        jumpShort = canGoof && roll(Brain.goofShortJump)
        if jumpShort { x = px + (x - px) * 0.6; y = py + (y - py) * 0.6 }
        clampPos(&x, &y)
        jumpToX = x; jumpToY = y; jumpWid = jumpShort ? 0 : wid
        jumpH = 50 * s + max(0, py - y) * 0.35
        standWid = 0
        startTrick(.jump, 650, now)
    }

    func tryClimb(_ now: Int) -> Bool {
        var options: [Surface] = []
        for sf in surfaces {
            if sf.wid == standWid { continue }
            let nearX = clamp(catCx, sf.x1 + 25 * s, sf.x2 - 25 * s)
            if abs(nearX - catCx) > 450 || abs(sf.y - footY) > 420 || abs(sf.y - footY) < 20 { continue }
            options.append(sf)
        }
        if standWid != 0 {
            let wa = workArea
            options.append(Surface(x1: Double(wa.minX), x2: Double(wa.maxX), y: Double(wa.maxY), wid: 0))
        }
        guard let pick = options.randomElement() else { return false }
        climbTo = pick
        let tcx = clamp(catCx, pick.x1 + 25 * s, pick.x2 - 25 * s)
        if abs(tcx - catCx) > 220 {
            walkTo(tcx - (tcx > catCx ? 150 : -150) - W / 2, py, .climb, now)
        } else { climbJump(now) }
        return true
    }

    func climbJump(_ now: Int) {
        var tcx = clamp(catCx, climbTo.x1 + 25 * s, climbTo.x2 - 25 * s)
        if abs(tcx - catCx) < 40 { tcx += tcx < (climbTo.x1 + climbTo.x2) / 2 ? 40 : -40 }
        startJump(tcx - W / 2, climbTo.y - H, climbTo.wid, true, now)
        st.fun = Store.clamp(st.fun + 2)
    }

    // ---------- fullscreen ----------
    func hideAll() {
        hidden = true
        window.orderOut(nil)
        bowl?.hide(); yarn?.hide()
        laser?.close(); laser = nil
        if act == .walk || act == .trick { act = .idle }
    }

    func showAll(_ now: Int) {
        hidden = false
        window.orderFrontRegardless()
        bowl?.show(); yarn?.show()
        lastSig = Int.min
        lastTick = now
    }

    // ---------- brain ----------
    func doDeed(_ d: Deed, _ now: Int) {
        lastDeed = d.key; lastDeedAt = now
        switch d {
        case .walk: if !st.stay { pickTarget(now) }
        case .nap:
            startSleep(.nap, now)
            napUntil = now + (st.energy < 30 ? rnd(120000, 300000) : rnd(30000, 90000))
        case .groom: startTrick(.groom, 3000, now)
        case .stretch: startTrick(.stretch, 2700, now)
        case .wiggle: startTrick(.wiggle, 1200, now)
        case .hunt: huntCrouch = 1300; startTrick(.hunt, 1700, now)
        case .yarn: if yarn != nil && !st.stay { startYarnChase(now) }
        case .climb: if !(gravity && !st.stay && tryClimb(now)) { setEmote(.smug, 2000, now) }
        case .askFood:
            startTrick(.beg, 4000, now)
            say(roll(0.5) ? "Food?" : st.shortName + " hungry...", 3500, now)
            Sounds.play("sad")
        case .zoomies: if !st.stay { startZoomies(now) }
        case .stare:
            startTrick(.stare, rnd(3000, 5000), now)
            stareDx = roll(0.5) ? -3 : 3; stareDy = rnd(-3, 0)
        case .chaseTail: startTrick(.chaseTail, 2600, now)
        case .smug: setEmote(.smug, rnd(3000, 6000), now)
        case .cheeky: setEmote(.cheeky, 2500, now)
        case .hop: hop(now)
        }
    }

    func checkTimeOfDay(_ now: Int, _ idleMs: Int) {
        curHat = st.currentHat()
        if idleMs < 15000 && st.lastVisit != Store.today { st.lastVisit = Store.today; gainXp(10, now) }
        if act == .sleep || act == .held || act == .eat || idleMs > 15000 || bubble != nil { return }
        let d = Date()
        let c = Calendar.current.dateComponents([.hour, .minute], from: d)
        let hm = (c.hour ?? 0) * 60 + (c.minute ?? 0)
        let today = Store.today
        if hm >= 300 && hm < 660 && st.greet != today {
            st.greet = today; say("Good morning!", 4000, now); setEmote(.happy, 2500, now); Sounds.play("meow")
        } else if hm >= 750 && hm < 840 && st.lunch != today {
            st.lunch = today; say("Lunch time! Go eat~", 6000, now); setEmote(.happy, 2500, now); Sounds.play("meow")
        } else if hm >= 1170 && hm < 1260 && st.dinner != today {
            st.dinner = today; say("Dinner time!", 6000, now); setEmote(.happy, 2500, now); Sounds.play("meow")
        } else if hm >= 1410 || hm < 240 {
            let night = Store.dateKey(d.addingTimeInterval(-5 * 3600))
            if st.late == night { return }
            st.late = night; say("It's late... sleep soon?", 6000, now); setEmote(.drowsy, 2500, now)
        } else { return }
        saveAll()
    }

    // ---------- main loop ----------
    func tick() {
        let now = Clock.ms
        var dt = Double(now - lastTick) / 1000
        lastTick = now
        if dt < 0 { dt = 0 }

        if now >= nextFsCheck {
            nextFsCheck = now + 1000
            let fs = Desktop.fullscreenActive()
            if fs && !hidden { hideAll() } else if !fs && hidden { showAll(now) }
        }
        if hidden {
            st.tick(minutes: min(dt, 2) / 60, sleeping: act == .sleep)
            setInterval(1000)
            if now >= nextSave { saveAll(); nextSave = now + 60000 }
            return
        }
        if dt > 0.25 { dt = 0.25 }
        st.tick(minutes: dt / 60, sleeping: act == .sleep)

        if gravity && act != .sleep && now >= nextScan {
            nextScan = now + 1000
            surfaces = Desktop.scan(minWidth: Double(Art.BodyW) * s / 2, minTopY: Double(Art.BodyH + 4) * s)
        }

        let idleMs = Int(Desktop.idleSeconds() * 1000)
        let cur = Screen.cursor()
        let cat = catRect()

        if hasEmote && now >= emoteUntil { hasEmote = false }
        if bubble != nil && now >= bubbleUntil { bubble = nil }
        if let b = bowl, bowlEaten, now >= bowlCloseAt { b.close(); bowl = nil }
        if let y = yarn { if y.putAway { removeYarn() } else { y.step(dt) } }
        if laser != nil { placeLaser(); if now >= laserUntil { endLaser(now) } }

        if now >= nextDayCheck { checkTimeOfDay(now, idleMs); nextDayCheck = now + 20000 }

        if idleMs > 3 * 60 * 1000 { activeSince = now }
        if st.breaks && act != .sleep && now - activeSince > breakAfterMs && now >= nextBreakNag {
            say("Take a break!", 8000, now); setEmote(.cheeky, 3000, now); Sounds.play("meow")
            nextBreakNag = now + 10 * 60 * 1000
        }

        if (act == .idle || act == .walk || act == .trick) && idleMs > awaySleepMs { startSleep(.away, now) }
        if act == .sleep {
            if sleepWhy == .away && idleMs < 1500 { wakeUp(now, "Welcome back!") }
            else if sleepWhy == .nap && now >= napUntil { wakeUp(now, nil) }
        }

        if (act == .idle || act == .walk) && !hasEmote && idleMs < 30000 && now - lastPet > lonelyMs && now >= nextLonely {
            act = .idle
            setEmote(.cry, 8000, now); say("Pet me...", 4000, now); Sounds.play("sad")
            nextLonely = now + 5 * 60 * 1000
            decideAt = now + 9000
        }

        let over = cat.contains(cur)
        if over && !hovering { hovering = true; hoverStart = now; hoverShown = false } else if !over { hovering = false }
        if hovering && !hoverShown && !mouseIsDown && act != .sleep && bubble == nil && now - hoverStart > 900 {
            let f = DateFormatter(); f.dateFormat = "h:mm a"
            say(st.shortName + " · " + f.string(from: Date()), 2500, now)
            hoverShown = true
        }

        let crying = hasEmote && emote == .cry
        let cdx = Double(cur.x - cat.midX), cdy = Double(cur.y - cat.midY)
        let cursorDist = (cdx * cdx + cdy * cdy).squareRoot()

        if act == .idle && !mouseIsDown && bowl != nil && !bowlEaten { goToBowl(now) }

        if act == .idle && !mouseIsDown && !crying {
            if laser != nil {
                if cursorDist < 110 * s / 2 && now >= nextLaserPounce {
                    huntCrouch = 300; startTrick(.hunt, 700, now); nextLaserPounce = now + rnd(700, 1500)
                } else if cursorDist > 50 * s / 2 {
                    act = .walk; goal = .laser; stepAt = now; hasTrip = false; hasForget = false; walkClamped = false
                }
            } else if let y = yarn, y.moving, !y.held, !st.stay, now - lastYarnEnd > 2500 {
                startYarnChase(now)
            } else if st.follow {
                var fx = Double(cur.x) - W / 2, fy = Double(cur.y) + 24 - Double(Art.OY) * s
                clampPos(&fx, &fy)
                if abs(fx - px) + abs(fy - py) > 80 { walkTo(fx, fy, .wander, now) }
            } else if st.hunger > 75 && now >= nextHungerNag {
                doDeed(.askFood, now); nextHungerNag = now + 5 * 60 * 1000
            } else if now >= decideAt {
                decideAt = now + rnd(4000, 10000)
                doDeed(Brain.pick(st, stay: st.stay, cursorNear: cursorDist < 450 && cursorDist > 60, night: isNight(),
                                  yarnOut: yarn != nil, canClimb: gravity && (!surfaces.isEmpty || standWid != 0)), now)
            }
        }

        // walking
        var tail = 0
        if act == .walk {
            if st.follow && goal == .wander {
                tx = Double(cur.x) - W / 2; ty = Double(cur.y) + 24 - Double(Art.OY) * s
                clampPos(&tx, &ty)
            }
            var reachedYarn = false
            if goal == .yarn {
                if let y = yarn {
                    let side: Double = y.centerX > catCx ? -1 : 1
                    tx = y.centerX + side * 12 * s - W / 2
                    ty = y.bottomY - H
                    clampPos(&tx, &ty)
                    reachedYarn = abs(y.centerX - catCx) < 24 * s && abs(y.bottomY - footY) < 30 * s
                } else { endDeed(now, 2000, 5000) }
            }
            if goal == .laser {
                if laser == nil { endDeed(now, 2000, 5000) }
                else { tx = Double(cur.x) - W / 2; ty = Double(cur.y) - Double(Art.OY + Art.BodyH - 4) * s; clampPos(&tx, &ty) }
            }
            if gravity && act == .walk && (goal == .yarn || goal == .laser) && !reachedYarn {
                let hdx = tx - px, vdy = ty - py
                if abs(vdy) > 20 * s && abs(hdx) < 90 * s && (goal == .yarn || now >= nextLaserPounce) {
                    if goal == .laser { nextLaserPounce = now + rnd(700, 1500) }
                    startJump(tx, ty, 0, false, now)
                } else { ty = py }
            }
            var speed: Double
            switch goal {
            case .zoom: speed = 230
            case .laser: speed = 200
            case .yarn: speed = 150
            case .bowl: speed = 75
            default: speed = st.follow ? 110 : 45
            }
            speed *= s / 2
            let dx = tx - px, dy = ty - py
            let d = (dx * dx + dy * dy).squareRoot(), step = speed * dt
            if goal == .zoom || goal == .laser { tail = dx > 0 ? 2 : 1 }

            if act != .walk {
            } else if goal == .yarn && reachedYarn {
                if now >= batCooldown && !(yarn?.held ?? true) { batYarn(now) }
            } else if goal == .yarn && now >= yarnChaseUntil {
                endDeed(now, 3000, 7000); lastYarnEnd = now
            } else if d <= step || (st.follow && goal == .wander && d < 40) {
                if !(st.follow && goal == .wander) { px = tx; py = ty }
                if goal == .yarn || goal == .laser { if goal == .laser { act = .idle } }
                else if goal == .bowl && bowl != nil { startEat(now) }
                else if goal == .climb { climbJump(now) }
                else if goal == .zoom {
                    zoomLeft -= 1
                    if zoomLeft > 0 { nextZoom(now) }
                    else { endDeed(now, 3000, 7000); setEmote(.smug, 2000, now); if roll(0.5) { say("Huff~", 1500, now) } }
                } else if walkClamped && roll(Brain.goofBonk) {
                    startTrick(.bonk, 1300, now); say("Ow!", 1300, now); goof()
                } else { endDeed(now, 2000, 7000) }
            } else {
                px += dx / d * step; py += dy / d * step
                if hasTrip && now >= tripAt {
                    hasTrip = false; startTrick(.trip, 1400, now); say("!?", 1200, now); goof()
                } else if hasForget && now >= forgetAt {
                    hasForget = false; startTrick(.forget, 2600, now); say("...why am I here?", 2400, now); goof()
                }
            }
            moveWindow()
            if now >= stepAt { stepLeft.toggle(); stepAt = now + (goal == .zoom ? 80 : 170) }
        }

        // gravity
        if act == .fall {
            vy = min(vy + 2200 * s / 2 * dt, 2600)
            let y0 = footY
            px += vx * dt; py += vy * dt
            let wa = workArea
            let minX = Double(wa.minX) - Double(Art.OX) * s, maxX = Double(wa.maxX) - Double(Art.OX + Art.BodyW) * s
            if px < minX { px = minX; vx = abs(vx) * 0.5 }
            if px > maxX { px = maxX; vx = -abs(vx) * 0.5 }
            if py < Double(wa.minY) - Double(Art.OY) * s { py = Double(wa.minY) - Double(Art.OY) * s; vy = abs(vy) * 0.3 }
            if vy > 0, let l = findLanding(y0, footY) { land(l, now) } else { moveWindow() }
        } else if gravity && act != .held && !(act == .trick && (trick == .jump || trick == .hunt)) {
            checkSupport(now)
        }

        if !blinking && now >= nextBlink { blinking = true; blinkEnd = now + 140 }
        else if blinking && now >= blinkEnd { blinking = false; nextBlink = now + rnd(2000, 6000) }

        // eyes
        var ldx = 0, ldy = 0
        let lookX = Double(cur.x) - Double(cat.midX), lookY = Double(cur.y) - (Double(cat.minY) + 15 * s)
        let vd = (lookX * lookX + lookY * lookY).squareRoot()
        if let y = yarn, y.moving || (act == .walk && goal == .yarn) {
            let yx = y.centerX - Double(cat.midX), yy = y.y + y.height / 2 - (Double(cat.minY) + 15 * s)
            let yd = max(1, (yx * yx + yy * yy).squareRoot())
            ldx = Int((yx / yd * 3).rounded()); ldy = Int((yy / yd * 3).rounded())
        } else if act == .walk && goal == .laser {
            ldx = Int((lookX / max(1, vd) * 3).rounded()); ldy = Int((lookY / max(1, vd) * 3).rounded())
        } else if act == .walk && !st.follow {
            ldx = tx > px ? 3 : -3; ldy = 0
        } else if vd < 600 && vd > 1 {
            ldx = Int((lookX / vd * 3).rounded()); ldy = Int((lookY / vd * 3).rounded())
        } else {
            if now >= glanceAt { glanceDx = rnd(-2, 3); glanceDy = rnd(-1, 2); glanceAt = now + rnd(2000, 5000) }
            ldx = glanceDx; ldy = glanceDy
        }

        hearts.removeAll { now - $0.1 > 1500 }
        let heartPts = hearts.map { CGPoint(x: $0.0, y: Art.OY + 2 - (now - $0.1) * 14 / 1500) }

        var hp = 0
        let hopAge = now - hopStart
        if hopAge >= 0 && hopAge < 450 { hp = -Int((sin(Double.pi * Double(hopAge) / 450) * 6).rounded()) }

        var ex: Expr
        if act == .held { ex = dizzy ? .cry : .surprised }
        else if act == .fall { ex = .surprised }
        else if act == .sleep { ex = now - sleepStart < 2500 ? .drowsy : .sleep }
        else if hasEmote { ex = emote }
        else if act == .idle && st.hunger > 85 { ex = .beg }
        else { ex = .wide }

        let anim = now
        var feet = act == .walk ? (stepLeft ? 1 : 2) : 0
        var bob = ex == .sleep ? (anim / 1200) % 2 : 0
        var lift = 0, ear = 0, pawDy = 0
        var paw = false, tongue = false, chomp = false, blink = blinking

        if act == .eat {
            let age = now - eatStart
            if age < 4000 {
                ex = .eat
                chomp = (age / 250) % 2 == 0
                bob = chomp ? 2 : 1
                if now >= nextBite { Sounds.play("crunch"); nextBite = now + 500 }
                bowl?.setGrid(age < 1300 ? Art.bowlFull : age < 2600 ? Art.bowlHalf : Art.bowlEmpty)
            } else { finishEat(now) }
        }

        if act == .trick {
            let age = now - trickStart
            let done = age >= trickLen
            switch trick {
            case .stretch:
                if age < 400 { lift = age * 4 / 400; ex = .content }
                else if age < 1500 { lift = 4; ex = .content }
                else if age < 1900 { lift = 4 - (age - 1500) * 4 / 400; ex = .content }
                else { ex = .drowsy }
                if done { endDeed(now, 3000, 7000) }
            case .groom:
                ex = .content; paw = true; pawDy = (age / 220) % 2; tongue = pawDy == 0
                if done { endDeed(now, 3000, 7000) }
            case .wiggle:
                ear = [1, 0, 2, 0][(age / 150) % 4]
                if done { endDeed(now, 2000, 6000) }
            case .hunt:
                if age < huntCrouch {
                    bob = 2; feet = (age / 110) % 2 + 1; ex = .wide
                } else if !done {
                    if !pounceSet {
                        pounceFromX = px; pounceFromY = py
                        var gx = Double(cur.x) - W / 2, gy = Double(cur.y) - Double(Art.OY + Art.BodyH) * s
                        let ddx = gx - px, ddy = gy - py, dd = (ddx * ddx + ddy * ddy).squareRoot()
                        if dd > 300 { gx = px + ddx / dd * 300; gy = py + ddy / dd * 300 }
                        huntMiss = laser == nil && roll(Brain.goofMiss)
                        if huntMiss { gx = px + (gx - px) * 0.55; gy = py + (gy - py) * 0.55 }
                        clampPos(&gx, &gy)
                        pounceToX = gx; pounceToY = gy; pounceSet = true
                    }
                    let t = Double(age - huntCrouch) / 400
                    px = pounceFromX + (pounceToX - pounceFromX) * t
                    py = pounceFromY + (pounceToY - pounceFromY) * t
                    hp = -Int((sin(Double.pi * t) * 8).rounded())
                    ex = .wide
                    moveWindow()
                } else {
                    if pounceSet { px = pounceToX; py = pounceToY }
                    moveWindow()
                    if laser != nil {
                        act = .idle
                        if roll(0.25) { say(roll(0.5) ? "Almost!" : "Grr!", 1000, now) }
                    } else {
                        st.count(Store.played)
                        st.fun = Store.clamp(st.fun + 8); st.energy = Store.clamp(st.energy - 2)
                        gainXp(3, now)
                        endDeed(now, 3000, 7000)
                        if huntMiss { setEmote(.dizzy, 1800, now); say("Meant that.", 2000, now); goof() }
                        else {
                            setEmote(roll(0.5) ? .happy : .smug, 1800, now)
                            if roll(0.5) { say("Got it!", 1800, now) }
                            Sounds.play("mrrp")
                        }
                    }
                }
            case .chaseTail:
                tail = (age / 260) % 2 + 1
                ldx = tail == 1 ? 3 : -3; ldy = 2
                feet = (age / 120) % 2 + 1
                if age % 520 < 130 { hp = -1 }
                if done {
                    st.count(Store.played); st.fun = Store.clamp(st.fun + 5); gainXp(3, now)
                    endDeed(now, 3000, 7000)
                    if roll(Brain.goofDizzy) { setEmote(.dizzy, 1800, now); say("Whoa...", 1600, now); goof() }
                    else { setEmote(.smug, 2000, now) }
                }
            case .stare:
                ex = .wide; blink = false; ldx = stareDx; ldy = stareDy
                if done {
                    if roll(Brain.goofPanic) { say("!!", 1200, now); hop(now); goof(); startZoomies(now) }
                    else { endDeed(now, 2000, 6000) }
                }
            case .trip:
                if age < 250 { ex = .surprised; hp = -2 } else { ex = .dizzy; bob = 3 }
                if done { act = .walk; stepAt = now }
            case .bonk:
                ex = .dizzy
                if age < 300 { hp = age < 150 ? -2 : -1 }
                if done { endDeed(now, 3000, 7000) }
            case .forget:
                ex = .wide; ldx = (age / 600) % 2 == 0 ? -3 : 3; ldy = 0
                if done { endDeed(now, 2000, 5000) }
            case .beg:
                ex = .beg
                if done { endDeed(now, 4000, 8000) }
            case .tangled:
                ex = (age / 700) % 2 == 0 ? .surprised : .cry
                feet = (age / 200) % 2 + 1
                if done { endDeed(now, 3000, 7000); say("Free!", 1500, now); setEmote(.smug, 1500, now) }
            case .land:
                bob = age < 200 ? 2 : 1
                ex = hasEmote ? emote : .content
                if done { act = .idle }
            case .jump:
                let t = min(1.0, Double(age) / 650)
                px = jumpFromX + (jumpToX - jumpFromX) * t
                py = jumpFromY + (jumpToY - jumpFromY) * t - 4 * jumpH * t * (1 - t)
                ex = t < 0.5 ? .wide : .surprised
                ldx = jumpToX > jumpFromX ? 3 : -3; ldy = t < 0.5 ? -2 : 2
                tail = jumpToX > jumpFromX ? 2 : 1
                moveWindow()
                if done {
                    act = .idle
                    decideAt = now + rnd(2500, 6000)
                    if jumpWid != 0, let r = Desktop.frame(jumpWid) {
                        standWid = jumpWid; standRect = r
                        py = Double(r.minY) - H
                        moveWindow()
                        Sounds.play("thump")
                        if roll(0.4) { setEmote(.smug, 1800, now) }
                    } else if jumpShort { say("Oops!", 1200, now); goof() }
                }
            }
        }

        look.expr = ex
        look.dx = max(-3, min(3, ldx)); look.dy = max(-3, min(3, ldy))
        look.blink = blink
        look.feet = feet; look.bob = bob; look.hop = hp; look.lift = lift; look.ear = ear
        look.paw = paw; look.pawDy = pawDy; look.tongue = tongue; look.chomp = chomp
        look.tail = tail; look.hat = curHat
        look.tangled = act == .trick && trick == .tangled
        look.stars = ex == .dizzy
        look.animMs = anim
        look.zzz = ex == .sleep
        look.bubble = bubble
        look.hearts = heartPts

        let sig = look.sig()
        if sig != lastSig { lastSig = sig; view.look = look; view.needsDisplay = true }

        let busy = act == .walk || act == .held || act == .trick || act == .eat || act == .fall
            || !hearts.isEmpty || hp != 0 || ex == .cry || ex == .dizzy || laser != nil || (yarn.map { $0.moving || $0.vy != 0 } ?? false)
        let fast = act == .fall || act == .held || (act == .trick && (trick == .jump || trick == .hunt))
            || (act == .walk && goal != .wander && goal != .bowl) || laser != nil || (yarn?.moving ?? false)
        setInterval(fast ? 40 : busy ? 60 : (act == .sleep ? 150 : 120))

        if now >= nextSave { saveAll(); nextSave = now + 60000 }
    }

    // ---------- mouse ----------
    func mouseDown(_ e: NSEvent) {
        if e.clickCount >= 2 { if act != .sleep { hop(Clock.ms) }; return }
        mouseIsDown = true; dragging = false; dizzy = false; shake = 0
        downScreen = Screen.cursor()
        dragOff = CGPoint(x: Double(downScreen.x) - px, y: Double(downScreen.y) - py)
        lastMoveMs = Clock.ms
    }

    func mouseDragged() {
        guard mouseIsDown else { return }
        let c = Screen.cursor()
        if !dragging && (abs(c.x - downScreen.x) > 4 || abs(c.y - downScreen.y) > 4) {
            dragging = true; act = .held; bubble = nil; standWid = 0; dragVx = 0; dragVy = 0
        }
        guard dragging else { return }
        let now = Clock.ms
        let nx = Double(c.x - dragOff.x), ny = Double(c.y - dragOff.y)
        let moved = abs(nx - px) + abs(ny - py)
        let dtm = max(0, Double(now - lastMoveMs)) / 1000
        if dtm > 0.005 { dragVx = 0.5 * dragVx + 0.5 * (nx - px) / dtm; dragVy = 0.5 * dragVy + 0.5 * (ny - py) / dtm }
        shake = shake * exp(-dtm * 2) + moved
        lastMoveMs = now
        if shake > 2500 { dizzy = true }
        px = nx; py = ny
        moveWindow()
    }

    func mouseUp() {
        guard mouseIsDown else { return }
        let now = Clock.ms
        if dragging {
            act = .idle
            decideAt = now + 6000
            if dizzy { setEmote(.cry, 6000, now); say("So dizzy...", 3000, now); Sounds.play("sad") }
            if gravity {
                let flung = now - lastMoveMs < 120 && abs(dragVx) + abs(dragVy) > 600
                vx = flung ? max(-1500, min(1500, dragVx * 0.6)) : 0
                vy = flung ? max(-1500, min(1500, dragVy * 0.6)) : 0
                if flung { act = .fall; fallFromY = py; standWid = 0; say("Wheee!", 1200, now) }
            }
            saveAll()
        } else { pet(now) }
        mouseIsDown = false; dragging = false; dizzy = false
    }

    // ---------- menu ----------
    func showMenu(_ e: NSEvent, in v: NSView) {
        let m = NSMenu()
        m.autoenablesItems = false
        let title = ActionItem(st.name, enabled: false)
        title.attributedTitle = NSAttributedString(string: st.name, attributes: [.font: NSFont.boldSystemFont(ofSize: 13)])
        m.addItem(title)
        m.addItem(.separator())
        m.addItem(ActionItem("Feed") { [weak self] in self?.feed() })

        let toys = NSMenuItem(title: "Toys", action: nil, keyEquivalent: "")
        let tm = NSMenu(); tm.autoenablesItems = false
        tm.addItem(ActionItem(yarn == nil ? "Yarn ball" : "Put yarn away") { [weak self] in self?.toggleYarn() })
        tm.addItem(ActionItem("Laser pointer (1 min)") { [weak self] in self?.startLaser() })
        toys.submenu = tm
        m.addItem(toys)

        let hats = NSMenuItem(title: "Hat", action: nil, keyEquivalent: "")
        let hm = NSMenu(); hm.autoenablesItems = false
        func hatItem(_ label: String, _ value: String, _ enabled: Bool) {
            hm.addItem(ActionItem(label, checked: st.hat == value, enabled: enabled) { [weak self] in
                guard let self = self else { return }
                self.st.hat = value; self.curHat = self.st.currentHat(); self.lastSig = Int.min; self.saveAll()
            })
        }
        hatItem("Auto (seasonal / best)", "auto", true)
        hatItem("None", "none", true)
        hm.addItem(.separator())
        for h in 1..<Art.hatNames.count {
            var label = Art.hatNames[h]
            if h == Art.hatSanta { label += "  (December)" }
            else if h == Art.hatWitch { label += "  (Halloween)" }
            else if !st.hatUnlocked(h) { label += "  (Lv \(Art.hatLevel[h]))" }
            hatItem(label, String(h), st.hatUnlocked(h))
        }
        hm.addItem(.separator())
        let lv = st.level
        let next = lv >= Store.levelXp.count ? "max level!" : "\(Store.levelXp[lv] - st.xp) XP to Lv \(lv + 1)"
        hm.addItem(ActionItem("Bond Lv \(lv)  ·  \(next)", enabled: false))
        hats.submenu = hm
        m.addItem(hats)
        m.addItem(ActionItem("Journal") { [weak self] in self?.openJournal() })
        m.addItem(.separator())

        m.addItem(ActionItem("Stay here", checked: st.stay) { [weak self] in
            guard let self = self else { return }
            self.st.stay.toggle()
            if self.st.stay { self.st.follow = false; if self.act == .walk && self.goal != .bowl { self.act = .idle } }
            self.saveAll()
        })
        m.addItem(ActionItem("Follow my mouse", checked: st.follow) { [weak self] in
            guard let self = self else { return }
            self.st.follow.toggle()
            if self.st.follow { self.st.stay = false }
            if self.act == .walk && self.goal != .bowl { self.act = .idle }
            self.saveAll()
        })
        m.addItem(ActionItem("Gravity (sits on windows)", checked: st.gravity) { [weak self] in
            guard let self = self else { return }
            self.st.gravity.toggle(); self.standWid = 0
            if self.act == .fall { self.act = .idle; self.vx = 0; self.vy = 0 }
            self.saveAll()
        })
        m.addItem(ActionItem("Take a nap") { [weak self] in self?.startSleep(.forced, Clock.ms) })

        let size = NSMenuItem(title: "Size", action: nil, keyEquivalent: "")
        let sm = NSMenu(); sm.autoenablesItems = false
        for (i, n) in ["Small", "Medium", "Large"].enumerated() {
            sm.addItem(ActionItem(n, checked: scale == i + 2) { [weak self] in self?.setScale(i + 2) })
        }
        size.submenu = sm
        m.addItem(size)
        m.addItem(.separator())
        m.addItem(ActionItem("Mute sounds", checked: Sounds.muted) { [weak self] in Sounds.muted.toggle(); self?.saveAll() })
        m.addItem(ActionItem("Break reminders", checked: st.breaks) { [weak self] in self?.st.breaks.toggle(); self?.saveAll() })
        if #available(macOS 13.0, *) {
            let on = SMAppService.mainApp.status == .enabled
            m.addItem(ActionItem("Open at login", checked: on) {
                if on { try? SMAppService.mainApp.unregister() } else { try? SMAppService.mainApp.register() }
            })
        }
        m.addItem(.separator())
        m.addItem(ActionItem("Quit") { [weak self] in self?.quit() })
        NSMenu.popUpContextMenu(m, with: e, for: v)
    }

    func openJournal() {
        if journal == nil || !(journal!.isVisible) { journal = JournalWindow(store: st) }
        NSApp.activate(ignoringOtherApps: true)
        journal?.makeKeyAndOrderFront(nil)
    }

    func setScale(_ sc: Int) {
        let footX = px + W / 2, fY = py + H
        scale = sc; st.scale = sc
        window.setContentSize(NSSize(width: Art.CW * scale, height: Art.CH * scale))
        view.frame = NSRect(x: 0, y: 0, width: Art.CW * scale, height: Art.CH * scale)
        view.scale = CGFloat(scale)
        px = footX - W / 2; py = fY - H
        clampPos(&px, &py)
        moveWindow()
        if let b = bowl { b.close(); bowl = nil; if act == .walk && goal == .bowl { act = .idle } }
        removeYarn()
        if let l = laser { l.close(); laser = nil }
        if act == .walk && goal == .laser { act = .idle }
        lastSig = Int.min
        saveAll()
    }

    func saveAll() { st.x = px; st.y = py; st.save() }

    func quit() {
        saveAll()
        NSApp.terminate(nil)
    }
}
