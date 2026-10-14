# City Planner

Design your own Shadows of Doubt city instead of rolling random seeds. You decide the size, how many people live there, where each district goes and how rich it is. Then plan as much or as little of the rest as you like: choose every building and street yourself, or leave any of them to the game. Interiors and residents are always generated as usual.

**Plan it loosely or exactly:**
- **Quick:** paint the districts and leave everything else on *game decides*. The game picks buildings that suit each district and lays out the streets itself.
- **In between:** place the landmarks you care about (City Hall, a hotel, a park belt) and let the game fill in the rest.
- **Exact:** choose the building on every tile, which way each one faces, the businesses inside it, and which gaps between tiles are main roads, back streets or alleys.

City Planner is what built [Margin City](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/MarginCity/). It also installs city packs: any mod that ships city files gets them added to your city list.

## What you control

- **Size:** how many blocks wide and tall, from 3 × 3 to 14 × 14, so bigger than the game's own largest if you want. The game adds a ring of waterfront around the blocks you plan.
- **Population:** fewer or more residents than the game would normally put in.
- **Districts:** where each one is, its name, its type (Residential, CBD, Chinatown, Red Light, Industrial…), its wealth and its density.
- **Buildings (optional):** which building goes on each tile, and which side its front faces. Tiles left on *game decides* get a building the game picks for that district.
- **Businesses (optional):** choose what goes in a building's shop units: the bar on the corner, the pharmacy next door, the gambling den in the basement. Units you leave alone are filled by the game.
- **Your apartment (optional):** pick the apartment you live in, or the one waiting for you to buy.
- **Streets (optional):** paint any gap between two tiles as a main road, back street or alley. Gaps you leave alone are decided by the game.

The plan also changes how wealth works in your city:
- **Homes take their wealth from their district**, so a rich district is rich from the ground floor up (in the base game it mostly depends on how high up a flat is).
- **Clean streets in rich districts.** Shacks, barrel fires and street junk stay out of them.
- **Themed streets follow the districts.** Chinatown's gates appear where streets enter or leave Chinatown.
- **Optional open alleys:** in districts you list, alleys become back streets without dead-end walls.

## How to plan a city

1. **Start the game, open *New Game*, and click *City Planner*.** The planner opens in your web browser, on the plan the game is using if you have one, otherwise on a blank 5 × 5 blocks. *Load Margin City example* shows a finished plan to learn from.
2. **Plan the city:** set the size, *Paint districts*, then pick buildings tile by tile or leave them on *game decides*. *Paint streets* lets you paint the strips between tiles. The *Checks* panel flags anything the game won't allow.
3. **Click *Send to game*.** The game must be running; any screen is fine. If it's busy generating a city, your plan is used for the next one.
4. **On the New Game screen, pick "Planned city (… blocks)" as the city size** and generate. Keep the game window in front while it generates: it pauses in the background.

Your plan is also saved as `BepInEx/config/cityplan.txt`, so it's still there next time you start the game. Prefer a text editor? Write that file yourself (`cityplan.example.txt` in the mod's folder is Margin City's plan, with comments); City Planner picks up changes when you open *New Game*. `city-planner.html` in the mod's folder also works if you open it straight from disk.

Not happy with the result? Generate again with a different seed: your districts, buildings and painted streets stay where you put them, while everything else changes.

## Painted streets

Every pair of neighbouring buildings has a road between them; you choose what kind:

- **Main road:** a named street. Main roads run straight and change name where they cross another main road.
- **Back street:** a quiet lane.
- **Alley:** a narrow-feeling lane with alley dressing. Alleys that touch become one alley, so no dead-end walls split them.

Buildings turn to face the busiest street beside them, and shops prefer busy streets, so a painted main road gets the frontages and the businesses. A front you set on a tile always wins.

In `cityplan.txt` the streets are a `[Streets]` section, counted like the map (rows and columns from the top left):

```
[Streets]
R3 C1-C7 S = main          # the south side of row 3, columns 1 to 7
R5-R7 C1-C3 edge = main    # all the way round a 3 x 3 block
R5-R7 C1-C3 inside = back  # the lanes inside it
```

The gaps can't be added, removed or narrowed: every road is as wide as the game makes it. Without a `[Streets]` section, the game lays out all streets itself.

## Businesses

Buildings with shop units show them when you click the tile: pick a business for any unit, or leave it to the game.

| Unit | Businesses |
|---|---|
| **Eatery** (street level) | Bar, Fast food, Chinese restaurant |
| **Shop** (street level) | Pharmacy, Launderette, Hardware store, Pawn shop, Supermarket, Sync clinic |
| **Den** (back rooms and basements) | Gambling den, Loan shark, Black market trader, Black market sync clinic, Weapons dealer, Squat, Empty |

| Building | Eateries | Shops | Dens |
|---|---|---|---|
| Commercial Tower | 1 | 2 | 5 |
| Apartment Tower (One Fifth) | 1–2 | 0–1 | 8 |
| Apartment Tower (Netherland) | 0–1 | 1–2 | 8–12 |
| Town-house with shopfront | 1 | 1 | 4 |
| Town-house | – | – | 5 |
| Mixed Industrial | 1 | – | 3 |
| Fathoms | – | – | 2 |
| Hotel | – | – | 4 |

The two apartment towers each have two street-level layouts: One Fifth has 2 eateries, or an eatery and a shop; Netherland has an eatery and a shop, or 2 shops. City Planner gives each tower the layout that fits the businesses you picked, and the planner warns you about a mix neither layout holds. The game allows at most 3 hardware stores, pawn shops, gambling dens, loan sharks, black market traders and black market sync clinics, and 4 weapons dealers. Extra ones are left to the game. If a business can't be placed, the unit is left to the game and `LogOutput.log` says why.

In `cityplan.txt` the businesses are a `[Businesses]` section, one line per tile:

```
[Businesses]
R5 C1 = Bar, Pharmacy, Gambling Den
R3 C3 = Sync Clinic, Supermarket
```

## Your apartment

Under *Your apartment* in the planner, pick a tile with homes and the number on the apartment's door: 1701 is floor 17, flat 01. Clicking a tile also offers *My apartment is in this building*. What happens to it is the `Home / Mode` setting:

- **Own** (the default): it's your starting apartment. Every new game in the city starts you there, as long as the game's sandbox *starting apartment* option is on.
- **Buy:** nobody moves in, and it's listed for sale like any empty apartment, waiting for you to buy it.

Flat numbers are handed out while the city is made, so if the building has no flat with your number, `LogOutput.log` lists the ones it has and the game picks your apartment as usual. In `cityplan.txt` it's a `[Home]` section:

```
[Home]
Apartment = R3 C1 501
```

## Settings

In `BepInEx/config/sodmods.cityplanner.cfg`:

| Setting | What it does |
|---|---|
| `Wealth / DistrictHomeWealth` | Homes take their wealth from their district (on by default). |
| `Wealth / AverageStreetWealth` | Street decoration follows the average wealth along the street (on). |
| `Wealth / StreetThemes` | Themed street decoration follows the districts a street runs through (on). |
| `Wealth / ChinatownGatesAtEdges` | Chinatown's gates only where streets enter or leave it (on). |
| `Wealth / GritDecorations`, `GritFreeFrom` | Which street decorations count as grit, and from which wealth level they're kept out. |
| `Streets / OpenAlleysIn` | Districts (by planned name) where alleys become back streets. Not used when the plan paints its own streets. |
| `Sizes / Keep` | Every city size you've planned, so cities made at those sizes still load. Filled in automatically; never remove or reorder entries. |
| `Home / Mode` | What happens to the apartment in the plan: `Own` (your starting apartment) or `Buy` (kept empty and for sale). |
| `Planner / Link` | Lets the planner page send plans straight to the game (on). Off: no connection is opened; copy plans into `cityplan.txt` instead. |
| `Planner / Port` | The port the planner page uses to send plans to the game (on this PC only). Change it only if another program needs port 47811. |

Every setting only affects cities generated with a plan. Without a `cityplan.txt`, city generation is unchanged.

## Is the planner connection safe?

*Send to game* goes from your browser to the game over a connection that only works on your own PC (127.0.0.1). Nothing goes over the internet. Only the planner page the game opens can use it: each time the game starts it makes a new secret code and puts it in that page, and anything without the code, such as a website open in the same browser, is refused. It's open only while the game is running.

Rather not have it at all? Set `Planner / Link` to `false`. The game then opens no connection, the *City Planner* button opens the page from disk, and you save plans with *Copy cityplan.txt* into `BepInEx/config/cityplan.txt`.

## Safe to remove?

**Yes, for playing.** Cities you generated and their saves load without City Planner, including Margin City. You only need it to generate a planned city. One exception: cities made with City Planner 1.1.0 that mixed placed buildings with *game decides* tiles need City Planner 1.1.1 or later to load.

## Compatibility

- Built for the main (IL2CPP) branch of the game.
- Works alongside other mods, including The Protectorate's.

## Thanks

- **Tayelikel** for the *Your apartment* idea and its first version, and for the bug reports and save files behind several fixes.
- **ටrionis** for the review that led to the tighter planner link, and for pushing to check whether cities need City Planner to load. They don't.

## Feedback

Built a city you're proud of? Share the plan or the share code on the Thunderstore page.

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
