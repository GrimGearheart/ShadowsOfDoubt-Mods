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
    /// <summary>Made up for customers the game didn't simulate while you were away (see Trade).</summary>
    public bool Offscreen { get; set; }
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
    /// <summary>The manager has a raise to handle restocking.</summary>
    public bool ManagerRestocks { get; set; }
    /// <summary>Rooms whose ceiling you've painted (their ceiling glow follows the Multiply colour).</summary>
    public List<int> PaintedCeilings { get; set; } = new();
    public List<Sale> Sales { get; set; } = new();
    /// <summary>When a customer last walked out with nothing (new regulars only come after a day without that).</summary>
    public float LastTurnedAway { get; set; } = -1000f;
    /// <summary>Regulars turned away today, as "citizen:errand" → times; settled at midnight.</summary>
    public Dictionary<string, int> Strikes { get; set; } = new();
    /// <summary>For the morning report.</summary>
    public int RegularsGained { get; set; }
    public List<string> RegularsLost { get; set; } = new();
    /// <summary>What the manager will mention in the next morning's report.</summary>
    public List<string> Notes { get; set; } = new();
    public Dictionary<string, int> Delivered { get; set; } = new();
    public bool BadNews { get; set; }
    /// <summary>The last week of the manager's reports (vmails).</summary>
    public List<Report> Reports { get; set; } = new();
}

/// <summary>Owned businesses, saved next to each save game.</summary>
internal static class Store
{
    private const string FileName = "proprietor.json";
    private const int MaxSales = 400;
    public static Dictionary<int, Business> Owned = new();

    public static Business Get(Company c) => c != null && Owned.TryGetValue(c.companyID, out var b) ? b : null;

    public static void Clear()
    {
        LedgerApp.Reset();
        SupermarketPatch.Reset();
        Owned = new Dictionary<int, Business>();
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
            foreach (var b in Owned.Values)
            {
                var c = FindCompany(b.CompanyId);
                GiveKeys(c, false);
                if (c != null)
                    Plugin.Logger.LogInfo($"{b.Name}: {Regulars(c):0} weighted regulars, about {ItemDemand(c):0.#} of each item a day, orders of {OrderSize(c)}; open {c.retailOpenHours.x:0.#}-{c.retailOpenHours.y:0.#}, manager restocks at {Restock.RestockHour(c)}:00");
            }
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
        var start = Plugin.TestStartingStock.Value >= 0 ? Plugin.TestStartingStock.Value : OrderSize(c) * 2;
        foreach (var item in Menu(c)) b.Stock[item.Key.name] = start;
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

    /// <summary>
    /// Foods counted one at a time, which read naturally in the plural ("out of Donuts"). Everything else, drinks and
    /// brand names especially, stays as it is ("out of Kola", "out of Ruby Choice").
    /// </summary>
    private static readonly HashSet<string> Countable = new(StringComparer.OrdinalIgnoreCase)
    {
        "burger", "hamburger", "cheeseburger", "donut", "doughnut", "dog", "hotdog", "sandwich", "pie", "muffin",
        "bagel", "croissant", "cookie", "pretzel", "taco", "burrito", "wrap", "roll", "bun", "cake", "cupcake",
        "waffle", "pancake", "sausage", "skewer", "dumpling", "pastry", "bar", "apple", "banana", "egg", "nugget"
    };

    /// <summary>"Donuts", "Hot Dogs", "Coffee", "Fries".</summary>
    public static string ItemNamePlural(string preset)
    {
        var name = ItemName(preset);
        if (string.IsNullOrEmpty(name)) return name;
        var last = name.Split(' ').Last();
        if (!Countable.Contains(last)) return name;
        if (last.EndsWith("y")) return name[..^1] + "ies";
        if (last.EndsWith("ch") || last.EndsWith("sh")) return name + "es";
        return name + "s";
    }

    public static int UnitCost(int price) => Mathf.Max(1, Mathf.CeilToInt(price * Plugin.WholesaleShare.Value));

    // ---- Selling ----

    public static void RecordSale(Business b, Sale sale)
    {
        b.Sales.Insert(0, sale);
        if (b.Sales.Count > MaxSales) b.Sales.RemoveAt(b.Sales.Count - 1);
    }

    // ---- Ordering ----

    public static bool PlaceOrder(Business b, string item, int price, out string message, bool tillOnly = false)
    {
        var qty = OrderSize(FindCompany(b.CompanyId));
        var cost = UnitCost(price) * qty;
        var fromTill = Math.Min(Math.Max(0, b.Till), cost);
        if (tillOnly && fromTill < cost)
        {
            message = $"The till can't cover the order (¢{cost})";
            return false;
        }
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
        message = $"Ordered {qty} {ItemNamePlural(item)} for ¢{cost}" + (fromWallet > 0 ? $" (¢{fromWallet} from your wallet)" : "") +
                  (days > 0 ? $", arriving in {days} days" : ", arriving soon (test setting)");
        return true;
    }

    // ---- Sending the till home ----

    internal const string Currency = "¢";

    public static int TransferNet(int till) => Math.Max(0, till - Mathf.RoundToInt(Math.Max(0, till) * Plugin.TransferFee.Value));

    /// <summary>
    /// What the till keeps back when you collect: a day's wages (and the manager's raise), so the business can pay
    /// its staff on its own. Stock is bought from the day's takings. Rounded up to 10.
    /// </summary>
    public static int Float(Business b)
    {
        if (!Plugin.KeepFloat.Value) return 0;
        var c = FindCompany(b.CompanyId);
        if (c == null) return 0;
        var need = StaffCount(c) * Plugin.WagePerStaff.Value + (b.ManagerRestocks ? Plugin.ManagerRaise.Value : 0);
        return Mathf.CeilToInt(need / 10f) * 10;
    }

    /// <summary>What COLLECT or TRANSFER takes: the till above its float.</summary>
    public static int Collectable(Business b) => Math.Max(0, b.Till - Float(b));

    /// <summary>A courier takes the till (above its float) to the player: it arrives the next morning, less the courier's fee.</summary>
    public static string Transfer(Business b)
    {
        var take = Collectable(b);
        var net = TransferNet(take);
        var fee = take - net;
        b.Transfers.Add(new Transfer { Amount = net, Arrives = TodayMidnight() + 24f + Plugin.TransferHour.Value });
        b.Till -= take;
        return $"Courier booked: {Currency}{net} arrives tomorrow morning" + (fee > 0 ? $" ({Currency}{fee} fee)" : "");
    }

    // ---- Time passing: deliveries, transfers and wages (driven by SOD.Common's game-clock events) ----

    /// <summary>Every in-game minute: deliveries and courier money that are due arrive.</summary>
    public static void CheckArrivals()
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
            var what = string.Join(", ", due.Select(o => o.Quantity + " " + ItemNamePlural(o.Item)));
            foreach (var o in due) b.Delivered[o.Item] = (b.Delivered.TryGetValue(o.Item, out var d) ? d : 0) + o.Quantity;
            Plugin.Logger.LogInfo($"{b.Name}: delivery arrived ({what})");
        }
    }

    /// <summary>At each in-game midnight: staff are paid.</summary>
    public static void PayWages()
    {
        foreach (var b in Owned.Values)
        {
            var company = FindCompany(b.CompanyId);
            var staff = StaffCount(company);
            var raise = b.ManagerRestocks ? Plugin.ManagerRaise.Value : 0;
            var wages = staff * Plugin.WagePerStaff.Value + raise;
            // The till pays first, then the owner's wallet; only what neither can cover is left as a debt on the till.
            var fromTill = Math.Min(Math.Max(0, b.Till), wages);
            var fromWallet = Math.Min(wages - fromTill, Math.Max(0, GameplayController.Instance.money));
            var unpaid = wages - fromTill - fromWallet;
            b.Till -= fromTill + unpaid;
            if (fromWallet > 0) GameplayController.Instance.AddMoney(-fromWallet, false, "proprietor_wages");
            b.Notes.Add($"Paid the staff {Currency}{wages} last night{(raise > 0 ? " (that's with my raise)" : "")}.");
            if (fromWallet > 0) b.Notes.Add($"The till was short, so {Currency}{fromWallet} of it came out of your pocket.");
            if (unpaid > 0)
            {
                b.Notes.Add($"We still owe {Currency}{unpaid} in wages. The till's in the red.");
                b.BadNews = true;
            }
            Plugin.Logger.LogInfo($"{b.Name}: wages ¢{wages} for {staff} staff: till ¢{fromTill}, wallet ¢{fromWallet}, unpaid ¢{unpaid}; till now ¢{b.Till}");
        }
    }

    /// <summary>
    /// gameTime counts hours since the game began, which wasn't at midnight; the wall clock is decimalClock.
    /// </summary>
    public static float TodayMidnight() => SessionData.Instance.gameTime - SessionData.Instance.decimalClock;


    /// <summary>Whole days from today until the given gameTime (0 = today).</summary>
    public static int DaysUntil(float time) => Mathf.FloorToInt((time - TodayMidnight()) / 24f);

    // ---- How much stock a business needs ----

    /// <summary>Expected sales of one menu item per day, from the business's regular customers.</summary>
    public static float ItemDemand(Company c)
    {
        if (c == null) return 1f;
        var items = Math.Max(1, Menu(c).Count);
        return Math.Max(1f, Regulars(c) * Plugin.DemandPerRegular.Value / items);
    }

    /// <summary>One order: a few days of an item's expected sales, rounded to 5 (busy places order more).</summary>
    public static int OrderSize(Company c)
    {
        var raw = ItemDemand(c) * Plugin.DaysOfStock.Value;
        var rounded = Mathf.RoundToInt(raw / 5f) * 5;
        return Mathf.Clamp(rounded, Plugin.MinOrder.Value, Plugin.MaxOrder.Value);
    }

    /// <summary>
    /// What a manager on a raise keeps of an item (in stock plus on order): a few days of what it actually sold over
    /// the last three days, or one order if it has barely sold.
    /// </summary>
    public static int RestockTarget(Business b, Company c, string item)
    {
        var now = SessionData.Instance.gameTime;
        var sold = b.Sales.Where(s => now - s.Time <= 72f).Sum(s => s.Items.Count(i => i.Item == item));
        var owned = Math.Max(1f, Math.Min(3f, (now - b.BoughtAt) / 24f));
        var perDay = sold / owned;
        var target = Mathf.CeilToInt(perDay * Plugin.DaysOfStock.Value);
        return Mathf.Clamp(target, OrderSize(c), Plugin.MaxStock.Value);
    }

    /// <summary>Jobs actually filled (vacant positions aren't paid).</summary>
    public static int StaffCount(Company c)
    {
        var n = 0;
        if (c?.companyRoster == null) return 0;
        foreach (var job in c.companyRoster)
            if (job?.employee != null) n++;
        return n;
    }

    public static Company FindCompany(int id)
    {
        foreach (var c in CityData.Instance.companyDirectory)
            if (c != null && c.companyID == id) return c;
        return null;
    }
}
