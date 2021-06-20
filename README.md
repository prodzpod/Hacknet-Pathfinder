# Pathfinder

An extensive modding API and loader for Hacknet that enables practically limitless programable extensions to the game.

### Installation

No pre-compiled packages are available (yet), so if you need to install either compile it yourself or ask a contributor

### Creating Mods

1. Start a new .NET library project with .NET Framework 4.0
2. Link it against the HacknetPathfinder.exe, PathfinderAPI.dll, FNA.dll, BepInEx.Core.dll, and BepInEx.Hacknet.dll. You may need more than this, but that's the reccomended set to begin on a simple mod.
    * You may need to go into the project options and set the project to target the x86 platform
3. Create a class that inherits from BepInEx.Hacknet.HacknetPlugin, and add the BepInEx.BepInPlugin attribute to it with a name, guid, and version.
4. And now you have a basic functioning mod, the rest is up to you. Install the mod by placing it in Hacknet/BepInEx/plugins

### Contributing to Pathfinder

1. Clone the project with `git clone https://github.com/Arkhist/Hacknet-Pathfinder` and switch to this branch with `git switch bepinex-harmony-rewrite`
2. Compile PathfinderPatcher and run it in the Hacknet directory
3. Copy HacknetPathfinder.dll and FNA.dll to the libs/ directory
4. Everything else should now compile fine
5. Everything in BepInEx.Hacknet/bin/Debug goes into Hacknet/BepInEx/core, also grab Mono.Cecil.dll from the libs folder, for some reason it won't copy to the output directory.
6. Copy/symlink the .dll (or their containing folder) for PathfinderAPI and optionally ExampleMod to Hacknet/BepInEx/plugins

## Links

[Discord](https://discord.gg/65SaxGg)

[Github](https://github.com/Arkhist/Hacknet-Pathfinder)

[Bug Reports](https://github.com/Arkhist/Hacknet-Pathfinder/issues)
