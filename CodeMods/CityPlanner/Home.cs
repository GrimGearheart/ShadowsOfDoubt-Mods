using System;
using HarmonyLib;
using UnityEngine;

namespace CityPlanner;

/// <summary>
/// The player's apartment ([Home] in the plan; idea by Tayelikel). Apartment numbers are the ones on the doors (1701 =
/// floor 17, flat 01) and are handed out while the city is made, so the apartment is looked up just before residents
/// move in. For that stage its residence is given a copy of its preset marked uninhabitable, so the game moves nobody
/// in and never picks it as a citizen's home. Then, by the Home / Mode setting:
/// - Own: the game's own pick of the player's starting apartment is swapped for this one. The city file remembers it,
///   so every new game in the city starts there (with the game's sandbox "starting apartment" option on).
/// - Buy: it stays empty, and the game lists every empty apartment for sale each day, this one included.
/// The real preset is put back as soon as residents have moved in, before anything is saved.
/// </summary>
internal static class Home
{
    internal enum Mode { Own, Buy }

    private static ResidenceController reserved;
    private static ResidencePreset original, stand_in;
    private static bool own, swapPlayerHome;

    public static void Prepare()
    {
        Restore();
        var want = Plugin.Plan?.Home;
        if (want == null) return;
        try
        {
            var where = $"R{want.Row} C{want.Col}";
            var plan = Plugin.Plan;
            var coord = new Vector2Int(want.Col, plan.Height - 1 - want.Row);
            if (!CityBoundaryAndTiles.Instance.cityTiles.TryGetValue(coord, out var tile) || tile.building == null)
            {
                Plugin.Logger.LogWarning($"Home: no building at tile {where}");
                return;
            }
            ResidenceController found = null;
            var numbers = new System.Collections.Generic.List<int>();
            foreach (var address in CityData.Instance.addressDirectory)
            {
                if (address?.building == null || address.building.Pointer != tile.building.Pointer || address.residence == null) continue;
                var n = address.residence.GetResidenceNumber();
                if (n > 0) numbers.Add(n); // basement flats come out negative and can't be planned
                if (n == want.Number) found = address.residence;
            }
            if (found == null)
            {
                numbers.Sort();
                Plugin.Logger.LogWarning($"Home: {tile.building.preset?.name} at {where} has no apartment {want.Number}" +
                                         (numbers.Count > 0 ? $"; it has {string.Join(", ", numbers)}" : "; it has no apartments"));
                return;
            }
            if (found.preset == null || !found.preset.habitable)
            {
                Plugin.Logger.LogWarning($"Home: apartment {want.Number} at {where} isn't a home anyone can live in");
                return;
            }
            original = found.preset;
            stand_in = UnityEngine.Object.Instantiate(original);
            stand_in.name = original.name;
            stand_in.habitable = false;
            found.preset = stand_in;
            reserved = found;
            own = swapPlayerHome = Plugin.HomeMode.Value == Mode.Own;
            Plugin.Logger.LogInfo($"Home: apartment {want.Number} in {tile.building.preset?.name} at {where} kept empty " +
                                  (own ? "for the player to own" : "and up for sale"));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Home: reserving the apartment failed: " + e);
            Restore();
        }
    }

    /// <summary>Residents have moved in: the apartment gets its real preset back.</summary>
    public static void Finish()
    {
        if (reserved == null) return;
        var playerHome = Player.Instance?.home != null && reserved.address != null && Player.Instance.home.Pointer == reserved.address.Pointer;
        var others = 0;
        if (reserved.address != null)
            foreach (var h in reserved.address.inhabitants)
                if (h != null && h.Pointer != Player.Instance?.Pointer) others++;
        Plugin.Logger.LogInfo($"Home: apartment {reserved.GetResidenceNumber()} has {others} residents besides the player" +
                              (playerHome ? ", and is the player's home" : own ? ", but the player didn't get it" : ", and goes up for sale"));
        Restore();
    }

    public static void Restore()
    {
        if (reserved != null && original != null) reserved.preset = original;
        if (stand_in != null) UnityEngine.Object.Destroy(stand_in);
        reserved = null;
        original = stand_in = null;
        own = swapPlayerHome = false;
    }

    /// <summary>Own: the game's pick of the player's starting apartment becomes the planned one.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetResidence))]
    private static class PlayerHomePatch
    {
        private static void Prefix(ref ResidenceController newHome)
        {
            if (!swapPlayerHome || reserved == null || newHome == null || newHome.Pointer == reserved.Pointer) return;
            swapPlayerHome = false;
            newHome = reserved;
        }
    }
}
