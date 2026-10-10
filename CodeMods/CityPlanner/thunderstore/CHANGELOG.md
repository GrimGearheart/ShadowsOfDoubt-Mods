# Changelog

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
