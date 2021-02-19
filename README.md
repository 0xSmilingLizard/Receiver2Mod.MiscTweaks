# MiscTweaks
 
A mod for Receiver 2, that lets you tweak killdrone lights (color and intensity of the various states) and alert volume (both beeps and the camera's alarm).

## Install

Install [BepInEx](https://github.com/BepInEx/BepInEx) into the Receiver 2 folder (the one containing `Receiver2.exe`), then start and exit Receiver 2 to have BepInEx generate its folder structure.
Then place the MiscTweaks folder in `BepInEx/plugins`.

It is recommended to use [BepInEx's Config Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) (installed the same way as this mod) to have an in-game UI to make the tweaks.

## Dependencies

The source code depends on `0Harmony.dll`, `BepInEx.dll`, `FMODDef.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, and `Wolfire.Receiver2.dll`. It is set up to expect these DLLs to be located in a folder called `libraries` next to the repository folder. All of these DLLs can be found as part of either Receiver 2's or BepInEx's install.
