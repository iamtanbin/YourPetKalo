# Contributing to YourPetKalo

Thanks for wanting to make Abu better! 🐾

## Ground rules
1. **Keep him light.** CatPet should stay around 10 MB RAM and well under 1% CPU. Don't redraw every frame,
   don't poll the system faster than needed, and pause work while he's hidden or asleep.
2. **No installs.** It must build with `build.ps1` using Windows' built-in `csc.exe` (C# 5, .NET Framework 4).
   That means no NuGet packages and no newer C# syntax (`$"..."`, `?.`, `=>` members, `out var`, ...).
3. **Be kind to the user's PC.** No network calls, no keylogging or reading other apps' content,
   no admin rights. Any new setting that changes Windows (like "Start with Windows") must be opt-in from the menu.

## How to contribute
1. Fork the repo and create a branch: `git checkout -b my-new-trick`
2. Make your change and build it: `powershell -ExecutionPolicy Bypass -File .\build.ps1`
3. Run `CatPet.exe` and check it works. If you changed art, render the preview (see README) and look at it.
4. Open a pull request describing what you changed, with a screenshot or GIF if it's visual.

## Ideas that fit well
- New tricks or goofs (add a `Deed` in `Brain.cs` and a `Trick` in `Cat.cs`)
- New hats or expressions (pixel grids in `Art.cs`)
- Bug reports with steps to reproduce — open an issue

## Assets
Code you contribute is licensed under MIT. Only contribute art or sounds you made yourself or that are
clearly licensed for redistribution, and say where they came from in your pull request.
