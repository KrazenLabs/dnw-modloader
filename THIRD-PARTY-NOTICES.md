# Third-party notices

The DnW Mod Loader bundles or depends on the following open-source components.

## Harmony (Lib.Harmony 2.4.2) — `0Harmony.dll`

Copyright (c) 2017 Andreas Pardeike. MIT License.
https://github.com/pardeike/Harmony

Harmony's `0Harmony.dll` (net472 build) merges MonoMod.Core / MonoMod.Utils / MonoMod.RuntimeDetour
(MIT License, https://github.com/MonoMod/MonoMod) and other components; see the Harmony repository for details.

## UnityDoorstop 4.4.1 — `winhttp.dll` in the drop-in package

Copyright (c) NeighTools. GNU Lesser General Public License v3.0 (as stated in the project repository).
https://github.com/NeighTools/UnityDoorstop

Shipped as an unmodified binary. It is loaded by Windows in place of the system winhttp.dll, forwards the real
functions, and runs `DnWModLoader.dll` after the Mono runtime starts. See `DOORSTOP-NOTICE.txt` next to it.

## BepInEx 5 plugin API — `BepInEx.dll`

`BepInEx.dll` in the loader folder is not BepInEx. It is the DnW Mod Loader's own implementation of the public API of
BepInEx 5.4 (`BepInEx`, `BepInEx.Configuration`, `BepInEx.Logging`, `BepInEx.Bootstrap`), named so that BepInEx 5
plugins load without BepInEx, and it reads and writes settings in BepInEx's `.cfg` format. No BepInEx code is included.
BepInEx is by the BepInEx contributors, GNU Lesser General Public License v2.1, https://github.com/BepInEx/BepInEx.

HarmonyX (MIT License, https://github.com/BepInEx/HarmonyX) is not included either: calls to HarmonyX-only members in
plugins are redirected to equivalents built on Harmony.

## Mono.Cecil 0.11.6 — used by the Doorstop preloader and to load BepInEx plugins

Copyright (c) 2008 - 2015 Jb Evain, Copyright (c) 2008 - 2011 Novell, Inc. MIT License.
https://github.com/jbevain/cecil

---

MIT License text:

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and
to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
