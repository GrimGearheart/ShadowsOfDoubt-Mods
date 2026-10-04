# Sanitation Department

The city is drowning in litter, and somebody has to deal with it. Sign up at **City Hall** and get paid to clean the streets and, if you've got the stomach for it, crime scenes. Play it as a day job between cases or as a whole new way to play.

## Getting started

Talk to the staff on shift at City Hall:

| Option | What it does |
|---|---|
| **Sanitation License** (¢150) | Start the job: trash gets tagged, binning it pays, and you get a bag |
| **Litter picker** (¢75) | Grab trash and junk from up to 4 m away |
| **Transfer to sanitation full-time** | No new murders. The Enforcers handle the killers while you clean (sandbox games only) |
| **Back to detective work** | Free and instant. Murders resume |
| **Route Supervisor** (¢500, after binning 100 items) | Nearby trash and ready dustbins glow, and your bag holds 30 |
| **Crime scene cleanup certification** (¢250) | The Enforcers release finished crime scenes to you for cleaning (sandbox games only) |
| **Mop and bucket** (¢100, once certified) | Scrub a wider area, faster |

## The job

- **Trash and junk are tagged.** Look at something and its name shows "Trash · ¢2" or "Junk · ¢2". Trash is what the game itself counts as trash (litter, finished food and drinks). Junk is the clutter left on the streets (cardboard boxes, old bags of rubbish and so on).
- **Your bag.** Trash and junk you pick up go straight into your bag (15 items) instead of your pockets. Hold **Left Shift** while picking something up to keep it instead.
- **Dustbins.** Look at a street dustbin and press **B** to collect a piece or two of trash. It refills after about 12 in-game hours, so plan a route.
- **Emptying the bag.** Look at any litter bin or dumpster and press **B** to cash in everything in the bag.
- **Bulky junk** (cardboard boxes, bags of rubbish). Either **toss it** into a dumpster whole for ¢5, or press **B while carrying it** to break it down into your bag.
- **Toss it in.** Anything that counts, dropped or thrown into a bin or dumpster, is disposed of and paid for.

## Crime scene cleanup

Get certified at City Hall and go full-time. Every half a day or so, a resident somewhere in the city is found dead at home. There's no murder case and no killer to catch; it's a job.

1. **A neighbour reports it** and you get a heads-up: *"Enforcers responding to a death at …"*
2. **The Enforcers investigate.** They search the place and post a guard for about 2 in-game hours. Keep out until they're done.
3. **The scene is released to you** with a radio call, and you get access to the address for a day, so you're not trespassing.
4. **Clean it up:**
   - **Bag the body.** Look at it and press **B**. You can drag the body first to get at the blood underneath.
   - **Mop up the blood pool.** Hold **B** while looking at it.
   - **Scrub the spatter.** Hold **B** while looking at blood; it comes off a spot at a time (three with the mop). You can reach a little behind edges, like down the side of a fridge.
5. **Once it's clean** (body bagged, pool gone, about 90% of the spatter scrubbed) you're paid ¢150 plus ¢1 per spot.
6. **Leave the building** and the coroner collects the body bag. If the victim lived alone, the landlord clears the flat out completely.

Scenes are only posted while you're on sanitation full-time, so they never compete with the detective game.

## Credentials card

Pause the game to see what you hold: your licenses and tools, how full your bag is, your progress toward Route Supervisor, and the status of any crime scene. If you also have **Detective License**, its license and permits appear on the same card.

## What it won't do

- **Your things are safe.** It never bags anything you own, anything belonging to someone else, or anything tied to a side job.
- **Evidence is safe.** Murder weapons, ammunition and calling cards are never junk, so you can't bin evidence for pocket change.
- **City property stays put.** Traffic cones, barrels and street stools aren't junk.
- **Quiet mode doesn't freeze anything.** If a killer is already mid-murder when you transfer, that one plays out and any open case stays solvable; only new murders stop. Other crime (muggings, burglaries, side jobs) carries on as normal.
- **It stays out of story mode.** Quiet mode is offered in sandbox games only, so it can't break the story campaign's case.

## Configuration

After the first launch, edit `BepInEx/config/sodmods.sanitationdepartment.cfg` (or use r2modman's config editor). Highlights:

| Setting | Default | Description |
|---|---|---|
| `License.Cost` / `PayPerItem` / `BulkyPay` | 150 / 2 / 5 | Prices and pay |
| `Bag.Capacity` / `SupervisorCapacity` | 15 / 30 | Bag size |
| `Bag.BreakdownPieces` | 3 | Bag items a broken-down box becomes |
| `Controls.ActionKey` | B | Empty the bag, collect from a dustbin, break down a carried item |
| `Controls.KeepKey` | LeftShift | Hold to keep an item instead of bagging it |
| `LitterPicker.Cost` / `Reach` | 75 / 4 | Litter picker price and reach (metres) |
| `RouteSupervisor.ItemsRequired` / `Cost` | 100 / 500 | Promotion requirements |
| `RouteSupervisor.GlowRange` / `GlowToggleKey` | 8 / G | Glow distance and on/off key |
| `Items.NotJunk` | TrafficCone, Barrel, PlasticStool, SmallCrate | Item types that are never junk |
| `Items.TrashSources` / `SourceRefillHours` | Dustbin / 12 | Containers you collect from, and their refill time |
| `Tag.ShowWithStreetCleanerDisk` | true | Also tag trash for players with the game's Street Cleaner sync disk |
| `CrimeScenes.CertificationCost` / `MopCost` | 250 / 100 | City Hall prices |
| `CrimeScenes.SceneFee` / `PayPerSpot` / `PoolSpots` | 150 / 1 / 20 | Pay per scene, per spot of blood, and what a blood pool counts as |
| `CrimeScenes.CleanThreshold` | 0.9 | Share of the blood that must be scrubbed |
| `CrimeScenes.IntervalMinHours` / `IntervalMaxHours` | 10 / 18 | In-game hours between new scenes |
| `CrimeScenes.OnlyWhenFullTime` | true | Only post scenes while you're on full-time |
| `CrimeScenes.InvestigationHours` | 2 | How long the Enforcers hold a scene before releasing it |
| `CrimeScenes.AccessHours` | 24 | How long a released scene lets you in |

The game's own **Street Cleaner** sync disk still works and stacks: you're paid whichever rate is higher.

## Safe to remove?

**Yes, your saves load normally without it.** Your license, bag and progress are kept in a small file next to each save, not in the save itself. What you've already done to the world stays done, as it would with anything in the base game: bagged bodies and apartments cleared out after a cleanup don't come back, and money earned stays earned.

## Compatibility

- Requires BepInExPack IL2CPP and SOD.Common (both installed automatically by r2modman/Gale).
- Built for the main (IL2CPP) branch of the game, not the mono beta branch.
- Progress (license, bag, dustbins, promotion, crime scenes) is saved with each save file.
- Crime scenes and quiet mode are sandbox-only, so they can't break the story campaign's case.

## Feedback

Found something that should (or shouldn't) count as junk? Turn on `Debug.LogItems`, look at the item, and include `BepInEx/LogOutput.log` in your comment on the Thunderstore page.
