import Foundation

enum Deed: Int, CaseIterable {
    case walk, nap, groom, stretch, wiggle, hunt, askFood, zoomies, stare, chaseTail, smug, cheeky, hop, yarn, climb
    var key: String {   // same names as the Windows version, so learned likes match
        ["Walk", "Nap", "Groom", "Stretch", "Wiggle", "Hunt", "AskFood", "Zoomies", "Stare", "ChaseTail",
         "Smug", "Cheeky", "Hop", "Yarn", "Climb"][rawValue]
    }
}

// Abu's tiny "dumbo brain": scores what he could do from his needs, mood and what you've
// rewarded him for, adds randomness, and picks one. Goofs are rolled in Cat.swift.
enum Brain {
    static let goofTrip = 0.08, goofForget = 0.06, goofBonk = 0.5, goofMiss = 0.3
    static let goofPanic = 0.4, goofDizzy = 0.35, goofTangle = 0.12, goofShortJump = 0.1, goofWalkOff = 0.2

    static func pick(_ st: Store, stay: Bool, cursorNear: Bool, night: Bool, yarnOut: Bool, canClimb: Bool) -> Deed {
        let hungry = st.hunger, tired = 100 - st.energy, bored = 100 - st.fun
        var w = [Double](repeating: 0, count: Deed.allCases.count)
        w[Deed.walk.rawValue] = stay ? 0 : 30
        w[Deed.nap.rawValue] = 3 + tired * 0.4 + (night ? 15 : 0)
        w[Deed.groom.rawValue] = 8
        w[Deed.stretch.rawValue] = 6
        w[Deed.wiggle.rawValue] = 6
        w[Deed.hunt.rawValue] = (!stay && cursorNear) ? 8 + bored * 0.15 : 0
        w[Deed.askFood.rawValue] = hungry > 60 ? (hungry - 50) * 1.2 : 0
        w[Deed.zoomies.rawValue] = stay ? 0 : 1.5 + bored * 0.06 + (st.energy > 70 ? 3 : 0) - (st.energy < 25 ? 1.5 : 0)
        w[Deed.stare.rawValue] = 3
        w[Deed.chaseTail.rawValue] = 2.5 + bored * 0.05
        w[Deed.smug.rawValue] = 8
        w[Deed.cheeky.rawValue] = 6
        w[Deed.hop.rawValue] = 3
        w[Deed.yarn.rawValue] = (yarnOut && !stay) ? 20 + bored * 0.3 : 0
        w[Deed.climb.rawValue] = (canClimb && !stay) ? 10 + bored * 0.05 : 0

        var total = 0.0
        for d in Deed.allCases {
            w[d.rawValue] = max(0, w[d.rawValue]) * st.like(d.key) * Double.random(in: 0.7...1.3)
            total += w[d.rawValue]
        }
        var pick = Double.random(in: 0..<max(total, 0.0001))
        for d in Deed.allCases {
            pick -= w[d.rawValue]
            if pick <= 0 { return d }
        }
        return .smug
    }
}
