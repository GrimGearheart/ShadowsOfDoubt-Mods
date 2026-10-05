using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// The Business Ledger program: a copy of the game's Sales Records program (same screen and list) with our rows.
/// It appears on crunchers in businesses the player owns, showing that business, and on crunchers in the player's
/// own apartments, starting from a list of all their businesses. Business rows: the till, one per menu item (stock
/// and orders), then recent sales. The program's print button becomes the action for the selected row:
/// Open / Back (at home), Collect (in the store) or Transfer (from home), Order.
/// </summary>
[HarmonyPatch]
internal static class LedgerApp
{
    public const string AppName = "BusinessLedger";
    public const string Marker = "​";
    private const string Currency = "¢";
    private static CruncherAppPreset preset;

    private enum RowKind { Till, Deposit, Item, Business, Back }
    private sealed class Row
    {
        public RowKind Kind;
        public string Item;
        public int Price;
        public int CompanyId;
    }

    private static readonly Dictionary<IntPtr, Row> rows = new();
    // Which business a home cruncher is showing (by computer); none = the list of businesses.
    private static readonly Dictionary<IntPtr, int> viewing = new();

    /// <summary>Forgets which business each home computer was showing (a different save has different computers).</summary>
    internal static void Reset()
    {
        rows.Clear();
        viewing.Clear();
    }

    /// <summary>True when this computer is in one of the player's own apartments.</summary>
    private static bool AtHome(ComputerController cc)
    {
        var address = cc?.ic?.interactable?.node?.gameLocation?.thisAsAddress;
        if (address == null || Player.Instance == null) return false;
        foreach (var a in Player.Instance.apartmentsOwned)
            if (a != null && a.Pointer == address.Pointer) return true;
        return false;
    }

    /// <summary>The business this screen is about: the one it stands in, or the one picked at home.</summary>
    private static Company Shown(ComputerController cc)
    {
        var here = CompanyAt(cc);
        if (Store.Get(here) != null) return here;
        return AtHome(cc) && viewing.TryGetValue(cc.Pointer, out var id) ? Store.FindCompany(id) : null;
    }

    private static bool IsLedger(ComputerController cc) =>
        preset != null && cc != null && cc.currentApp != null && cc.currentApp.Pointer == preset.Pointer;

    private static Company CompanyAt(ComputerController cc) => cc?.ic?.interactable?.node?.gameLocation?.thisAsAddress?.company;

    // ---- Installing the program ----

    // Wizcards taken off a computer type's app list to make room for the ledger, so it can be put back.
    private static readonly Dictionary<IntPtr, CruncherAppPreset> wizcardsRemoved = new();

    /// <summary>
    /// Adjusts the computer type's app list right before a desktop draws (the list is shared by every computer of that
    /// type). In a business you own, the ledger takes the Wizcards game's place (the desktop only fits eight icons); at
    /// home the ledger is added; everywhere else both are as the game had them. Only these two entries are ever
    /// touched, so apps other mods add (like Stock Market) are left alone.
    /// </summary>
    [HarmonyPatch(typeof(DesktopApp), nameof(DesktopApp.UpdateIcons))]
    [HarmonyPrefix]
    private static void Install(DesktopApp __instance)
    {
        try
        {
            var apps = __instance.controller?.ic?.interactable?.preset?.additionalApps;
            if (apps == null) return;
            if (preset == null) Create();
            if (preset == null) return;

            var cc = __instance.controller;
            var business = Store.Get(CompanyAt(cc)) != null;
            var home = !business && Store.Owned.Count > 0 && AtHome(cc);
            var ledger = IndexOf(apps, a => a.Pointer == preset.Pointer);

            if (business)
            {
                if (ledger >= 0) return;
                var wiz = IndexOf(apps, IsWizcards);
                if (wiz >= 0)
                {
                    wizcardsRemoved[apps.Pointer] = apps[wiz];
                    apps[wiz] = preset;
                }
                else apps.Add(preset);
                return;
            }

            // Not a business of yours: put Wizcards back where the ledger was, if it was swapped out here.
            if (ledger >= 0 && wizcardsRemoved.TryGetValue(apps.Pointer, out var wizcards))
            {
                apps[ledger] = wizcards;
                wizcardsRemoved.Remove(apps.Pointer);
                ledger = -1;
            }
            if (home && ledger < 0) apps.Add(preset);
            else if (!home && ledger >= 0) apps.RemoveAt(ledger);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't set up the Business Ledger: " + e);
        }
    }

    private static int IndexOf(Il2CppSystem.Collections.Generic.List<CruncherAppPreset> apps, Func<CruncherAppPreset, bool> match)
    {
        for (var i = 0; i < apps.Count; i++)
            if (apps[i] != null && match(apps[i])) return i;
        return -1;
    }

    private static bool IsWizcards(CruncherAppPreset app)
    {
        foreach (var go in app.appContent)
            if (go != null && go.GetComponent<WizcardsApp>() != null) return true;
        return false;
    }

    private static void Create()
    {
        CruncherAppPreset sales = null;
        foreach (var app in Resources.FindObjectsOfTypeAll<CruncherAppPreset>())
            if (app != null && app.name != AppName && IsSalesRecords(app)) { sales = app; break; }
        if (sales == null) return;
        preset = UnityEngine.Object.Instantiate(sales);
        preset.name = AppName;
        // Where it shows is decided by Install, so none of the game's own conditions apply.
        preset.alwaysInstalled = true;
        preset.companyOnly = false;
        preset.salesRecordsOnly = false;
        preset.onlyIfOwner = false;
        preset.onlyIfResidential = false;
        preset.onlyIfCorporateSabotageSkill = false;
        preset.installationConditions = new Il2CppSystem.Collections.Generic.List<CruncherAppPreset.AppAccess>();
        preset.onlyInAddresses = new Il2CppSystem.Collections.Generic.List<AddressPreset>();
        UnityEngine.Object.DontDestroyOnLoad(preset);
        Plugin.Logger.LogInfo($"Business Ledger program created from '{sales.name}'");
    }

    private static bool IsSalesRecords(CruncherAppPreset app)
    {
        foreach (var go in app.appContent)
            if (go != null && go.GetComponent<SalesRecordsApp>() != null) return true;
        return false;
    }

    [HarmonyPatch(typeof(DesktopIconController), nameof(DesktopIconController.Setup))]
    [HarmonyPostfix]
    private static void IconName(DesktopIconController __instance, CruncherAppPreset newApp)
    {
        if (preset != null && newApp != null && newApp.Pointer == preset.Pointer)
            __instance.iconText.text = "Business Ledger";
    }

    // ---- The screen ----

    [HarmonyPatch(typeof(SalesRecordsApp), nameof(SalesRecordsApp.UpdateEntries))]
    [HarmonyPrefix]
    private static bool Entries(SalesRecordsApp __instance)
    {
        if (!IsLedger(__instance.controller)) return true;
        try
        {
            Fill(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Business Ledger couldn't list: " + e);
        }
        return false;
    }

    private static void Fill(SalesRecordsApp app)
    {
        var cc = app.controller;
        var home = AtHome(cc) && Store.Get(CompanyAt(cc)) == null;
        var company = Shown(cc);
        var b = Store.Get(company);
        rows.Clear();
        var options = new Il2CppSystem.Collections.Generic.List<ComputerOSMultiSelect.OSMultiOption>();

        if (b == null && home)
        {
            app.titleText.text = "MY BUSINESSES";
            foreach (var biz in Store.Owned.Values.OrderBy(x => x.Name))
                options.Add(Option(Summary(biz), new Row { Kind = RowKind.Business, CompanyId = biz.CompanyId }));
            if (options.Count == 0) options.Add(Option("You don't own any businesses.", null));
            app.list.UpdateElements(options);
            app.UpdateSelected();
            return;
        }

        app.titleText.text = company != null ? company.name.ToUpperInvariant() : "BUSINESS LEDGER";
        if (b == null)
        {
            options.Add(Option("You don't own this business.", null));
            app.list.UpdateElements(options);
            return;
        }
        if (home) options.Add(Option("< ALL BUSINESSES\nBack to the list", new Row { Kind = RowKind.Back }));

        var now = SessionData.Instance.gameTime;
        var today = b.Sales.Where(s => now - s.Time <= 24f).ToList();
        var staff = Store.StaffCount(company);
        var sending = b.Transfers.Sum(t => t.Amount);
        options.Add(Option(
            $"TILL: {Currency}{b.Till}" + (sending > 0 ? $"\n{Currency}{sending} on its way to you" : "") +
            $"\n{OpenState(company)}" +
            $"\n24h: {today.Count} sales, {Currency}{today.Sum(s => s.Total)}, {b.TurnedAway} turned away" +
            $"\n{(b.ManagerRestocks ? "Wages+raise" : "Wages")}: {Currency}{staff * Plugin.WagePerStaff.Value + (b.ManagerRestocks ? Plugin.ManagerRaise.Value : 0)}/day",
            new Row { Kind = RowKind.Till, CompanyId = b.CompanyId }));

        options.Add(Option($"PUT MONEY IN\nFrom your wallet, {Currency}{Plugin.DepositStep.Value} at a time\nPays wages and the manager's restocking",
            new Row { Kind = RowKind.Deposit, CompanyId = b.CompanyId }));

        foreach (var kv in Store.Menu(company))
        {
            var name = kv.Key.name;
            var stock = b.Stock.TryGetValue(name, out var n) ? n : 0;
            var orders = b.Orders.Where(o => o.Item == name).ToList();
            var text = $"{Store.ItemName(name)}: {(stock > 0 ? stock + " in stock" : "OUT OF STOCK")}\n" +
                       $"Price {Currency}{kv.Value}\nCost {Currency}{Store.UnitCost(kv.Value)}";
            foreach (var o in orders)
            {
                var days = Store.DaysUntil(o.Arrives);
                text += $"\n+{o.Quantity} due {(days <= 0 ? "today" : days == 1 ? "tomorrow" : $"in {days} days")}";
            }
            options.Add(Option(text, new Row { Kind = RowKind.Item, Item = name, Price = kv.Value, CompanyId = b.CompanyId }));
        }

        foreach (var s in b.Sales.Take(40))
        {
            var head = SessionData.Instance.TimeAndDate(s.Time, false, true, true) + "  " + s.Customer;
            var body = string.Join("\n", s.Items.Select(i =>
                "<align=\"left\">" + Store.ItemName(i.Item) + "  <align=\"right\">" + Currency + i.Price));
            options.Add(Option(head + "\n" + body, null));
        }
        app.list.UpdateElements(options);
        app.UpdateSelected();
    }

    /// <summary>
    /// Open or closed right now, and how many staff are in. A business only opens when it's within its hours and at
    /// least one member of staff has turned up; until then, arriving customers turn around at the door.
    /// </summary>
    private static string OpenState(Company c)
    {
        var inNow = c.currentStaff?.Count ?? 0;
        var total = Store.StaffCount(c);
        // The game only flips a business to closed when staff are there to close up, so after everyone has gone home it
        // can still read as open; the schedule (openForBusinessDesired) is the reliable part.
        var state = !c.openForBusinessDesired ? "Closed (hours)" : c.openForBusinessActual ? "Open" : "Closed (no staff in)";
        if (inNow == 0 && Plugin.LogEachSale.Value) LogStaff(c);
        return $"{state}, {inNow}/{total} staff in";
    }

    /// <summary>Where everyone is, when a business that should be open has nobody in (to find out why).</summary>
    private static void LogStaff(Company c)
    {
        try
        {
            Plugin.Logger.LogInfo($"[Staff] {c.name} at {SessionData.Instance.decimalClock:0.00} ({SessionData.Instance.day}): open={c.openForBusinessActual}, wanted open={c.openForBusinessDesired}");
            foreach (var job in c.companyRoster)
            {
                var h = job?.employee;
                if (h == null) { Plugin.Logger.LogInfo($"[Staff]   {job?.preset?.name}: vacant"); continue; }
                var days = new List<string>();
                foreach (var d in job.workDaysList) days.Add(d.ToString().Substring(0, 3));
                Plugin.Logger.LogInfo($"[Staff]   {h.GetCitizenName()} ({job.preset?.name}) shift {job.startTimeDecimalHour:0.#}-{job.endTimeDecialHour:0.#} {string.Join("/", days)}: " +
                                      $"atWork={h.isAtWork} dead={h.isDead} asleep={h.isAsleep} at '{h.currentGameLocation?.name}' goal '{h.ai?.currentGoal?.preset?.name}'");
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Staff log failed: " + e.Message);
        }
    }

    /// <summary>One business in the home list.</summary>
    private static string Summary(Business b)
    {
        var now = SessionData.Instance.gameTime;
        var today = b.Sales.Where(s => now - s.Time <= 24f).ToList();
        var low = b.Stock.Count(kv => kv.Value <= Plugin.LowStock.Value);
        return $"{b.Name}\nTill {Currency}{b.Till}\n24h: {today.Count} sales, {Currency}{today.Sum(s => s.Total)}\n" +
               (low > 0 ? $"LOW STOCK: {low} item{(low == 1 ? "" : "s")}" : "Stock OK") +
               (b.Orders.Count > 0 ? $"\nOrders due: {b.Orders.Count}" : "");
    }

    private static ComputerOSMultiSelect.OSMultiOption Option(string text, Row row)
    {
        var o = new ComputerOSMultiSelect.OSMultiOption(Marker + text, null);
        if (row != null) rows[o.Pointer] = row;
        return o;
    }

    private static Row Selected(SalesRecordsApp app)
    {
        var opt = app.list?.selected?.option;
        return opt != null && rows.TryGetValue(opt.Pointer, out var r) ? r : null;
    }

    /// <summary>The game's list rows show two lines; ours carry more after a marker.</summary>
    [HarmonyPatch(typeof(ComputerOSMultiSelectElement), nameof(ComputerOSMultiSelectElement.Setup))]
    [HarmonyPostfix]
    private static void RowText(ComputerOSMultiSelectElement __instance, ComputerOSMultiSelect.OSMultiOption newOpt)
    {
        var text = newOpt?.text;
        if (text == null || !text.StartsWith(Marker)) return;
        var cut = text.IndexOf('\n');
        __instance.elementText.text = (cut < 0 ? text : text.Substring(0, cut)).Substring(Marker.Length);
        if (__instance.elementText2 != null) __instance.elementText2.text = cut < 0 ? "" : text.Substring(cut + 1);
    }

    /// <summary>The print button becomes the action for the selected row.</summary>
    [HarmonyPatch(typeof(SalesRecordsApp), nameof(SalesRecordsApp.UpdateSelected))]
    [HarmonyPostfix]
    private static void ActionButton(SalesRecordsApp __instance)
    {
        if (!IsLedger(__instance.controller) || __instance.printButton == null) return;
        try
        {
            var row = Selected(__instance);
            var b = row != null ? Store.Get(Store.FindCompany(row.CompanyId)) : null;
            var remote = Store.Get(CompanyAt(__instance.controller)) == null;
            string label = row?.Kind switch
            {
                RowKind.Back => "BACK",
                RowKind.Business => "OPEN",
                RowKind.Deposit when b != null => $"DEPOSIT {Currency}{Plugin.DepositStep.Value}",
                RowKind.Till when b != null && remote => $"TRANSFER {Currency}{Store.TransferNet(b.Till)}",
                RowKind.Till when b != null => $"COLLECT {Currency}{Math.Max(0, b.Till)}",
                RowKind.Item when b != null => $"ORDER {Store.OrderSize(Store.FindCompany(row.CompanyId))} - {Currency}{Store.UnitCost(row.Price) * Store.OrderSize(Store.FindCompany(row.CompanyId))}",
                _ => null
            };
            __instance.printButton.gameObject.SetActive(label != null);
            var t = label != null ? Label(__instance) : null;
            if (t != null) t.text = label;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Business Ledger button failed: " + e.Message);
        }
    }

    /// <summary>
    /// The print button shows a printer picture, not text. We hide its pictures and add our own label,
    /// a copy of the screen's page-count text so it uses the cruncher's font.
    /// </summary>
    private static TextMeshProUGUI Label(SalesRecordsApp app)
    {
        var text = app.printButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return null;
        // This component keeps the button reading "Print"; switch it off so our text stays.
        var auto = text.GetComponent<ComputerAutoTextController>();
        if (auto != null && auto.enabled)
        {
            auto.enabled = false;
            Widen(app.printButton, 2.5f);
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = text.fontSize * 0.6f;
            text.enableAutoSizing = true;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = TextAlignmentOptions.Center;
        }
        return text;
    }

    /// <summary>Widens the button to the left, keeping its right edge where it was.</summary>
    private static void Widen(RectTransform rect, float factor)
    {
        try
        {
            var width = rect.rect.width;
            var extra = width * (factor - 1f);
            Plugin.Logger.LogInfo($"[Button] width {width}, anchors {rect.anchorMin}-{rect.anchorMax}, pivot {rect.pivot}, pos {rect.anchoredPosition}, size {rect.sizeDelta}");
            var layout = rect.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout != null)
            {
                if (layout.preferredWidth > 0) layout.preferredWidth = width * factor;
                if (layout.minWidth > 0) layout.minWidth = width * factor;
            }
            rect.sizeDelta = new Vector2(rect.sizeDelta.x + extra, rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x - extra * (1f - rect.pivot.x), rect.anchoredPosition.y);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't widen the button: " + e.Message);
        }
    }

    private static void LogHierarchy(UnityEngine.Transform t, int depth)
    {
        var parts = new List<string>();
        foreach (var c in t.GetComponents<UnityEngine.Component>()) parts.Add(c.GetIl2CppType().Name);
        Plugin.Logger.LogInfo($"[Button] {new string(' ', depth * 2)}{t.name} ({string.Join(", ", parts)})");
        for (var i = 0; i < t.childCount; i++) LogHierarchy(t.GetChild(i), depth + 1);
    }

    [HarmonyPatch(typeof(SalesRecordsApp), nameof(SalesRecordsApp.OnPrintEntry))]
    [HarmonyPrefix]
    private static bool Action(SalesRecordsApp __instance)
    {
        if (!IsLedger(__instance.controller)) return true;
        try
        {
            var row = Selected(__instance);
            if (row == null) return false;
            var cc = __instance.controller;
            if (row.Kind == RowKind.Business || row.Kind == RowKind.Back)
            {
                if (row.Kind == RowKind.Business) viewing[cc.Pointer] = row.CompanyId;
                else viewing.Remove(cc.Pointer);
                Sound(cc, AudioControls.Instance.computerPrint);
                __instance.list.page = 0;
                Fill(__instance);
                __instance.OnChangePage();
                return false;
            }
            var b = Store.Get(Store.FindCompany(row.CompanyId));
            if (b == null) return false;
            var remote = Store.Get(CompanyAt(cc)) == null;
            if (row.Kind == RowKind.Deposit)
            {
                var amount = Plugin.DepositStep.Value;
                if (GameplayController.Instance.money < amount)
                {
                    Plugin.Message($"You don't have {Currency}{amount} on you.", false);
                    Sound(cc, AudioControls.Instance.computerInvalidPasscode);
                    return false;
                }
                GameplayController.Instance.AddMoney(-amount, true, "proprietor_deposit");
                b.Till += amount;
                Plugin.Logger.LogInfo($"{b.Name}: deposited ¢{amount}, till now ¢{b.Till}");
                Sound(cc, AudioControls.Instance.computerPrint);
            }
            else if (row.Kind == RowKind.Till)
            {
                if (b.Till <= 0)
                {
                    Plugin.Message("The till is empty.", false);
                    Sound(cc, AudioControls.Instance.computerInvalidPasscode);
                    return false;
                }
                if (remote)
                {
                    var sent = Store.Transfer(b);
                    Plugin.Message(sent);
                    Plugin.Logger.LogInfo($"{b.Name}: {sent}");
                }
                else
                {
                    GameplayController.Instance.AddMoney(b.Till, true, "proprietor_collect");
                    Plugin.Logger.LogInfo($"{b.Name}: collected ¢{b.Till}");
                    b.Till = 0;
                }
                Sound(cc, AudioControls.Instance.computerPrint);
            }
            else
            {
                var ok = Store.PlaceOrder(b, row.Item, row.Price, out var message);
                Plugin.Message(message, ok);
                Plugin.Logger.LogInfo($"{b.Name}: {message}");
                Sound(cc, ok ? AudioControls.Instance.computerPrint : AudioControls.Instance.computerInvalidPasscode);
            }
            var page = __instance.list.page;
            Fill(__instance);
            if (page != __instance.list.page) __instance.list.NextPage(page - __instance.list.page);
            __instance.OnChangePage();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Business Ledger action failed: " + e);
        }
        return false;
    }

    private static void Sound(ComputerController cc, AudioEvent e)
    {
        try
        {
            AudioController.Instance.PlayWorldOneShot(e, Player.Instance, cc.ic.interactable.node, cc.ic.interactable.wPos);
        }
        catch { }
    }
}

/// <summary>
/// The game's list control treats any list on a computer in the player's own apartment as the login screen: it adds
/// the player's profile and deletes the other rows. For the ledger's own list (rows carry our marker) the rows are
/// drawn here instead, the same way the game draws a page of them.
/// </summary>
[HarmonyPatch(typeof(ComputerOSMultiSelect), "SpawnList")]
internal static class LedgerListPatch
{
    private static bool Prefix(ComputerOSMultiSelect __instance)
    {
        var all = __instance.allOptions;
        if (all == null || all.Count == 0 || all[0]?.text == null || !all[0].text.StartsWith(LedgerApp.Marker)) return true;
        try
        {
            foreach (var old in __instance.options)
                if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            __instance.options.Clear();
            var first = __instance.usePages ? __instance.page * __instance.maxPerPage : 0;
            var last = __instance.usePages ? first + __instance.maxPerPage : all.Count;
            var y = 0f;
            for (var i = first; i < last && i < all.Count; i++)
            {
                var element = UnityEngine.Object.Instantiate(__instance.elementPrefab, __instance.elementParent)
                    .GetComponent<ComputerOSMultiSelectElement>();
                element.Setup(all[i], __instance);
                element.rect.anchoredPosition = new UnityEngine.Vector2(0f, y);
                y -= element.rect.sizeDelta.y;
                __instance.options.Add(element);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Business Ledger couldn't draw its list: " + e.Message);
        }
        return false;
    }
}
