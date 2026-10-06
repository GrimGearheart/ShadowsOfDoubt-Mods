using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Key = InteractablePreset.InteractionKey;

namespace SitAnywhere;

/// <summary>
/// Puts "Sit", "Get Up", "Pass Time" and the alarm controls into the game's on-screen control
/// hints, the same way the game shows "Drag" for bodies: an action with only a name and no
/// interactable behind it. Only keys the game leaves free are used, so anything you're actually
/// looking at (a door, a phone, a person) keeps its normal actions. Pressing the key does nothing
/// in the game for such an action, so this mod handles the press itself.
///
/// The game rebuilds every key's hint whenever your view changes, clearing the free ones and then
/// redrawing the list. Our hints are slipped in as the game clears each key (with the same action
/// object every time), so the redraw sees them unchanged and doesn't fade them out and back in.
/// </summary>
internal static class Prompts
{
    private sealed class Hint
    {
        public string Name;
        /// <summary>Keys to try, in the game's key order; the first one free gets the hint.</summary>
        public Key[] Keys;
    }

    private static readonly Dictionary<Key, string> Buttons = new()
    {
        [Key.primary] = "Primary",
        [Key.secondary] = "Secondary",
        [Key.alternative] = "Alternative",
        [Key.scrollAxisUp] = "ScrollAxisUp",
        [Key.scrollAxisDown] = "ScrollAxisDown",
        [Key.jump] = "Jump",
    };

    private static readonly Dictionary<(Key, string), Interactable.InteractableCurrentAction> actions = new();
    private static readonly List<Hint> wanted = new();
    /// <summary>Which key each hint landed on in the last rebuild.</summary>
    private static readonly Dictionary<Key, string> placed = new();
    private static bool rebuilding;
    private static bool seatAvailable;
    private static bool wasSmoking;
    private static float nextScan;

    /// <summary>Re-checks for somewhere to sit a few times a second; redraws the hints when that changes.</summary>
    public static void Scan(Player p)
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.1f;

        var available = CanLookForSeat(p) && SeatFinder.TryFind(p, out _);
        if (Plugin.DebugLogging.Value && !available && SeatFinder.LastFailure != null && InputController.Instance?.player?.GetButton("Primary") == true)
            Plugin.Logger.LogInfo("Sit: " + SeatFinder.LastFailure);
        if (available == seatAvailable) return;
        seatAvailable = available;
        Refresh();
    }

    /// <summary>Lighting up or finishing a smoke moves Sit/Pass Time off Interact and back.</summary>
    public static void WatchSmoking()
    {
        var smoking = Smoking();
        if (smoking == wasSmoking) return;
        wasSmoking = smoking;
        Refresh();
    }

    public static void Refresh()
    {
        if (InteractionController.Instance != null) InteractionController.Instance.UpdateInteractionText();
    }

    private static bool Smoking() => FirstPersonItemController.Instance != null && FirstPersonItemController.Instance.smokingActive > 0;

    private static bool CanLookForSeat(Player p)
    {
        var ic = InteractionController.Instance;
        return p.fps != null && p.fps.enableMovement && p.charController != null && p.charController.enabled &&
               p.charController.isGrounded && !p.transitionActive && !p.inAirVent && !p.autoTravelActive &&
               !p.playerKOInProgress && p.currentVehicle == null && ic != null && ic.lockedInInteraction == null && ic.carryingObject == null &&
               !ic.dialogMode && ic.currentlyDragging == null &&
               !(ModifiersController.Instance != null && ModifiersController.Instance.ratDetectiveModifierEnabled);
    }

    private static bool HintsAllowed()
    {
        var ic = InteractionController.Instance;
        return ic != null && !ic.dialogMode && ic.currentlyDragging == null &&
               InterfaceController.Instance != null && !InterfaceController.Instance.desktopMode &&
               BioScreenController.Instance != null && !BioScreenController.Instance.isOpen &&
               Player.Instance != null && !Player.Instance.autoTravelActive;
    }

    /// <summary>Works out which hints to show, before the game rebuilds its key list.</summary>
    public static void BeginRebuild()
    {
        rebuilding = true;
        wanted.Clear();
        placed.Clear();
        if (!HintsAllowed()) return;
        var p = Player.Instance;

        // A drag on a cigarette is Interact whenever nothing else is using it, so while smoking
        // our main action moves to the right mouse button (or Alternative, if that's taken).
        var main = Smoking() ? new[] { Key.secondary, Key.alternative } : new[] { Key.primary };

        switch (Seated.Phase)
        {
            case Phase.None:
                if (seatAvailable) Want("Sit", main);
                break;
            case Phase.Seated when p.setAlarmMode:
                Want("Pass Time", Key.primary); // drags are blocked while setting the alarm
                Want("Cancel", Key.secondary);
                Want("Switch Hours/Minutes", Key.alternative);
                Want("Alarm Forward", Key.scrollAxisUp);
                Want("Alarm Back", Key.scrollAxisDown);
                break;
            case Phase.Seated when p.spendingTimeMode || p.spendingTimeDelay > 0f:
                Want("Cancel", Key.secondary);
                Want("Get Up", Key.jump);
                break;
            case Phase.Seated:
                if (Plugin.PassTime.Value) Want("Pass Time", main);
                Want("Get Up", Key.jump);
                break;
        }
    }

    public static void EndRebuild() => rebuilding = false;

    /// <summary>The game is setting <paramref name="key"/>; if it's leaving it empty and we want it, fill it.</summary>
    public static void OnSetKey(Key key, ref Interactable.InteractableCurrentAction action)
    {
        if (action != null)
        {
            if (rebuilding) return;
            placed.Remove(key);
            return;
        }

        if (!rebuilding)
        {
            // A one-off clear outside a rebuild (e.g. a timed control tip expiring): keep ours.
            if (placed.TryGetValue(key, out var kept)) action = Action(key, kept);
            return;
        }

        foreach (var hint in wanted)
        {
            if (placed.ContainsValue(hint.Name) || Array.IndexOf(hint.Keys, key) < 0) continue;
            placed[key] = hint.Name;
            action = Action(key, hint.Name);
            return;
        }
    }

    private static void Want(string name, params Key[] keys) => wanted.Add(new Hint { Name = name, Keys = keys });

    private static Interactable.InteractableCurrentAction Action(Key key, string name)
    {
        if (!actions.TryGetValue((key, name), out var action))
        {
            action = new Interactable.InteractableCurrentAction
            {
                display = true,
                enabled = true,
                overrideInteractionName = name,
            };
            actions[(key, name)] = action;
        }
        return action;
    }

    /// <summary>True if <paramref name="name"/> is showing and its key was just pressed.</summary>
    private static bool Pressed(string name)
    {
        var ic = InteractionController.Instance;
        foreach (var entry in placed)
        {
            if (entry.Value != name || !Buttons.TryGetValue(entry.Key, out var button)) continue;
            if (!ic.currentInteractions.TryGetValue(entry.Key, out var setting) || setting?.currentSetting == null) continue;
            if (setting.currentSetting.Pointer != Action(entry.Key, name).Pointer) continue;
            if (InputController.Instance.player.GetButtonDown(button)) return true;
        }
        return false;
    }

    /// <summary>Handles presses on our hints.</summary>
    public static void HandleInput()
    {
        if (placed.Count == 0 || !HintsAllowed()) return;
        var ic = InteractionController.Instance;
        if (ic.inputCooldown > 0f || InterfaceController.Instance.playerTextInputActive) return;
        if (InputController.Instance?.player == null) return;
        var p = Player.Instance;

        switch (Seated.Phase)
        {
            case Phase.None:
                if (Pressed("Sit"))
                {
                    if (SeatFinder.TryFind(p, out var seat)) Seated.SitDown(p, seat);
                    else Refresh();
                    ic.inputCooldown = 0.1f;
                }
                break;

            case Phase.Seated when p.setAlarmMode:
                if (Pressed("Pass Time"))
                {
                    // As the game's chair does: confirm the alarm, put the watch away, start the clock.
                    AudioController.Instance.PlayWorldOneShot(AudioControls.Instance.setAlarm, p, p.currentNode, p.lookAtThisTransform.position);
                    p.SetSettingAlarmMode(false);
                    SelectSlot(FirstPersonItemController.InventorySlot.StaticSlot.holster);
                    p.spendingTimeDelay = 0.5f;
                    Done(ic);
                }
                else if (Pressed("Alarm Forward")) ActionController.Instance.WatchForward(null, null, null);
                else if (Pressed("Alarm Back")) ActionController.Instance.WatchBack(null, null, null);
                else if (Pressed("Switch Hours/Minutes")) ActionController.Instance.HoursMinutesToggle(null, null, null);
                else if (Pressed("Cancel"))
                {
                    p.SetSettingAlarmMode(false);
                    SelectSlot(FirstPersonItemController.InventorySlot.StaticSlot.holster);
                    Done(ic);
                }
                break;

            case Phase.Seated when p.spendingTimeMode || p.spendingTimeDelay > 0f:
                if (Pressed("Cancel"))
                {
                    p.spendingTimeDelay = 0f;
                    if (p.spendingTimeMode) p.SetSpendingTimeMode(false);
                    Done(ic);
                }
                break;

            case Phase.Seated:
                if (Pressed("Pass Time"))
                {
                    SelectSlot(FirstPersonItemController.InventorySlot.StaticSlot.watch);
                    p.SetSettingAlarmMode(true);
                    Done(ic);
                }
                break;
        }
    }

    private static void Done(InteractionController ic)
    {
        ic.inputCooldown = 0.1f;
        Refresh();
    }

    private static void SelectSlot(FirstPersonItemController.InventorySlot.StaticSlot which)
    {
        var fpic = FirstPersonItemController.Instance;
        if (fpic == null || BioScreenController.Instance == null) return;
        foreach (var slot in fpic.slots)
        {
            if (slot == null || slot.isStatic != which) continue;
            BioScreenController.Instance.SelectSlot(slot);
            return;
        }
    }
}

[HarmonyPatch(typeof(InteractionController), nameof(InteractionController.UpdateInteractionText), new Type[0])]
internal static class HintsPatch
{
    private static void Prefix()
    {
        try
        {
            Prompts.BeginRebuild();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    private static void Finalizer() => Prompts.EndRebuild();
}

[HarmonyPatch(typeof(InteractionController), nameof(InteractionController.SetCurrentPlayerInteraction))]
internal static class SetKeyPatch
{
    private static void Prefix(Key key, ref Interactable.InteractableCurrentAction newCurrentAction)
    {
        try
        {
            Prompts.OnSetKey(key, ref newCurrentAction);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

/// <summary>No drag on a cigarette while you're setting the alarm (Interact confirms it).</summary>
[HarmonyPatch(typeof(FirstPersonItemController), nameof(FirstPersonItemController.StartSmokeToke))]
internal static class NoTokeWhileSettingAlarmPatch
{
    private static bool Prefix() => !(Seated.Phase == Phase.Seated && Player.Instance != null && Player.Instance.setAlarmMode);
}

[HarmonyPatch(typeof(InteractionController), "Update")]
internal static class InputPatch
{
    private static float nextErrorLog;

    private static void Postfix()
    {
        try
        {
            if (SessionData.Instance == null || !SessionData.Instance.play) return;
            if (Player.Instance == null || Player.Instance.transitionActive) return;
            Prompts.WatchSmoking();
            Prompts.HandleInput();
        }
        catch (Exception e)
        {
            if (Time.unscaledTime >= nextErrorLog)
            {
                nextErrorLog = Time.unscaledTime + 10f;
                Plugin.Logger.LogError(e);
            }
        }
    }
}
