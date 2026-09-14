# DnW Mod Loader

A mod loader for **Drag'n Wash** (Gator Dragon Games; Unity 6000.3).

It loads mod DLLs from a `Mods` folder next to the game, gives them a small API and shows an in-game overlay with the mod list and logs.

## Install

1. Download the most recent [release](https://github.com/KrazenLabs/dnw-modloader/releases).
2. Extract it into the game folder next to the `DragNWash.exe`.
3. Start the game as usual.

The structure should look like this after installation:
```
Drag'n Wash/
├── DragNWash.exe
├── winhttp.dll                <- UnityDoorstop: starts the loader with the game
├── doorstop_config.ini
├── DnWModLoader/              <- the loader runtime: DnWModLoader.dll, 0Harmony.dll, Mono.Cecil.dll, docs/
└── Mods/                      <- your mods go here
    ├── ExampleMod/
    │   ├── ExampleMod.dll
    │   └── mod.json
    ├── config/                <- one JSON file per mod
    ├── ModLoader.json         <- loader settings
    └── ModLoader.log          <- log of the last launch
```

## Uninstall

Delete `winhttp.dll` and `doorstop_config.ini`.
The `Mods` folder with your mods and settings can remain if you wish to reinstall the mod loader later (or you can delete them).

### Building from source

- Make sure you have .NET SDK 8 or 9 installed.
- Run build.ps1 using Powershell.  

## Writing a mod

First install the mod loader. Then copy `templates/ModTemplate`, rename `MyMod` in the `.csproj`, `MyMod.cs` and `mod.json`, then run `dotnet build`.
The build copies `MyMod.dll`, `MyMod.pdb` and `mod.json` into `<game>/Mods/MyMod/`.

Game and engine assemblies are referenced with `<Private>false</Private>`: never ship them (or the loader / Harmony)
inside a mod folder.

### `mod.json`

```json
{
  "id": "yourname.mymod",            // required; lower-case letters, digits, . _ -
  "name": "My Mod",
  "version": "1.2.0",
  "author": "Your Name",
  "description": "What it does.",
  "assembly": "MyMod.dll",           // optional if the folder contains exactly one DLL
  "entryType": "MyMod.MyMod",        // optional if the DLL contains exactly one Mod subclass
  "enabled": true,
  "loaderVersion": ">=1.0.0",        // optional, but recommended
  "dependencies": [
    "other.mod",
    { "id": "another.mod", "version": ">=2.1", "optional": true }
  ],
  "loadAfter": ["some.mod"],         // soft ordering
  "loadBefore": ["late.mod"],
  "url": "https://example.com"       // optional
}
```

Version constraints: `>=1.2`, `>1.2`, `<=`, `<`, `=1.2.0`, `^1.2` (same major), `~1.2` (same major.minor), `*`.
For a bare DLL without `mod.json` describe it with `[ModInfo("id", "Name", "1.0.0")]` on the entry class.

Check out the included "ExampleMod" for an example of a simple mod that displays some debugging information and uses some Harmony hooks.

## Repository layout

```
DnWModLoader/
├── build.ps1                      <- builds everything and assembles the package
├── Directory.Build.props          <- game folder detection, shared build settings
├── DnWModLoader.sln
├── src/DnWModLoader/              <- the runtime (DnWModLoader.dll)
├── src/mods/ExampleMod/           <- sample mod (config, callbacks, three Harmony patches)
├── templates/ModTemplate/         <- starter project for new mods
├── tools/doorstop/                <- UnityDoorstop binary + config template for the package
├── tools/dropin/                  <- Uninstall.cmd and Mods/README.txt for the package
├── release/                       <- DnWModLoader-<version>.zip (created by build.ps1)
└── THIRD-PARTY-NOTICES.md
```
