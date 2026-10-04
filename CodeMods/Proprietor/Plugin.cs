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
[BepInPlugin(Guid, "Under New Management", "1.0.0")]
[BepInDependency("Venomaus.SOD.Common")]
public class Plugin : BasePlugin
{
    public const string Guid = "sodmods.proprietor";
    internal static ManualLogSource Logger;

    internal static ConfigEntry<int> BasePrice, PricePerRegular, StartingStock, OrderSize, WagePerStaff, MinDeliveryDays, MaxDeliveryDays;
    internal static ConfigEntry<float> WholesaleShare, TransferFee, TransferHour, TypicalMenuPrice, SellBackShare;
    internal static ConfigEntry<int> LowStock;
    internal static ConfigEntry<bool> LogEachSale, ProbeShops;
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
        StartingStock = Config.Bind("Stock", "StartingStock", 20, "How many of each menu item a business has when you buy it.");
        OrderSize = Config.Bind("Stock", "OrderSize", 10, "How many of an item one order brings.");
        WholesaleShare = Config.Bind("Stock", "WholesaleShare", 0.3f, "What stock costs you, as a share of its menu price (0.3 = 30%).");
        MinDeliveryDays = Config.Bind("Stock", "MinDeliveryDays", 2, "Fewest in-game days an order takes to arrive.");
        MaxDeliveryDays = Config.Bind("Stock", "MaxDeliveryDays", 6, "Most in-game days an order takes to arrive.");
        LowStock = Config.Bind("Stock", "LowStockWarning", 5, "The home ledger warns about items with this many or fewer left.");
        TransferFee = Config.Bind("Remote", "TransferFee", 0.1f,
            "Share of the till a courier keeps when you send it home from your apartment's cruncher (0.1 = 10%). 0 = free.");
        TransferHour = Config.Bind("Remote", "TransferArrivalHour", 8f, "Hour of the next morning the courier arrives with the money.");
        WagePerStaff = Config.Bind("Running", "WagePerStaff", 8, "Paid out of the till every in-game day, per job at the business.");
        LogEachSale = Config.Bind("Debug", "LogEachSale", false, "Write every sale at a business to the log.");
        ProbeShops = Config.Bind("Debug", "ProbeShops", false,
            "Research: log the game's shopping goals and what citizens do inside businesses you own.");
        TestStock = Config.Bind("Debug", "TestStock", -1,
            "Testing: when a save loads, set every item at your businesses to this many in stock. -1 = off.");
        TestDeliveryHours = Config.Bind("Debug", "TestDeliveryHours", -1f,
            "Testing: orders arrive this many in-game hours after you place them (orders already on the way are brought forward when a save loads). -1 = off.");

        Lib.SaveGame.OnAfterLoad += (_, args) => Store.Load(args);
        Lib.SaveGame.OnAfterSave += (_, args) => Store.Save(args);
        Lib.SaveGame.OnBeforeNewGame += (_, _) => Store.Clear();

        Dialogs.Register();
        BossGreetingPatch.Register();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Under New Management loaded");
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
