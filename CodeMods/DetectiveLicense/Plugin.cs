using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SOD.Common;
using SOD.Common.Helpers;

namespace DetectiveLicense;

/// <summary>
/// Adds two documents sold at City Hall:
/// - Crime Scene Permit: guest passes (the game's own mechanic) for every active crime scene, for a few hours.
/// - Private Investigator License: permanent crime-scene access plus better odds when asking
///   "Can I come in and take a look around?", scaled by the resident's attitude to authority.
/// </summary>
[BepInPlugin(Guid, "Detective License", "1.1.2")]
[BepInDependency("Venomaus.SOD.Common")]
public class Plugin : BasePlugin
{
    public const string Guid = "sodmods.detectivelicense";

    internal static ManualLogSource Logger;

    internal static ConfigEntry<int> PermitCost;
    internal static ConfigEntry<float> PermitHours;
    internal static ConfigEntry<int> LicenseCost;
    internal static ConfigEntry<float> LookAroundBonus;
    internal static ConfigEntry<float> TrustAuthorityBonus;
    internal static ConfigEntry<float> DistrustAuthorityBonus;
    internal static ConfigEntry<string> DeskJobs;

    /// <summary>Whether the player owns a PI license in the current save.</summary>
    internal static bool Licensed;

    public override void Load()
    {
        Logger = Log;
        PermitCost = Config.Bind("Permit", "Cost", 100, "Price of a crime scene permit, in crows.");
        PermitHours = Config.Bind("Permit", "Hours", 4f, "How many in-game hours a permit lasts (1 in-game hour is about 6.5 real minutes).");
        LicenseCost = Config.Bind("License", "Cost", 1500, "Price of a private investigator's license, in crows.");
        LookAroundBonus = Config.Bind("License", "LookAroundBonus", 0.3f,
            "Added to the chance a resident lets you look around, for licensed investigators (0.3 = +30%).");
        TrustAuthorityBonus = Config.Bind("License", "TrustAuthorityBonus", 0.2f,
            "Extra chance on top of LookAroundBonus for residents who trust authority.");
        DistrustAuthorityBonus = Config.Bind("License", "DistrustAuthorityBonus", -0.25f,
            "Change on top of LookAroundBonus for residents who distrust authority (negative = the license helps less).");
        DeskJobs = Config.Bind("CityHall", "DeskJobs", "",
            "Comma-separated job names at City Hall who sell permits and licenses (e.g. Receptionist). Empty = any City Hall employee at work.");

        Lib.SaveGame.OnAfterLoad += (_, args) =>
        {
            Licensed = SaveData.Load(args);
            Log.LogInfo($"Save loaded: licensed = {Licensed}");
        };
        Lib.SaveGame.OnAfterSave += (_, args) => SaveData.Save(args, Licensed);
        Lib.SaveGame.OnBeforeNewGame += (_, _) => Licensed = false;

        Dialogs.Register();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Detective License loaded");
    }
}

/// <summary>Stores license ownership next to each save file (via SOD.Common's per-save folder).</summary>
internal static class SaveData
{
    private const string FileName = "detectivelicense.txt";

    public static bool Load(SaveGameArgs args)
    {
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            return File.Exists(path) && File.ReadAllText(path).Trim() == "licensed";
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to load license state: " + e);
            return false;
        }
    }

    public static void Save(SaveGameArgs args, bool licensed)
    {
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (licensed) File.WriteAllText(path, "licensed");
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to save license state: " + e);
        }
    }
}

/// <summary>
/// Places covered by open murder cases. The game's own "crime scene" flag only lasts ~2 in-game hours
/// after enforcers arrive, so cases are used instead: for every murder whose body has been reported
/// and whose case isn't solved, the murder location and the victim's home.
/// </summary>
internal static class CaseLocations
{
    private static readonly HashSet<int> cachedIds = new();
    private static float cachedAt = -10f;

    public static List<NewAddress> Get()
    {
        var result = new List<NewAddress>();
        var controller = MurderController.Instance;
        if (controller == null) return result;
        Collect(controller.activeMurders, result);
        Collect(controller.inactiveMurders, result);
        return result;
    }

    private static void Collect(Il2CppSystem.Collections.Generic.List<MurderController.Murder> murders, List<NewAddress> into)
    {
        if (murders == null) return;
        for (int i = 0; i < murders.Count; i++)
        {
            var murder = murders[i];
            if (murder == null || murder.state == MurderController.MurderState.solved) continue;
            var death = murder.victim?.death;
            if (death == null || !death.isDead || !death.reported) continue;
            if (LogMurders)
                Plugin.Logger.LogDebug($"Open case: victim {murder.victim.GetCitizenName()}, state {murder.state}, " +
                                      $"location '{murder.location?.name}' (address: {murder.location?.thisAsAddress != null}), home '{murder.victim.home?.name}'");
            Add(murder.location?.thisAsAddress, into);
            Add(murder.victim.home, into);
        }
    }

    private static void Add(NewAddress address, List<NewAddress> into)
    {
        if (address != null && !into.Contains(address)) into.Add(address);
    }

    /// <summary>Cached lookup for the trespass check, which runs very often.</summary>
    public static bool Covers(NewAddress address)
    {
        if (address == null) return false;
        var now = UnityEngine.Time.unscaledTime;
        if (now - cachedAt > 2f)
        {
            cachedAt = now;
            cachedIds.Clear();
            foreach (var a in Get()) cachedIds.Add(a.id);
        }
        return cachedIds.Contains(address.id);
    }

    public static void Invalidate() => cachedAt = -10f;

    private static bool LogMurders;

    /// <summary>Issues guest passes for every open case location; returns how many were covered.</summary>
    public static int IssuePermit(float hours)
    {
        LogMurders = true;
        var addresses = Get();
        LogMurders = false;
        foreach (var address in addresses)
        {
            GameplayController.Instance.AddGuestPass(address, hours);
            Plugin.Logger.LogInfo($"Permit covers '{address.name}' (id {address.id}, building '{address.building?.name}')");
        }
        return addresses.Count;
    }
}

/// <summary>Licensed investigators may be at open-case locations (murder site and victim's home).</summary>
[HarmonyPatch(typeof(Human), nameof(Human.IsTrespassing))]
internal static class LicensedCaseAccessPatch
{
    private static void Postfix(Human __instance, NewRoom room, ref int trespassEscalation, ref bool __result)
    {
        if (!__result || !Plugin.Licensed || !__instance.isPlayer || __instance.inAirVent) return;
        if (!CaseLocations.Covers(room?.gameLocation?.thisAsAddress)) return;
        __result = false;
        trespassEscalation = 0;
    }
}

/// <summary>License perks, applied through the game's existing sync-disk effects.</summary>
[HarmonyPatch(typeof(UpgradeEffectController), nameof(UpgradeEffectController.GetUpgradeEffect))]
internal static class UpgradeEffectPatch
{
    private static void Postfix(SyncDiskPreset.Effect effect, ref float __result)
    {
        if (!Plugin.Licensed) return;

        // Same effect as the crime-scene sync disk: active crime scenes aren't trespassing.
        if (effect == SyncDiskPreset.Effect.allowedAtCrimeScenes)
            __result = Math.Max(__result, 1f);
        // Only read by the "Can I come in and take a look around?" success roll.
        else if (effect == SyncDiskPreset.Effect.guestPassIssueModifier)
        {
            if (LookAroundTarget.Current != null)
            {
                var bonus = LookAroundTarget.Bonus(LookAroundTarget.Current);
                __result += bonus;
                Plugin.Logger.LogDebug($"[LookAround] bonus +{bonus:P0} applied for {LookAroundTarget.Current.GetCitizenName()} (modifier now {__result:0.00})");
            }
            else
            {
                Plugin.Logger.LogDebug("[LookAround] guestPassIssueModifier read with no current target (bonus not applied)");
            }
        }
    }
}

/// <summary>Remembers who the player is asking to look around, so the bonus can depend on their traits.</summary>
[HarmonyPatch(typeof(DialogController), nameof(DialogController.ExecuteDialog))]
internal static class LookAroundTarget
{
    internal static Human Current;

    private static void Prefix(EvidenceWitness.DialogOption dialog, Interactable saysTo)
    {
        Current = null;
        if (dialog?.preset != null && dialog.preset.specialCase == DialogPreset.SpecialCase.lookAroundHome)
        {
            Current = saysTo?.isActor?.TryCast<Human>();
            var p = dialog.preset;
            Plugin.Logger.LogDebug($"[LookAround] asking {Current?.GetCitizenName()}: baseChance={p.baseChance:0.00} " +
                                  $"useSuccessTest={p.useSuccessTest} traitRules={p.modifySuccessChanceTraits?.Count} " +
                                  $"trust={HasTrait(Current, "Principle-TrustOfAuthority")} distrust={HasTrait(Current, "Principle-DistrustOfAuthority")} " +
                                  $"licensed={Plugin.Licensed}");
        }
    }

    private static void Postfix(EvidenceWitness.DialogOption dialog, bool __result)
    {
        if (dialog?.preset != null && dialog.preset.specialCase == DialogPreset.SpecialCase.lookAroundHome)
            Plugin.Logger.LogDebug($"[LookAround] result: {(__result ? "SUCCESS" : "refused")}");
    }

    private static void Finalizer() => Current = null;

    internal static float Bonus(Human resident)
    {
        var bonus = Plugin.LookAroundBonus.Value;
        if (HasTrait(resident, "Principle-TrustOfAuthority")) bonus += Plugin.TrustAuthorityBonus.Value;
        if (HasTrait(resident, "Principle-DistrustOfAuthority")) bonus += Plugin.DistrustAuthorityBonus.Value;
        return bonus;
    }

    private static bool HasTrait(Human human, string traitName)
    {
        var traits = human?.characterTraits;
        if (traits == null) return false;
        for (int i = 0; i < traits.Count; i++)
            if (traits[i]?.trait != null && traits[i].trait.name == traitName) return true;
        return false;
    }
}
