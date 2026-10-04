using System;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// The game's Apartment Decor editor, unlocked inside businesses you own: repaint walls, floors and ceilings, and add
/// your own furniture. The business's own furniture and items (counters, registers, tables, seats...) are what staff and
/// customers use, so they stay put: they can't be moved, stored or sold.
///
/// The editor only works where the game thinks you live (it checks Player.apartmentsOwned in several places), so while
/// you're editing a business, its address is counted as one of your apartments. It is taken off again as soon as you
/// stop editing or leave, and before any save, so it never ends up in a save file.
/// </summary>
[HarmonyPatch]
internal static class DecorPatch
{
    private static NewAddress lent;

    private static NewAddress OwnedBusinessHere()
    {
        var address = Player.Instance?.currentGameLocation?.thisAsAddress;
        return address != null && Store.Get(address.company) != null ? address : null;
    }

    /// <summary>Same rule as the game's own button: sandbox games, or the story once it reaches the apartment chapter.</summary>
    private static bool DecorAllowed() =>
        Game.Instance.sandboxMode || (ChapterController.Instance != null && ChapterController.Instance.currentPart >= 30);

    private static void Lend(NewAddress address)
    {
        if (lent != null || address == null || Player.Instance.apartmentsOwned.Contains(address)) return;
        Player.Instance.apartmentsOwned.Add(address);
        lent = address;
    }

    internal static void Return()
    {
        if (lent == null) return;
        try
        {
            Player.Instance?.apartmentsOwned.Remove(lent);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Decor: couldn't hand the business back: " + e.Message);
        }
        lent = null;
    }

    // ---- The Edit Decor button ----

    [HarmonyPatch(typeof(BioScreenController), nameof(BioScreenController.UpdateDecorEditButton))]
    [HarmonyPostfix]
    private static void ShowButton(BioScreenController __instance)
    {
        try
        {
            if (!SessionData.Instance.isFloorEdit && DecorAllowed() && OwnedBusinessHere() != null)
                __instance.editDecorButton.gameObject.SetActive(true);
        }
        catch { }
    }

    [HarmonyPatch(typeof(BioScreenController), nameof(BioScreenController.DecorEditButton))]
    [HarmonyPrefix]
    private static bool StartEditing()
    {
        try
        {
            var business = OwnedBusinessHere();
            if (business == null || SessionData.Instance.isDecorEdit || !DecorAllowed()) return true;
            Lend(business);
            InteractionController.Instance.StartDecorEdit();
            Plugin.Logger.LogInfo($"Decor: editing {business.name}");
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Decor: couldn't start editing: " + e.Message);
            Return();
            return true;
        }
    }

    /// <summary>
    /// Still decorating: the editor is up, or you're out in the room placing furniture or trying a paint. (The game
    /// unpauses for placing and painting and clears its decor-edit flag meanwhile, so that flag alone isn't enough.)
    /// </summary>
    private static bool StillEditing()
    {
        if (SessionData.Instance.isDecorEdit) return true;
        var apc = PlayerApartmentController.Instance;
        if (apc != null && (apc.furniturePlacementMode || apc.decoratingMode)) return true;
        foreach (var window in InterfaceController.Instance.activeWindows)
            if (window != null && window.preset != null && window.preset.name == "ApartmentDecor") return true;
        return false;
    }

    /// <summary>Hands the business back once editing is over or you've left it.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    [HarmonyPostfix]
    private static void Watch()
    {
        if (lent == null) return;
        try
        {
            var here = Player.Instance.currentGameLocation?.thisAsAddress;
            if (!StillEditing() || here == null || here.Pointer != lent.Pointer) Return();
        }
        catch
        {
            Return();
        }
    }

    // ---- Painted ceilings ----
    //
    // Some business rooms (diners, for one) have a lit-panel ceiling: it has only a Multiply colour, and while the
    // lights are on the game adds a white glow on top, which washes the Multiply out. On ceilings you paint, the glow
    // is tinted by the Multiply colour instead (black: no glow; a colour: it glows in that colour).

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.ApplyDecor))]
    [HarmonyPostfix]
    private static void RememberCeiling(PlayerApartmentController __instance, MaterialGroupPreset.MaterialType decorType, bool saveChanges)
    {
        if (!Editing || !saveChanges || decorType != MaterialGroupPreset.MaterialType.ceiling) return;
        try
        {
            var room = __instance.decoratingRoom;
            var b = Store.Get(lent.company);
            if (room == null || b == null) return;
            if (!b.PaintedCeilings.Contains(room.roomID)) b.PaintedCeilings.Add(room.roomID);
            var changed = TintGlow(room);
            Plugin.Logger.LogInfo($"Decor: painted ceiling in {room.name} (room {room.roomID}); glow tinted on {changed} piece(s)");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Decor: couldn't record the painted ceiling: " + e.Message);
        }
    }

    private static readonly int Emissive = Shader.PropertyToID("_EmissiveColor");
    private static float nextGlowLog;

    /// <summary>
    /// Sets the glow on every piece of the room's ceiling: the combined ceiling mesh and, before that is built, each
    /// floor tile's own ceiling piece (each can carry its own copy of the material). The glow is what the game would
    /// use (its boost × the room's light colour, nothing while the lights are off) multiplied by the ceiling's
    /// Multiply colour, so a black Multiply gives no glow and a coloured one glows in that colour.
    /// Returns how many pieces were changed.
    /// </summary>
    private static int TintGlow(NewRoom room)
    {
        if (room?.preset == null) return 0;
        var target = Color.black;
        if (room.mainLightStatus)
        {
            var light = room.lightZones != null && room.lightZones.Count > 0 ? room.lightZones[0].areaLightColour : Color.white;
            var tint = room.ceilingMatKey != null && room.ceilingMatKey.mainColour != Color.clear ? room.ceilingMatKey.mainColour : Color.white;
            target = room.preset.ceilingEmissionBoost * light * tint;
        }
        var changed = 0;
        changed += SetGlow(room.ceilingMat, target);
        changed += SetGlow(room.combinedCeiling, target);
        foreach (var node in room.nodes)
            if (node != null) changed += SetGlow(node.spawnedCeiling, target);
        return changed;
    }

    private static int SetGlow(GameObject ceiling, Color target)
    {
        if (ceiling == null) return 0;
        var changed = 0;
        foreach (var renderer in ceiling.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var mat in renderer.sharedMaterials)
                changed += SetGlow(mat, target);
        return changed;
    }

    private static int SetGlow(Material mat, Color target)
    {
        if (mat == null || !mat.HasProperty(Emissive)) return 0;
        var current = mat.GetColor(Emissive);
        if (Mathf.Abs(current.r - target.r) < 0.01f && Mathf.Abs(current.g - target.g) < 0.01f && Mathf.Abs(current.b - target.b) < 0.01f) return 0;
        mat.SetColor(Emissive, target);
        return 1;
    }

    /// <summary>While you're in one of your businesses, its painted ceilings keep their tinted glow (the game resets the glow on load and when lights change).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    [HarmonyPostfix]
    private static void KeepCeilingsPainted()
    {
        try
        {
            var b = Store.Get(Player.Instance?.currentGameLocation?.thisAsAddress?.company);
            if (b == null || b.PaintedCeilings.Count == 0) return;
            foreach (var id in b.PaintedCeilings)
                if (CityData.Instance.roomDictionary.TryGetValue(id, out var room) && TintGlow(room) > 0 && Time.unscaledTime >= nextGlowLog)
                {
                    // The game turned the glow back on (load, lights, rebuilt mesh); logged at most every few seconds.
                    nextGlowLog = Time.unscaledTime + 5f;
                    Plugin.Logger.LogInfo($"Decor: the game reset the ceiling glow in {room.name}; tinted again");
                }
        }
        catch { }
    }

    // ---- The business's own furniture and items stay put ----

    private static bool Editing => lent != null;

    private static bool Refuse(string what)
    {
        Plugin.Message("That belongs to the business. It stays where it is.", false);
        Plugin.Logger.LogInfo("Decor: kept " + what);
        return false;
    }

    private static bool Locked(FurnitureLocation furniture) =>
        Editing && furniture != null && !furniture.userPlaced && !IsRefuse(furniture);

    /// <summary>
    /// Litter and junk the game places as furniture: litter decals and piles, ceiling grime, junk heaps and cardboard
    /// boxes (by preset name). Bins are never refuse.
    /// </summary>
    private static bool IsRefuse(FurnitureLocation furniture)
    {
        var name = furniture.furniture?.name?.ToLowerInvariant();
        if (name == null || name.Contains("bin")) return false;
        return name.Contains("litter") || name.Contains("ceilingrot") || name.StartsWith("junk") || name.Contains("cardboardbox");
    }

    private static bool Locked(Interactable item)
    {
        if (!Editing || item == null || IsRefuse(item)) return false;
        var owner = item.belongsTo;
        return owner == null || Player.Instance == null || owner.Pointer != Player.Instance.Pointer;
    }

    /// <summary>
    /// Litter and junk nobody uses: the game's own trash (on the city's cleanup list, used-up food and drink,
    /// packaging), clutter placed in junk slots when the city was generated, and cardboard boxes. Bins, case
    /// evidence and side-job items are never refuse.
    /// </summary>
    private static bool IsRefuse(Interactable item)
    {
        try
        {
            var preset = item.preset;
            if (preset == null || item.inInventory != null || item.jobParent != null || IsBin(item) || IsCaseEvidence(item)) return false;
            var cleanup = CleanupController.Instance;
            if (cleanup != null && cleanup.trash.Contains(item)) return true;
            if (preset.retailItem != null && preset.retailItem.isConsumable && item.cs <= 0.1f) return true;
            if (preset.markAsTrashOnCreate) return true;
            var slot = item.subObject?.preset?.name;
            if (slot != null && slot.Contains("Junk")) return true;
            return preset.name != null && preset.name.StartsWith("CardboardBox");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBin(Interactable item)
    {
        if (item.preset.specialCaseFlag == InteractablePreset.SpecialCase.garbageDisposal) return true;
        if (item.preset.prefab != null && item.preset.prefab.CompareTag("Garbage")) return true;
        return item.spawnedObject != null && item.spawnedObject.CompareTag("Garbage");
    }

    /// <summary>Murder weapons, ammo and calling cards stay where they are, so evidence can't be sold off.</summary>
    private static bool IsCaseEvidence(Interactable item)
    {
        var mc = MurderController.Instance;
        if (mc == null) return false;
        foreach (var murders in new[] { mc.activeMurders, mc.inactiveMurders })
        {
            if (murders == null) continue;
            foreach (var m in murders)
            {
                if (m == null) continue;
                if ((m.weapon != null && m.weapon.id == item.id) ||
                    (m.ammo != null && m.ammo.id == item.id) ||
                    (m.callingCard != null && m.callingCard.id == item.id)) return true;
            }
        }
        return false;
    }

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.SetFurniturePlacementMode))]
    [HarmonyPrefix]
    private static bool NoMoving(bool val, PlayerApartmentController.FurniturePlacement newPlacement)
    {
        if (!val || newPlacement?.existing == null || newPlacement.existing.id == 0) return true;
        return !Locked(newPlacement.existing) || Refuse(newPlacement.existing.furniture?.name);
    }

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.MoveFurnitureToStorage))]
    [HarmonyPrefix]
    private static bool NoStoring(FurnitureLocation newStorage) => !Locked(newStorage) || Refuse(newStorage.furniture?.name);

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.SellFurniture))]
    [HarmonyPrefix]
    private static bool NoSelling(FurnitureLocation newSell) => !Locked(newSell) || Refuse(newSell.furniture?.name);

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.MoveItemToStorage))]
    [HarmonyPrefix]
    private static bool NoStoringItems(Interactable newStorage) => !Locked(newStorage) || Refuse(newStorage.preset?.name);

    [HarmonyPatch(typeof(PlayerApartmentController), nameof(PlayerApartmentController.SellItem))]
    [HarmonyPrefix]
    private static bool NoSellingItems(Interactable newSell) => !Locked(newSell) || Refuse(newSell.preset?.name);
}
