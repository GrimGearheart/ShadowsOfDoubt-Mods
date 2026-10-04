using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace Prone;

/// <summary>
/// Lie down on the floor to look under desks, beds and dressers, and crawl slowly.
/// Built on top of the game's crouch: you stay crouched (keeping its stealth benefits) while this
/// mod shrinks the player's capsule and lowers the camera further, using the game's own
/// Player.SetPlayerHeight / SetCameraHeight.
/// </summary>
[BepInPlugin("sodmods.prone", "Prone", "1.0.1")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<float> HoldTime;
    internal static ConfigEntry<KeyCode> ProneKey;
    internal static ConfigEntry<float> BodyHeight;
    internal static ConfigEntry<float> EyeHeight;
    internal static ConfigEntry<float> CrawlSpeed;
    internal static ConfigEntry<float> TransitionTime;

    public override void Load()
    {
        Logger = Log;
        HoldTime = Config.Bind("Controls", "HoldCrouchSeconds", 0.5f,
            "Hold Crouch this long to lie down. 0 = disable the hold gesture (use ProneKey instead).");
        ProneKey = Config.Bind("Controls", "ProneKey", KeyCode.None,
            "Optional dedicated key to lie down / get back up to a crouch.");
        BodyHeight = Config.Bind("Prone", "BodyHeight", 0.6f, "Height of your body (metres) while lying down.");
        EyeHeight = Config.Bind("Prone", "EyeHeight", 0.25f, "Height of your eyes above the floor (metres) while lying down.");
        CrawlSpeed = Config.Bind("Prone", "CrawlSpeed", 0.35f, "Movement speed while lying down, as a fraction of walking speed.");
        TransitionTime = Config.Bind("Prone", "TransitionSeconds", 0.7f, "How long getting down or up takes.");

        new Harmony("sodmods.prone").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Prone loaded");
    }
}

internal static class ProneState
{
    /// <summary>Whether the player wants to be lying down.</summary>
    internal static bool Prone;
    /// <summary>0 = crouched, 1 = fully lying down; eased towards the target each frame.</summary>
    internal static float Amount;

    internal static void Enter(Player player)
    {
        if (Prone) return;
        if (!player.isCrouched) player.SetCrouched(true);
        Prone = true;
        player.RestorePlayerMovementSpeed();
    }

    /// <summary>Back up to a crouch, if there's room. Returns false if blocked.</summary>
    internal static bool TryExit(Player player, bool quiet = false)
    {
        if (!Prone) return true;
        if (!Room.ToCrouch(player))
        {
            if (!quiet)
                InterfaceController.Instance?.NewGameMessage(InterfaceController.GameMessageType.notification, 0,
                    "There's no room to get up here", InterfaceControls.Icon.hand);
            return false;
        }
        Prone = false;
        player.RestorePlayerMovementSpeed();
        return true;
    }

    /// <summary>Drops out of prone without a room check, for vents, hiding and scripted moves.</summary>
    internal static void Reset()
    {
        Prone = false;
        Amount = 0f;
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class ProneUpdatePatch
{
    private static float crouchDownAt = -1f;
    private static bool holdConsumed;

    private static void Postfix(Player __instance)
    {
        try
        {
            var player = __instance;
            if (SessionData.Instance == null || !SessionData.Instance.play) return;

            // The game takes over the player's height in these situations.
            if (player.inAirVent || player.transitionActive)
            {
                if (ProneState.Prone || ProneState.Amount > 0f) ProneState.Reset();
                return;
            }

            HandleInput(player);

            var target = ProneState.Prone ? 1f : 0f;
            if (ProneState.Amount == target && target == 0f) return;

            var step = Time.deltaTime / Mathf.Max(0.05f, Plugin.TransitionTime.Value);
            ProneState.Amount = Mathf.MoveTowards(ProneState.Amount, target, step);
            Apply(player, Mathf.SmoothStep(0f, 1f, ProneState.Amount));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    private static void HandleInput(Player player)
    {
        var input = InputController.Instance?.player;
        if (input == null) return;
        var locked = InteractionController.Instance != null && InteractionController.Instance.lockedInInteraction != null;

        // Hold Crouch: the game crouches you on the press; still holding after HoldTime lies you down.
        var hold = Plugin.HoldTime.Value;
        if (hold > 0f)
        {
            if (input.GetButtonDown("Crouch"))
            {
                crouchDownAt = Time.unscaledTime;
                holdConsumed = false;
            }
            if (!input.GetButton("Crouch")) crouchDownAt = -1f;
            if (crouchDownAt > 0f && !holdConsumed && !locked && !ProneState.Prone &&
                Time.unscaledTime - crouchDownAt >= hold)
            {
                holdConsumed = true;
                ProneState.Enter(player);
            }
        }

        var key = Plugin.ProneKey.Value;
        if (key != KeyCode.None && Input.GetKeyDown(key) && !locked)
        {
            if (ProneState.Prone) ProneState.TryExit(player);
            else ProneState.Enter(player);
        }

        // Something else stood the player up (e.g. the game uncrouched them): follow it.
        if (ProneState.Prone && !player.isCrouched) ProneState.TryExit(player, quiet: true);
    }

    /// <summary>Blends body and camera height between the game's crouch values and prone.</summary>
    private static void Apply(Player player, float t)
    {
        var cc = player.charController;
        var crouchHeight = player.GetPlayerHeightCrouched();
        var proneHeight = Mathf.Max(Plugin.BodyHeight.Value, cc.radius * 2f + 0.02f);
        player.SetPlayerHeight(Mathf.Lerp(crouchHeight, proneHeight, t));

        // SetCameraHeight is relative to the player's origin, which sits half a standing height
        // above the floor (the game keeps the feet on the floor as the capsule shrinks).
        var proneCamera = Plugin.EyeHeight.Value - player.GetPlayerHeightNormal() * 0.5f;
        player.SetCameraHeight(Mathf.Lerp(GameplayControls.Instance.cameraHeightCrouched, proneCamera, t));
    }
}

/// <summary>Pressing Crouch while lying down gets you up to a crouch instead of straight to standing.</summary>
[HarmonyPatch(typeof(Actor), nameof(Actor.SetCrouched))]
internal static class SetCrouchedPatch
{
    private static bool Prefix(Actor __instance, bool newVal)
    {
        if (newVal || !ProneState.Prone || !__instance.isPlayer) return true;
        var player = __instance.TryCast<Player>();
        if (player == null || player.inAirVent || player.transitionActive) return true;
        ProneState.TryExit(player);
        return false; // stay crouched either way
    }
}

/// <summary>Crawling speed while lying down; no running.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.SetMaxSpeed))]
internal static class CrawlSpeedPatch
{
    private static void Prefix(Player __instance, ref float newWalkSpeed, ref float newRunSpeed)
    {
        if (!ProneState.Prone || __instance.transitionActive) return;
        newWalkSpeed *= Plugin.CrawlSpeed.Value;
        newRunSpeed = newWalkSpeed;
    }
}

/// <summary>No jumping while lying down.</summary>
[HarmonyPatch(typeof(FirstPersonController), nameof(FirstPersonController.UpdateMovement))]
internal static class NoJumpPatch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (ProneState.Prone || ProneState.Amount > 0f) __instance.m_Jump = false;
    }
}

internal static class Room
{
    /// <summary>Is there room for a crouched body where the player is lying?</summary>
    public static bool ToCrouch(Player player)
    {
        var cc = player.charController;
        var t = player.transform;
        var feet = t.position + Vector3.down * (player.GetPlayerHeightNormal() * 0.5f);
        var height = player.GetPlayerHeightCrouched();
        var bottom = feet + Vector3.up * (cc.radius + 0.05f);
        var top = feet + Vector3.up * (height - cc.radius);
        var hits = Physics.OverlapCapsule(bottom, top, cc.radius * 0.9f, Toolbox.Instance.playerMovementLayerMask,
            QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            if (c == null || c == cc || c.transform.IsChildOf(t)) continue;
            return false;
        }
        return true;
    }
}
