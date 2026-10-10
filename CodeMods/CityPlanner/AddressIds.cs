using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CityPlanner;

/// <summary>
/// Address numbers. While a city loads, the game rebuilds some addresses (stairwell landings) and numbers them
/// "highest number loaded so far + 1". That only works when buildings load in the order their addresses were
/// numbered. A plan that mixes placed buildings with "game decides" tiles numbered the placed ones first, so on load
/// a landing could take the number of an address still to come (a bar, say). That address then never registered, and
/// every citizen with it as a favourite broke the load.
/// </summary>
internal static class AddressIds
{
    /// <summary>
    /// New cities: put buildings back in map order (the order the game saves and loads them) before their addresses
    /// are numbered, so planned cities number addresses exactly as the game's own do.
    /// </summary>
    public static void SortBuildings()
    {
        try
        {
            var order = new Dictionary<IntPtr, int>();
            var i = 0;
            foreach (var entry in CityBoundaryAndTiles.Instance.cityTiles) order[entry.Value.Pointer] = i++;
            var list = CityBuildings.Instance.buildingDirectory;
            var buildings = new List<NewBuilding>();
            for (var b = 0; b < list.Count; b++) buildings.Add(list[b]);
            int Rank(NewBuilding nb) => nb?.cityTile != null && order.TryGetValue(nb.cityTile.Pointer, out var r) ? r : int.MaxValue;
            var sorted = new List<NewBuilding>(buildings);
            sorted.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            var moved = 0;
            for (var b = 0; b < sorted.Count; b++)
                if (sorted[b].Pointer != buildings[b].Pointer) moved++;
            if (moved == 0) return;
            list.Clear();
            foreach (var nb in sorted) list.Add(nb);
            Plugin.Logger.LogInfo($"Buildings put in map order before numbering addresses ({moved} moved)");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Sorting buildings failed: " + e);
        }
    }
}

/// <summary>
/// Loading any city: once its file is read, start numbering rebuilt addresses above every saved address, so they can
/// never take a saved address's number. Repairs cities generated before the fix, too.
/// </summary>
[HarmonyPatch(typeof(CityConstructor), "Update")]
internal static class LoadAddressIdsPatch
{
    private static bool done;

    private static void Prefix(CityConstructor __instance)
    {
        try
        {
            if (__instance.generateNew || __instance.loadState == CityConstructor.LoadState.parsingFile)
            {
                done = false;
                return;
            }
            if (done || __instance.currentData?.cityTiles == null) return;
            done = true;
            var max = 0;
            foreach (var tile in __instance.currentData.cityTiles)
            {
                var floors = tile?.building?.floors;
                if (floors == null) continue;
                foreach (var floor in floors)
                {
                    if (floor?.addresses == null) continue;
                    foreach (var address in floor.addresses)
                        if (address != null) max = Math.Max(max, address.id);
                }
            }
            if (max + 1 > NewAddress.assignID)
            {
                NewAddress.assignID = max + 1;
                Plugin.Logger.LogInfo($"Loading city: rebuilt addresses are numbered from {max + 1}, above every saved address");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Setting address numbers for loading failed: " + e);
        }
    }
}
