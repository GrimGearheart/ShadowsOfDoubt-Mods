# Mantle

Your detective isn't Superman, but they can climb. Jump at a ledge and haul yourself up onto it, or vault over a fence and drop down the other side. It's slow and effortful, the way a real person climbs.

## How to use it

Run or walk at something, **jump, and keep holding Jump and forward**.

- **Climb** onto anything with a surface to stand on: dumpsters, crates, filing cabinets, cars, vending machines, low walls, fire-escape landings.
- **Vault** over thin obstacles like fences and railings, then drop down the far side.
- A plain jump without holding forward stays a normal jump, so you won't climb things by accident.

## How it feels

- **Slow and deliberate.** Climbing takes about 1.2 seconds and vaulting about 1 second, a little quicker if you hit the ledge sprinting.
- **Your view dips and leans** as you haul yourself up, then settles as you come over the top.
- **Landings are real.** After a vault you drop under gravity, with the game's own landing sound and thud.

## What it won't do

- **No climbing through walls.** It uses the game's own collision; if something gets in the way mid-climb, it cancels and gives you control back.
- **No suicide vaults.** It won't vault you off anything with more than a 4 m drop on the far side, which is about where fall damage starts.
- **No full walls.** Anything taller than about 2 m above your feet is out of reach (jumping first reaches higher).
- **It stays out of scripted moves.** It's off in air vents, while hiding, in rat mode, and during scripted moves.
- **Trespassing is still trespassing.** Climbing into somewhere you shouldn't be counts exactly as it would on foot.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.mantle.cfg` (or use r2modman's config editor):

| Setting | Default | Description |
|---|---|---|
| `Mantle.MinLedgeHeight` | 0.5 | Lowest ledge (metres above your feet) that triggers a climb |
| `Mantle.MaxLedgeHeight` | 2.0 | Highest ledge you can reach |
| `Mantle.Reach` | 0.6 | How far in front of you a ledge can be |
| `Mantle.MaxSurfaceSlope` | 50 | Steepest top surface (degrees) you can climb onto |
| `Mantle.Duration` | 1.2 | Seconds to climb onto a ledge |
| `Mantle.SprintMultiplier` | 0.85 | Duration multiplier when sprinting (lower = quicker) |
| `Vault.Enabled` | true | Vault over thin obstacles |
| `Vault.Duration` | 1.0 | Seconds to vault |
| `Vault.MaxDrop` | 4.0 | Furthest drop allowed on the far side of a vault |
| `Camera.DipAngle` | 9 | How far your view tips down while climbing (0 = off) |
| `Camera.RollAngle` | 3 | How far your view leans while climbing (0 = off) |
| `Controls.RequireForward` | true | Only climb while holding forward |
| `Debug.LogMantles` | false | Log ledge detection (useful for bug reports) |

## Safe to remove?

**Yes.** It doesn't store anything in your saves, so you can add or remove it at any time.

## Compatibility

- Requires BepInExPack IL2CPP (installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.

## Feedback

Found something you should be able to climb but can't (or the other way round)? Turn on `Debug.LogMantles`, try it again, and include `BepInEx/LogOutput.log` in your comment on the Thunderstore page.
