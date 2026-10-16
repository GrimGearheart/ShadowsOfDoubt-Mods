# Changelog

## 1.5.0

- **Paint your own colours:** under the floor, walls and ceiling you've chosen in *Decor*, a colour picker for each part you can paint (marble walls get base and veins, carpets two or three colours). Unpainted parts keep the scheme's colours. Painted colours are saved in the city itself, so they stay even without City Planner.
- **Grime:** tick *Override grime* in *Decor* and pick spotless to filthy for a building or a whole district. It covers every interior surface: shared spaces, apartments and businesses.
- **Rich lobbies are clean now.** The game set lobby grime from the district's value the wrong way round, so the richer the district, the grimier its lobbies and stairwells. In planned cities it now goes the right way: very rich is spotless, very poor is filthy.
- **No more flooded basements, if you like:** set `Basements / Flooded` to `false` and the water in the lowest basements is gone in every city while you play (needs City Planner installed).
- Paint, grime and dry basements were all suggested by Tayelikel.

## 1.4.1

- **Wood tone:** choose how light or dark the wood is in a building's shared spaces. Wood floors, marble floors and wooden trim take the wood tone, not the colour scheme, so until now they came out light or dark by chance.
- **See what the colours do:** under each floor, wall and ceiling, dots in your scheme's colours show which of its five colours it takes, so you can see "white marble" before you pick it. Ones that keep their own colours say so.

## 1.4.0

- **Decor:** a new *Decor* step in the planner sets the colours, floor, walls, ceiling and era of a building's shared spaces (lobby, stairwells, building bathrooms), for one building or as a default for a whole district. Pictures of each floor and wall come from the game itself. Lobbies keep what you chose instead of swapping to lino or marble on the ground floor and in stairwells. In the plan this is a `[Decor]` section. Suggested by Tayelikel.

## 1.3.0

- **Choose your apartment:** pick a building and an apartment number under *Your apartment* in the planner (a `[Home]` section in the plan). With `Home / Mode` on `Own` it's your starting apartment; on `Buy` nobody moves in and it's listed for sale. Idea and first version by Tayelikel.
- Added a *Thanks* section to the README.

## 1.2.2

- Corrected the README's *Safe to remove?* section: cities and saves load without City Planner (tested with Margin City). You only need it to generate a planned city. No changes to the mod itself.

## 1.2.1

- **Towers get the layout your businesses need.** One Fifth and Netherland towers have two street-level layouts, and the game used to pick one at random, so a Netherland asked for Fast Food and a Launderette could roll the layout with two shops and no eatery. City Planner now picks the layout that fits, and the planner warns about a mix neither layout holds.
- **Fixed loading a second save in one session** in custom-size cities (planned cities, Margin City): it froze on *Creating Blueprints*. Going back to the main menu rebuilt the game's list of city sizes without the extra ones, so the game looked for the city at 5 × 5. The extra sizes are now added back every time.
- **Tighter planner link:** only the planner page the game opens can send plans to it now. Each game session makes a new secret code that's written into that page, and requests without it are refused. Before, a page opened from disk was also accepted, and a website could pretend to be one. A planner tab left open from an earlier session asks you to reopen it from the *City Planner* button; your plan is kept.
- **New setting `Planner / Link`:** turn it off and the game opens no connection at all. The *City Planner* button then opens the page from disk, and you save plans with *Copy cityplan.txt*.

## 1.2.0

- **Choose the businesses in a building:** click a tile to pick what goes in each of its shop units, such as a bar, pharmacy, launderette, pawn shop or sync clinic at street level, or a gambling den, loan shark or weapons dealer in the back rooms. Units you leave alone are filled by the game as before. In the plan file this is a `[Businesses]` section.
- The planner warns when a plan asks for more of a business than the game allows, or fills every unit without one the city needs, such as a sync clinic.

## 1.1.1

- **Fixed saves that wouldn't load** (stuck on "Creating interiors" or "Loading game state") in cities planned with some tiles left on *game decides*. Placed buildings were numbered ahead of the rest, so on load an address could lose its number and every citizen who liked that place broke the load. Cities you already generated load again with this update; new cities are numbered the way the game expects.
- Planned city sizes are set up as soon as the game starts, not only after opening New Game.

## 1.1.0

- City Planner is now its own mod. It used to ship inside Margin City; Margin City is now a city pack that uses it.
- City packs: any installed mod that ships city files gets them added to your city list, not just Margin City.
- **Planner in the game:** a *City Planner* button on the New Game screen opens the planner in your browser, and *Send to game* hands your plan straight to the game. No files to copy, no restart. The planner also comes with the mod as `city-planner.html`.
- **Plan as much or as little as you like:** any tile can be left on *game decides* (`?` in the plan), so you can just paint districts and let the game choose the buildings.
- New planner buttons: *Start blank*, *Clear districts*, *Clear streets*, and *Undo* for the last clear.
- Hand-edited `cityplan.txt` changes are picked up when you open New Game, and every planned city size is remembered automatically.
- **Painted streets:** choose which gaps between tiles are main roads, back streets or alleys (*Paint streets* on the planner page, or a `[Streets]` section in the plan). Buildings face the busiest street beside them, so main roads get the frontages and shops. Touching alleys join up with no dead-end walls.

## 1.0.0 – 1.0.4

Released as part of [Margin City](https://thunderstore.io/c/shadows-of-doubt/p/The_Protectorate/MarginCity/); see its changelog.
