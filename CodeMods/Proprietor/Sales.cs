using System;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Citizens buy food and drink through ActionController.TakeConsumable, picking each item with
/// Human.PickConsumable from the business's price list; every picked item is bought. At a business the player owns,
/// items that are out of stock are hidden from the pick, and each item sold pays into the till and uses up stock.
/// </summary>
[HarmonyPatch]
internal static class PurchasePatch
{
    private static Human buyer;
    private static Company company;
    private static Business business;
    private static Vector3 where;
    private static Sale sale;
    private static bool anyHidden;

    [HarmonyPatch(typeof(ActionController), nameof(ActionController.TakeConsumable))]
    [HarmonyPrefix]
    private static void Start(Interactable what, NewNode where, Actor who)
    {
        End();
        try
        {
            if (who == null || who.isPlayer) return;
            var human = who.TryCast<Human>();
            // Vending machines and other menus have their own price list and no company behind them.
            if (human == null || what == null || what.preset == null || what.preset.menuOverride != null) return;
            var c = where?.gameLocation?.thisAsAddress?.company;
            if (c == null) return;
            Begin(human, c, what.wPos);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Purchase tracking failed: " + e.Message);
        }
    }

    /// <summary>Starts recording a purchase by this customer at this business (also used by supermarket shopping).</summary>
    internal static void Begin(Human human, Company c, Vector3 position)
    {
        End();
        buyer = human;
        company = c;
        business = Store.Get(c);
        where = position;
    }

    [HarmonyPatch(typeof(ActionController), nameof(ActionController.TakeConsumable))]
    [HarmonyFinalizer]
    internal static void Finish()
    {
        try
        {
            // A customer who found nothing in stock leaves without buying.
            if (business != null && sale == null && anyHidden)
            {
                business.TurnedAway++;
                Loyalty.TurnedAway(business, company, buyer);
                Plugin.Logger.LogInfo($"{business.Name}: {buyer?.GetCitizenName()} left, nothing they wanted was in stock");
            }
        }
        catch { }
        End();
    }

    private static void End()
    {
        buyer = null;
        company = null;
        business = null;
        sale = null;
        anyHidden = false;
    }

    [HarmonyPatch(typeof(Human), nameof(Human.PickConsumable))]
    [HarmonyPrefix]
    private static void HideOutOfStock(Human __instance, ref Il2CppSystem.Collections.Generic.List<InteractablePreset> ignore)
    {
        if (business == null || buyer == null || __instance.Pointer != buyer.Pointer) return;
        try
        {
            Il2CppSystem.Collections.Generic.List<InteractablePreset> hidden = null;
            foreach (var kv in company.prices)
            {
                if (kv.Key == null || business.Stock.TryGetValue(kv.Key.name, out var n) && n > 0) continue;
                if (hidden == null)
                {
                    // A copy: the game's own list doubles as the customer's basket.
                    hidden = new Il2CppSystem.Collections.Generic.List<InteractablePreset>();
                    if (ignore != null) foreach (var i in ignore) hidden.Add(i);
                }
                hidden.Add(kv.Key);
            }
            if (hidden != null)
            {
                ignore = hidden;
                anyHidden = true;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Stock check failed: " + e.Message);
        }
    }

    [HarmonyPatch(typeof(Human), nameof(Human.PickConsumable))]
    [HarmonyPostfix]
    private static void Picked(Human __instance, InteractablePreset __result)
    {
        if (buyer == null || __result == null || __instance.Pointer != buyer.Pointer) return;
        try
        {
            var price = company.prices.ContainsKey(__result) ? company.prices[__result] : 0;
            Ledger.Count(company, where, price);
            if (business == null) return;
            if (sale == null)
            {
                sale = new Sale { Time = SessionData.Instance.gameTime, Customer = buyer.GetCitizenName() };
                Store.RecordSale(business, sale);
                Loyalty.Served(business, company, buyer);
            }
            sale.Items.Add(new SaleItem { Item = __result.name, Price = price });
            business.Till += price;
            business.TotalTaken += price;
            if (business.Stock.TryGetValue(__result.name, out var n)) business.Stock[__result.name] = Math.Max(0, n - 1);
            if (Plugin.LogEachSale.Value)
                Plugin.Logger.LogInfo($"{business.Name}: {sale.Customer} bought {__result.name} for ¢{price}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't record a sale: " + e.Message);
        }
    }
}

/// <summary>Hourly log summary of purchases city-wide, for tuning prices.</summary>
internal static class Ledger
{
    private static int hour = -1, count, money;

    public static void Count(Company company, Vector3 where, int price)
    {
        var h = Mathf.FloorToInt(SessionData.Instance.gameTime);
        if (h != hour)
        {
            if (hour >= 0) Plugin.Logger.LogInfo($"[Ledger] Hour ending {SessionData.Instance.GameTimeToClock24String(h, false)}: {count} items sold city-wide, ¢{money}");
            hour = h;
            count = money = 0;
        }
        count++;
        money += price;
    }
}

/// <summary>
/// A business you own is never trespassing for you: not after hours, not in staff rooms, and not for loitering
/// (the player's own check, Player.IsTrespassing, adds a loitering rule on top of the general one).
/// </summary>
[HarmonyPatch]
internal static class OwnerAccessPatch
{
    [HarmonyPatch(typeof(Human), nameof(Human.IsTrespassing))]
    [HarmonyPostfix]
    private static void General(Human __instance, NewRoom room, ref int trespassEscalation, ref bool __result)
    {
        if (__instance.isPlayer) Clear(__instance, room, ref trespassEscalation, ref __result);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.IsTrespassing))]
    [HarmonyPostfix]
    private static void PlayerCheck(Player __instance, NewRoom room, ref int trespassEscalation, ref bool __result)
        => Clear(__instance, room, ref trespassEscalation, ref __result);

    private static void Clear(Human who, NewRoom room, ref int trespassEscalation, ref bool result)
    {
        if (who.inAirVent || Store.Get(room?.gameLocation?.thisAsAddress?.company) == null) return;
        result = false;
        trespassEscalation = 0;
    }
}

/// <summary>Nobody starts a mugging against you inside a business you own (muggings stay on the streets).</summary>
[HarmonyPatch(typeof(NewAIController), nameof(NewAIController.IsMuggingValid))]
internal static class NoMuggingAtHomePatch
{
    private static void Postfix(Human target, ref bool __result)
    {
        if (!__result || target == null || !target.isPlayer) return;
        if (Store.Get(target.currentRoom?.gameLocation?.thisAsAddress?.company) != null) __result = false;
    }
}

/// <summary>
/// The game's loitering rules (staff asking you to buy something or leave, then treating you as a trespasser) are
/// switched off by a sync-disk effect. You have that effect while inside a business you own.
/// </summary>
[HarmonyPatch(typeof(UpgradeEffectController), nameof(UpgradeEffectController.GetUpgradeEffect))]
internal static class NoLoiteringPatch
{
    private static void Postfix(SyncDiskPreset.Effect effect, ref float __result)
    {
        if (effect != SyncDiskPreset.Effect.disableLoitering || __result >= 1f) return;
        try
        {
            var company = Player.Instance?.currentGameLocation?.thisAsAddress?.company;
            if (Store.Get(company) != null) __result = 1f;
        }
        catch { }
    }
}

/// <summary>
/// The game stops your watch's time-skip when the business you're standing in closes (to send customers home).
/// In a business you own, the wait carries on to your alarm.
/// </summary>
[HarmonyPatch(typeof(Company), nameof(Company.SetOpen))]
internal static class KeepWaitingAtClosingPatch
{
    private static void Prefix(Company __instance, bool openClosed, ref bool forceActual, out bool __state)
    {
        __state = false;
        try
        {
            // The game only finishes closing while a member of staff is there; once they've all gone home it retries
            // every minute and the place stays unlocked all night. For your businesses, with nobody left but you
            // (customers still inside are trespassing by now), close it as the staff would have.
            if (!openClosed && !forceActual && __instance.openForBusinessActual && __instance.currentStaff.Count == 0 &&
                Store.Get(__instance) != null && __instance.address != null && OnlyOwnerLeft(__instance))
            {
                forceActual = true;
                LightsOut(__instance);
                Plugin.Logger.LogInfo($"{__instance.name}: closed up after hours (no staff left to lock up)");
            }

            var p = Player.Instance;
            __state = !openClosed && p != null && p.spendingTimeMode && Store.Get(__instance) != null &&
                      p.currentGameLocation != null && __instance.address != null &&
                      p.currentGameLocation.Pointer == __instance.address.Pointer;
        }
        catch { }
    }

    private static void Postfix(Company __instance, bool openClosed, bool __state)
    {
        if (!openClosed && Plugin.LogEachSale.Value) LogOccupants(__instance);
        if (!__state) return;
        try
        {
            var p = Player.Instance;
            if (!p.spendingTimeMode && SessionData.Instance.gameTime < p.alarm) p.SetSpendingTimeMode(true);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't keep waiting at closing time: " + e.Message);
        }
    }

    private static bool OnlyOwnerLeft(Company c)
    {
        foreach (var a in c.address.currentOccupants)
            if (a != null && !a.isPlayer && !a.isTrespassing && !a.isAtWork) return false;
        return true;
    }

    /// <summary>
    /// What the last member of staff would have done on the way out: lights off, entrance doors shut and locked.
    /// (You have the keys, and the light switches still work, if you're staying.)
    /// </summary>
    private static void LightsOut(Company c)
    {
        try
        {
            foreach (var room in c.address.rooms)
                if (room != null && room.mainLightStatus) room.SetMainLights(false, "Under New Management: closing up");
            foreach (var entrance in c.address.entrances)
            {
                var door = entrance?.door;
                if (door == null) continue;
                door.SetOpen(0f, null);
                door.SetLocked(true, null, false);
                if (Plugin.LogEachSale.Value) Plugin.Logger.LogInfo($"{c.name}: entrance door to '{entrance.GetOtherGameLocation(c.address)?.name}' " +
                                      $"locked={door.isLocked}, closed={door.isClosed}, lock type={door.preset?.lockType}");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't close up: " + e.Message);
        }
    }

    private static float nextOccupantLog;

    /// <summary>Debug: who's still inside a business of yours as it closes, and how the game classes them (hourly at most).</summary>
    private static void LogOccupants(Company c)
    {
        try
        {
            if (Store.Get(c) == null || c.address == null) return;
            if (SessionData.Instance.gameTime < nextOccupantLog) return;
            nextOccupantLog = SessionData.Instance.gameTime + 1f;
            var lines = new System.Collections.Generic.List<string>();
            foreach (var a in c.address.currentOccupants)
            {
                if (a == null) continue;
                var h = a.TryCast<Human>();
                var who = a.isPlayer ? "YOU" : h != null ? h.GetCitizenName() : a.name;
                var staff = h?.job?.employer != null && h.job.employer.companyID == c.companyID;
                lines.Add($"{who}{(staff ? " (staff)" : "")} atWork={a.isAtWork} trespassing={a.isTrespassing} room='{a.currentRoom?.name}'");
            }
            Plugin.Logger.LogInfo($"[Closing] {c.name} closing at {SessionData.Instance.decimalClock:0.00}: open={c.openForBusinessActual}, staff in={c.currentStaff.Count}, inside: " +
                                  (lines.Count == 0 ? "nobody" : string.Join("; ", lines)));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Closing log failed: " + e.Message);
        }
    }
}

/// <summary>
/// Staff only lock up after closing once everyone left inside is either at work or trespassing. You're never
/// trespassing in a business you own, so to them you were a customer who never leaves: they'd wait for you, the doors
/// stayed unlocked and the business never properly closed. Inside a business you own you count as at work (the player
/// has no job, so the game itself never sets this for you).
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.Update))]
internal static class OwnerAtWorkPatch
{
    private static bool set;

    private static void Postfix(Player __instance)
    {
        try
        {
            var mine = Store.Get(__instance.currentGameLocation?.thisAsAddress?.company) != null;
            if (mine && !__instance.isAtWork)
            {
                __instance.isAtWork = true;
                set = true;
            }
            else if (!mine && set)
            {
                __instance.isAtWork = false;
                set = false;
            }
        }
        catch { }
    }
}
