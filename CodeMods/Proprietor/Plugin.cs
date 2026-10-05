using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SOD.Common;

namespace Proprietor;

/// <summary>
/// Own a business. Ask a business's owner to sell it to you; they stay on as manager. Customers' real purchases
/// earn money into the till, use up stock, and you run it all from the "Business Ledger" program on the
/// business's cruncher: collect the till, check stock, order more (delivered in a few days), see every sale.
/// </summary>
[BepInPlugin(Guid, "Under New Management", "1.2.0")]
[BepInDependency("Venomaus.SOD.Common")]
public class Plugin : BasePlugin
{
    public const string Guid = "sodmods.proprietor";
    internal static ManualLogSource Logger;

    internal static ConfigEntry<int> BasePrice, PricePerRegular, MinOrder, MaxOrder, MaxStock, WagePerStaff, MinDeliveryDays, MaxDeliveryDays;
    internal static ConfigEntry<float> WholesaleShare, TransferFee, TransferHour, TypicalMenuPrice, SellBackShare;
    internal static ConfigEntry<int> LowStock, ManagerRaise, DepositStep;
    internal static ConfigEntry<float> DemandPerRegular, DaysOfStock;
    internal static ConfigEntry<bool> LogEachSale;
    internal static ConfigEntry<int> TestStock;
    internal static ConfigEntry<float> TestDeliveryHours;

    public override void Load()
    {
        Logger = Log;
        BasePrice = Config.Bind("Buying", "BasePrice", 1500, "Price of a business before its size is counted, in crows.");
        PricePerRegular = Config.Bind("Buying", "PricePerRegular", 12,
            "Added to the price for each regular customer (citizens whose favourite place it is; a regular for meals counts fully, for snacks or coffee 0.6, for drinks 0.8).");
        TypicalMenuPrice = Config.Bind("Buying", "TypicalMenuPrice", 4f,
            "Average menu price of an ordinary diner. Businesses with pricier menus cost more to buy (and less if cheaper).");
        SellBackShare = Config.Bind("Buying", "SellBackShare", 0.6f,
            "Selling a business back to its manager returns this share of what you paid (0.6 = 60%), plus whatever is in the till.");
        DemandPerRegular = Config.Bind("Stock", "DemandPerRegular", 0.25f,
            "Expected items sold per day for each (weighted) regular customer. Order sizes and starting stock scale with it, so busy places get bigger orders.");
        DaysOfStock = Config.Bind("Stock", "DaysOfStock", 3f,
            "One order covers about this many days of an item's sales; a manager on a raise keeps this many days of recent sales in stock.");
        MinOrder = Config.Bind("Stock", "MinOrder", 10, "Smallest order, for the quietest places.");
        MaxOrder = Config.Bind("Stock", "MaxOrder", 100, "Largest order, for the busiest places.");
        MaxStock = Config.Bind("Stock", "MaxStock", 300, "A manager on a raise never stocks more than this many of one item.");
        WholesaleShare = Config.Bind("Stock", "WholesaleShare", 0.3f, "What stock costs you, as a share of its menu price (0.3 = 30%).");
        MinDeliveryDays = Config.Bind("Stock", "MinDeliveryDays", 1, "Fewest in-game days an order takes to arrive.");
        MaxDeliveryDays = Config.Bind("Stock", "MaxDeliveryDays", 3, "Most in-game days an order takes to arrive.");
        LowStock = Config.Bind("Stock", "LowStockWarning", 5, "The home ledger warns about items with this many or fewer left.");
        TransferFee = Config.Bind("Remote", "TransferFee", 0.1f,
            "Share of the till a courier keeps when you send it home from your apartment's cruncher (0.1 = 10%). 0 = free.");
        TransferHour = Config.Bind("Remote", "TransferArrivalHour", 8f, "Hour of the next morning the courier arrives with the money.");
        ManagerRaise = Config.Bind("Running", "ManagerRaise", 50,
            "Daily raise for a manager who handles restocking (offer it in conversation). Paid with the wages.");
        DepositStep = Config.Bind("Running", "DepositStep", 100, "How much each press of DEPOSIT in the ledger moves from your wallet into the till.");
        WagePerStaff = Config.Bind("Running", "WagePerStaff", 8, "Paid out of the till every in-game day, per job at the business.");
        LogEachSale = Config.Bind("Debug", "LogEachSale", false, "Write every sale at a business to the log.");
        TestStock = Config.Bind("Debug", "TestStock", -1,
            "Testing: when a save loads, set every item at your businesses to this many in stock. -1 = off.");
        TestDeliveryHours = Config.Bind("Debug", "TestDeliveryHours", -1f,
            "Testing: orders arrive this many in-game hours after you place them (orders already on the way are brought forward when a save loads). -1 = off.");

        Lib.SaveGame.OnBeforeLoad += (_, _) => DecorPatch.Return();
        Lib.SaveGame.OnAfterLoad += (_, args) => Store.Load(args);
        // A business lent to the decor editor is never saved as one of your apartments.
        Lib.SaveGame.OnBeforeSave += (_, _) => DecorPatch.Return();
        Lib.SaveGame.OnAfterSave += (_, args) => Store.Save(args);
        Lib.SaveGame.OnBeforeNewGame += (_, _) => { DecorPatch.Return(); Store.Clear(); };
        Lib.Time.OnMinuteChanged += (_, _) => Safely("arrivals", Store.CheckArrivals);
        Lib.Time.OnDayChanged += (_, _) => Safely("wages", Store.PayWages);
        Lib.Time.OnHourChanged += (_, args) => Safely("morning restock", () => Restock.Morning(args.Current.Hour));

        // Patches first, so a problem setting up anything else can't leave the mod half-loaded.
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Dialogs.Register();
        Restock.Register();
        Log.LogInfo("Under New Management loaded");
    }

    private static void Safely(string what, Action action)
    {
        try
        {
            if (SessionData.Instance != null && SessionData.Instance.startedGame) action();
        }
        catch (Exception e)
        {
            Logger.LogError($"Business {what} failed: " + e.Message);
        }
    }

    internal static void Message(string text, bool good = true)
    {
        try
        {
            Lib.GameMessage.Broadcast(text, icon: InterfaceControls.Icon.money,
                color: good ? InterfaceControls.Instance.messageGreen : InterfaceControls.Instance.messageRed);
        }
        catch (Exception e)
        {
            Logger.LogError("Couldn't show message: " + e.Message);
        }
    }
}
