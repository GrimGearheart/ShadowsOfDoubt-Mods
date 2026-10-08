# City Planner

Design your own Shadows of Doubt city instead of rolling random seeds. You decide the size, how many people live there, where each district goes and how rich it is. Then plan as much or as little of the rest as you like: choose every building and street yourself, or leave any of them to the game. Interiors and residents are always generated as usual.

**Plan it loosely or exactly:**
- **Quick:** paint the districts and leave everything else on *game decides*. The game picks buildings that suit each district and lays out the streets itself.
- **In between:** place the landmarks you care about (City Hall, a hotel, a park belt) and let the game fill in the rest.
- **Exact:** choose the building on every tile, which way each one faces, and which gaps between tiles are main roads, back streets or alleys.

City Planner is what built [Margin City](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/MarginCity/). It also installs city packs: any mod that ships city files gets them added to your city list.

## What you control

- **Size:** how many blocks wide and tall, from 3 × 3 to 14 × 14, so bigger than the game's own largest if you want. The game adds a ring of waterfront around the blocks you plan.
- **Population:** fewer or more residents than the game would normally put in.
- **Districts:** where each one is, its name, its type (Residential, CBD, Chinatown, Red Light, Industrial…), its wealth and its density.
- **Buildings (optional):** which building goes on each tile, and which side its front faces. Tiles left on *game decides* get a building the game picks for that district.
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
| `Planner / Port` | The port the planner page uses to send plans to the game (on this PC only). Change it only if another program needs port 47811. |

Every setting only affects cities generated with a plan. Without a `cityplan.txt`, city generation is unchanged.

## Is the planner connection safe?

*Send to game* goes from your browser to the game over a connection that only works on your own PC. Nothing goes over the internet, and only the City Planner page can use it: plans from anywhere else are refused. It's open only while the game is running.

## Safe to remove?

**Not for planned cities with a custom size.** A city made at a size the game doesn't have (such as 9 × 10) needs City Planner to load, and so does any city pack built that way, including Margin City. Saves in normal-sized cities aren't affected and load without it.

## Compatibility

- Built for the main (IL2CPP) branch of the game.
- Works alongside other mods, including The Protectorate's.

## Feedback

Built a city you're proud of? Share the plan or the share code on the Thunderstore page.

---

*Made with the help of AI (Claude, by Anthropic). Designed, play-tested and released by The Protectorate.*
