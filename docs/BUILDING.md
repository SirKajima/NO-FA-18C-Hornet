# Building the F/A-18C Hornet from source

You only need this to change the mod. To play it, use the files in [`release/`](../release/).

## What's in this repo
| Path | What it is |
|---|---|
| `release/` | Ready-to-use build: `.nobp` (Blueprinter bundle), plugin `.dll`, `.catapults` file for Aryx's carriers |
| `unity/Assets/Blueprinter/Mods/FA18C/` | The Blueprinter mod folder: prefab, parameters, liveries, model assets, Blueprinter ops (hangars, Multi-Missile weapons) |
| `unity/Assets/Editor/` | Editor scripts used while making the mod (see below) |
| `plugin/` | BepInEx plugin source (`Plugin.cs`, `FA18C.HornetSystems`) |

## 1. The aircraft bundle (`.nobp`)
1. Set up nikkorap's **Blueprinter-Editor** Unity project (Unity 2022.3, as its guide describes) with the game's DLLs imported.
2. Copy `unity/Assets/` from this repo into the Blueprinter-Editor project's `Assets/` folder (it adds `Assets/Blueprinter/Mods/FA18C/` and the editor scripts).
3. Open the project and let Unity import.
4. Build the mod with Blueprinter's mod builder: mod key **`FA18C`**, display name **`FA-18C Hornet`**, version **`0.6.4`**. You can also run `MMCompat.BuildFA18C()` from `Assets/Editor/MMCompat.cs`, which calls `Blueprinter.ModBuilder.Build` with those values and writes to `E:/Project/NOModding/Builds`; change the path first.
5. Put the new `.nobp` in `BepInEx\plugins\` (remove the old one).

**Don't** add runtime scripts (`.cs` outside `Assets/Editor`) or editor packages to the Blueprinter-Editor project: they compile into a project `Assembly-CSharp` that clashes with the game's and corrupts the bundle's serialised data.

### Multi-Missile compatibility ops
`MMCompat.cs` creates one `Blueprinter.OpAddWeaponToHardpoint` asset per Multi-Missile weapon (`MM_<weaponKey>.asset`). Each one names the weapon's `jsonKey`, this aircraft (`FA18C`) and the hardpoint-set indices it goes on. The table is the `Rows` array in the script. Run `MMCompat.Create()` to regenerate them. The ops are string-keyed, so the bundle doesn't need Multi-Missile to build or load.

## 2. The plugin (`.dll`)
The plugin targets the game's Mono runtime and references the game's own DLLs, BepInEx and Harmony.
1. Have a C# compiler: Unity's bundled Roslyn works (`Editor/Data/DotNetSdkRoslyn/csc.dll`, run with `Editor/Data/NetCoreRuntime/dotnet.exe`).
2. In PowerShell, from `plugin/`:
   ```powershell
   .\build.ps1 -NuclearOptionDir "D:\Steam\steamapps\common\Nuclear Option" `
     -Dotnet "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\NetCoreRuntime\dotnet.exe" `
     -Csc "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\DotNetSdkRoslyn\csc.dll"
   ```
   It fills `<NUCLEAR_OPTION_DIR>` in `build.rsp.template` (references every `NuclearOption_Data/Managed/*.dll` plus `BepInEx/core/BepInEx.dll` and `0Harmony.dll`, compiled with `-nostdlib`) and writes `FA18C.HornetSystems.dll` next to the source.
3. Copy the DLL into `BepInEx\plugins\`.

## Editor scripts
The scripts in `unity/Assets/Editor/` are the tooling used to make the mod: prefab setup, model import, collider and status-display generation, Multi-Missile ops. Several contain absolute paths from the author's machine (`E:/Project/NOModding/...`). `F14Bridge.cs` is a small file-polling bridge (`E:/Project/NOModding/F14Bridge/job.txt` runs a static method) used for automated builds. Treat them as reference: read them before running, and change the paths for your setup.

## Known gap in the Unity project
Before 0.4.0 the Hornet used an earlier placeholder model. A few prefab references still point at that model's `Model/FA-18C.FBX` (one material slot and the simplified main-gear leg meshes). Its source and licence couldn't be confirmed, so it is **not** in this repo, and those references show as missing in Unity. The release `.nobp` is unaffected. To rebuild from source, swap those gear-leg meshes and the material for your own (or for parts of the CC BY model) first.
