using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Shoppers for retail businesses (pawn shops, hardware stores, chemists). The game never sends citizens shopping for goods, so this does: through
/// each shop's opening hours a few shoppers a day (Retail.ShoppersPerDay) come in and buy something off the shelves.
/// While you're near the shop they're real people: a citizen nearby gets the game's "go to" errand to the shop, walks to
/// the counter and buys there. Anywhere else the sale just happens, so a shop earns the same wherever you are.
/// Killers buying a weapon at your shop (the game's own purchase) are counted too, and are never turned away.
/// </summary>
[HarmonyPatch]
internal static class Retail
{
    /// <summary>Shoppers on their way: humanID -> (shop, when they were sent).</summary>
    private static readonly Dictionary<int, (int CompanyId, float Sent)> walking = new();
    /// <summary>When each citizen last bought at one of your shops: nobody shops twice in a day.</summary>
    private static readonly Dictionary<int, float> lastShopped = new();
    private const float ShopAgainHours = 24f;

    /// <summary>Shoppers to send this hour: (company, minute of the hour).</summary>
    private static readonly List<(int CompanyId, int Minute)> due = new();

    private const float SeeDistance = 60f;
    private const float FetchDistance = 150f;
    private const float GiveUpHours = 1.5f;

    /// <summary>When in the day people go shopping: quiet mornings, busy afternoons, a few late customers.</summary>
    private static readonly float[] HourWeight =
    {
        0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.2f, 0.4f, 0.6f, 0.8f, 1f, 1f,
        1.2f, 1.2f, 1.2f, 1.2f, 1.2f, 1.2f, 1f, 0.8f, 0.6f, 0.4f, 0.3f, 0.2f
    };

    public static void Reset()
    {
        walking.Clear();
        due.Clear();
        pending.Clear();
        lastShopped.Clear();
    }

    /// <summary>Retail businesses that can be bought: shop types listed in Retail.ShopTypes.</summary>
    public static bool IsRetail(Company c)
    {
        var name = c?.preset?.name;
        if (string.IsNullOrEmpty(name)) return false;
        return Plugin.RetailShopTypes.Value.Split(',').Any(t => t.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>On the hour: decide how many shoppers come to each owned shop this hour, and when.</summary>
    public static void Hourly(int hour)
    {
        due.Clear();
        var perDay = Plugin.RetailShoppersPerDay.Value;
        if (perDay <= 0f) return;
        foreach (var b in Store.Owned.Values)
        {
            var c = Store.FindCompany(b.CompanyId);
            if (!IsRetail(c) || !c.openForBusinessDesired || Store.StaffCount(c) == 0) continue;
            var openWeight = Enumerable.Range(0, 24).Where(h => Trade.OpenAt(c, h)).Sum(h => HourWeight[h]);
            if (openWeight <= 0f || !Trade.OpenAt(c, hour)) continue;
            var expected = perDay * HourWeight[hour] / openWeight;
            var count = Mathf.FloorToInt(expected + UnityEngine.Random.value);
            for (var i = 0; i < count; i++) due.Add((b.CompanyId, UnityEngine.Random.Range(0, 60)));
        }
    }

    /// <summary>Every minute: send the shoppers due now, and let go of any who never made it.</summary>
    public static void Minutely()
    {
        var now = SessionData.Instance.gameTime;
        var minute = Mathf.FloorToInt((SessionData.Instance.decimalClock % 1f) * 60f);
        for (var i = due.Count - 1; i >= 0; i--)
        {
            if (due[i].Minute > minute) continue;
            var id = due[i].CompanyId;
            due.RemoveAt(i);
            var b = Store.Owned.TryGetValue(id, out var owned) ? owned : null;
            var c = Store.FindCompany(id);
            if (b != null && c?.address != null) Shopper(b, c);
        }
        foreach (var kv in walking.Where(kv => now - kv.Value.Sent > GiveUpHours).ToList())
            walking.Remove(kv.Key);
    }

    private static void Shopper(Business b, Company c)
    {
        var anchor = Player.Instance.FindSafeTeleport(c.address);
        if (anchor == null) return;
        var nearby = Vector3.Distance(Player.Instance.transform.position, anchor.position) <= SeeDistance;
        if (nearby && Send(c, anchor)) return;
        // Not watching (or nobody free to send): the sale happens off-screen, to someone from the neighbourhood.
        var who = Neighbour(c, anchor.position);
        if (who != null) Buy(b, c, who, offscreen: true);
    }

    /// <summary>Sends a free citizen near the shop there with the game's "go to" errand. False if nobody suits.</summary>
    private static bool Send(Company c, NewNode anchor)
    {
        Human pick = null;
        var best = FetchDistance;
        foreach (var h in CityData.Instance.citizenDirectory)
        {
            if (!Free(h, c) || walking.ContainsKey(h.humanID) || ShoppedRecently(h)) continue;
            if (h.currentGameLocation != null && h.currentGameLocation.thisAsAddress == c.address) continue;
            var d = Vector3.Distance(h.transform.position, anchor.position);
            if (d < best)
            {
                best = d;
                pick = h;
            }
        }
        if (pick == null) return false;
        var goal = pick.ai.CreateNewGoal(RoutineControls.Instance.toGoWalkGoal, 0f, 0f, pick.FindSafeTeleport(c.address));
        if (goal == null) return false;
        walking[pick.humanID] = (c.companyID, SessionData.Instance.gameTime);
        Plugin.Logger.LogInfo($"{c.name}: {pick.GetCitizenName()} is coming in to shop ({best:0}m away)");
        return true;
    }

    /// <summary>Someone who could go shopping right now: awake, not working, not busy with anything that matters.</summary>
    private static bool Free(Human h, Company c)
    {
        if (h == null || h.isPlayer || h.isDead || h.isAsleep || h.isAtWork || h.isStunned || h.inConversation || h.ai == null) return false;
        if (h.job?.employer != null && h.job.employer.companyID == c.companyID) return false;
        var goal = h.ai.currentGoal;
        if (goal != null && (goal.murderRef != null || goal.preset == RoutineControls.Instance.toGoGoal ||
                             goal.preset == RoutineControls.Instance.toGoWalkGoal)) return false;
        var m = MurderController.Instance;
        if (m != null && (h == m.currentMurderer || h == m.currentVictim)) return false;
        return true;
    }

    private static bool ShoppedRecently(Human h) =>
        lastShopped.TryGetValue(h.humanID, out var t) && SessionData.Instance.gameTime - t < ShopAgainHours;

    /// <summary>A random citizen living within reach of the shop, for off-screen sales.</summary>
    private static Human Neighbour(Company c, Vector3 shop)
    {
        var list = new List<Human>();
        foreach (var h in CityData.Instance.citizenDirectory)
        {
            if (h == null || h.isDead || h.home == null || ShoppedRecently(h)) continue;
            if (h.job?.employer != null && h.job.employer.companyID == c.companyID) continue;
            var home = h.home.nodes.Count > 0 ? h.home.nodes[0].position : h.transform.position;
            if (Vector3.Distance(home, shop) <= 300f) list.Add(h);
        }
        return list.Count > 0 ? list[UnityEngine.Random.Range(0, list.Count)] : null;
    }

    /// <summary>Insert a trip to the counter once a shopper is through the door.</summary>
    [HarmonyPatch(typeof(NewAIController), nameof(NewAIController.AITick))]
    [HarmonyPostfix]
    private static void Arrived(NewAIController __instance)
    {
        try
        {
            var h = __instance?.human;
            if (h == null || !walking.TryGetValue(h.humanID, out var w)) return;
            var c = h.currentGameLocation?.thisAsAddress?.company;
            if (c == null || c.companyID != w.CompanyId) return;
            var goal = __instance.currentGoal;
            if (goal == null || goal.preset == RoutineControls.Instance.toGoWalkGoal) return;
            walking.Remove(h.humanID);
            var counter = Toolbox.Instance.FindNearestWithAction(RoutineControls.Instance.purchaseItem, h.currentRoom, h,
                AIActionPreset.FindSetting.allAreas, false, null, c.address);
            pending[h.humanID] = w.CompanyId;
            if (counter == null || !goal.TryInsertInteractableAction(counter, RoutineControls.Instance.purchaseItem, 99))
            {
                // No counter to walk to: buy on the spot.
                pending.Remove(h.humanID);
                var b = Store.Get(c);
                if (b != null) Buy(b, c, h, offscreen: false);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Shopper arrival failed: " + e.Message);
        }
    }

    /// <summary>Shoppers headed for the counter: humanID -> shop.</summary>
    private static readonly Dictionary<int, int> pending = new();

    /// <summary>At the counter: our shoppers buy; the game's own buyers (killers after a weapon) are counted.</summary>
    [HarmonyPatch(typeof(ActionController), nameof(ActionController.ExecuteAction))]
    [HarmonyPostfix]
    private static void AtCounter(AIActionPreset action, Interactable what, NewNode where, Actor who)
    {
        if (action == null || who == null || who.isPlayer || action != RoutineControls.Instance.purchaseItem) return;
        try
        {
            var c = where?.gameLocation?.thisAsAddress?.company;
            var b = Store.Get(c);
            if (b == null) return;
            var h = who.TryCast<Human>();
            if (h == null) return;
            if (pending.Remove(h.humanID))
            {
                Buy(b, c, h, offscreen: false);
                return;
            }
            var bought = h.ai?.currentAction?.passedAcquireItems;
            if (bought == null || bought.Count == 0) return;
            var sale = new Sale { Time = SessionData.Instance.gameTime, Customer = h.GetCitizenName() };
            foreach (var item in bought)
            {
                if (item == null) continue;
                var price = c.prices.ContainsKey(item) ? c.prices[item] : 0;
                sale.Items.Add(new SaleItem { Item = item.name, Price = price });
                b.Till += price;
                b.TotalTaken += price;
                if (b.Stock.TryGetValue(item.name, out var n)) b.Stock[item.name] = Math.Max(0, n - 1);
            }
            if (sale.Items.Count > 0) Store.RecordSale(b, sale);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Counter purchase failed: " + e.Message);
        }
    }

    /// <summary>
    /// One shopper buys one thing they can afford from what's in stock (cheaper things more often, wealthier people
    /// reach higher), paid into the till and written in the shop's sales records like any sale.
    /// </summary>
    private static void Buy(Business b, Company c, Human who, bool offscreen)
    {
        lastShopped[who.humanID] = SessionData.Instance.gameTime;
        var inStock = Store.Menu(c).Where(kv => b.Stock.TryGetValue(kv.Key.name, out var n) && n > 0).ToList();
        if (inStock.Count == 0)
        {
            b.TurnedAway++;
            b.LastTurnedAway = SessionData.Instance.gameTime;
            Plugin.Logger.LogInfo($"{b.Name}: {who.GetCitizenName()} left, the shelves are empty");
            return;
        }
        var wealth = Mathf.Clamp01(who.societalClass);
        var weights = inStock.Select(kv => Mathf.Pow(Mathf.Max(1, kv.Value), -0.5f + wealth)).ToList();
        var roll = UnityEngine.Random.value * weights.Sum();
        var pick = inStock[^1];
        for (var i = 0; i < inStock.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f)
            {
                pick = inStock[i];
                break;
            }
        }

        var item = pick.Key;
        var price = pick.Value;
        var sale = new Sale { Time = SessionData.Instance.gameTime, Customer = who.GetCitizenName(), Offscreen = offscreen };
        sale.Items.Add(new SaleItem { Item = item.name, Price = price });
        Store.RecordSale(b, sale);
        b.Till += price;
        b.TotalTaken += price;
        b.Stock[item.name] = b.Stock[item.name] - 1;
        if (c.preset.recordSalesData) c.AddSalesRecord(who, item, SessionData.Instance.gameTime);
        Plugin.Logger.LogInfo($"{b.Name}: {who.GetCitizenName()} bought {Store.ItemName(item.name)} for ¢{price}" + (offscreen ? " (off-screen)" : ""));
    }
}
