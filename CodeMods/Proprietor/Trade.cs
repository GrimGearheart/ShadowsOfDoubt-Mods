using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Off-screen trade. The game only fully simulates citizens near the player: across town people still eat, but far
/// less often, so a business you aren't standing next to takes a fraction of what it takes with you nearby. Every hour
/// this tops a business up to what it would sell with you around (its regulars × Stock.DemandPerRegular a day, spread
/// over its opening hours by mealtimes), counting everything it really sold in the last day first, so nothing is
/// added while the real customers keep up. Top-up customers are the business's own regulars, buy from its stock, and
/// walk out if nothing they want is left, the same as real ones.
/// </summary>
internal static class Trade
{
    private const int ItemsPerVisit = 3;

    /// <summary>How busy each hour of the day is: breakfast, lunch and dinner rushes, quiet nights.</summary>
    private static readonly float[] HourWeight =
    {
        0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.6f, 2f, 2f, 2f, 1f, 1f,
        1.6f, 1.6f, 0.8f, 0.8f, 0.8f, 1f, 1.5f, 1.5f, 1.5f, 0.7f, 0.7f, 0.7f
    };

    public static void Hourly(int hour)
    {
        if (Plugin.DemandPerRegular.Value <= 0f) return;
        var now = SessionData.Instance.gameTime;
        foreach (var b in Store.Owned.Values)
        {
            var c = Store.FindCompany(b.CompanyId);
            if (c?.address == null || !c.openForBusinessDesired || Store.StaffCount(c) == 0) continue;
            try
            {
                TopUp(b, c, hour, now);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Off-screen trade at {b.Name} failed: " + e.Message);
            }
        }
    }

    private static void TopUp(Business b, Company c, int hour, float now)
    {
        var perDay = Store.Regulars(c) * Plugin.DemandPerRegular.Value;
        if (perDay <= 0f) return;

        // What it should have sold over the last day (only hours it was open, and only since you bought it), and what
        // it has sold, real customers and earlier top-ups alike.
        var openWeight = Enumerable.Range(0, 24).Where(h => OpenAt(c, h)).Sum(h => HourWeight[h]);
        if (openWeight <= 0f) return;
        var expected = 0f;
        for (var back = 0; back < 24; back++)
        {
            if (now - back < b.BoughtAt) break;
            var h = ((hour - back) % 24 + 24) % 24;
            if (OpenAt(c, h)) expected += perDay * HourWeight[h] / openWeight;
        }
        var sold = b.Sales.Where(s => now - s.Time <= 24f).Sum(s => s.Items.Count);
        // At most two hours' worth at once, so a long gap doesn't land as one rush.
        var thisHour = perDay * HourWeight[hour] / openWeight;
        var missing = Mathf.Min(expected - sold, thisHour * 2f);
        var visits = Mathf.FloorToInt(missing / ItemsPerVisit + UnityEngine.Random.value);
        if (visits <= 0) return;

        var regulars = Regulars(c);
        if (regulars.Count == 0) return;
        var menu = Store.Menu(c);
        int served = 0, takings = 0, away = 0;
        for (var i = 0; i < visits; i++)
        {
            var who = regulars[UnityEngine.Random.Range(0, regulars.Count)];
            var inStock = menu.Where(kv => b.Stock.TryGetValue(kv.Key.name, out var n) && n > 0).ToList();
            if (inStock.Count == 0)
            {
                b.TurnedAway++;
                Loyalty.TurnedAway(b, c, who);
                away++;
                continue;
            }
            var sale = new Sale { Time = now, Customer = who.GetCitizenName(), Offscreen = true };
            for (var k = 0; k < ItemsPerVisit && inStock.Count > 0; k++)
            {
                var pick = inStock[UnityEngine.Random.Range(0, inStock.Count)];
                var item = pick.Key;
                var price = pick.Value;
                sale.Items.Add(new SaleItem { Item = item.name, Price = price });
                b.Till += price;
                b.TotalTaken += price;
                var left = b.Stock[item.name] - 1;
                b.Stock[item.name] = left;
                if (left <= 0) inStock.Remove(pick);
                takings += price;
            }
            Store.RecordSale(b, sale);
            served++;
        }
        Plugin.Logger.LogInfo($"{b.Name}: off-screen trade at {hour}:00, {served} customer(s) for ¢{takings}" +
                              (away > 0 ? $", {away} turned away" : "") + $" (day so far {sold}/{expected:0} items)");
    }

    /// <summary>Whether the business's opening hours include this hour of the day.</summary>
    private static bool OpenAt(Company c, int h)
    {
        var open = c.retailOpenHours.x % 24f;
        var close = c.retailOpenHours.y % 24f;
        if (c.retailOpenHours.y - c.retailOpenHours.x >= 23.5f || Mathf.Approximately(open, close)) return true;
        return open < close ? h >= open && h < close : h >= open || h < close;
    }

    private static List<Human> Regulars(Company c)
    {
        var list = new List<Human>();
        foreach (var h in c.address.favouredCustomers)
            if (h != null && !h.isDead && (h.job?.employer == null || h.job.employer.companyID != c.companyID)) list.Add(h);
        return list;
    }
}
