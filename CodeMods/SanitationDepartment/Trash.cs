using System;
using HarmonyLib;
using UnityEngine;

namespace SanitationDepartment;

internal enum Kind { None, Trash, Junk }

/// <summary>
/// What counts. "Trash" is the game's own definition (copied from ActionController.Dispose, the
/// street-cleaning payout check). "Junk" is this mod's addition: unowned clutter the city placed in
/// junk slots at generation, or loose unowned objects lying on the street.
/// </summary>
internal static class Trash
{
    public static bool IsTrash(Interactable item)
    {
        if (item?.preset == null || IsSceneItem(item)) return false;
        var player = Player.Instance;
        var mine = player != null && item.belongsTo == player;
        var cleanup = CleanupController.Instance;

        // Someone else's discarded item, on the city's cleanup list.
        if (!mine && cleanup != null && cleanup.trash.Contains(item)) return true;
        // A used-up consumable (empty can, finished meal...).
        var retail = item.preset.retailItem;
        if (retail != null && retail.isConsumable && item.cs <= 0.1f) return true;
        // Your own packaging-type junk.
        return mine && item.preset.markAsTrashOnCreate;
    }

    public static bool IsJunk(Interactable item)
    {
        if (item?.preset == null || item.belongsTo != null || item.inInventory != null || item.jobParent != null || IsSceneItem(item)) return false;
        if (IsBin(item) || IsSource(item) || InList(Plugin.NotJunk.Value, item) || IsCaseEvidence(item)) return false;

        // Placed as clutter in a junk slot when the city was generated.
        var slotClass = item.subObject?.preset?.name;
        if (slotClass != null && slotClass.Contains("Junk")) return true;

        // A loose, carryable object lying on a street.
        var onStreet = item.node?.gameLocation?.thisAsStreet != null;
        var loose = item.preset.isInventoryItem || item.preset.apartmentPlacementMode == InteractablePreset.ApartmentPlacementMode.physics;
        return onStreet && loose && item.preset.spawnable;
    }

    public static Kind Classify(Interactable item)
    {
        if (!State.Licensed && !Plugin.TagWithStreetCleanerDisk.Value) return Kind.None;
        if (IsTrash(item)) return Kind.Trash;
        return State.Licensed && IsJunk(item) ? Kind.Junk : Kind.None;
    }

    /// <summary>Body bags, blood pools and wounds: the game marks the last two as trash, but they're not litter.</summary>
    private static bool IsSceneItem(Interactable item) =>
        Scenes.IsBodyBag(item) || Cleaning.IsPool(item) || item.objectRef?.TryCast<Human.Wound>() != null;

    /// <summary>A dustbin (or other configured container) you collect trash from.</summary>
    public static bool IsSource(Interactable item) => item?.preset != null && InList(Plugin.TrashSources.Value, item);

    /// <summary>Hours until a dustbin has trash again (0 = ready).</summary>
    public static float SourceRefillLeft(Interactable item)
    {
        if (!State.SourceEmptied.TryGetValue(item.id, out var emptied)) return 0f;
        return Mathf.Max(0f, emptied + Plugin.SourceRefillHours.Value - SessionData.Instance.gameTime);
    }

    private static bool InList(string csv, Interactable item)
    {
        var name = item.preset.name;
        foreach (var entry in csv.Split(','))
            if (entry.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Bulky = can't go in the inventory; you carry it in both hands.</summary>
    public static bool IsBulky(Interactable item) => item?.preset != null && !item.preset.isInventoryItem;

    /// <summary>A bin, dumpster or other refuse container, by the same test the game uses.</summary>
    public static bool IsBin(Interactable item)
    {
        if (item?.preset == null) return false;
        if (item.preset.specialCaseFlag == InteractablePreset.SpecialCase.garbageDisposal) return true;
        if (item.preset.prefab != null && item.preset.prefab.CompareTag("Garbage")) return true;
        return item.spawnedObject != null && item.spawnedObject.CompareTag("Garbage");
    }

    /// <summary>Murder weapons, ammo and calling cards are never junk, so evidence can't be binned for cash.</summary>
    private static bool IsCaseEvidence(Interactable item)
    {
        var mc = MurderController.Instance;
        if (mc == null) return false;
        return InList(mc.activeMurders, item) || InList(mc.inactiveMurders, item);
    }

    private static bool InList(Il2CppSystem.Collections.Generic.List<MurderController.Murder> murders, Interactable item)
    {
        if (murders == null) return false;
        for (int i = 0; i < murders.Count; i++)
        {
            var m = murders[i];
            if (m == null) continue;
            if ((m.weapon != null && m.weapon.id == item.id) || (m.ammo != null && m.ammo.id == item.id) ||
                (m.callingCard != null && m.callingCard.id == item.id))
                return true;
        }
        return false;
    }

    /// <summary>What one item pays right now (0 if the player can't earn from trash).</summary>
    public static int Pay => (int)Math.Round(UpgradeEffectController.Instance?.GetUpgradeEffect(SyncDiskPreset.Effect.streetCleaningMoney) ?? 0f);

    /// <summary>Pays for disposed items and counts them toward Route Supervisor.</summary>
    public static void Payout(int items, int amount)
    {
        if (amount > 0) GameplayController.Instance.AddMoney(amount, true, Reason);
        State.AddBinned(items);
    }

    /// <summary>The game's own reason string for street-cleaning pay; our payouts use a different one.</summary>
    public const string GameReason = "readingforstreetcleaning";
    public const string Reason = "sanitation";
}

/// <summary>License perk: binning trash pays, through the game's own street-cleaning payout.</summary>
[HarmonyPatch(typeof(UpgradeEffectController), nameof(UpgradeEffectController.GetUpgradeEffect))]
internal static class StreetCleaningPayPatch
{
    private static void Postfix(SyncDiskPreset.Effect effect, ref float __result)
    {
        if (effect == SyncDiskPreset.Effect.streetCleaningMoney && State.Licensed)
            __result = Math.Max(__result, Plugin.PayPerItem.Value);
    }
}

/// <summary>Counts the game's own paid trash disposals.</summary>
[HarmonyPatch(typeof(GameplayController), nameof(GameplayController.AddMoney))]
internal static class BinnedCounterPatch
{
    private static void Postfix(int addVal, string reason)
    {
        if (reason == Trash.GameReason && addVal > 0 && State.Licensed) State.AddBinned(1);
    }
}

/// <summary>
/// The bin's "dispose" action only pays for the game's trash. When licensed, junk put in a bin
/// the normal way is paid too.
/// </summary>
[HarmonyPatch(typeof(ActionController), nameof(ActionController.Dispose))]
internal static class DisposeJunkPatch
{
    private static void Prefix(Interactable what, Actor who, out bool __state)
    {
        __state = false;
        if (!State.Licensed || who == null || !who.isPlayer || !Trash.IsBin(what)) return;
        var held = BioScreenController.Instance?.selectedSlot?.GetInteractable();
        __state = held != null && !Trash.IsTrash(held) && Trash.IsJunk(held);
    }

    private static void Postfix(bool __state)
    {
        if (__state) Trash.Payout(1, Trash.Pay);
    }
}

/// <summary>Small tag on the name of the thing you're looking at: trash/junk value, or bin hints.</summary>
[HarmonyPatch(typeof(InteractionController), nameof(InteractionController.UpdateInteractionText), new[] { typeof(string) })]
internal static class TagPatch
{
    private static void Prefix(ref string newText)
    {
        try
        {
            if (string.IsNullOrEmpty(newText)) return;
            var item = InteractionController.Instance?.currentLookingAtInteractable?.interactable;
            if (item == null) return;
            if (Plugin.LogItems.Value) Diagnostics.Describe(item);

            string tag = null;
            var scene = Scenes.At(Player.Instance?.currentGameLocation);
            if (scene != null && scene.State == SceneState.Released && !scene.Bagged && item.isActor != null &&
                item.isActor.Pointer == scene.Victim?.Pointer)
            {
                tag = $"{Plugin.ActionKey.Value}: bag the body";
            }
            else if (scene != null && scene.State == SceneState.Released && Cleaning.IsPool(item))
            {
                tag = $"Hold {Plugin.ActionKey.Value}: mop up";
            }
            else if (State.Licensed && Trash.IsBin(item) && State.BagCount > 0)
            {
                tag = $"{Plugin.ActionKey.Value}: empty bag ({State.BagCount}/{State.BagCapacity})";
            }
            else if (State.Licensed && Trash.IsSource(item))
            {
                var left = Trash.SourceRefillLeft(item);
                tag = left <= 0f ? $"{Plugin.ActionKey.Value}: collect trash" : $"Emptied · refills in {Mathf.CeilToInt(left)}h";
            }
            else
            {
                var kind = Trash.Classify(item);
                if (kind == Kind.None) return;
                if (!State.Licensed && Trash.Pay <= 0) return;
                var pay = kind == Kind.Junk && Trash.IsBulky(item) ? Plugin.BulkyPay.Value : Trash.Pay;
                var label = kind == Kind.Trash ? "Trash" : "Junk";
                tag = pay > 0 ? $"{label} · {CityControls.Instance.cityCurrency}{pay}" : label;
                if (kind == Kind.Junk && Trash.IsBulky(item) && State.Licensed)
                    tag += $" · {Plugin.ActionKey.Value} while carrying: break down";
            }
            newText = $"{newText} <size=70%><color=#9FB48A>{tag}</color></size>";
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

/// <summary>Litter picker: reach trash and junk from further away.</summary>
[HarmonyPatch(typeof(Interactable), nameof(Interactable.GetReachDistance))]
internal static class PickerReachPatch
{
    private static void Postfix(Interactable __instance, ref float __result)
    {
        if (!State.Picker || __result >= Plugin.PickerReach.Value) return;
        if (Trash.IsTrash(__instance) || Trash.IsJunk(__instance)) __result = Plugin.PickerReach.Value;
    }
}

internal static class Diagnostics
{
    private static int lastId = -1;

    /// <summary>Logs, once per item looked at, why it is or isn't trash/junk.</summary>
    public static void Describe(Interactable item)
    {
        if (item.id == lastId) return;
        lastId = item.id;
        Plugin.Logger.LogInfo($"[Item] {item.GetName()} preset={item.preset?.name} trash={Trash.IsTrash(item)} junk={Trash.IsJunk(item)} " +
                              $"owner={(item.belongsTo != null ? item.belongsTo.GetCitizenName() : "none")} slot={item.subObject?.preset?.name ?? "none"} " +
                              $"street={item.node?.gameLocation?.thisAsStreet != null} inventoryItem={item.preset?.isInventoryItem} " +
                              $"placement={item.preset?.apartmentPlacementMode} bin={Trash.IsBin(item)}");
    }
}
