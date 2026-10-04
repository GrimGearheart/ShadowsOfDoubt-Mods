using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SOD.Common;
using SOD.Common.Helpers;
using UnityEngine;

namespace Proprietor;

public class Sale
{
    public float Time { get; set; }
    public string Customer { get; set; }
    public List<SaleItem> Items { get; set; } = new();
    public int Total => Items.Sum(i => i.Price);
}

public class SaleItem
{
    public string Item { get; set; }
    public int Price { get; set; }
}

public class Transfer
{
    public int Amount { get; set; }
    public float Arrives { get; set; }
}

public class Order
{
    public string Item { get; set; }
    public int Quantity { get; set; }
    public float Arrives { get; set; }
}

/// <summary>A business the player owns.</summary>
public class Business
{
    public int CompanyId { get; set; }
    public string Name { get; set; }
    public int PricePaid { get; set; }
    public float BoughtAt { get; set; }
    public int Till { get; set; }
    public int TotalTaken { get; set; }
    public int TurnedAway { get; set; }
    public Dictionary<string, int> Stock { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
    public List<Transfer> Transfers { get; set; } = new();
    public List<Sale> Sales { get; set; } = new();
}

/// <summary>Owned businesses, saved next to each save game.</summary>
internal static class Store
{
    private const string FileName = "proprietor.json";
    private const int MaxSales = 150;
    public static Dictionary<int, Business> Owned = new();
    private static int lastDay = -1;

    public static Business Get(Company c) => c != null && Owned.TryGetValue(c.companyID, out var b) ? b : null;

    public static void Clear()
    {
        Owned = new Dictionary<int, Business>();
        lastDay = -1;
    }

    public static void Load(SaveGameArgs args)
    {
        Clear();
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (File.Exists(path))
                Owned = JsonSerializer.Deserialize<List<Business>>(File.ReadAllText(path)).ToDictionary(b => b.CompanyId);
            Plugin.Logger.LogInfo($"Save loaded: {Owned.Count} business(es) owned");
            foreach (var b in Owned.Values) GiveKeys(FindCompany(b.CompanyId), false);
            ShopProbe.DumpGoals();
            if (Plugin.TestStock.Value >= 0)
                foreach (var b in Owned.Values)
                {
                    foreach (var item in b.Stock.Keys.ToList()) b.Stock[item] = Plugin.TestStock.Value;
                    Plugin.Logger.LogInfo($"Test: {b.Name} stock set to {Plugin.TestStock.Value} of each item");
                }
            if (Plugin.TestDeliveryHours.Value >= 0f)
            {
                var soon = SessionData.Instance.gameTime + Plugin.TestDeliveryHours.Value;
                foreach (var b in Owned.Values)
                    foreach (var o in b.Orders)
                        if (o.Arrives > soon) o.Arrives = soon;
                Plugin.Logger.LogInfo($"Test: orders on the way now arrive within {Plugin.TestDeliveryHours.Value} hours");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't load owned businesses: " + e);
        }
    }

    public static void Save(SaveGameArgs args)
    {
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (Owned.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            File.WriteAllText(path, JsonSerializer.Serialize(Owned.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't save owned businesses: " + e);
        }
    }

    // ---- Buying ----

    public static bool Sellable(Company c) =>
        c != null && c.director != null && !c.director.isPlayer && c.prices != null && c.prices.Count > 0 &&
        c.address != null && !Owned.ContainsKey(c.companyID) && HasCustomers(c);

    /// <summary>
    /// The errands that bring customers often enough to run a business on, and how much a regular customer for each is
    /// worth. The game has no errand for retail, washing or medical, and grocery runs are rare, so those businesses
    /// aren't for sale.
    /// </summary>
    private static readonly (CompanyPreset.CompanyCategory Category, float Weight)[] CustomerCategories =
    {
        (CompanyPreset.CompanyCategory.meal, 1f), (CompanyPreset.CompanyCategory.snack, 0.6f),
        (CompanyPreset.CompanyCategory.caffeine, 0.6f), (CompanyPreset.CompanyCategory.recreational, 0.8f)
    };

    public static bool HasCustomers(Company c)
    {
        var categories = c.preset?.companyCategories;
        if (categories == null) return false;
        foreach (var (category, _) in CustomerCategories)
            if (categories.Contains(category)) return true;
        return false;
    }

    /// <summary>
    /// Regular customers: citizens whose favourite place for a meal, snack, coffee or a drink is this business,
    /// weighted by how much that errand is worth.
    /// </summary>
    public static float Regulars(Company c)
    {
        var address = c.address;
        if (address == null) return 0f;
        var score = 0f;
        foreach (var human in address.favouredCustomers)
        {
            if (human == null) continue;
            foreach (var (category, weight) in CustomerCategories)
                if (human.favouritePlaces.TryGetValue(category, out var fav) && fav != null && fav.Pointer == address.Pointer)
                    score += weight;
        }
        return score;
    }

    /// <summary>
    /// Asking price: regular customers × area (the address's land value, 0.7–1.5×) × menu (how expensive its menu is
    /// compared to a typical diner, 0.7–2×; upscale places charge more and earn more).
    /// </summary>
    public static int Price(Company c)
    {
        var lv = Mathf.Clamp01(c.address?.normalizedLandValue ?? 0.5f);
        var raw = (Plugin.BasePrice.Value + Plugin.PricePerRegular.Value * Regulars(c)) * (0.7f + 0.8f * lv) * MenuFactor(c);
        return Mathf.Max(50, Mathf.RoundToInt(raw / 50f) * 50);
    }

    public static float MenuFactor(Company c)
    {
        var menu = Menu(c);
        if (menu.Count == 0) return 1f;
        var average = (float)menu.Average(kv => kv.Value);
        return Mathf.Clamp(Mathf.Sqrt(average / Plugin.TypicalMenuPrice.Value), 0.7f, 2f);
    }

    public static Business Buy(Company c, int price)
    {
        var b = new Business
        {
            CompanyId = c.companyID, Name = c.name, PricePaid = price, BoughtAt = SessionData.Instance.gameTime
        };
        foreach (var item in Menu(c)) b.Stock[item.Key.name] = Plugin.StartingStock.Value;
        Owned[c.companyID] = b;
        GiveKeys(c, true);
        return b;
    }

    /// <summary>
    /// Sells a business back to its manager: a share of the price paid, plus the till and any money a courier is
    /// still carrying (a negative till is taken off). Stock on order is lost.
    /// </summary>
    public static int Sell(Company c, out string message)
    {
        var b = Get(c);
        var back = Mathf.RoundToInt(b.PricePaid * Plugin.SellBackShare.Value);
        var total = Math.Max(0, back + b.Till + b.Transfers.Sum(t => t.Amount));
        Owned.Remove(b.CompanyId);
        if (total > 0) GameplayController.Instance.AddMoney(total, true, "proprietor_sell");
        message = $"You sold {b.Name} for {Currency}{total}" +
                  (total > back ? $" ({Currency}{back} for the business, plus {Currency}{total - back} from the till)"
                   : total < back ? $" ({Currency}{back} for the business, less {Currency}{back - total} the till owed)" : "");
        return total;
    }

    /// <summary>The owner gets keys to every door of the business.</summary>
    public static void GiveKeys(Company c, bool message)
    {
        try
        {
            if (c?.address != null && Player.Instance != null) Player.Instance.AddToKeyring(c.address, message);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't hand over the keys: " + e.Message);
        }
    }

    /// <summary>The business's menu: each item it sells and its price.</summary>
    public static List<KeyValuePair<InteractablePreset, int>> Menu(Company c)
    {
        var list = new List<KeyValuePair<InteractablePreset, int>>();
        foreach (var kv in c.prices)
            if (kv.Key != null) list.Add(new KeyValuePair<InteractablePreset, int>(kv.Key, kv.Value));
        return list.OrderBy(kv => ItemName(kv.Key.name)).ToList();
    }

    public static string ItemName(string preset) => Strings.Get("evidence.names", preset);

    public static int UnitCost(int price) => Mathf.Max(1, Mathf.CeilToInt(price * Plugin.WholesaleShare.Value));

    // ---- Selling ----

    public static void RecordSale(Business b, Sale sale)
    {
        b.Sales.Insert(0, sale);
        if (b.Sales.Count > MaxSales) b.Sales.RemoveAt(b.Sales.Count - 1);
    }

    // ---- Ordering ----

    public static bool PlaceOrder(Business b, string item, int price, out string message)
    {
        var qty = Plugin.OrderSize.Value;
        var cost = UnitCost(price) * qty;
        var fromTill = Math.Min(b.Till, cost);
        var fromWallet = cost - fromTill;
        if (fromWallet > GameplayController.Instance.money)
        {
            message = $"Not enough money: the order costs ¢{cost}";
            return false;
        }
        b.Till -= fromTill;
        if (fromWallet > 0) GameplayController.Instance.AddMoney(-fromWallet, true, "proprietor_order");
        var days = UnityEngine.Random.Range(Plugin.MinDeliveryDays.Value, Plugin.MaxDeliveryDays.Value + 1);
        // Arrive during the morning of the delivery day.
        var arrives = TodayMidnight() + days * 24f + UnityEngine.Random.Range(7f, 11f);
        if (Plugin.TestDeliveryHours.Value >= 0f)
        {
            arrives = SessionData.Instance.gameTime + Plugin.TestDeliveryHours.Value;
            days = 0;
        }
        b.Orders.Add(new Order { Item = item, Quantity = qty, Arrives = arrives });
        message = $"Ordered {qty} {ItemName(item)} for ¢{cost}" + (fromWallet > 0 ? $" (¢{fromWallet} from your wallet)" : "") +
                  (days > 0 ? $", arriving in {days} days" : ", arriving soon (test setting)");
        return true;
    }

    // ---- Sending the till home ----

    private const string Currency = "¢";

    public static int TransferNet(int till) => Math.Max(0, till - Mathf.RoundToInt(Math.Max(0, till) * Plugin.TransferFee.Value));

    /// <summary>A courier takes the till to the player: it arrives the next morning, less the courier's fee.</summary>
    public static string Transfer(Business b)
    {
        var net = TransferNet(b.Till);
        var fee = b.Till - net;
        b.Transfers.Add(new Transfer { Amount = net, Arrives = TodayMidnight() + 24f + Plugin.TransferHour.Value });
        b.Till = 0;
        return $"Courier booked: {Currency}{net} arrives tomorrow morning" + (fee > 0 ? $" ({Currency}{fee} fee)" : "");
    }

    // ---- Time passing: deliveries, transfers and wages ----

    public static void Tick()
    {
        if (Owned.Count == 0 || SessionData.Instance == null) return;
        var now = SessionData.Instance.gameTime;
        foreach (var b in Owned.Values)
        {
            var paid = b.Transfers.Where(t => t.Arrives <= now).ToList();
            foreach (var t in paid)
            {
                GameplayController.Instance.AddMoney(t.Amount, true, "proprietor_transfer");
                b.Transfers.Remove(t);
                Plugin.Message($"{b.Name}: courier delivered {Currency}{t.Amount}");
                Plugin.Logger.LogInfo($"{b.Name}: transfer of ¢{t.Amount} delivered");
            }

            var due = b.Orders.Where(o => o.Arrives <= now).ToList();
            if (due.Count == 0) continue;
            foreach (var o in due)
            {
                b.Stock[o.Item] = (b.Stock.TryGetValue(o.Item, out var s) ? s : 0) + o.Quantity;
                b.Orders.Remove(o);
            }
            var what = string.Join(", ", due.Select(o => o.Quantity + " " + ItemName(o.Item)));
            Plugin.Message($"{b.Name}: delivery arrived ({what})");
            Plugin.Logger.LogInfo($"{b.Name}: delivery arrived ({what})");
        }

        var day = DayNumber();
        if (lastDay < 0) { lastDay = day; return; }
        if (day == lastDay) return;
        for (var d = lastDay; d < day; d++) PayWages();
        lastDay = day;
    }

    private static void PayWages()
    {
        foreach (var b in Owned.Values)
        {
            var company = FindCompany(b.CompanyId);
            var staff = company?.companyRoster?.Count ?? 0;
            var wages = staff * Plugin.WagePerStaff.Value;
            // The till pays first, then the owner's wallet; only what neither can cover is left as a debt on the till.
            var fromTill = Math.Min(Math.Max(0, b.Till), wages);
            var fromWallet = Math.Min(wages - fromTill, Math.Max(0, GameplayController.Instance.money));
            var unpaid = wages - fromTill - fromWallet;
            b.Till -= fromTill + unpaid;
            if (fromWallet > 0) GameplayController.Instance.AddMoney(-fromWallet, false, "proprietor_wages");
            var detail = fromWallet > 0 ? $" (¢{fromWallet} from your wallet)" : "";
            if (unpaid > 0) detail += $", ¢{unpaid} owed from the till";
            Plugin.Message($"{b.Name}: paid ¢{wages} in wages{detail}. Till: ¢{b.Till}", unpaid == 0);
            Plugin.Logger.LogInfo($"{b.Name}: wages ¢{wages} for {staff} staff: till ¢{fromTill}, wallet ¢{fromWallet}, unpaid ¢{unpaid}; till now ¢{b.Till}");
        }
    }

    /// <summary>
    /// gameTime counts hours since the game began, which wasn't at midnight; the wall clock is decimalClock.
    /// </summary>
    public static float TodayMidnight() => SessionData.Instance.gameTime - SessionData.Instance.decimalClock;

    public static int DayNumber() => Mathf.RoundToInt(TodayMidnight() / 24f);

    /// <summary>Whole days from today until the given gameTime (0 = today).</summary>
    public static int DaysUntil(float time) => Mathf.FloorToInt((time - TodayMidnight()) / 24f);

    public static Company FindCompany(int id)
    {
        foreach (var c in CityData.Instance.companyDirectory)
            if (c != null && c.companyID == id) return c;
        return null;
    }
}
