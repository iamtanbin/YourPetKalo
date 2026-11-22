// YourPetKalo for macOS - Abu Khaleel Bamblast, a little black pixel cat on your desktop.
//  Click: pet him   Double-click: hop   Drag: carry / throw   Right-click: menu
import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    var cat: Cat?
    func applicationDidFinishLaunching(_ n: Notification) { cat = Cat() }
    func applicationWillTerminate(_ n: Notification) { cat?.saveAll() }
}

// only one Abu at a time
let me = Bundle.main.bundleIdentifier ?? "io.github.yourpetkalo"
if NSRunningApplication.runningApplications(withBundleIdentifier: me).count > 1 { exit(0) }

let app = NSApplication.shared
app.setActivationPolicy(.accessory)   // no Dock icon
let delegate = AppDelegate()
app.delegate = delegate
app.run()
