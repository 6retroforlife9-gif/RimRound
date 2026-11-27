# Building RimRound Rework 1.0

The four C# projects target .NET Framework 4.8. Build them from a clone of the release tag or an extracted copy of `RimRound-1.0-Source.zip`. Run the build in a working copy since it writes DLLs into that directory.

## Requirements

- Windows PowerShell or PowerShell 7, and a .NET SDK with `dotnet` on PATH. The release build check uses SDK 10.0.401.
- A local RimWorld 1.6 installation with `RimWorldWin64_Data/Managed`. The reference game for this release is 1.6.4871.
- The 1.6 Workshop assemblies for Harmony (2009463077) and Humanoid Alien Races (839005762). The game also requires Vanilla Expanded Framework to play. NuGet supplies the .NET Framework 4.8 reference assemblies.

## Build

From the directory containing `build-1.6.ps1`, point these two arguments at your own game and Workshop installations.

```powershell
.\build-1.6.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\RimWorld' -WorkshopRoot 'D:\SteamLibrary\steamapps\workshop\content\294100'
```

The script builds the core, SwellGlow, the feeding companion and Junk Food in dependency order.

| Project | Runtime output |
| --- | --- |
| `Source/RimRound/RimRound.csproj` | `1.6/Assemblies/RimRound.dll` |
| `1.6/ExternalMods/SwellGlow/Source/SwellGlow/SwellGlow.csproj` | `1.6/ExternalMods/SwellGlow/Assemblies/SwellGlow.dll` |
| `Source/RimRoundFeedOther/RimRoundFeedOther.csproj` | `1.6/Assemblies/RimRoundFeedOther.dll` |
| `Addons/JunkFood/Source/JunkFood.csproj` | `1.6/ExternalMods/JunkFood/1.6/Assemblies/Oinky.JunkFood.dll` |

The source ZIP contains the projects, build script and credits. The ready-to-play ZIP and Git repository also contain the game definitions, translations, artwork and sounds. For a source-only checkout, copy your rebuilt DLLs into the matching locations in the ready-to-play mod to try them in game. The core project assigns an assembly version at build time, so a later build can have a different version and checksum from the released DLL.
