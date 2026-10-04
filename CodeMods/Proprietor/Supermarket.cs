using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Supermarket shoppers browse shelves with the "ShopForGroceries" action, which in the game is only an animation:
/// nothing is bought. At a supermarket the player owns, a shopper's visit becomes a real purchase of 1-3 items,
/// chosen with the game's own logic (Human.PickConsumable: needs, wealth and taste against the store's prices).
/// The purchase is recorded like any other sale: till, stock, turned-away customers, ledger.
/// </summary>
[HarmonyPatch(typeof(ActionController), nameof(ActionController.ExecuteAction))]
internal static class SupermarketPatch
{
    // One purchase per shopper per visit (they browse several shelves): humanID -> gameTime of their last purchase.
    private static readonly Dictionary<int, float> lastBought = new();

    private static void Postfix(AIActionPreset action, Interactable what, NewNode where, Actor who)
    {
        if (action == null || who == null || who.isPlayer || action.name != "ShopForGroceries") return;
        try
        {
            var company = where?.gameLocation?.thisAsAddress?.company;
            if (Store.Get(company) == null) return;
            var human = who.TryCast<Human>();
            if (human == null) return;
            var now = SessionData.Instance.gameTime;
            if (lastBought.TryGetValue(human.humanID, out var last) && now - last < 1f) return;
            lastBought[human.humanID] = now;

            PurchasePatch.Begin(human, company, what != null ? what.wPos : human.transform.position);
            try
            {
                var prices = company.prices;
                var basket = new Il2CppSystem.Collections.Generic.List<InteractablePreset>();
                var count = UnityEngine.Random.Range(1, 4);
                for (var i = 0; i < count; i++)
                {
                    var picked = human.PickConsumable(ref prices, out _, basket);
                    if (picked == null) break;
                    basket.Add(picked);
                }
            }
            finally
            {
                PurchasePatch.Finish();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Supermarket purchase failed: " + e.Message);
        }
    }
}
