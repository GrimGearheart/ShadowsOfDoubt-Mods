using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ScrollThrottle;

/// <summary>
/// Star Citizen style throttle: the mouse wheel sets a persistent movement speed multiplier.
/// The multiplier is applied on top of whatever speed the game asks for (Player.SetMaxSpeed),
/// so crouching, injuries, air vents and speed sync disks keep working, just scaled.
/// </summary>
[BepInPlugin("sodmods.scrollthrottle", "Scroll Throttle", "1.1.2")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<float> Throttle;
    internal static ConfigEntry<float> MinThrottle;
    internal static ConfigEntry<float> MaxThrottle;
    internal static ConfigEntry<float> Step;
    internal static ConfigEntry<bool> ApplyToRun;
    internal static ConfigEntry<bool> RememberThrottle;
    internal static ConfigEntry<KeyCode> Modifier;
    internal static ConfigEntry<KeyCode> ResetKey;
    internal static ConfigEntry<bool> InvertScroll;
    internal static ConfigEntry<bool> ShowMessages;
    internal static ConfigEntry<bool> Detent;
    internal static ConfigEntry<float> DetentTime;

    public override void Load()
    {
        Logger = Log;
        Throttle = Config.Bind("Throttle", "Current", 1f,
            "Current throttle (1 = normal speed). Saved between sessions if RememberThrottle is true.");
        MinThrottle = Config.Bind("Throttle", "Minimum", 0.2f, "Lowest throttle the wheel can set.");
        MaxThrottle = Config.Bind("Throttle", "Maximum", 1.5f, "Highest throttle the wheel can set.");
        Step = Config.Bind("Throttle", "Step", 0.1f, "Throttle change per wheel notch.");
        ApplyToRun = Config.Bind("Throttle", "ApplyToRun", true,
            "Scale running speed too. If false, only walking is affected.");
        RememberThrottle = Config.Bind("Throttle", "RememberThrottle", true,
            "Keep the throttle between game sessions. If false it starts at 100% each launch.");
        Modifier = Config.Bind("Controls", "ModifierKey", KeyCode.None,
            "Key that must be held while scrolling. None = the wheel alone controls the throttle.");
        ResetKey = Config.Bind("Controls", "ResetKey", KeyCode.None,
            "Key that resets the throttle to 100%. None to disable. Note: middle mouse (Mouse2) is the game's flashlight by default.");
        InvertScroll = Config.Bind("Controls", "InvertScroll", false, "Scroll down to speed up instead.");
        Detent = Config.Bind("Controls", "DetentAt100", true,
            "Pause at 100% when scrolling through it, so a quick flick always stops at normal speed.");
        DetentTime = Config.Bind("Controls", "DetentSeconds", 0.35f,
            "How long the throttle holds at 100% before further scrolling in the same direction continues.");
        ShowMessages = Config.Bind("Display", "ShowMessages", true, "Show an on-screen speed notification.");
        var configVersion = Config.Bind("Internal", "ConfigVersion", 1, "Used to migrate old settings. Don't edit.");

        // 1.0.0 defaulted ResetKey to middle mouse, which also toggles the flashlight.
        if (configVersion.Value < 2)
        {
            if (ResetKey.Value == KeyCode.Mouse2) ResetKey.Value = KeyCode.None;
            configVersion.Value = 2;
        }

        if (!RememberThrottle.Value) Throttle.Value = 1f;
        Throttle.Value = Mathf.Clamp(Throttle.Value, MinThrottle.Value, MaxThrottle.Value);

        new Harmony("sodmods.scrollthrottle").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"Scroll Throttle loaded (throttle {Throttle.Value:P0})");
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetMaxSpeed))]
internal static class SetMaxSpeedPatch
{
    // The unscaled speeds the game last requested, so a throttle change can re-apply them.
    internal static float BaseWalk = -1f, BaseRun = -1f;

    private static void Prefix(Player __instance, ref float newWalkSpeed, ref float newRunSpeed)
    {
        BaseWalk = newWalkSpeed;
        BaseRun = newRunSpeed;
        // Scripted transitions (ladders, vaulting etc.) use fixed speeds; leave those alone.
        if (__instance.transitionActive) return;
        var t = Plugin.Throttle.Value;
        newWalkSpeed *= t;
        if (Plugin.ApplyToRun.Value) newRunSpeed *= t;
    }

    internal static void Reapply()
    {
        var player = Player.Instance;
        if (player == null || BaseWalk < 0f || player.transitionActive) return;
        player.SetMaxSpeed(BaseWalk, BaseRun);
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdatePatch
{
    private static float pendingMessageAt = -1f;

    private static void Postfix()
    {
        try
        {
            if (!InFreeMovement()) return;

            var modifier = Plugin.Modifier.Value;
            if (modifier != KeyCode.None && !Input.GetKey(modifier)) return;

            var reset = Plugin.ResetKey.Value;
            if (reset != KeyCode.None && Input.GetKeyDown(reset))
            {
                SetThrottle(1f);
                return;
            }

            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                var notches = Mathf.Sign(scroll) * (Plugin.InvertScroll.Value ? -1f : 1f);
                Scroll(notches);
            }

            // One notification after the wheel settles, rather than one per notch.
            if (pendingMessageAt > 0f && Time.unscaledTime >= pendingMessageAt)
            {
                pendingMessageAt = -1f;
                ShowMessage();
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    /// <summary>
    /// Applies one wheel notch, with a detent at 100%: passing through normal speed stops there,
    /// and continued scrolling keeps it held until the wheel has been still for DetentSeconds.
    /// </summary>
    private static void Scroll(float direction)
    {
        var now = Time.unscaledTime;
        var current = Plugin.Throttle.Value;
        var target = current + direction * Plugin.Step.Value;

        if (Plugin.Detent.Value)
        {
            var atNormal = Mathf.Abs(current - 1f) < 0.001f;
            if (atNormal && now < detentUntil)
            {
                detentUntil = now + Plugin.DetentTime.Value;
                return;
            }
            if ((current < 1f && target > 1f) || (current > 1f && target < 1f)) target = 1f;
            if (!atNormal && Mathf.Abs(target - 1f) < 0.001f) detentUntil = now + Plugin.DetentTime.Value;
        }

        SetThrottle(target);
    }

    private static float detentUntil = -1f;

    private static void SetThrottle(float value)
    {
        var clamped = Mathf.Clamp(Mathf.Round(value * 100f) / 100f, Plugin.MinThrottle.Value, Plugin.MaxThrottle.Value);
        if (Mathf.Approximately(clamped, Plugin.Throttle.Value)) return;
        Plugin.Throttle.Value = clamped;
        SetMaxSpeedPatch.Reapply();
        pendingMessageAt = Time.unscaledTime + 0.35f;
    }

    private static void ShowMessage()
    {
        if (!Plugin.ShowMessages.Value || InterfaceController.Instance == null) return;
        InterfaceController.Instance.NewGameMessage(InterfaceController.GameMessageType.notification, 0,
            $"Speed {Plugin.Throttle.Value:P0}", InterfaceControls.Icon.run);
    }

    /// <summary>True while the player is walking around in first person (not in menus, map,
    /// documents, computers or conversations), so the wheel keeps its normal UI uses.</summary>
    private static bool InFreeMovement()
    {
        if (SessionData.Instance == null || !SessionData.Instance.play) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;
        if (InterfaceController.Instance != null && InterfaceController.Instance.desktopMode) return false;
        if (InteractionController.Instance != null && InteractionController.Instance.dialogMode) return false;
        return true;
    }
}
