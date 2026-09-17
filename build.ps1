<#
.SYNOPSIS
  Builds the DnW Mod Loader solution and assembles the package:
  release\DnWModLoader-<version>.zip - extract it into the game folder.

.PARAMETER Configuration
  Release (default) or Debug.

.PARAMETER GameDir
  Folder that contains DragNWash.exe. When omitted, the script looks in DNW_GAME_DIR, in the parent of this
  folder, and in every Steam library on this machine.
#>
param(
    [string]$Configuration = "Release",
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$release = Join-Path $root "release"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup |
    ForEach-Object { $_.DnwLoaderVersion } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = "0.0.0" }

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path ([System.IO.Path]::Combine($dir, "DragNWash.exe")))
}

function Get-SteamLibraries {
    $libraries = @()
    try { $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction Stop).SteamPath } catch { $steam = $null }
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
        }
    }
    $libraries += "C:\Program Files (x86)\Steam"
    return $libraries | Select-Object -Unique
}

function Find-GameDir {
    if (Test-GameDir $env:DNW_GAME_DIR) { return $env:DNW_GAME_DIR }
    $parent = Split-Path $root -Parent
    if (Test-GameDir $parent) { return $parent }
    foreach ($library in Get-SteamLibraries) {
        $candidate = Join-Path $library "steamapps\common\Drag'n Wash"
        if (Test-GameDir $candidate) { return $candidate }
    }
    return $null
}

if ($GameDir -eq "") { $GameDir = Find-GameDir }
if (-not (Test-GameDir $GameDir)) {
    throw "Drag'n Wash was not found (no DragNWash.exe). Pass -GameDir <game folder> or set the DNW_GAME_DIR environment variable."
}
$GameDir = (Resolve-Path $GameDir).Path.TrimEnd('\')

$doorstop = Join-Path $root "tools\doorstop\winhttp.dll"
if (-not (Test-Path $doorstop)) { throw "tools\doorstop\winhttp.dll (UnityDoorstop) is missing; the package cannot start without it." }

Write-Host "Building $version ($Configuration) against $GameDir ..." -ForegroundColor Cyan
& dotnet build (Join-Path $root "DnWModLoader.sln") -c $Configuration -nologo "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$loaderBin = Join-Path $root "src\DnWModLoader\bin\$Configuration"
$loaderFiles = @(
    "DnWModLoader.dll", "DnWModLoader.pdb", "DnWModLoader.xml",
    "BepInEx.dll", "BepInEx.pdb",
    "MelonLoader.dll", "MelonLoader.pdb", "Tomlet.dll",
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll", "MonoMod.Core.dll", "MonoMod.Utils.dll",
    "MonoMod.Backports.dll", "MonoMod.ILHelpers.dll", "MonoMod.Iced.dll",
    "System.ValueTuple.dll",
    "Mono.Cecil.dll", "Mono.Cecil.Mdb.dll", "Mono.Cecil.Pdb.dll", "Mono.Cecil.Rocks.dll"
)
$docs = @("README.md", "THIRD-PARTY-NOTICES.md", "LICENSE")
$mods = @("ExampleMod")

Write-Host "Assembling the package..." -ForegroundColor Cyan
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
$stage = Join-Path $release "stage"
New-Item -ItemType Directory -Force -Path (Join-Path $stage "DnWModLoader\docs") | Out-Null
foreach ($f in $loaderFiles) { Copy-Item (Join-Path $loaderBin $f) (Join-Path $stage "DnWModLoader") }

foreach ($f in $docs) { Copy-Item (Join-Path $root $f) (Join-Path $stage "DnWModLoader\docs") }
Copy-Item (Join-Path $root "tools\doorstop\DOORSTOP-NOTICE.txt") (Join-Path $stage "DnWModLoader\docs")
Copy-Item (Join-Path $root "tools\doorstop\doorstop_config.ini") $stage
Copy-Item $doorstop $stage
foreach ($mod in $mods) {
    $modBin = Join-Path $root "src\mods\$mod\bin\$Configuration"
    $modDir = Join-Path $stage "Mods\$mod"
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    foreach ($f in @("$mod.dll", "$mod.pdb", "mod.json")) { Copy-Item (Join-Path $modBin $f) $modDir }
}
Copy-Item (Join-Path $root "tools\dropin\Mods-README.txt") (Join-Path $stage "Mods\README.txt")

$zip = Join-Path $release "DnWModLoader-$version.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Done: $zip" -ForegroundColor Green
Write-Host "Extract it into the game folder (DragNWash.exe)."
