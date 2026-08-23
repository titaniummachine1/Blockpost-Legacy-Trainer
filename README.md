# Blockpost Legacy Trainer — BepInEx port

This repository is the managed BepInEx IL2CPP port for the 32-bit Blockpost Legacy build.

The original native C++ trainer remains in the separate [legacy repository](https://github.com/titaniummachine1/Blockpost-Legacy-Trainer) and is kept as a reference for the original feature intent, generated type names, and game-specific behavior.

## Status

The current milestone is a BepInEx IL2CPP plugin with a visible IMGUI menu, offline ESP boxes, a configurable aimbot activation key, angle-based FOV slider in degrees, optional auto-shoot via Win32 mouse input, selectable plain/silent aim style, and a managed no-recoil hook. ESP renders independently of the menu, and the aimbot selects the closest visible target within the angular FOV and writes the current camera and controller aim angles. It resolves and patches `Controll.Update` and `Controll.OnGUI`, with opt-in throttled runtime diagnostics. No-bullet-spread remains disabled until a safe current-build method is mapped.

## Build

```powershell
dotnet restore
dotnet build -c Release
```

Builds automatically copy `BlockpostTrainer.dll` to `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\plugins` after a successful build. Override the destination with `-p:GamePluginDirectory=...`, or disable deployment with `-p:AutoDeploy=false`. The project references generated interop assemblies from `C:\Steam\steamapps\common\BLOCKPOST\BepInEx\interop`; override that location with `-p:InteropDirectory=...` when building on another installation.

Do not commit the BepInEx runtime, generated `interop` assemblies, game files, or logs to this repository.
