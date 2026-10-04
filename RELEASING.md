# Release checklist

Run through this before uploading any new version to Thunderstore. Claude does the steps marked 🤖; you do the ones marked 🎮.

## 1. Prepare the version 🤖

- [ ] **Pick the version number.** Fixes and README-only changes: last number (1.0.3 → 1.0.4). New features: middle number (1.0.4 → 1.1.0).
- [ ] **Set it in all three places:** the `.csproj` `<Version>`, the `[BepInPlugin(...)]` line in the code, and `thunderstore/manifest.json`. (The packaging script refuses if they don't match.)
- [ ] **Changelog:** add an entry at the top of `thunderstore/CHANGELOG.md` saying what changed, in players' words.
- [ ] **README:** describe any new features and settings. Keep the "Safe to remove?" section accurate.
- [ ] **Test settings are off** in the mod's defaults (debug logging, probes, test stock or fast deliveries). Untested features stay out.

## 2. Test in your own game 🎮

- [ ] **The new feature or fix works** the way it's described in the changelog.
- [ ] **Load an existing save** made with the previous version. Nothing is lost or broken.
- [ ] **Save, quit, reload.** Whatever the mod remembers is still there.
- [ ] **No errors in the log.** 🤖 Claude checks `BepInEx/LogOutput.log` for red lines from the mod.

## 3. Test it the way players install it 🎮🤖

Players use r2modman or Gale, which keep mods in a separate profile folder. This is the test that would have caught the Margin City city-list bug.

- [ ] 🤖 **Switch off your hand-installed BepInEx:** rename `winhttp.dll` in the game folder to `winhttp.dll.off`.
- [ ] **In r2modman** (or Gale), use a test profile and *Import local mod* the new zip from the `thunderstore/dist` folder (plus its dependencies from Thunderstore, such as SOD.Common).
- [ ] **Start modded** from the manager. The mod loads, and its feature works.
- [ ] **Bundled content shows up**, for example Margin City in the city list.
- [ ] 🤖 **Switch your own setup back on:** rename `winhttp.dll.off` back to `winhttp.dll`.

## 4. Check removal is safe 🎮

Only needed when the mod saves something new or changes what it saves.

- [ ] **Disable the mod** and load a save made with it. The save loads and the game plays normally.
- [ ] **Re-enable it.** Its data comes back.
- [ ] 🤖 If the result differs from the README's "Safe to remove?" section, update the section.

## 5. Package and publish

- [ ] 🤖 **Package:** `python tools/package_thunderstore.py CodeMods/<Mod>` makes the zip in `thunderstore/dist` and copies it to `Releases/` (one current zip per mod, not published to GitHub).
- [ ] 🎮 **Upload** the zip from `Releases/` to Thunderstore as a new version of the mod.
- [ ] 🤖 **GitHub:** commit the changes and push, so the source matches the release.
- [ ] 🎮 **Tell people:** post the update on Discord ("In the base game…" / **With this mod:** / link). For bug fixes, reply to whoever reported it and remind them to **update in their mod manager**. Installed mods don't update by themselves.

## Before a mod's very first release

- [ ] 🤖 **Icon** (256×256, `tools/make_icon.py`), **manifest** description under 250 characters, **dependencies** listed, **website link** to the mod's folder on GitHub.
- [ ] 🤖 **Add the mod's folder to the whitelist** in `.gitignore`, or it won't be published to GitHub.
- [ ] 🤖 **Root README:** add the mod to the table.
