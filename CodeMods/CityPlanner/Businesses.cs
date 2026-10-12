using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CityPlanner;

/// <summary>
/// Planned businesses. Every unit in a building has a layout (Eatery, Retail, Den...), and the game later picks what
/// each unit becomes from the address presets that fit that layout, scoring them by street footfall, district and how
/// many the city already has. Just before that companies stage, each business the plan asks for is put into a free unit
/// of its kind in that tile's building: the other presets that fit the unit are switched off for one AssignPurpose
/// call, so the game's own code makes the choice, sets up the company and counts it. The game then skips those units
/// and fills the rest as usual. A request that can't be met (no free unit of its kind, or the city limit for that
/// business is reached) is logged and the unit is left to the game.
/// </summary>
internal static class Businesses
{
    /// <summary>Units already given their planned business (by address pointer); the game's pass skips them.</summary>
    internal static readonly HashSet<IntPtr> Planned = new();
    private static bool assigning;

    public static void Run()
    {
        Planned.Clear();
        var plan = Plugin.Plan;
        if (!plan.Tiles.Values.Any(t => t.Businesses.Count > 0)) return;
        try
        {
            Apply(plan);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Placing businesses failed: " + e);
        }
    }

    private static void Apply(Plan plan)
    {
        var presets = new Dictionary<string, AddressPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Toolbox.Instance.allAddressPresets)
            if (p != null) presets[p.name] = p;

        var units = new Dictionary<IntPtr, List<NewAddress>>();
        foreach (var ad in CityData.Instance.addressDirectory)
        {
            if (ad?.building == null || ad.preset == null || !ad.preset.assignPurpose) continue;
            if (!units.TryGetValue(ad.building.Pointer, out var list)) units[ad.building.Pointer] = list = new();
            list.Add(ad);
        }
        // Street level first, then upstairs, then the basements from the top down.
        static int Order(NewAddress a) => a.floor == null ? 999 : a.floor.floor >= 0 ? a.floor.floor : 100 - a.floor.floor;
        foreach (var list in units.Values) list.Sort((a, b) => Order(a) != Order(b) ? Order(a).CompareTo(Order(b)) : a.id.CompareTo(b.id));

        int placed = 0, missed = 0;
        foreach (var ((x, y), tile) in plan.Tiles)
        {
            if (tile.Businesses.Count == 0) continue;
            var where = $"tile R{plan.Height - 1 - y} C{x}";
            if (!CityBoundaryAndTiles.Instance.cityTiles.TryGetValue(new Vector2Int(x, y), out var cityTile) || cityTile.building == null ||
                !units.TryGetValue(cityTile.building.Pointer, out var free))
            {
                Plugin.Logger.LogWarning($"Businesses for {where}: its building has no business units");
                missed += tile.Businesses.Count;
                continue;
            }
            var building = cityTile.building;
            foreach (var name in tile.Businesses)
            {
                if (!presets.TryGetValue(name, out var preset))
                {
                    Plugin.Logger.LogWarning($"Businesses for {where}: the game has no business called '{name}'");
                    missed++;
                    continue;
                }
                var unit = free.FirstOrDefault(a => !Planned.Contains(a.Pointer) && Fits(preset, a.preset));
                if (unit == null)
                {
                    Plugin.Logger.LogWarning($"Businesses for {where}: {building.preset.name} has no free unit for {name}; left to the game");
                    missed++;
                    continue;
                }
                var why = WhyNot(preset, unit);
                if (why != null)
                {
                    Plugin.Logger.LogWarning($"Businesses for {where}: {name} can't go in {building.preset.name} ({why}); the unit is left to the game");
                    missed++;
                    continue;
                }
                Assign(unit, preset);
                Planned.Add(unit.Pointer);
                if (unit.addressPreset == preset)
                {
                    placed++;
                    Plugin.Logger.LogInfo($"{name} in {building.preset.name} at {where}, floor {unit.floor?.floor}");
                }
                else
                {
                    missed++;
                    Plugin.Logger.LogWarning($"Businesses for {where}: asked for {name}, the game made it {unit.addressPreset?.name ?? "nothing"}");
                    // An empty unit goes back to the game's own pass.
                    if (unit.addressPreset == null) Planned.Remove(unit.Pointer);
                }
            }
        }
        Plugin.Logger.LogInfo($"Planned businesses: {placed} placed" + (missed > 0 ? $", {missed} left to the game (see warnings above)" : ""));
    }

    private static bool Fits(AddressPreset preset, LayoutConfiguration layout)
    {
        if (layout == null) return false;
        foreach (var l in preset.compatible)
            if (l != null && l.Pointer == layout.Pointer) return true;
        return false;
    }

    /// <summary>The rules AssignPurpose itself applies, so a planned pick never leaves a unit with nothing.</summary>
    private static string WhyNot(AddressPreset preset, NewAddress unit)
    {
        if (preset.disableThis) return "switched off in the game";
        if (unit.floor != null && (unit.floor.floor < preset.minMaxFloors.x || unit.floor.floor > preset.minMaxFloors.y)) return "not on that floor";
        if (preset.limitToBuildings.Count > 0 && !preset.limitToBuildings.Contains(unit.building.preset)) return "only in other buildings";
        if (preset.hardSizeLimits && (unit.nodes.Count < preset.fitsUnitSizeMin || unit.nodes.Count > preset.fitsUnitSizeMax)) return "unit is the wrong size";
        var refs = CityData.Instance.addressTypeReference;
        if (refs.ContainsKey(preset) && refs[preset].Count >= preset.maxInstances) return $"the city already has the most allowed, {preset.maxInstances}";
        return null;
    }

    private static void Assign(NewAddress unit, AddressPreset preset)
    {
        var off = new List<AddressPreset>();
        foreach (var p in Toolbox.Instance.allAddressPresets)
        {
            if (p == null || p.Pointer == preset.Pointer || p.disableThis || !Fits(p, unit.preset)) continue;
            p.disableThis = true;
            off.Add(p);
        }
        assigning = true;
        try
        {
            unit.CalculateLandValue();
            unit.AssignPurpose();
        }
        finally
        {
            assigning = false;
            foreach (var p in off) p.disableThis = false;
        }
    }

    [HarmonyPatch(typeof(NewAddress), nameof(NewAddress.AssignPurpose))]
    private static class SkipPlanned
    {
        private static bool Prefix(NewAddress __instance) => assigning || Planned.Count == 0 || !Planned.Contains(__instance.Pointer);
    }
}
