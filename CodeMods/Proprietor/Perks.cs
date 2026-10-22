using System;
using HarmonyLib;

namespace Proprietor;

/// <summary>
/// On the house: at a business you own, anything on its menu that's in stock is free for you. It still comes out of
/// the stock, so a hungry owner costs the business a little.
/// </summary>
[HarmonyPatch]
internal static class OnTheHousePatch
{
    [HarmonyPatch(typeof(ShopSelectButtonController), nameof(ShopSelectButtonController.Setup))]
    [HarmonyPrefix]
    private static void Price(InteractablePreset newPreset, ref int newPrice, BuyInterfaceController newBuyController, bool newSellMode)
    {
        if (Free(newPreset, newBuyController?.company, newSellMode) != null) newPrice = 0;
    }

    [HarmonyPatch(typeof(ShopSelectButtonController), nameof(ShopSelectButtonController.Setup))]
    [HarmonyPostfix]
    private static void Label(ShopSelectButtonController __instance)
    {
        try
        {
            if (Free(__instance.preset, __instance.buyController?.company, __instance.sellMode) != null && __instance.priceText != null)
                __instance.priceText.text = "Free";
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't label a free item: " + e.Message);
        }
    }

    [HarmonyPatch(typeof(ShopSelectButtonController), nameof(ShopSelectButtonController.PurchaseExecute))]
    [HarmonyPrefix]
    private static void TakeFromStock(ShopSelectButtonController __instance)
    {
        try
        {
            var b = Free(__instance.preset, __instance.buyController?.company, __instance.sellMode);
            if (b == null || __instance.price != 0) return;
            var name = __instance.preset.name;
            b.Stock[name] = Math.Max(0, b.Stock[name] - 1);
            Plugin.Logger.LogInfo($"{b.Name}: {Store.ItemName(name)} on the house for the boss ({b.Stock[name]} left)");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't take a free item from stock: " + e.Message);
        }
    }

    /// <summary>The business giving this away, if it's yours, it's on the menu and it's in stock.</summary>
    private static Business Free(InteractablePreset item, Company c, bool selling)
    {
        if (!Plugin.FreeForOwner.Value || selling || item == null) return null;
        var b = Store.Get(c);
        if (b == null || !b.Stock.TryGetValue(item.name, out var n) || n <= 0) return null;
        return c.prices != null && c.prices.ContainsKey(item) ? b : null;
    }
}
