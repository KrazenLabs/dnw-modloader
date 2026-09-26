![DnW Mod Loader](https://github.com/KrazenLabs/dnw-modloader/blob/main/assets/logo.png)

# DnW Mod Loader

A mod loader for **[Drag'n Wash](https://gatordragongames.itch.io/dragnwash)** (Gator Dragon Games; Unity 6000.3).

It loads mod DLLs from a `Mods` folder next to the game, gives them a small API and shows an in-game overlay with the mod list and logs.

## Install

Download the [latest version of the DnW Mod Manager](https://github.com/KrazenLabs/dnw-modmanager/releases/latest), which will automatically install the mod loader for you!

## BepInEx plugins (experimental)

The loader can also run BepInEx 5 plugins through a compatibility layer.

- Only install them through the Mod Manager!

## MelonLoader mods (experimental)

The loader can also run MelonLoader **mods** through a compatibility layer.

- Only install them through the Mod Manager!

## Uninstall

Delete `winhttp.dll` and `doorstop_config.ini`.
The `Mods` folder with your mods and settings can remain if you wish to reinstall the mod loader later (or you can delete them).

### Building from source

- Make sure you have .NET SDK 8 or 9 installed.
- Run build.ps1 using Powershell.  

## Writing a mod

First install the mod loader. Then copy `templates/ModTemplate`, rename `MyMod` in the `.csproj`, `MyMod.cs` and `mod.json`, then run `dotnet build`.
The build copies `MyMod.dll`, `MyMod.pdb` and `mod.json` into `<game>/Mods/MyMod/`.

Game and engine assemblies are referenced with `<Private>false</Private>`: never ship them (or the loader, Harmony or MonoMod)
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
  "resources": [                     // optional, needs loader 1.9.0
    { "folder": "Songs", "description": "Your own music as .ogg files.", "extensions": [".ogg"], "section": "Music" }
  ],
  "url": "https://example.com"       // optional
}
```

Version constraints: `>=1.2`, `>1.2`, `<=`, `<`, `=1.2.0`, `^1.2` (same major), `~1.2` (same major.minor), `*`.
For a bare DLL without `mod.json` describe it with `[ModInfo("id", "Name", "1.0.0")]` on the entry class.

### Resource folders

You can create resource folders inside your mod structure where players can import external resources (for example textures or audio files).
The loader creates them and offers buttons in the Mod config UI and the Mod Manager that let's players add their resources.
Only imports files with the listed `extensions` (none = any file; programs and scripts are not allowed).

- `folder` (required): path inside your mod folder. Paths and links outside the mod folder are not allowed.
- `name`, `description`: label and help text. `section`: settings section to show the row in.
- `recursive` (default `true`): whether to search through subfolders as well.

`GetResourceFolder("FolderName").Files` lists the files and `OnResourcesChanged(folder, changes)` is called when players add, remove or change files while the game is running.

Check out the included "ExampleMod" for an example of a simple mod that displays some debugging information and uses some Harmony hooks.

## Known issues

Direct3D 12 can cause random crashes during UI start. Please use the -force-d3d11 launch options to launch the game in Direct3D 11. This is a Unity bug, so not much I can do about it :(
