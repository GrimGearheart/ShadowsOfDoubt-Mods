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

    /// <summary>This choice, with anything it leaves open taken from the fallback.</summary>
    public DecorChoice Over(DecorChoice fallback) => fallback == null ? this : new DecorChoice
    {
        Scheme = Scheme ?? fallback.Scheme, Floor = Floor ?? fallback.Floor, Walls = Walls ?? fallback.Walls,
        Ceiling = Ceiling ?? fallback.Ceiling, Style = Style ?? fallback.Style, Wood = Wood ?? fallback.Wood,
    };
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
        if (applied.Scheme != null || applied.Wood != null || floor != null || walls != null || ceiling != null)
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
        }
        planned[b.Pointer] = applied;
        Plugin.Logger.LogInfo($"Decor: {where} shared spaces" +
                              (applied.Style != null ? $", style {applied.Style}" : "") + (applied.Scheme != null ? $", colours {applied.Scheme}" : "") +
                              (applied.Floor != null ? $", floor {applied.Floor}" : "") + (applied.Walls != null ? $", walls {applied.Walls}" : "") +
                              (applied.Ceiling != null ? $", ceiling {applied.Ceiling}" : "") + (applied.Wood != null ? $", wood {applied.Wood}" : "") +
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
}
