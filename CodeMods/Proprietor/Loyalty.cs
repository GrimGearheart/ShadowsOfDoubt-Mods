using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SOD.Common;
using SOD.Common.Helpers;
using UnityEngine;

namespace Proprietor;

/// <summary>A citizen's favourite place for one errand, changed by this mod.</summary>
public class FavouriteChange
{
    public int HumanId { get; set; }
    public int Category { get; set; }
    public int AddressId { get; set; }
    /// <summary>The city's original favourite, put back before another save loads.</summary>
    public int OriginalAddressId { get; set; }
}

/// <summary>
/// Regulars that come and go. Every citizen has one favourite place per errand (a meal, a snack, coffee, a drink),
/// picked when the city is generated. A regular who walks out of your business because nothing they wanted was in
/// stock gets a strike, and at midnight each strike is a chance they switch to the nearest rival. A customer who isn't
/// a regular may make your place their favourite, as long as nobody has been turned away there in the last day.
///
/// Changes live in proprietor_regulars.json next to the save and are re-applied when it loads; the city file is never
/// touched, so without the mod everyone goes back to their original favourites.
/// </summary>
internal static class Loyalty
{
    private const string FileName = "proprietor_regulars.json";
    private static Dictionary<(int, int), FavouriteChange> changes = new();

    private static readonly CompanyPreset.CompanyCategory[] Errands =
    {
        CompanyPreset.CompanyCategory.meal, CompanyPreset.CompanyCategory.snack,
        CompanyPreset.CompanyCategory.caffeine, CompanyPreset.CompanyCategory.recreational
    };

    // ---- Saving ----

    public static void Load(SaveGameArgs args)
    {
        changes = new Dictionary<(int, int), FavouriteChange>();
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (!File.Exists(path)) return;
            var applied = 0;
            foreach (var ch in JsonSerializer.Deserialize<List<FavouriteChange>>(File.ReadAllText(path)))
            {
                var human = FindHuman(ch.HumanId);
                var address = FindAddress(ch.AddressId);
                if (human == null || address == null) continue;
                var cat = (CompanyPreset.CompanyCategory)ch.Category;
                // The original is whatever the city gave them (ignore the saved one if the city changed).
                if (human.favouritePlaces.TryGetValue(cat, out var orig) && orig != null) ch.OriginalAddressId = orig.id;
                SetFavourite(human, cat, address);
                changes[(ch.HumanId, ch.Category)] = ch;
                applied++;
            }
            Plugin.Logger.LogInfo($"Regulars: {applied} changed favourite place(s) applied");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't load regulars: " + e);
        }
    }

    public static void Save(SaveGameArgs args)
    {
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (changes.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            File.WriteAllText(path, JsonSerializer.Serialize(changes.Values.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't save regulars: " + e);
        }
    }

    /// <summary>Puts every changed favourite back as the city had it (before another save or a new game loads).</summary>
    public static void Revert()
    {
        try
        {
            foreach (var ch in changes.Values)
            {
                var human = FindHuman(ch.HumanId);
                var original = FindAddress(ch.OriginalAddressId);
                if (human != null && original != null) SetFavourite(human, (CompanyPreset.CompanyCategory)ch.Category, original);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't put regulars back: " + e.Message);
        }
        changes = new Dictionary<(int, int), FavouriteChange>();
    }

    // ---- Coming and going ----

    /// <summary>A customer walked out with nothing: if they're a regular, that's a strike against the business.</summary>
    public static void TurnedAway(Business b, Company c, Human who)
    {
        b.LastTurnedAway = SessionData.Instance.gameTime;
        if (who == null || c?.address == null) return;
        foreach (var cat in ErrandsFor(who, c))
        {
            if (!IsFavourite(who, cat, c.address)) continue;
            var key = StrikeKey(who.humanID, cat);
            b.Strikes[key] = (b.Strikes.TryGetValue(key, out var n) ? n : 0) + 1;
            Plugin.Logger.LogInfo($"{b.Name}: regular {who.GetCitizenName()} turned away ({b.Strikes[key]} strike(s))");
        }
    }

    /// <summary>A customer bought something: if they aren't a regular yet, they might become one.</summary>
    public static void Served(Business b, Company c, Human who)
    {
        if (who == null || c?.address == null || who.isDead) return;
        if (who.job?.employer != null && who.job.employer.companyID == c.companyID) return;
        if (SessionData.Instance.gameTime - b.LastTurnedAway < 24f) return;
        foreach (var cat in ErrandsFor(who, c))
        {
            if (IsFavourite(who, cat, c.address)) continue;
            if (UnityEngine.Random.value > Plugin.WinChance.Value) continue;
            var from = Change(who, cat, c.address);
            b.RegularsGained++;
            // Won from another of your own businesses: that one hears about it in its report.
            if (from?.company != null && Store.Get(from.company) is { } other) other.RegularsLost.Add(c.name);
            Plugin.Logger.LogInfo($"{b.Name}: {who.GetCitizenName()} is a new regular for {cat}" + (from != null ? $" (was {from.name})" : ""));
        }
    }

    /// <summary>Midnight: regulars with strikes may leave for the nearest rival.</summary>
    public static void Midnight()
    {
        foreach (var b in Store.Owned.Values)
        {
            var c = Store.FindCompany(b.CompanyId);
            if (c?.address == null) { b.Strikes.Clear(); continue; }
            foreach (var kv in b.Strikes)
            {
                if (!ParseKey(kv.Key, out var id, out var cat)) continue;
                var human = FindHuman(id);
                if (human == null || human.isDead || !IsFavourite(human, cat, c.address)) continue;
                var chance = 1f - Mathf.Pow(1f - Plugin.LoseChance.Value, kv.Value);
                if (UnityEngine.Random.value > chance) continue;
                var rival = NearestRival(human, cat, c);
                if (rival == null) continue;
                Change(human, cat, rival.address);
                b.RegularsLost.Add(rival.name);
                Plugin.Logger.LogInfo($"{b.Name}: regular {human.GetCitizenName()} left for {rival.name} ({cat}, {kv.Value} strike(s))");
            }
            b.Strikes.Clear();
        }
    }

    /// <summary>
    /// What the manager has noticed about the regulars since the last report (a hunch, not numbers); null when
    /// nothing stood out. Clears the tally.
    /// </summary>
    public static string Report(Business b)
    {
        var lost = b.RegularsLost.Count;
        var gained = b.RegularsGained;
        b.RegularsGained = 0;
        b.RegularsLost.Clear();
        if (lost >= 2) return "Some of the regulars haven't been in lately.";
        if (lost == 1) return "Haven't seen one of the regulars in a while.";
        if (gained >= 2) return "Lot of new faces lately.";
        if (gained == 1) return "Looks like we've got a new regular.";
        return null;
    }

    /// <summary>How many citizens count this business as a favourite for at least one errand.</summary>
    public static int Count(Company c)
    {
        if (c?.address == null) return 0;
        var n = 0;
        foreach (var h in c.address.favouredCustomers)
            if (h != null && Errands.Any(cat => IsFavourite(h, cat, c.address))) n++;
        return n;
    }

    // ---- Helpers ----

    /// <summary>The errands this customer could be on here: what they're out for now, else everything the place serves.</summary>
    private static IEnumerable<CompanyPreset.CompanyCategory> ErrandsFor(Human who, Company c)
    {
        var cats = c.preset?.companyCategories;
        if (cats == null) yield break;
        var goal = who.ai?.currentGoal?.preset;
        if (goal != null && Errands.Contains(goal.desireCategory) && cats.Contains(goal.desireCategory))
        {
            yield return goal.desireCategory;
            yield break;
        }
        foreach (var cat in Errands)
            if (cats.Contains(cat)) yield return cat;
    }

    private static bool IsFavourite(Human h, CompanyPreset.CompanyCategory cat, NewAddress address) =>
        h.favouritePlaces.TryGetValue(cat, out var fav) && fav != null && fav.Pointer == address.Pointer;

    /// <summary>Changes a favourite and remembers it (keeping the city's original for putting back).</summary>
    private static NewAddress Change(Human h, CompanyPreset.CompanyCategory cat, NewAddress to)
    {
        h.favouritePlaces.TryGetValue(cat, out var from);
        var key = (h.humanID, (int)cat);
        if (!changes.TryGetValue(key, out var ch))
            changes[key] = ch = new FavouriteChange { HumanId = h.humanID, Category = (int)cat, OriginalAddressId = from?.id ?? -1 };
        ch.AddressId = to.id;
        SetFavourite(h, cat, to);
        return from;
    }

    private static void SetFavourite(Human h, CompanyPreset.CompanyCategory cat, NewAddress to)
    {
        h.favouritePlaces.TryGetValue(cat, out var from);
        if (from != null && from.Pointer == to.Pointer) return;
        h.favouritePlaces[cat] = to;
        // Still a regular at the old place for another errand? Then they stay on its list.
        if (from != null && !Errands.Any(e => IsFavourite(h, e, from)) && from.favouredCustomers.Contains(h))
            from.favouredCustomers.Remove(h);
        if (!to.favouredCustomers.Contains(h)) to.favouredCustomers.Add(h);
    }

    /// <summary>The nearest other business serving this errand, measured from the citizen's home.</summary>
    private static Company NearestRival(Human h, CompanyPreset.CompanyCategory cat, Company not)
    {
        var from = h.home != null && h.home.nodes.Count > 0 ? h.home.nodes[0].position
            : not.address.nodes.Count > 0 ? not.address.nodes[0].position : Vector3.zero;
        Company best = null;
        var bestDist = float.MaxValue;
        foreach (var c in CityData.Instance.companyDirectory)
        {
            if (c == null || c.companyID == not.companyID || c.address == null || c.address.nodes.Count == 0) continue;
            if (!c.publicFacing || c.preset?.companyCategories == null || !c.preset.companyCategories.Contains(cat)) continue;
            if (c.prices == null || c.prices.Count == 0) continue;
            var d = Vector3.Distance(from, c.address.nodes[0].position);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    private static string StrikeKey(int id, CompanyPreset.CompanyCategory cat) => $"{id}:{(int)cat}";

    private static bool ParseKey(string key, out int id, out CompanyPreset.CompanyCategory cat)
    {
        id = 0;
        cat = default;
        var parts = key.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out id) || !int.TryParse(parts[1], out var c)) return false;
        cat = (CompanyPreset.CompanyCategory)c;
        return true;
    }

    private static Human FindHuman(int id) =>
        CityData.Instance.citizenDictionary.TryGetValue(id, out var h) ? h : null;

    private static NewAddress FindAddress(int id) =>
        id >= 0 && CityData.Instance.addressDictionary.TryGetValue(id, out var a) ? a : null;
}
