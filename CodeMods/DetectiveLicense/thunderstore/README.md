# Detective License

You're a detective. Now you can have the paperwork to prove it. Visit **City Hall** and talk to the staff on shift to buy one of two documents.

## Crime Scene Permit — 100 crows

- Legal access to every **open murder case** for **4 in-game hours**: the place the murder happened *and* the victim's home.
- Uses the game's own guest passes, so you're simply allowed to be there. People inside won't treat you as an intruder, and cameras and laser grids at those addresses won't react to you being there.
- If there are no open cases, the clerk says so and you aren't charged.

## Private Investigator License — 1,500 crows

- **Permanent** access to every open murder case (murder site and victim's home), for as long as each case stays open.
- **Residents are more likely to let you in.** "Can I come in and take a look around?" gets a +30% bonus, more with people who trust authority and less with people who distrust it.
- Saved with each save file. Once you own it, the City Hall options disappear.

## Credentials card

Pause the game to see what you hold: the license, and every crime scene permit still running with its time left. If you also have **Sanitation Department**, its licenses and jobs appear on the same card.

## What it doesn't do

- **It won't let you barge into random homes.** Anywhere that isn't an open case works exactly as before; you still need to be invited in.
- **Illegal actions are still illegal.** Picking locks, breaking things or stealing makes you an intruder again, license or not.
- **A solved case's locations are no longer covered.**

Prices use the game's own dialog costs, so the options grey out when you can't afford them, and you're only charged when the clerk agrees.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.detectivelicense.cfg` (or use r2modman's config editor):

| Setting | Default | Description |
|---|---|---|
| `Permit.Cost` | 100 | Permit price in crows |
| `Permit.Hours` | 4 | How long a permit lasts, in in-game hours (1 hour ≈ 6.5 real minutes) |
| `License.Cost` | 1500 | License price in crows |
| `License.LookAroundBonus` | 0.3 | Bonus chance to be let in (0.3 = +30%) |
| `License.TrustAuthorityBonus` | 0.2 | Extra bonus with residents who trust authority |
| `License.DistrustAuthorityBonus` | -0.25 | Change with residents who distrust authority |
| `CityHall.DeskJobs` | (empty) | Limit sales to certain City Hall jobs (e.g. `Receptionist`); empty = any City Hall staff on shift |

## Safe to remove?

**Yes.** Your license and permits are kept in a small file next to each save, not in the save itself. Without the mod your saves load normally; the license and permits just stop applying (money already spent isn't refunded).

## Compatibility

- Requires BepInExPack IL2CPP and SOD.Common (both installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.
- The license adds to the same "look around" modifier as the relevant sync disks, so they stack.

## Feedback

Found a bug or have a suggestion? Please leave a comment on the Thunderstore page.

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
