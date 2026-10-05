using System;
using System.Collections.Generic;
using System.Linq;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;

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

    /// <summary>Every morning: managers on a raise restock, and you get a report of what's still low or out.</summary>
    public static void Morning(int hour)
    {
        if (hour != ReportHour) return;
        foreach (var b in Store.Owned.Values)
        {
            var company = Store.FindCompany(b.CompanyId);
            if (company == null) continue;
            var menu = Store.Menu(company);
            var notes = new List<string>();

            if (b.ManagerRestocks)
            {
                var spent = 0;
                var couldNotAfford = false;
                foreach (var kv in menu)
                {
                    var name = kv.Key.name;
                    var target = Store.RestockTarget(b, company, name);
                    while (Level(b, name) < target)
                    {
                        var cost = Store.UnitCost(kv.Value) * Store.OrderSize(company);
                        if (b.Till < cost) { couldNotAfford = true; break; }
                        Store.PlaceOrder(b, name, kv.Value, out _, tillOnly: true);
                        spent += cost;
                    }
                }
                if (spent > 0) notes.Add($"manager ordered stock for ¢{spent}");
                if (couldNotAfford) notes.Add("the till couldn't cover all the restocking");
            }

            var low = menu.Select(kv => kv.Key.name).ToList();
            var outOf = low.Count(n => Stock(b, n) <= 0 && !OnOrder(b, n));
            var running = low.Count(n => Stock(b, n) > 0 && Stock(b, n) <= Plugin.LowStock.Value && !OnOrder(b, n));
            if (outOf > 0) notes.Add($"{outOf} item{(outOf == 1 ? "" : "s")} out of stock");
            if (running > 0) notes.Add($"{running} running low");

            if (notes.Count == 0) continue;
            var text = $"{b.Name}: " + string.Join(", ", notes);
            Plugin.Message(text, outOf == 0 && running == 0);
            Plugin.Logger.LogInfo("Morning report: " + text);
        }
    }

    private static int Stock(Business b, string item) => b.Stock.TryGetValue(item, out var n) ? n : 0;
    private static bool OnOrder(Business b, string item) => b.Orders.Any(o => o.Item == item);
    private static int Level(Business b, string item) => Stock(b, item) + b.Orders.Where(o => o.Item == item).Sum(o => o.Quantity);
}
