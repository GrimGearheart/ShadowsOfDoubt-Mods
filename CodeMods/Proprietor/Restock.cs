using System;
using System.Collections.Generic;
using System.Linq;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// Restocking by the manager, for a raise. Offer the manager (in person, or by phone through staff on shift) a raise
/// to handle the restocking: from then on the raise is paid every day with the wages, and every morning the manager
/// tops up anything running low, paid from the till only. Every morning you also get a stock report for any business
/// that's running low or out.
/// </summary>
internal static class Restock
{
    public const string HireName = "Proprietor_RaiseManager";
    public const string HireViaStaffName = "Proprietor_RaiseManagerViaStaff";
    public const string StopName = "Proprietor_EndRaise";
    public const string StopViaStaffName = "Proprietor_EndRaiseViaStaff";
    private const int ReportHour = 7;

    public static void Register()
    {
        Lib.Dialogs.Builder(HireName)
            .SetText("I'll give you a raise if you handle the restocking.")
            .SetDialogLogic(new RaiseLogic(viaStaff: false, start: true))
            .ModifyDialogOptions(p => p.ranking = 3)
            .AddResponse("Fine. I'll keep the shelves stocked. Don't expect miracles if the till's empty.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder(HireViaStaffName)
            .SetText("Can you get the manager on the phone? I want to offer them a raise to handle the restocking.")
            .SetDialogLogic(new RaiseLogic(viaStaff: true, start: true))
            .ModifyDialogOptions(p => p.ranking = 3)
            .AddResponse("Hang on... They say fine, they'll keep the shelves stocked. As long as the till can cover it.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder(StopName)
            .SetText("You can go back to your old pay. I'll handle the restocking myself.")
            .SetDialogLogic(new RaiseLogic(viaStaff: false, start: false))
            .ModifyDialogOptions(p => p.ranking = 3)
            .AddResponse("Suit yourself.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder(StopViaStaffName)
            .SetText("Can you call the manager? Tell them they can go back to their old pay; I'll do the restocking.")
            .SetDialogLogic(new RaiseLogic(viaStaff: true, start: false))
            .ModifyDialogOptions(p => p.ranking = 3)
            .AddResponse("Okay... They said suit yourself.", isSuccesful: true)
            .CreateAndRegister();
    }

    private sealed class RaiseLogic : IDialogLogic
    {
        private readonly bool viaStaff, start;

        public RaiseLogic(bool viaStaff, bool start)
        {
            this.viaStaff = viaStaff;
            this.start = start;
        }

        private Business Target(Human h)
        {
            var b = Store.Get(Dialogs.Returnable(h, viaStaff));
            return b != null && b.ManagerRestocks != start ? b : null;
        }

        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef) => Target(saysTo) != null;

        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => Target(saysTo) != null ? DialogController.ForceSuccess.success : DialogController.ForceSuccess.fail;

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (!success) return;
            var b = Target(saysTo);
            if (b == null) return;
            b.ManagerRestocks = start;
            var text = start
                ? $"{b.Name}: the manager now handles restocking (raise ¢{Plugin.ManagerRaise.Value} a day, paid with the wages)"
                : $"{b.Name}: you handle the restocking again; the manager's raise ends";
            Plugin.Message(text);
            Plugin.Logger.LogInfo(text);
        }
    }

    /// <summary>
    /// An hour before closing (11pm for places that never close), a manager on a raise orders whatever is running
    /// low, paid from the day's takings in the till. It goes in the next morning's report.
    /// </summary>
    public static void BeforeClosing(int hour)
    {
        foreach (var b in Store.Owned.Values)
        {
            if (!b.ManagerRestocks) continue;
            var company = Store.FindCompany(b.CompanyId);
            if (company == null || hour != RestockHour(company)) continue;
            var spent = 0;
            var ordered = new List<string>();
            var couldNotAfford = false;
            // The manager never spends the money set aside for wages.
            var keep = Store.Float(b);
            foreach (var kv in Store.Menu(company))
            {
                var name = kv.Key.name;
                var target = Store.RestockTarget(b, company, name);
                while (Level(b, name) < target)
                {
                    var cost = Store.UnitCost(kv.Value) * Store.OrderSize(company);
                    if (b.Till - cost < keep) { couldNotAfford = true; break; }
                    Store.PlaceOrder(b, name, kv.Value, out _, tillOnly: true);
                    spent += cost;
                    if (!ordered.Contains(name)) ordered.Add(name);
                }
            }
            if (spent > 0) b.Notes.Add($"Ordered more {List(ordered)} before closing, {Store.Currency}{spent} from the till.");
            if (couldNotAfford)
            {
                b.Notes.Add("Couldn't order everything we need without touching the wage money.");
                b.BadNews = true;
            }
            Plugin.Logger.LogInfo($"{b.Name}: manager restocked at {hour}:00 for ¢{spent}{(couldNotAfford ? " (till ran short)" : "")}; till now ¢{b.Till}");
        }
    }

    /// <summary>The hour before the business closes; 11pm for places open round the clock.</summary>
    public static int RestockHour(Company c)
    {
        var open = c.retailOpenHours.x;
        var close = c.retailOpenHours.y;
        if (close - open >= 23.5f || Mathf.Approximately(open % 24f, close % 24f)) return 23;
        return ((Mathf.CeilToInt(close) - 1) % 24 + 24) % 24;
    }

    /// <summary>
    /// Every morning each manager vmails you a report (wages, deliveries, last night's restocking, what's low, how
    /// the regulars seem). One message tells you the reports are in.
    /// </summary>
    public static void Morning(int hour)
    {
        if (hour != ReportHour) return;
        var sent = 0;
        var anyBad = false;
        foreach (var b in Store.Owned.Values)
        {
            var company = Store.FindCompany(b.CompanyId);
            if (company == null) continue;
            var menu = Store.Menu(company);
            var lines = new List<string>(b.Notes);
            var bad = b.BadNews;

            if (b.Delivered.Count > 0)
                lines.Add("Deliveries came in: " + string.Join(", ", b.Delivered.Select(kv => $"{kv.Value} {Store.ItemNamePlural(kv.Key)}")) + ".");

            var items = menu.Select(kv => kv.Key.name).ToList();
            var outOf = items.Where(n => Stock(b, n) <= 0 && !OnOrder(b, n)).ToList();
            var outComing = items.Where(n => Stock(b, n) <= 0 && OnOrder(b, n)).ToList();
            var running = items.Where(n => Stock(b, n) > 0 && Stock(b, n) <= Plugin.LowStock.Value && !OnOrder(b, n)).ToList();
            if (outOf.Count > 0)
            {
                lines.Add($"We're out of {List(outOf)}.");
                bad = true;
            }
            if (outComing.Count > 0) lines.Add($"Out of {List(outComing)}, but more is on the way.");
            if (running.Count > 0) lines.Add($"Running low on {List(running)}.");

            var regulars = Loyalty.Report(b);
            if (regulars != null) lines.Add(regulars);
            if (lines.Count == 0) lines.Add("Quiet one. Nothing much to report.");

            var signature = Reports.Manager(company)?.GetFirstName();
            var body = "Boss,\n\n" + string.Join("\n", lines) + $"\n\nTill's at {Store.Currency}{b.Till}." +
                       (signature != null ? $"\n\n- {signature}" : "");
            Reports.Send(b, company, body);
            Plugin.Logger.LogInfo($"Morning report from {b.Name}: " + string.Join(" ", lines));

            b.Notes.Clear();
            b.Delivered.Clear();
            b.BadNews = false;
            sent++;
            anyBad |= bad;
        }
        if (sent > 0)
            Plugin.Message(sent == 1 ? "Your manager's morning report is in your vmail." : "Your managers' morning reports are in your vmail.", !anyBad);
    }

    /// <summary>"Fries", "Fries and Coffee", "Fries, Coffee and Donuts".</summary>
    private static string List(List<string> items)
    {
        var names = items.Select(Store.ItemNamePlural).ToList();
        return names.Count <= 1 ? names.FirstOrDefault() ?? "" : string.Join(", ", names.Take(names.Count - 1)) + " and " + names.Last();
    }

    private static int Stock(Business b, string item) => b.Stock.TryGetValue(item, out var n) ? n : 0;
    private static bool OnOrder(Business b, string item) => b.Orders.Any(o => o.Item == item);
    private static int Level(Business b, string item) => Stock(b, item) + b.Orders.Where(o => o.Item == item).Sum(o => o.Quantity);
}
