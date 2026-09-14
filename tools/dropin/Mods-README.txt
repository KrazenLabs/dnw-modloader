DnW Mod Loader - Mods folder
============================

Put each mod in its own sub-folder here, e.g. Mods/CoolMod/CoolMod.dll + Mods/CoolMod/mod.json.
Bare DLLs dropped directly into this folder are loaded too.

ModLoader.json   - loader settings (overlay hotkey, log level, disabled mods)
ModLoader.log    - log of the last game start (ModLoader.prev.log = the one before)
config/          - one JSON file per mod with its settings

In game, press F10 (configurable) for the overlay: every mod's settings, the mod list and the log.
To disable a mod without deleting it, rename its folder to start with an underscore (e.g. _CoolMod)
or add its id to "disabledMods" in ModLoader.json.
