# Detective Squint

Narrow your eyes. Hold **Z** and your view zooms in smoothly, so you can read a sign across the street, get a good look at a face, or case a window before you go anywhere near it. Let go and it eases back out.

## Features

- **Smooth zoom** to 2.5× while you hold the key.
- **Steady aim.** Mouse look slows down to match the zoom, so a magnified view doesn't feel twitchy.
- **Works at computer screens too**, which makes small text easier to read.
- **Stays out of the way** in menus, the map, documents and conversations.
- **Your settings are safe.** Your field-of-view setting is restored exactly when you let go, and your saved sensitivity is never changed.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.detectivesquint.cfg` (or use r2modman's config editor):

| Setting | Default | Description |
|---|---|---|
| `Controls.SquintKey` | Z | Key to squint |
| `Controls.Toggle` | false | Press once to squint, again to stop, instead of holding |
| `Squint.Zoom` | 2.5 | Magnification while squinting |
| `Squint.Speed` | 6 | How quickly the view zooms in and out (higher = snappier) |
| `Squint.ScaleMouseSensitivity` | true | Slow mouse look down to match the zoom |

## Safe to remove?

**Yes.** It doesn't store anything in your saves, so you can add or remove it at any time.

## Compatibility

- Requires BepInExPack IL2CPP (installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.

## Feedback

Found a bug or have a suggestion? Please leave a comment on the Thunderstore page.

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
