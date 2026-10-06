# Sit Anywhere

In the base game you can only sit on proper chairs. A detective on a long stakeout deserves better. With this mod you can sit on anything that's the right height: a crate in an alley, a park bench, a low wall, the edge of a desk, or a rooftop ledge with your legs dangling over the city.

## Controls

| Input | Action |
|---|---|
| **Interact** while looking at a seat-height surface or a ledge | Sit |
| Any **movement** key or **Jump** | Get up |
| **Interact** while seated | Pass Time (set your watch alarm) |

The **Sit** and **Pass Time** prompts appear in the on-screen controls, like any other action. They only take the Interact key when nothing else is using it, so doors, phones and people you look at keep their normal actions.

**While smoking**, Sit and Pass Time move to the **right mouse button**, so Interact still takes a drag.

## Where you can sit

- **Seat-height surfaces**, roughly 30 to 85 cm off the floor: crates, benches, low walls, steps, planters, bed and desk edges. You turn around and sit on the near edge, the way you'd sit on a bench.
- **Ledges.** Look at the edge of a roof, balcony or landing, or at a low wall with a drop behind it, and you sit on the edge facing out with your legs over the drop.
- You need room for your head and body. You can't sit through a wall or window, or squeeze under something low.

## Passing time

While seated, **Pass Time** brings up your watch, just like sitting on a chair:

| Input | Action |
|---|---|
| **Mouse wheel** | Move the alarm forward or back |
| **Alternative** | Switch between hours and minutes |
| **Interact** | Start passing time |
| **Secondary** | Cancel |

Time speeds up until the alarm goes off. Moving or jumping stops it and gets you up.

## Good to know

- **You can look around** while seated, up to 120° left or right.
- **Getting hurt gets you up.** No fall damage from standing up off a high ledge.
- **Crouching is off** while seated.
- **The game stays in charge.** Scripted moves and hiding spots end sitting automatically.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.sitanywhere.cfg` (or use your mod manager's config editor):

| Setting | Default | Description |
|---|---|---|
| `Seats.MinSeatHeight` | 0.3 | Lowest surface you can sit on (metres above your feet) |
| `Seats.MaxSeatHeight` | 0.85 | Highest surface you can sit on |
| `Seats.Reach` | 2.0 | How far away a spot can be (metres) |
| `Ledges.Enabled` | true | Sit on ledges with your legs over the drop |
| `Ledges.MinDrop` | 0.8 | How far the ground has to fall away past an edge for it to count as a ledge (metres) |
| `Seated.EyeHeightAboveSeat` | 0.75 | Eye height above the seat (metres) |
| `Seated.SitDownSeconds` | 0.7 | How long sitting down takes |
| `Seated.StandUpSeconds` | 0.5 | How long getting up takes |
| `Seated.LookAroundDegrees` | 120 | How far you can turn your head while seated (180 = no limit) |
| `Seated.TurnAround` | true | Turn around to sit on things in front of you (off = sit facing the way you were looking) |
| `Seated.PassTime` | true | Allow passing time while seated |
| `Debug.LogSeats` | false | Log why a spot was rejected while you hold Interact (for bug reports) |

## Safe to remove?

**Yes.** It doesn't store anything in your saves, so you can add or remove it at any time. If you save while seated, you'll be standing at that spot when you load without the mod.

## Compatibility

- Requires BepInExPack IL2CPP (installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.
- Works alongside Prone and Mantle. With Scroll Throttle installed, using the mouse wheel to set the alarm also changes your walking speed.

## Feedback

Found a bug or have a suggestion? Please leave a comment on the Thunderstore page.

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
