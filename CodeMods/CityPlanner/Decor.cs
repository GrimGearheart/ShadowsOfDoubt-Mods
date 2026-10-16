using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace CityPlanner;

/// <summary>A decor choice from the [Decor] section. Null fields are left to the game (or the district's default).</summary>
internal class DecorChoice
{
    public string Scheme, Floor, Walls, Ceiling, Style, Wood;
    /// <summary>Grime override, 0 (spotless) to 10 (filthy), for every interior surface in the building; null = the game decides.</summary>
    public int? Grime;
    /// <summary>Painted colours, by "floor-c1" style keys (surface floor/walls/ceiling, slot main/c1/c2/c3) = #RRGGBB.</summary>
    public Dictionary<string, string> Paint = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This choice, with anything it leaves open taken from the fallback.</summary>
    public DecorChoice Over(DecorChoice fallback)
    {
        if (fallback == null) return this;
        var merged = new DecorChoice
        {
            Scheme = Scheme ?? fallback.Scheme, Floor = Floor ?? fallback.Floor, Walls = Walls ?? fallback.Walls,
            Ceiling = Ceiling ?? fallback.Ceiling, Style = Style ?? fallback.Style, Wood = Wood ?? fallback.Wood,
            Grime = Grime ?? fallback.Grime,
        };
        foreach (var kv in fallback.Paint) merged.Paint[kv.Key] = kv.Value;
        foreach (var kv in Paint) merged.Paint[kv.Key] = kv.Value;
        return merged;
    }
}

/// <summary>
/// Shared spaces. A building's lobby, stairwells, building bathrooms, atrium and ballroom all copy one building-wide
/// decor (design style, colour scheme, floor, walls, ceiling), picked when the building is made. For planned
/// buildings the plan's choices replace the game's (the tile's own choices first, then its district's defaults).
/// Lobbies then swap their floor (and corporate/hotel lobbies their walls) on the ground floor and in stairwells, so
/// for planned buildings whatever the plan set is put back over that swap; anything left open behaves as normal.
/// Echelon floors keep their own fixed look.
/// </summary>
internal static class Decor
{
    /// <summary>Which parts were planned, per building (filled on the main thread, read while interiors are made).</summary>
    private static readonly Dictionary<IntPtr, DecorChoice> planned = new();
    private static Dictionary<string, ColourSchemePreset> schemes;

    public static void Reset() => planned.Clear();

    /// <summary>The plan's decor for a tile: its own choices over its district's defaults.</summary>
    internal static DecorChoice For(Plan plan, int x, int y)
    {
        // Planned tiles only: the waterfront ring and its boundary pieces keep the game's look.
        if (!plan.Tiles.ContainsKey((x, y))) return null;
        plan.TileDecor.TryGetValue((x, y), out var own);
        var tile = plan.Nearest(x, y);
        plan.DistrictDecor.TryGetValue(tile.District.Key, out var district);
        return own?.Over(district) ?? district;
    }

    private static ColourSchemePreset Scheme(string name)
    {
        if (schemes == null)
        {
            schemes = new Dictionary<string, ColourSchemePreset>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ColourSchemePreset>()))
            {
                var s = o.TryCast<ColourSchemePreset>();
                if (s != null && !schemes.ContainsKey(s.name)) schemes[s.name] = s;
            }
        }
        return schemes.TryGetValue(name, out var found) ? found : null;
    }

    private static MaterialGroupPreset Material(string name, MaterialGroupPreset.MaterialType type)
    {
        foreach (var m in Toolbox.Instance.allMaterialGroups)
            if (m != null && m.materialType == type && string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    private static DesignStylePreset Style(string name)
    {
        foreach (var d in Toolbox.Instance.allDesignStyles)
            if (d != null && string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    [HarmonyPatch(typeof(NewBuilding), nameof(NewBuilding.UpdateColourSchemeAndMaterials))]
    private static class BuildingDecorPatch
    {
        private static void Postfix(NewBuilding __instance)
        {
            if (!Plugin.Active || __instance?.cityTile == null) return;
            var c = __instance.cityTile.cityCoord;
            var want = For(Plugin.Plan, c.x, c.y);
            if (want == null) return;
            try
            {
                Apply(__instance, want, $"{__instance.preset?.name} at R{Plugin.Plan.Height - 1 - c.y} C{c.x}");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Decor: " + e);
            }
        }
    }

    private static void Apply(NewBuilding b, DecorChoice want, string where)
    {
        var applied = new DecorChoice();
        var problems = new List<string>();
        if (want.Style != null)
        {
            var s = Style(want.Style);
            if (s != null) { b.designStyle = s; applied.Style = s.name; }
            else problems.Add($"no design style '{want.Style}'");
        }
        if (want.Scheme != null)
        {
            var s = Scheme(want.Scheme);
            if (s != null) { b.colourScheme = s; applied.Scheme = s.name; }
            else problems.Add($"no colour scheme '{want.Scheme}'");
        }
        // Wood tone (a colour like #5A3A22): wood floors and wooden trim take it instead of the colour scheme.
        if (want.Wood != null)
        {
            if (ColorUtility.TryParseHtmlString(want.Wood.StartsWith("#") ? want.Wood : "#" + want.Wood, out var wood))
            {
                wood.a = 1f;
                b.wood = wood;
                applied.Wood = want.Wood;
            }
            else problems.Add($"'{want.Wood}' isn't a colour like #5A3A22");
        }
        MaterialGroupPreset Pick(string name, MaterialGroupPreset.MaterialType type)
        {
            if (name == null) return null;
            var m = Material(name, type);
            if (m == null) problems.Add($"no {type} material '{name}'");
            return m;
        }
        var floor = Pick(want.Floor, MaterialGroupPreset.MaterialType.floor);
        var walls = Pick(want.Walls, MaterialGroupPreset.MaterialType.walls);
        var ceiling = Pick(want.Ceiling, MaterialGroupPreset.MaterialType.ceiling);
        // A new style also steers the materials left open: the game picked those for its own style, so pick again.
        var lobbyClass = b.preset?.lobbyPreset?.roomConfig != null && b.preset.lobbyPreset.roomConfig.Count > 0 ? b.preset.lobbyPreset.roomConfig[0].roomClass : null;
        if (applied.Style != null && lobbyClass != null)
        {
            var wealth = Toolbox.Instance.GetNormalizedLandValue(b);
            var seedText = "decor" + b.buildingID;
            floor ??= Toolbox.Instance.SelectMaterial(lobbyClass, wealth, b.designStyle, MaterialGroupPreset.MaterialType.floor, ref seedText);
            walls ??= Toolbox.Instance.SelectMaterial(lobbyClass, wealth, b.designStyle, MaterialGroupPreset.MaterialType.walls, ref seedText);
            ceiling ??= Toolbox.Instance.SelectMaterial(lobbyClass, wealth, b.designStyle, MaterialGroupPreset.MaterialType.ceiling, ref seedText);
        }
        // Only what the plan named counts as planned (and is put back over the lobby swaps); style re-picks don't.
        if (floor != null) { b.floorMaterial = floor; if (want.Floor != null) applied.Floor = floor.name; }
        if (walls != null) { b.defaultWallMaterial = walls; if (want.Walls != null) applied.Walls = walls.name; }
        if (ceiling != null) { b.ceilingMaterial = ceiling; if (want.Ceiling != null) applied.Ceiling = ceiling.name; }

        // The colour keys tint each material with the scheme, so they're remade whenever either changed.
        if (applied.Scheme != null || applied.Wood != null || want.Paint.Count > 0 || want.Grime != null || floor != null || walls != null || ceiling != null)
        {
            var seed = b.buildingID * 7919;
            Toolbox.MaterialKey Key(MaterialGroupPreset m, int salt)
            {
                var v = m.variations.Count > 0 ? m.variations[Math.Abs(seed + salt) % m.variations.Count] : null;
                return MaterialsController.Instance.GenerateMaterialKey(v, b.colourScheme, null, true, b);
            }
            if (b.floorMaterial != null) b.floorMatKey = Key(b.floorMaterial, 1);
            if (b.defaultWallMaterial != null) b.defaultWallKey = Key(b.defaultWallMaterial, 2);
            if (b.ceilingMaterial != null) b.ceilingMatKey = Key(b.ceilingMaterial, 3);

            // Painted colours go straight into the finished colours. The city file saves those as they are, so the
            // paint stays even without City Planner; the scheme still colours the furniture.
            var painted = 0;
            foreach (var (key, hex) in want.Paint)
            {
                var dash = key.IndexOf('-');
                var surface = dash > 0 ? key[..dash].ToLowerInvariant() : "";
                var target = surface switch { "floor" => b.floorMatKey, "walls" => b.defaultWallKey, "ceiling" => b.ceilingMatKey, _ => null };
                if (target == null || !ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var colour))
                {
                    problems.Add($"can't paint '{key} {hex}'");
                    continue;
                }
                colour.a = 1f;
                switch (key[(dash + 1)..].ToLowerInvariant())
                {
                    case "main": target.mainColour = colour; break;
                    case "c1": target.colour1 = colour; break;
                    case "c2": target.colour2 = colour; break;
                    case "c3": target.colour3 = colour; break;
                    default: problems.Add($"'{key}' isn't main, c1, c2 or c3"); continue;
                }
                painted++;
            }
            if (painted > 0) applied.Paint = new Dictionary<string, string>(want.Paint, StringComparer.OrdinalIgnoreCase);
        }
        planned[b.Pointer] = applied;
        Plugin.Logger.LogInfo($"Decor: {where} shared spaces" +
                              (applied.Style != null ? $", style {applied.Style}" : "") + (applied.Scheme != null ? $", colours {applied.Scheme}" : "") +
                              (applied.Floor != null ? $", floor {applied.Floor}" : "") + (applied.Walls != null ? $", walls {applied.Walls}" : "") +
                              (applied.Ceiling != null ? $", ceiling {applied.Ceiling}" : "") + (applied.Wood != null ? $", wood {applied.Wood}" : "") +
                              (want.Grime != null ? $", grime {want.Grime} on every surface" : "") +
                              (applied.Paint.Count > 0 ? $", painted {string.Join(" ", applied.Paint.Select(kv => kv.Key + " " + kv.Value))}" : "") +
                              (problems.Count > 0 ? " (left to the game: " + string.Join("; ", problems) + ")" : ""));
    }

    /// <summary>Puts the plan's materials back over the lobbies' ground-floor and stairwell swaps.</summary>
    [HarmonyPatch(typeof(NewRoom), nameof(NewRoom.UpdateColourSchemeAndMaterials))]
    private static class RoomDecorPatch
    {
        private static void Postfix(NewRoom __instance)
        {
            if (planned.Count == 0 || __instance?.building == null || __instance.preset == null) return;
            if (__instance.preset.decorSetting != RoomConfiguration.DecorSetting.borrowFromBuilding) return;
            if (!planned.TryGetValue(__instance.building.Pointer, out var want)) return;
            var b = __instance.building;
            // Echelon floors keep their own look.
            if (Game.Instance.allowEchelons && b.preset.buildingFeaturesEchelonFloors && __instance.floor != null && __instance.floor.floor >= b.preset.echelonFloorStart) return;
            var getMaterial = InteriorCreator.Instance == null || !InteriorCreator.Instance.threadedInteriorCreationActive;
            if (want.Floor != null && __instance.floorMaterial != b.floorMaterial)
            {
                __instance.floorMatKey = b.floorMatKey;
                __instance.SetFloorMaterial(b.floorMaterial, null, false, getMaterial);
            }
            if (want.Walls != null && __instance.defaultWallMaterial != b.defaultWallMaterial)
            {
                __instance.defaultWallKey = b.defaultWallKey;
                __instance.SetWallMaterialDefault(b.defaultWallMaterial, null, false, getMaterial);
            }
            if (want.Ceiling != null && __instance.ceilingMaterial != b.ceilingMaterial)
            {
                __instance.ceilingMatKey = b.ceilingMatKey;
                __instance.SetCeilingMaterial(b.ceilingMaterial, null, false, getMaterial);
            }
        }
    }

    /// <summary>
    /// Grime. Every wall, floor and ceiling gets a grime level (0 to 1) when its colours are worked out: apartments from
    /// their residents and value, businesses from their value, and shared spaces from the district's land value, which
    /// the game does the wrong way round (the richer the district, the grimier the lobby). While a planned city is
    /// made, shared spaces go by value the right way (very low = filthy, very high = spotless), and a building whose
    /// plan overrides grime gets exactly that on every interior surface: shared spaces, apartments and businesses.
    /// Grime is saved with the colours, so it stays without City Planner. Upper Echelon floors stay spotless.
    /// </summary>
    [HarmonyPatch(typeof(MaterialsController), nameof(MaterialsController.GenerateMaterialKey))]
    private static class GrimePatch
    {
        private static void Postfix(NewRoom room, bool useGrubiness, NewBuilding building, Toolbox.MaterialKey __result)
        {
            if (!useGrubiness || __result == null || !Plugin.Active || SessionData.Instance == null || SessionData.Instance.startedGame) return;
            var b = building ?? room?.building;
            if (b?.cityTile == null) return;
            if (room != null && room.gameLocation != null && room.gameLocation.isOutside) return;
            if (room != null && b.preset != null && Game.Instance.allowEchelons && b.preset.buildingFeaturesEchelonFloors && room.floor != null &&
                room.floor.floor >= b.preset.echelonFloorStart) return;
            var c = b.cityTile.cityCoord;
            var grime = For(Plugin.Plan, c.x, c.y)?.Grime;
            if (grime != null)
                __result.grubiness = Mathf.Clamp(grime.Value, 0, 10) / 10f;
            else if (room == null && (b.preset == null || !b.preset.overrideGrubiness))
                // The building-wide colours (copied by its shared spaces): by value, the right way round.
                __result.grubiness = Mathf.Clamp01(1f - (int)b.cityTile.landValue * 0.25f);
        }
    }
}
