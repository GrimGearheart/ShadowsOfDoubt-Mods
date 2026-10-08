# Shadows of Doubt mods by The Protectorate

Source code for The Protectorate's [Shadows of Doubt](https://store.steampowered.com/app/986130/Shadows_of_Doubt/) mods. Install them from Thunderstore with r2modman or Gale; this repository is here so anyone can read exactly what the mods do.

| Mod | What it does | Source |
|---|---|---|
| [Scroll Throttle](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/ScrollThrottle/) | Set your walking speed with the mouse wheel | [CodeMods/ScrollThrottle](CodeMods/ScrollThrottle) |
| [Detective Squint](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/DetectiveSquint/) | Hold a key to zoom in | [CodeMods/DetectiveSquint](CodeMods/DetectiveSquint) |
| [Mantle](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/Mantle/) | Climb up ledges and vault over fences | [CodeMods/Mantle](CodeMods/Mantle) |
| [Prone](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/Prone/) | Lie down and crawl, to look under furniture | [CodeMods/Prone](CodeMods/Prone) |
| [Sit Anywhere](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/SitAnywhere/) | Sit on crates, benches, low walls and rooftop ledges, and pass time there | [CodeMods/SitAnywhere](CodeMods/SitAnywhere) |
| [Detective License](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/DetectiveLicense/) | Crime scene permits and a private investigator's license from City Hall | [CodeMods/DetectiveLicense](CodeMods/DetectiveLicense) |
| [Sanitation Department](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/SanitationDepartment/) | Get paid to clean the streets and crime scenes | [CodeMods/SanitationDepartment](CodeMods/SanitationDepartment) |
| [City Planner](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/CityPlanner/) | Design your own city in a planner that sends it to the game: paint districts, or choose every building and street | [CodeMods/CityPlanner](CodeMods/CityPlanner) |
| [Margin City](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/MarginCity/) | A hand-planned city, built with City Planner | [Cities/MarginCity](Cities/MarginCity) |
| [Under New Management](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/UnderNewManagement/) | Buy and run diners, cafés and bars | [CodeMods/Proprietor](CodeMods/Proprietor) |

`CodeMods/Shared` holds code used by more than one mod (the credentials card on the pause screen). `Cities/` holds city packs: content only, no code, loaded by City Planner. City Planner's planner page and city scripts are in `CodeMods/CityPlanner/tools`.

## Are they safe?

- **No internet access, nothing outside the game.** The mods patch the game's own code while it runs (BepInEx + Harmony) and write small text files next to your save games. They don't touch the save files themselves. The one exception is City Planner's *Send to game*: the planner page in your browser hands plans to the game over a connection that only works on your own PC, only while the game runs.
- **Readable.** The released DLLs aren't obfuscated. Open one in [ILSpy](https://github.com/icsharpcode/ILSpy) and compare it with the source here, or scan the download on [VirusTotal](https://www.virustotal.com/).
- **Removable.** Each mod's page has a "Safe to remove?" section. All of them can be removed without breaking saves, except that saves made in Margin City need Margin City to load.

## Building

Each mod is a .NET 6 class library for BepInEx 6 (IL2CPP). The projects reference the game's BepInEx interop assemblies, so you need the game with BepInExPack_IL2CPP installed and run at least once. Set the game folder with `-p:GameDir=...` if it isn't at the default path in the `.csproj`.

```
dotnet build -c Release CodeMods/Prone
```

A Release build copies the DLL into the game's `BepInEx/plugins` folder. `tools/package_thunderstore.py CodeMods/<Mod>` (or `Cities/<City>` for a city pack) builds the Thunderstore zip from the `thunderstore` folder.

## How they're made

The mods are made with the help of AI (Claude, by Anthropic). They're designed, play-tested and released by The Protectorate, and every release goes through the [release checklist](RELEASING.md).

## Feedback

Found a bug or have an idea? Open an issue here or comment on the mod's Thunderstore page.

## License

[MIT](LICENSE). Shadows of Doubt is © ColePowered Games. Nothing from the game itself is included here.
