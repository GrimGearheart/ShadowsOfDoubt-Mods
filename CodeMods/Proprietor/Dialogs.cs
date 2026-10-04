using System;
using HarmonyLib;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;

namespace Proprietor;

/// <summary>
/// "I'd like to buy your business." — offered to a business's owner (its director). The asking price goes through
/// the game's own dialog cost, so it is shown on the option, greyed out when you can't afford it, and only taken
/// when the sale goes through.
/// </summary>
internal static class Dialogs
{
    public const string BuyName = "Proprietor_Buy";
    public const string SellName = "Proprietor_Sell";
    public const string StaffBuyName = "Proprietor_BuyViaStaff";
    public const string StaffSellName = "Proprietor_SellViaStaff";

    public static void Register()
    {
        Lib.Dialogs.Builder(BuyName)
            .SetText("I'd like to buy your business.")
            .SetDialogLogic(new BuyLogic(viaStaff: false))
            .ModifyDialogOptions(p =>
            {
                p.cost = 1; // replaced per business by CostPatch
                p.ranking = 5;
            })
            .AddResponse("You've got yourself a deal. I'll stay on and keep the place running for you. The office cruncher's yours too.", isSuccesful: true)
            .AddResponse("Come back when you've got the money.", isSuccesful: false)
            .CreateAndRegister();

        // The owner isn't always in: staff on shift can phone them.
        Lib.Dialogs.Builder(StaffBuyName)
            .SetText("I'd like to buy this business. Can you get the owner on the phone?")
            .SetDialogLogic(new BuyLogic(viaStaff: true))
            .ModifyDialogOptions(p =>
            {
                p.cost = 1; // replaced per business by CostPatch
                p.ranking = 5;
            })
            .AddResponse("Hang on, I'll call the boss... They say you've got a deal. They'll stay on and run the place for you.", isSuccesful: true)
            .AddResponse("The boss says come back when you've got the money.", isSuccesful: false)
            .CreateAndRegister();

        Lib.Dialogs.Builder(StaffSellName)
            .SetText("Can you call the manager? I want to sell the business back to them.")
            .SetDialogLogic(new SellLogic(viaStaff: true))
            .ModifyDialogOptions(p => p.ranking = 4)
            .AddResponse("One moment... They'll take it back. The money's on its way to you.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder(SellName)
            .SetText("I'd like to sell the business back to you.")
            .SetDialogLogic(new SellLogic(viaStaff: false))
            .ModifyDialogOptions(p => p.ranking = 4)
            .AddResponse("If that's what you want. Here's your money; I'll take it from here.", isSuccesful: true)
            .CreateAndRegister();
    }

    /// <summary>The business this citizen owns and could sell, if any.</summary>
    public static Company SellerCompany(Human h)
    {
        if (h == null || h.isPlayer) return null;
        var c = h.job?.employer;
        if (c != null && c.director != null && c.director.humanID == h.humanID) return c;
        return null;
    }

    /// <summary>The business this citizen works at, if they aren't its owner and are on shift there right now.</summary>
    public static Company StaffOnShift(Human h)
    {
        if (h == null || h.isPlayer || !h.isAtWork) return null;
        var c = h.job?.employer;
        if (c == null || c.address == null || c.director == null || c.director.humanID == h.humanID) return null;
        var here = h.currentGameLocation?.thisAsAddress;
        return here != null && here.Pointer == c.address.Pointer ? c : null;
    }

    /// <summary>The business a citizen can sell to the player: its owner, or (via staff) the one they're working in.</summary>
    public static Company ForSale(Human h, bool viaStaff)
    {
        var c = viaStaff ? StaffOnShift(h) : SellerCompany(h);
        return c != null && Store.Sellable(c) ? c : null;
    }

    /// <summary>The player's business this citizen can take back: as its manager, or (via staff) the one they're working in.</summary>
    public static Company Returnable(Human h, bool viaStaff)
    {
        var c = viaStaff ? StaffOnShift(h) : ManagedForPlayer(h);
        return c != null && Store.Get(c) != null ? c : null;
    }

    /// <summary>The business the player owns that this citizen manages, if any.</summary>
    public static Company ManagedForPlayer(Human h)
    {
        if (h == null || h.isPlayer) return null;
        var c = h.job?.employer;
        return c != null && c.director != null && c.director.humanID == h.humanID && Store.Get(c) != null ? c : null;
    }

    private sealed class SellLogic : IDialogLogic
    {
        private readonly bool viaStaff;
        public SellLogic(bool viaStaff) => this.viaStaff = viaStaff;

        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef) => Returnable(saysTo, viaStaff) != null;

        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => Returnable(saysTo, viaStaff) != null ? DialogController.ForceSuccess.success : DialogController.ForceSuccess.fail;

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (!success) return;
            try
            {
                var c = Returnable(saysTo, viaStaff);
                if (c == null) return;
                Store.Sell(c, out var message);
                Plugin.Message(message);
                Plugin.Logger.LogInfo(message);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Selling failed: " + e);
            }
        }
    }

    private sealed class BuyLogic : IDialogLogic
    {
        private readonly bool viaStaff;
        public BuyLogic(bool viaStaff) => this.viaStaff = viaStaff;

        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef) => ForSale(saysTo, viaStaff) != null;

        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
        {
            var c = ForSale(saysTo, viaStaff);
            return c != null && GameplayController.Instance.money >= Store.Price(c)
                ? DialogController.ForceSuccess.success
                : DialogController.ForceSuccess.fail;
        }

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (!success) return;
            try
            {
                var c = ForSale(saysTo, viaStaff);
                if (c == null) return;
                var price = Store.Price(c);
                Store.Buy(c, price);
                // The cruncher's "<Business>_Admin" login uses the owner's passcode: knowing it fills it in.
                if (c.director.passcode != null) GameplayController.Instance.AddPasscode(c.director.passcode, false);
                Plugin.Message($"You now own {c.name}. Log in to its cruncher as {c.shortName}_Admin.");
                Plugin.Logger.LogInfo($"Bought {c.name} (company {c.companyID}) for ¢{price}: regulars {Store.Regulars(c):0.#}, " +
                                      $"land value {(c.address != null ? c.address.normalizedLandValue : 0f):0.00}, menu factor {Store.MenuFactor(c):0.00}");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Buying failed: " + e);
            }
        }
    }
}

/// <summary>Each business has its own asking price.</summary>
[HarmonyPatch(typeof(DialogPreset), nameof(DialogPreset.GetCost))]
internal static class CostPatch
{
    private static void Postfix(DialogPreset __instance, Actor talkingTo, ref int __result)
    {
        if (__instance == null) return;
        var name = __instance.name;
        if (name != Dialogs.BuyName && name != Dialogs.StaffBuyName) return;
        try
        {
            var c = Dialogs.ForSale(talkingTo?.TryCast<Human>(), name == Dialogs.StaffBuyName);
            if (c != null) __result = Store.Price(c);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Price lookup failed: " + e.Message);
        }
    }
}
