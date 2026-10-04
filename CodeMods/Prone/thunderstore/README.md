# Prone

Get down on the floor. The thing you're looking for might have rolled under the bed, been kicked under a desk or slipped behind a dresser. Now you can actually get low enough to see it.

## Controls

| Input | Action |
|---|---|
| Hold **Crouch** (about half a second) | Lie down |
| Press **Crouch** while lying down | Get back up to a crouch |
| Press **Crouch** again | Stand up, as normal |

## How it works

- **Eye level is about 25 cm off the floor**, low enough to see under desks, beds and dressers.
- **You can crawl slowly** (about a third of walking speed). No running or jumping while lying down.
- **You stay crouched under the hood**, so you keep all of crouching's stealth benefits.
- **No getting up into a desk.** If there isn't room to rise (you've crawled under something), you're told so and stay down.
- **Getting down and up is smooth**, a short blend instead of a snap.
- **The game stays in charge where it matters.** It switches off automatically in air vents, hiding spots and scripted moves.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.prone.cfg` (or use r2modman's config editor):

| Setting | Default | Description |
|---|---|---|
| `Controls.HoldCrouchSeconds` | 0.5 | How long to hold Crouch to lie down (0 = turn the hold gesture off) |
| `Controls.ProneKey` | None | Optional dedicated key to lie down and get back up |
| `Prone.BodyHeight` | 0.6 | Body height while lying down (metres) |
| `Prone.EyeHeight` | 0.25 | Eye height above the floor (metres) |
| `Prone.CrawlSpeed` | 0.35 | Crawling speed as a fraction of walking speed |
| `Prone.TransitionSeconds` | 0.7 | How long getting down or up takes |

Tip: if you're already crouched, the first press of Crouch stands you up before the hold takes you down. If that bothers you, set a `ProneKey` instead.

## Safe to remove?

**Yes.** It doesn't store anything in your saves, so you can add or remove it at any time.

## Compatibility

- Requires BepInExPack IL2CPP (installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.
- Works alongside Scroll Throttle: your throttle still scales your crawling speed.

## Feedback

Found a bug or have a suggestion? Please leave a comment on the Thunderstore page.
