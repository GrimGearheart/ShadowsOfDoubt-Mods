using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Research for shops (Debug.ProbeShops): lists the citizen goals that send people to businesses by category, the
/// categories of the businesses you own, and every action citizens take inside a business you own.
/// </summary>
internal static class ShopProbe
{
    public static void DumpGoals()
    {
        if (!Plugin.ProbeShops.Value) return;
        try
        {
            foreach (var g in Resources.FindObjectsOfTypeAll<AIGoalPreset>()
                         .Where(g => g != null && (g.locationOption == AIGoalPreset.LocationOption.commercialDecision ||
                                                   g.locationOption == AIGoalPreset.LocationOption.commercial))
                         .OrderBy(g => g.name))
                Plugin.Logger.LogInfo($"[Probe] goal '{g.name}' location={g.locationOption} category={g.desireCategory}");
            foreach (var b in Store.Owned.Values)
            {
                var c = Store.FindCompany(b.CompanyId);
                if (c == null) continue;
                var cats = new List<string>();
                foreach (var cat in c.preset.companyCategories) cats.Add(cat.ToString());
                var items = new List<string>();
                foreach (var kv in c.prices)
                    if (kv.Key != null) items.Add($"{kv.Key.name}={kv.Value}({kv.Key.retailItem?.desireCategory})");
                Plugin.Logger.LogInfo($"[Probe] {b.Name}: {c.address.favouredCustomers.Count} regular customers (citizens whose favourite place this is)");
                Plugin.Logger.LogInfo($"[Probe] {b.Name}: preset '{c.preset.name}' categories [{string.Join(", ", cats)}] " +
                                      $"records sales={c.preset.recordSalesData}, menu: {string.Join(", ", items)}");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Probe failed: " + e.Message);
        }
    }
}

[HarmonyPatch(typeof(ActionController), nameof(ActionController.ExecuteAction))]
internal static class ShopActionProbe
{
    private static readonly HashSet<string> seen = new();

    private static void Prefix(AIActionPreset action, Interactable what, NewNode where, Actor who)
    {
        if (!Plugin.ProbeShops.Value || action == null || who == null || who.isPlayer) return;
        try
        {
            var c = where?.gameLocation?.thisAsAddress?.company;
            var b = Store.Get(c);
            if (b == null) return;
            var human = who.TryCast<Human>();
            var worker = human?.job?.employer != null && human.job.employer.companyID == c.companyID;
            var key = $"{human?.humanID}|{action.name}|{Mathf.FloorToInt(SessionData.Instance.gameTime)}";
            if (!seen.Add(key)) return;
            var goal = human?.ai?.currentGoal?.preset?.name;
            Plugin.Logger.LogInfo($"[Probe] {b.Name}: {(worker ? "staff" : "visitor")} {human?.GetCitizenName()} " +
                                  $"did '{action.name}' on '{what?.preset?.name}' (goal '{goal}')");
        }
        catch { }
    }
}
