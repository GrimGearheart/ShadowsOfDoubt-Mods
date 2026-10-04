using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace CityPlanner;

/// <summary>
/// City Planner: generates a hand-planned city. The plan (BepInEx/config/cityplan.txt) sets the city size,
/// population, districts (type, name, wealth) and the building on every tile. Pick "Planned city" in the
/// size dropdown when generating a new city; the game still generates streets, interiors and residents.
/// </summary>
[BepInPlugin("sodmods.cityplanner", "City Planner", "1.0.2")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    internal static Plan Plan;
    internal static BepInEx.Configuration.ConfigEntry<bool> HomeWealth;
    internal static BepInEx.Configuration.ConfigEntry<string> KeepSizes;
    internal static BepInEx.Configuration.ConfigEntry<bool> LogPerformance;
    internal static BepInEx.Configuration.ConfigEntry<bool> StreetWealth;
    internal static BepInEx.Configuration.ConfigEntry<string> GritClusters;
    internal static BepInEx.Configuration.ConfigEntry<bool> StreetThemes;
    internal static BepInEx.Configuration.ConfigEntry<bool> EdgeGates;
    internal static BepInEx.Configuration.ConfigEntry<string> OpenAlleysIn;
    internal static BepInEx.Configuration.ConfigEntry<int> EdgeGateReach;
    internal static BepInEx.Configuration.ConfigEntry<BuildingPreset.LandValue> GritFreeFrom;

    public override void Load()
    {
        Logger = Log;
        KeepSizes = Config.Bind("Sizes", "Keep", "10x10,9x10",
            "Sizes of cities made with earlier plans, so they still load. Only ever add to the end of this list; never remove or reorder.");
        HomeWealth = Config.Bind("Wealth", "DistrictHomeWealth", true, "Homes take their wealth mainly from their district (otherwise mostly from floor height, as in vanilla).");
        StreetWealth = Config.Bind("Wealth", "AverageStreetWealth", true, "Streets take the average wealth of the tiles they cover (vanilla: their first tile), for street decoration.");
        StreetThemes = Config.Bind("Wealth", "StreetThemes", true,
            "District-themed street decoration (Chinatown gates etc.) follows the districts a street actually runs through.");
        OpenAlleysIn = Config.Bind("Streets", "OpenAlleysIn", "Chinatown,Red Light,The Bricks,Brickrow",
            "Districts (planned names) where alleys become back streets, so no dead-end walls split them. Empty = vanilla.");
        EdgeGates = Config.Bind("Wealth", "ChinatownGatesAtEdges", true, "Paifang gates only where a street enters or leaves Chinatown.");
        EdgeGateReach = Config.Bind("Wealth", "EdgeGateReach", 8, "How close (in path nodes; a city block is about 19) a gate must be to Chinatown's edge.");
        GritClusters = Config.Bind("Wealth", "GritDecorations", "Shanty,BurningBarrel,StreetJunk,StaticLitter",
            "Street decorations (name fragments) kept out of wealthy districts in planned cities.");
        GritFreeFrom = Config.Bind("Wealth", "GritFreeFrom", BuildingPreset.LandValue.high,
            "Districts at this land value or above get none of the grit decorations.");
        LogPerformance = Config.Bind("Debug", "LogPerformance", false, "Write frame rate and memory use to the log every minute.");
        InstallBundledCities();
        var path = Path.Combine(Paths.ConfigPath, "cityplan.txt");
        try
        {
            string error = null;
            Plan = File.Exists(path) ? Plan.Load(path, out error) : null;
            if (Plan == null && File.Exists(path)) Log.LogError("City plan not loaded: " + error);
            else if (Plan == null) Log.LogInfo("No city plan in the config folder: city generation is unchanged.");
            else Log.LogInfo($"City plan loaded: {Plan.Width}x{Plan.Height}, {Plan.Districts.Count} districts, population x{Plan.Population}");
        }
        catch (Exception e)
        {
            Log.LogError("City plan not loaded: " + e.Message);
        }
        new Harmony("sodmods.cityplanner").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("City Planner loaded");
    }

    /// <summary>
    /// Cities shipped with the mod (in its Cities folder) are copied into the game's own Cities folder, where the
    /// new-game screen lists them. A city is copied again only if the shipped file differs (an update).
    /// </summary>
    private void InstallBundledCities()
    {
        try
        {
            // BepInEx loads plugins without a file location, so find this mod's folder by its dll.
            var dll = Directory.GetFiles(Paths.PluginPath, "CityPlanner.dll", SearchOption.AllDirectories).FirstOrDefault();
            var source = dll == null ? null : Path.Combine(Path.GetDirectoryName(dll)!, "Cities");
            if (source == null || !Directory.Exists(source))
            {
                Log.LogInfo("No bundled cities to install.");
                return;
            }
            var target = Path.Combine(Application.persistentDataPath, "Cities");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                var dest = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(file).Length) continue;
                File.Copy(file, dest, true);
                Log.LogInfo("Installed city file " + Path.GetFileName(file));
            }
        }
        catch (Exception e)
        {
            Log.LogError("Couldn't install the bundled cities: " + e.Message);
        }
    }

    /// <summary>Generating a new city at the plan's size.</summary>
    internal static bool Active =>
        Plan != null && CityConstructor.Instance != null && CityConstructor.Instance.generateNew &&
        RestartSafeController.Instance.cityX == Plan.Width && RestartSafeController.Instance.cityY == Plan.Height;
}

/// <summary>The plan's size is added to the game's size list, so share codes, file names and loading all work.</summary>
internal static class Sizes
{
    internal static int VanillaCount = -1;

    /// <summary>
    /// Adds the extra sizes. A city remembers its size as a slot in this list, so slots must never move:
    /// sizes from earlier plans (Sizes.Keep in the config) come first, in a fixed order, then the plan's size.
    /// </summary>
    public static void Ensure()
    {
        var list = CityControls.Instance?.citySizes;
        if (list == null || VanillaCount >= 0) return;
        VanillaCount = list.Count;
        foreach (var entry in Plugin.KeepSizes.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = entry.Trim().ToLowerInvariant().Split('x');
            if (p.Length == 2 && int.TryParse(p[0], out var x) && int.TryParse(p[1], out var y)) Add(list, x, y);
        }
        if (Plugin.Plan != null) Add(list, Plugin.Plan.Width, Plugin.Plan.Height);
    }

    private static void Add(Il2CppSystem.Collections.Generic.List<CityControls.CitySize> list, int w, int h)
    {
        for (var i = 0; i < list.Count; i++)
            if (Mathf.RoundToInt(list[i].v2.x) == w && Mathf.RoundToInt(list[i].v2.y) == h) return;
        list.Add(new CityControls.CitySize { size = CityControls.Size.veryLarge, v2 = new Vector2(w, h) });
        Plugin.Logger.LogInfo($"City size slot {list.Count - 1}: {w}x{h}");
    }
}

[HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.LoadDropdownContent))]
internal static class DropdownPatch
{
    private static void Prefix() => Sizes.Ensure();

    private static void Postfix(MainMenuController __instance)
    {
        try
        {
            var dd = __instance.citySizeDropdown?.dropdown;
            var list = CityControls.Instance.citySizes;
            if (dd == null || Sizes.VanillaCount < 0) return;
            var plan = Plugin.Plan;
            for (var i = Sizes.VanillaCount; i < list.Count; i++)
            {
                var w = Mathf.RoundToInt(list[i].v2.x);
                var h = Mathf.RoundToInt(list[i].v2.y);
                // Every slot needs an entry: the game maps dropdown position to size slot.
                var planned = plan != null && w == plan.Width && h == plan.Height;
                dd.options.Add(new TMP_Dropdown.OptionData(planned ? $"Planned city ({w}x{h})" : $"Extra large {w}x{h}"));
            }
            dd.RefreshShownValue();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Toolbox), nameof(Toolbox.GetCitySizeFromValue))]
internal static class SizeLookupPatch
{
    private static void Prefix() => Sizes.Ensure();
}

/// <summary>The game resets the population multiplier when it lays out a city; set the plan's just before residents are made.</summary>
[HarmonyPatch(typeof(CitizenCreator), "Populate")]
internal static class PopulationPatch
{
    private static void Prefix()
    {
        if (!Plugin.Active) return;
        CityData.Instance.populationMultiplier = Mathf.Clamp(Plugin.Plan.Population, 0.1f, 2f);
        Plugin.Logger.LogInfo($"Populating with multiplier {CityData.Instance.populationMultiplier}");
    }
}

/// <summary>
/// Districts. The game grows its own districts at random; once it's done (and before tiles are grouped into
/// blocks), every tile is moved into the planned district. Coastline tiles join the nearest planned district.
/// </summary>
internal static class DistrictsPatch
{
    /// <summary>Planned district for each created district controller, by district ID (for naming).</summary>
    internal static readonly Dictionary<int, PlannedDistrict> Created = new();

    public static void Run()
    {
        try
        {
            Apply(Plugin.Plan);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Applying districts failed: " + e);
        }
    }

    private static void Apply(Plan plan)
    {
        var presets = new Dictionary<string, DistrictPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Toolbox.Instance.allDistricts) presets[p.name] = p;

        var cityDistricts = CityDistricts.Instance;
        var old = cityDistricts.districtDirectory.ToArray().ToList();
        var controllers = new Dictionary<char, DistrictController>();
        Created.Clear();
        foreach (var d in plan.Districts.Values)
        {
            if (!presets.TryGetValue(d.Type, out var preset))
            {
                Plugin.Logger.LogError($"Unknown district type '{d.Type}' for {d.Name}");
                continue;
            }
            var controller = UnityEngine.Object.Instantiate(cityDistricts.districtPrefab, PrefabControls.Instance.cityContainer.transform)
                .GetComponent<DistrictController>();
            controller.Setup(preset);
            controllers[d.Key] = controller;
            Created[controller.districtID] = d;
        }

        var moved = 0;
        foreach (var entry in CityBoundaryAndTiles.Instance.cityTiles)
        {
            var c = entry.Key;
            var planned = plan.Nearest(c.x, c.y);
            if (!controllers.TryGetValue(planned.District.Key, out var controller)) continue;
            controller.AddCityTile(entry.Value);
            moved++;
        }

        // The game's own districts are now empty: remove them.
        foreach (var d in old)
        {
            if (d.cityTiles.Count > 0) continue;
            cityDistricts.districtDirectory.Remove(d);
            UnityEngine.Object.Destroy(d.gameObject);
        }
        Plugin.Logger.LogInfo($"Districts applied: {controllers.Count} planned districts, {moved} tiles, removed {old.Count} generated districts");
    }
}

/// <summary>
/// Buildings. After the game's density pass (which would otherwise overwrite them), each planned tile gets
/// its land value, density and building. The game then only fills the coastline.
/// </summary>
internal static class BuildingsPatch
{
    public static void Run()
    {
        var __instance = CityBuildings.Instance;
        try
        {
            var plan = Plugin.Plan;
            var presets = new Dictionary<string, BuildingPreset>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in AssetLoader.Instance.GetAllBuildingPresets()) presets[b.name] = b;

            var tiles = CityBoundaryAndTiles.Instance.cityTiles;
            var placed = 0;
            foreach (var entry in tiles)
            {
                var c = entry.Key;
                var tile = entry.Value;
                var planned = plan.Nearest(c.x, c.y);
                tile.landValue = planned.District.LandValue;
                tile.density = planned.District.Density;

                if (!plan.Tiles.TryGetValue((c.x, c.y), out var exact) || tile.building != null) continue;
                if (!presets.TryGetValue(exact.Building, out var preset))
                {
                    Plugin.Logger.LogError($"Unknown building '{exact.Building}' at {c}");
                    continue;
                }
                UnityEngine.Object.Instantiate(__instance.buildingPrefab, tile.transform).GetComponent<NewBuilding>().Setup(tile, preset);
                placed++;
            }
            foreach (var block in CityBlocks.Instance.blocksDirectory) block.UpdateAverageDensity();
            Plugin.Logger.LogInfo($"Buildings placed: {placed}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Placing buildings failed: " + e);
        }
    }
}

/// <summary>
/// The generator's stage methods are small async wrappers that the compiled game can inline, so they can't be
/// hooked. Instead, the generation loop is watched: just before it starts the blocks stage the districts are
/// done, and just before the buildings stage the density pass is done.
/// </summary>
[HarmonyPatch(typeof(CityConstructor), "Update")]
internal static class StagePatch
{
    internal static bool DistrictsDone, BuildingsDone, AlleysDone;

    private static void Prefix(CityConstructor __instance)
    {
        if (__instance.loadingOperationActive || !Plugin.Active) return;
        if (__instance.loadState == CityConstructor.LoadState.generateBlocks && !DistrictsDone)
        {
            DistrictsDone = true;
            DistrictsPatch.Run();
        }
        else if (__instance.loadState == CityConstructor.LoadState.generateBuildings && !BuildingsDone)
        {
            BuildingsDone = true;
            BuildingsPatch.Run();
        }
        else if (__instance.loadState == CityConstructor.LoadState.generateBlueprints && !AlleysDone)
        {
            AlleysDone = true;
            OpenAlleys.Run();
        }
        else if (__instance.loadState == CityConstructor.LoadState.generateInteriors && !StreetThemes.Prepared)
        {
            StreetThemes.Prepare();
        }
        else if (__instance.loadState >= CityConstructor.LoadState.savingData && StreetThemes.Prepared)
        {
            StreetThemes.Restore();
        }
    }
}

/// <summary>
/// Dead-end walls. The game builds a wall wherever two different alleys meet, which is how it splits long
/// quiet lanes into dead ends. In the districts listed in the config, alleys are made back streets once the
/// streets are laid out (before any walls are built), so those lanes run through.
/// </summary>
internal static class OpenAlleys
{
    public static void Run()
    {
        try
        {
            var districts = new HashSet<string>(Plugin.OpenAlleysIn.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (districts.Count == 0) return;
            var opened = 0;
            foreach (var street in CityData.Instance.streetDirectory)
            {
                if (!street.isAlley || street.tiles.Count == 0) continue;
                var majority = street.tiles.ToArray()
                    // District names are only applied near the end of generation, so go by the plan.
                    .Select(t => t?.cityTile?.district is { } d && DistrictsPatch.Created.TryGetValue(d.districtID, out var pd) ? pd.Name : null)
                    .Where(n => n != null)
                    .GroupBy(n => n)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
                if (majority == null || !districts.Contains(majority)) continue;
                street.isAlley = false;
                street.SetAsBackstreet();
                opened++;
            }
            Plugin.Logger.LogInfo($"Opened {opened} alleys into back streets in {string.Join(", ", districts)}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Opening alleys failed: " + e);
        }
    }
}

/// <summary>
/// Building facing. The game turns each building toward its best street access when it lays out the streets;
/// the city editor's rotate button overrides that. A facing in the plan (e.g. CH/S) does the same.
/// </summary>
[HarmonyPatch(typeof(NewBuilding), nameof(NewBuilding.CalculateFacing))]
internal static class FacingPatch
{
    internal static int Count;

    private static bool Prefix(NewBuilding __instance)
    {
        if (!Plugin.Active || __instance.cityTile == null) return true;
        var c = __instance.cityTile.cityCoord;
        if (!Plugin.Plan.Tiles.TryGetValue((c.x, c.y), out var planned) || planned.Facing == null) return true;
        __instance.SetFacing(planned.Facing.Value);
        Count++;
        Plugin.Logger.LogInfo($"{__instance.preset?.name} at {c} faces {planned.Facing.Value} (planned)");
        return false;
    }
}

/// <summary>Planned districts keep their planned names (the game names districts near the end of generation).</summary>
[HarmonyPatch(typeof(DistrictController), nameof(DistrictController.UpdateName))]
internal static class DistrictNamePatch
{
    private static void Postfix(DistrictController __instance)
    {
        if (!DistrictsPatch.Created.TryGetValue(__instance.districtID, out var planned)) return;
        __instance.name = planned.Name;
        __instance.transform.name = planned.Name;
    }
}

// ---------------------------------------------------------------- Wealth

/// <summary>
/// Home wealth. The game scores each address mostly by floor height and size; the tile's land value is worth at
/// most 1/12 of the score, so rich and poor districts come out nearly the same. In planned cities the district's
/// wealth sets the band and height and size only move a home around within it. This value picks furniture,
/// materials, grime, property prices and who lives there, and is saved with the city.
/// </summary>
[HarmonyPatch(typeof(NewAddress), nameof(NewAddress.CalculateLandValue))]
internal static class AddressWealthPatch
{
    // Kept inside the range vanilla cities produce (about 0.05-0.83): address types and furniture are only
    // authored for that range, and values above it stalled interior generation.
    private static readonly float[] BandStart = { 0.05f, 0.15f, 0.28f, 0.45f, 0.6f };
    private static readonly float[] BandEnd = { 0.18f, 0.30f, 0.45f, 0.62f, 0.78f };
    internal static int Count;

    private static void Postfix(NewAddress __instance)
    {
        if (!Plugin.Active || !Plugin.HomeWealth.Value || __instance.building?.cityTile == null || __instance.floor == null) return;
        var lv = Mathf.Clamp((int)__instance.building.cityTile.landValue, 0, 4);
        var size = 0;
        foreach (var room in __instance.rooms) size += room.nodes.Count;
        var t = Mathf.Clamp01((Mathf.Min(__instance.floor.floor / 4f, 5f) + size * 0.05f) / 11f);
        var value = Mathf.Lerp(BandStart[lv], BandEnd[lv], t);
        if (__instance.addressPreset != null)
            value = Mathf.Clamp(value, __instance.addressPreset.minimumLandValue, __instance.addressPreset.maximumLandValue);
        __instance.normalizedLandValue = value;
        Count++;
    }
}

/// <summary>
/// Street wealth (used to pick street decoration such as wrecked cars, shacks and barrel fires). The game uses
/// the land value of the first tile a street touches; long streets run across districts, so a planned city
/// uses the average over every tile the street covers instead. Interiors are generated on several threads,
/// so the cache is a concurrent one.
/// </summary>
[HarmonyPatch(typeof(Toolbox), nameof(Toolbox.GetNormalizedLandValue), new[] { typeof(NewGameLocation), typeof(bool) })]
internal static class StreetWealthPatch
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, float> Cache = new();

    internal static void Reset() => Cache.Clear();

    private static void Postfix(NewGameLocation location, ref float __result)
    {
        if (!Plugin.Active || !Plugin.StreetWealth.Value || location == null || location.thisAsAddress != null || location.nodes == null || location.nodes.Count == 0) return;
        var fallback = __result;
        __result = Cache.GetOrAdd(location.Pointer, _ =>
        {
            var sum = 0f;
            var n = 0;
            foreach (var node in location.nodes)
            {
                var tile = node?.tile?.cityTile;
                if (tile == null) continue;
                sum += (int)tile.landValue / 4f;
                n++;
            }
            return n > 0 ? sum / n : fallback;
        });
    }
}

/// <summary>
/// District-themed street decoration (Chinatown gates, Red Light neon...) and street grit.
/// The game checks a decoration's district rule against the street's single district, but streets run through
/// several, so themed pieces rarely appear where they belong. Before interiors are generated (on one thread),
/// each street takes the district it mostly runs through, and street decorations with district rules have
/// those rules lifted; the rule is then enforced spot by spot when a place is chosen (thread-safe, read-only
/// data). Everything is put back once the city is generated.
/// </summary>
internal static class StreetThemes
{
    internal sealed class Rule
    {
        public HashSet<string> Limit, Ban;
        public bool Gate;
        public FurnitureCluster Cluster;
        public bool OriginalLimit, OriginalBan;
    }

    internal static readonly Dictionary<IntPtr, Rule> Rules = new();
    internal static readonly HashSet<IntPtr> Grit = new();
    internal static bool Prepared;
    internal static int Placed, Refused;
    private static bool listed;

    public static void Prepare()
    {
        Restore();
        Prepared = true;
        try
        {
            ListThemes();
            var gritNames = Plugin.GritClusters.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(g => g.Trim()).ToArray();
            foreach (var cluster in Toolbox.Instance.allFurnitureClusters)
            {
                if (cluster == null || !cluster.allowOnStreets) continue;
                var name = cluster.name;
                if (gritNames.Any(g => name.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)) Grit.Add(cluster.Pointer);
                if (!Plugin.StreetThemes.Value || (!cluster.limitToDistricts && !cluster.banFromDistricts)) continue;
                Rules[cluster.Pointer] = new Rule
                {
                    Limit = cluster.limitToDistricts ? new HashSet<string>(cluster.allowedInDistricts.ToArray().Select(d => d.name)) : null,
                    Ban = cluster.banFromDistricts ? new HashSet<string>(cluster.notAllowedInDistricts.ToArray().Select(d => d.name)) : null,
                    Gate = name.IndexOf("Paifang", StringComparison.OrdinalIgnoreCase) >= 0,
                    Cluster = cluster,
                    OriginalLimit = cluster.limitToDistricts,
                    OriginalBan = cluster.banFromDistricts,
                };
                cluster.limitToDistricts = false;
                cluster.banFromDistricts = false;
            }

            var reassigned = 0;
            if (Plugin.StreetThemes.Value)
            {
                foreach (var street in CityData.Instance.streetDirectory)
                {
                    var counts = new Dictionary<int, (DistrictController d, int n)>();
                    foreach (var room in street.rooms)
                    foreach (var node in room.nodes)
                    {
                        var d = node?.tile?.cityTile?.district;
                        if (d == null) continue;
                        counts.TryGetValue(d.districtID, out var c);
                        counts[d.districtID] = (d, c.n + 1);
                    }
                    if (counts.Count == 0) continue;
                    var majority = counts.Values.OrderByDescending(c => c.n).First().d;
                    if (street.district != majority)
                    {
                        street.district = majority;
                        reassigned++;
                    }
                }
            }
            Plugin.Logger.LogInfo($"Street themes ready: {Rules.Count} district rules lifted, {Grit.Count} grit decorations, {reassigned} streets re-districted");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Street themes: " + e);
        }
    }

    public static void Restore()
    {
        foreach (var rule in Rules.Values)
        {
            if (rule.Cluster == null) continue;
            rule.Cluster.limitToDistricts = rule.OriginalLimit;
            rule.Cluster.banFromDistricts = rule.OriginalBan;
        }
        Rules.Clear();
        Grit.Clear();
        Prepared = false;
    }

    /// <summary>Logs every district-themed street decoration once, so we know what each district has.</summary>
    private static void ListThemes()
    {
        if (listed) return;
        listed = true;
        foreach (var c in Toolbox.Instance.allFurnitureClusters)
        {
            if (c == null || !c.allowOnStreets || (!c.limitToDistricts && !c.banFromDistricts)) continue;
            var only = c.limitToDistricts ? "only " + string.Join("+", c.allowedInDistricts.ToArray().Select(d => d.name)) : "";
            var not = c.banFromDistricts ? " not " + string.Join("+", c.notAllowedInDistricts.ToArray().Select(d => d.name)) : "";
            Plugin.Logger.LogInfo($"Themed decoration {c.name}: {only}{not} (max {c.maximumPerRoom} per street, chance {c.placementChance})");
        }
    }
}

/// <summary>
/// Where the game chooses a spot for a decoration (called from several threads): enforce lifted district rules
/// spot by spot, keep Chinatown gates to its edges, and keep grit out of wealthy streets.
/// </summary>
[HarmonyPatch(typeof(GenerationController), nameof(GenerationController.GetBestFurnitureClusterLocation))]
internal static class PlacementPatch
{
    private static void Postfix(NewRoom room, FurnitureCluster cluster, ref FurnitureClusterLocation __result)
    {
        if (__result == null || !StreetThemes.Prepared || cluster == null || room?.gameLocation == null) return;
        var onStreet = room.gameLocation.thisAsStreet != null;

        if (StreetThemes.Rules.TryGetValue(cluster.Pointer, out var rule))
        {
            // Inside buildings this is the game's own rule (the room's district); on streets, the spot's district.
            var district = onStreet ? __result.anchorNode?.tile?.cityTile?.district : room.gameLocation.district;
            var preset = district?.preset;
            var ok = preset != null && (rule.Limit == null || rule.Limit.Contains(preset.name)) && (rule.Ban == null || !rule.Ban.Contains(preset.name));
            if (ok && onStreet && rule.Gate && Plugin.EdgeGates.Value)
                ok = NearDistrictEdge(__result.anchorNode, district, Plugin.EdgeGateReach.Value);
            if (!ok)
            {
                __result = null;
                return;
            }
            if (onStreet) System.Threading.Interlocked.Increment(ref StreetThemes.Placed);
        }

        if (!onStreet || !StreetThemes.Grit.Contains(cluster.Pointer)) return;
        var tile = __result.anchorNode?.tile?.cityTile;
        if (tile == null || (int)tile.landValue < (int)Plugin.GritFreeFrom.Value) return;
        __result = null;
        System.Threading.Interlocked.Increment(ref StreetThemes.Refused);
    }

    /// <summary>Is there ground belonging to another district within this many nodes of the spot?</summary>
    private static bool NearDistrictEdge(NewNode node, DistrictController district, int reach)
    {
        if (node == null || district == null) return false;
        var map = PathFinder.Instance.nodeMap;
        var c = node.nodeCoord;
        for (var i = 1; i <= reach; i++)
        {
            foreach (var (dx, dy) in new[] { (i, 0), (-i, 0), (0, i), (0, -i) })
            {
                if (!map.TryGetValue(new Vector3Int(c.x + dx, c.y + dy, c.z), out var other)) continue;
                var d = other?.tile?.cityTile?.district;
                if (d != null && d.districtID != district.districtID) return true;
            }
        }
        return false;
    }
}

// ---------------------------------------------------------------- Performance log (from the size experiment)

[HarmonyPatch(typeof(CityConstructor), nameof(CityConstructor.GenerateNewCity))]
internal static class GenerateTimer
{
    internal static Stopwatch Timer;

    private static void Prefix()
    {
        StagePatch.DistrictsDone = StagePatch.BuildingsDone = StagePatch.AlleysDone = false;
        AddressWealthPatch.Count = 0;
        StreetThemes.Restore();
        StreetThemes.Refused = 0;
        StreetThemes.Placed = 0;
        StreetWealthPatch.Reset();
        Timer = Stopwatch.StartNew();
        var r = RestartSafeController.Instance;
        Plugin.Logger.LogInfo($"Generating {r.cityName} at {r.cityX}x{r.cityY}{(Plugin.Active ? " from the plan" : "")}");
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class Telemetry
{
    private static bool started;
    private static float windowStart, lastFrame;
    private static int frames;
    private static float minFps = float.MaxValue;

    private static long MemoryMb() => Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

    private static void Postfix()
    {
        if (!Plugin.LogPerformance.Value || SessionData.Instance == null || !SessionData.Instance.startedGame) return;
        var now = Time.unscaledTime;
        if (!started)
        {
            started = true;
            windowStart = now;
            var size = CityData.Instance.citySize;
            var gen = GenerateTimer.Timer != null ? $"{GenerateTimer.Timer.Elapsed.TotalSeconds:0}s since generation started" : "loaded from file";
            Plugin.Logger.LogInfo($"In game: city {size.x}x{size.y}, {CityData.Instance.citizenDirectory.Count} citizens, {gen}, memory {MemoryMb()} MB" +
                                  (AddressWealthPatch.Count > 0 ? $", {AddressWealthPatch.Count} addresses re-valued, {StreetThemes.Refused} grit decorations kept out of wealthy streets, {StreetThemes.Placed} themed decorations placed" : ""));
            GenerateTimer.Timer = null;
        }
        frames++;
        if (lastFrame > 0f && now > lastFrame) minFps = Mathf.Min(minFps, 1f / (now - lastFrame));
        lastFrame = now;
        if (now - windowStart < 60f) return;
        Plugin.Logger.LogInfo($"Perf: avg {frames / (now - windowStart):0} fps, worst frame {1000f / Mathf.Max(minFps, 0.1f):0} ms, memory {MemoryMb()} MB");
        windowStart = now;
        frames = 0;
        minFps = float.MaxValue;
    }
}
