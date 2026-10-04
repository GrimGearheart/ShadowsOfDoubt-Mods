# Scroll Throttle

A Star Citizen-style throttle for Shadows of Doubt. Scroll the mouse wheel to set how fast you move, and it stays set until you change it. Creep through an apartment at 30%, stroll a crime scene at 60%, or hurry across the city at 150%.

## Controls

| Input | Action |
|---|---|
| Scroll up / down | Speed up / slow down in 10% steps (20%–150%) |

The throttle has a **notch at 100%**: scroll through normal speed and it stops there, so a quick flick always brings you back to normal. Pause for a moment and keep scrolling to go past it.

A short "Speed 60%" notification appears when you stop scrolling.

## How it works

- **It stacks with the game.** The throttle multiplies whatever speed the game sets, so crouching, air vents, injuries and speed sync disks all keep working, just scaled.
- **It stays out of the way.** The wheel only controls the throttle while you're walking around in first person. It keeps its normal job on the map, in documents, on computer screens, in menus and in conversations.
- **Scripted movement is untouched.** Climbing and other scripted moves aren't scaled.
- **It remembers your setting.** Your last throttle is kept between sessions (can be turned off).

## Configuration

After the first launch, edit `BepInEx/config/sodmods.scrollthrottle.cfg` (or use r2modman's config editor):

| Setting | Default | Description |
|---|---|---|
| `Throttle.Minimum` / `Maximum` | 0.2 / 1.5 | Throttle range |
| `Throttle.Step` | 0.1 | Change per wheel notch |
| `Throttle.ApplyToRun` | true | Also scale running speed (false = walking only) |
| `Throttle.RememberThrottle` | true | Keep the throttle between sessions |
| `Controls.ModifierKey` | None | Key to hold while scrolling (e.g. `LeftAlt`), if you want the wheel free otherwise |
| `Controls.ResetKey` | None | Optional key that resets to 100% (middle mouse is the game's flashlight, so pick something else) |
| `Controls.DetentAt100` | true | Stop at 100% when scrolling through it |
| `Controls.DetentSeconds` | 0.35 | How long it holds at 100% before scrolling continues |
| `Controls.InvertScroll` | false | Scroll down to speed up |
| `Display.ShowMessages` | true | Show the speed notification |

## Safe to remove?

**Yes.** It doesn't store anything in your saves, so you can add or remove it at any time.

## Compatibility

- Requires BepInExPack IL2CPP (installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.
- Other mods that change the player's max speed through `Player.SetMaxSpeed` will stack with this one rather than fight it.

## Feedback

Found a bug or have a suggestion? Please leave a comment on the Thunderstore page.
