using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SanitationDepartment;

/// <summary>
/// The sanitation bag. It's virtual: the game's trash bag item is carry-only and can't sit in an
/// inventory slot, so the bag is a counter. While licensed, trash and junk you pick up go straight
/// into it instead of a slot. Empty it by looking at a bin or dumpster and pressing the action key.
/// </summary>
[HarmonyPatch(typeof(FirstPersonItemController), nameof(FirstPersonItemController.PickUpItem))]
internal static class BagPickupPatch
{
    private static bool Prefix(Interactable pickUpThis, ref bool __result)
    {
        try
        {
            if (!State.Licensed || pickUpThis?.preset == null) return true;
            if (Plugin.KeepKey.Value != KeyCode.None && Input.GetKey(Plugin.KeepKey.Value)) return true;
            // Never bag the player's own things (the game counts e.g. used-up gear as trash).
            if (Player.Instance != null && pickUpThis.belongsTo == Player.Instance) return true;
            var kind = Trash.IsTrash(pickUpThis) ? Kind.Trash : Trash.IsJunk(pickUpThis) ? Kind.Junk : Kind.None;
            if (kind == Kind.None) return true;

            if (State.BagCount >= State.BagCapacity)
            {
                Bag.Message($"Bag's full ({State.BagCount}/{State.BagCapacity}). Empty it at a bin.");
                return true; // fall back to a normal pickup into a slot
            }

            pickUpThis.SafeDelete();
            State.BagCount++;
            Bag.Message($"Bagged ({State.BagCount}/{State.BagCapacity})");
            __result = true;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
            return true;
        }
    }
}

/// <summary>Remembers trash and junk the player drops or throws, to check whether it lands in a bin.</summary>
[HarmonyPatch(typeof(InteractableController), nameof(InteractableController.DropThis))]
internal static class TossTrackPatch
{
    private static void Prefix(InteractableController __instance)
    {
        try
        {
            if (!State.Licensed) return;
            var item = __instance.interactable;
            var player = Player.Instance;
            if (item == null || player == null) return;
            // Only the player's own drops and throws (NPCs drop things too).
            if (!__instance.isCarriedByPlayer &&
                (__instance.transform.position - player.transform.position).sqrMagnitude > 25f) return;
            if (Trash.IsTrash(item) || Trash.IsJunk(item)) Bag.Track(item);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class BagUpdatePatch
{
    private static void Postfix()
    {
        try
        {
            if (!State.Licensed || SessionData.Instance == null || !SessionData.Instance.play) return;
            if (Input.GetKeyDown(Plugin.ActionKey.Value) && Cursor.lockState == CursorLockMode.Locked) Bag.Action();
            Bag.CheckTossed();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

internal static class Bag
{
    private const float TossWindow = 4f;
    private static readonly List<(Interactable item, float until, bool bulky)> tossed = new();
    private static float nextTossCheck;
    private static string lastMessage;
    private static float lastMessageAt;

    /// <summary>Action key: break down a carried bulky item, or empty the bag at a bin.</summary>
    public static void Action()
    {
        var carried = InteractionController.Instance?.carryingObject;
        if (carried != null)
        {
            BreakDown(carried);
            return;
        }

        var looking = InteractionController.Instance?.currentLookingAtInteractable?.interactable;
        if (looking != null && Trash.IsSource(looking))
        {
            CollectFrom(looking);
            return;
        }
        if (looking == null || !Trash.IsBin(looking)) return;
        if (State.BagCount <= 0)
        {
            Message("Your bag is empty.");
            return;
        }
        var count = State.BagCount;
        State.BagCount = 0;
        Trash.Payout(count, count * Trash.Pay);
        Message($"Emptied the bag: {count} item{(count == 1 ? "" : "s")}");
    }

    /// <summary>Takes a little trash out of a dustbin; it refills after a while.</summary>
    private static void CollectFrom(Interactable source)
    {
        var left = Trash.SourceRefillLeft(source);
        if (left > 0f)
        {
            Message($"Already emptied. Check back in about {Mathf.CeilToInt(left)} hours.");
            return;
        }
        var room = State.BagCapacity - State.BagCount;
        if (room <= 0)
        {
            Message($"Bag is full ({State.BagCount}/{State.BagCapacity}). Empty it at a bin or dumpster.");
            return;
        }
        var amount = Mathf.Min(room, UnityEngine.Random.Range(1, 3)); // 1 or 2
        State.BagCount += amount;
        State.SourceEmptied[source.id] = SessionData.Instance.gameTime;
        Message($"Collected {amount} from the dustbin ({State.BagCount}/{State.BagCapacity})");
    }

    private static void BreakDown(InteractableController carried)
    {
        var item = carried.interactable;
        if (item == null || !Trash.IsJunk(item) || !Trash.IsBulky(item)) return;
        var pieces = Plugin.BreakdownPieces.Value;
        if (State.BagCount + pieces > State.BagCapacity)
        {
            Message($"Not enough room in the bag to break that down ({State.BagCount}/{State.BagCapacity}).");
            return;
        }
        carried.DropThis(false);
        item.SafeDelete();
        State.BagCount += pieces;
        Message($"Broke it down: +{pieces} ({State.BagCount}/{State.BagCapacity})");
    }

    public static void Track(Interactable item)
    {
        tossed.RemoveAll(t => t.item == null || t.item.id == item.id);
        tossed.Add((item, Time.unscaledTime + TossWindow, Trash.IsBulky(item)));
    }

    /// <summary>Has a dropped or thrown item landed in (or on) a bin or dumpster? Then it's disposed.</summary>
    public static void CheckTossed()
    {
        if (tossed.Count == 0 || Time.unscaledTime < nextTossCheck) return;
        nextTossCheck = Time.unscaledTime + 0.1f;

        for (int i = tossed.Count - 1; i >= 0; i--)
        {
            var (item, until, bulky) = tossed[i];
            var obj = item?.spawnedObject;
            if (obj == null || Time.unscaledTime > until)
            {
                tossed.RemoveAt(i);
                continue;
            }
            if (!TouchingBin(obj)) continue;

            tossed.RemoveAt(i);
            var pay = bulky ? Plugin.BulkyPay.Value : Trash.Pay;
            item.SafeDelete();
            Trash.Payout(1, pay);
            Message(bulky ? "Into the dumpster." : "In the bin.");
        }
    }

    private static bool TouchingBin(GameObject obj)
    {
        var hits = Physics.OverlapSphere(obj.transform.position, 0.6f, ~0, QueryTriggerInteraction.Collide);
        foreach (var c in hits)
        {
            if (c == null || c.transform.IsChildOf(obj.transform)) continue;
            var controller = c.GetComponentInParent<InteractableController>();
            if (controller != null && Trash.IsBin(controller.interactable)) return true;
            if (c.gameObject.CompareTag("Garbage")) return true;
        }
        return false;
    }

    /// <summary>Notifications, without repeating the same one in quick succession.</summary>
    public static void Message(string text)
    {
        if (text == lastMessage && Time.unscaledTime - lastMessageAt < 2f) return;
        lastMessage = text;
        lastMessageAt = Time.unscaledTime;
        InterfaceController.Instance?.NewGameMessage(InterfaceController.GameMessageType.notification, 0, text, InterfaceControls.Icon.trash);
        Plugin.Logger.LogInfo(text);
    }
}
