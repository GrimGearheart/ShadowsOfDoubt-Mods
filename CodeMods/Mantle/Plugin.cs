using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace Mantle;

/// <summary>
/// Jump at a ledge while holding forward and you pull yourself up onto it.
/// All player movement goes through FirstPersonController.UpdateMovement (input, gravity, then
/// CharacterController.Move). While mantling, this mod runs that step itself: up to ledge height,
/// then forward onto it, still through CharacterController.Move so walls can't be clipped through.
/// </summary>
[BepInPlugin("sodmods.mantle", "Mantle", "1.0.2")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<float> MinHeight;
    internal static ConfigEntry<float> MaxHeight;
    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<float> Duration;
    internal static ConfigEntry<float> VaultDuration;
    internal static ConfigEntry<float> SprintSpeedup;
    internal static ConfigEntry<bool> RequireForward;
    internal static ConfigEntry<float> DipAngle;
    internal static ConfigEntry<float> RollAngle;
    internal static ConfigEntry<float> MaxSlope;
    internal static ConfigEntry<bool> EnableVault;
    internal static ConfigEntry<float> MaxVaultDrop;
    internal static ConfigEntry<bool> DebugLogging;

    public override void Load()
    {
        Logger = Log;
        MinHeight = Config.Bind("Mantle", "MinLedgeHeight", 0.5f,
            "Lowest ledge (metres above your feet) that triggers a mantle. Lower things you can just step or jump onto.");
        MaxHeight = Config.Bind("Mantle", "MaxLedgeHeight", 2.0f,
            "Highest ledge (metres above your feet) you can pull yourself onto. Measured from where your feet are at that moment, so jumping first reaches higher.");
        Reach = Config.Bind("Mantle", "Reach", 0.6f, "How far in front of you (metres) a ledge can be.");
        Duration = Config.Bind("Mantle", "Duration", 1.2f, "How long climbing onto a ledge takes, in seconds.");
        VaultDuration = Config.Bind("Vault", "Duration", 1.0f, "How long vaulting over an obstacle takes, in seconds.");
        SprintSpeedup = Config.Bind("Mantle", "SprintMultiplier", 0.85f,
            "Duration multiplier when you hit the ledge while sprinting (0.85 = 15% quicker; 1 = no change).");
        MaxSlope = Config.Bind("Mantle", "MaxSurfaceSlope", 50f,
            "Steepest top surface (degrees) you can climb onto, e.g. sloped lids and car trunks.");
        EnableVault = Config.Bind("Vault", "Enabled", true,
            "Vault over thin obstacles (fences, railings) instead of only climbing onto things.");
        MaxVaultDrop = Config.Bind("Vault", "MaxDrop", 4f,
            "Furthest drop (metres below the top) allowed on the far side of a vault. Drops up to about 4m do not risk fall damage.");
        DipAngle = Config.Bind("Camera", "DipAngle", 9f,
            "How far (degrees) your view tips down as you haul yourself up. 0 = no dip.");
        RollAngle = Config.Bind("Camera", "RollAngle", 3f,
            "How far (degrees) your view tilts sideways during the climb. 0 = no tilt.");
        RequireForward = Config.Bind("Controls", "RequireForward", true,
            "Only mantle while holding forward, so a plain jump in front of a wall stays a jump.");
        DebugLogging = Config.Bind("Debug", "LogMantles", false, "Log ledge detection results (for tuning and bug reports).");

        new Harmony("sodmods.mantle").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Mantle loaded");
        PhysicsSelfTest();
    }

    /// <summary>
    /// IL2CPP builds strip engine methods the game never calls, which then fail at runtime.
    /// Call every physics query this mod uses once, so a missing one shows up immediately in the log.
    /// </summary>
    private void PhysicsSelfTest()
    {
        try
        {
            var far = new Vector3(0f, -10000f, 0f);
            Physics.Raycast(far, Vector3.down, out RaycastHit _, 1f, ~0, QueryTriggerInteraction.Ignore);
            Physics.CheckSphere(far, 0.1f, ~0, QueryTriggerInteraction.Ignore);
            Physics.OverlapCapsule(far, far + Vector3.up, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            Log.LogInfo("Physics self-test OK");
        }
        catch (Exception e)
        {
            Log.LogError("Physics self-test FAILED, mantling will not work: " + e.Message);
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), nameof(FirstPersonController.UpdateMovement))]
internal static class MantlePatch
{
    private static bool active;
    private static Vector3 startCenter, liftCenter, endCenter;
    private static float elapsed;
    private static int stuckSteps;
    private static float cooldownUntil;
    private static float nextErrorLog;

    private static bool Prefix(FirstPersonController __instance)
    {
        try
        {
            var cc = __instance.m_CharacterController;
            if (cc == null || !cc.enabled)
            {
                active = false;
                return true;
            }

            var dt = Game.Instance.updateMovementEveryFrame ? Time.smoothDeltaTime : Time.fixedDeltaTime;
            if (active)
            {
                Step(__instance, cc, dt);
                return false;
            }

            if (!CanStart(__instance, cc)) return true;
            if (!Ledge.TryFind(__instance.transform, cc, out var target, out var vault)) return true;

            Begin(__instance, cc, target, vault);
            Step(__instance, cc, dt);
            return false;
        }
        catch (Exception e)
        {
            // Rate-limited: an error every physics tick would flood the log and stutter the game.
            if (Time.unscaledTime >= nextErrorLog)
            {
                nextErrorLog = Time.unscaledTime + 10f;
                Plugin.Logger.LogError(e);
            }
            active = false;
            return true;
        }
    }

    private static bool hookLogged;
    private static float nextReasonLog;

    private static bool CanStart(FirstPersonController fpc, CharacterController cc)
    {
        if (!hookLogged)
        {
            hookLogged = true;
            Plugin.Logger.LogInfo("Movement hook active");
        }

        var input = InputController.Instance?.player;
        var jumpHeld = input != null && input.GetButton("Jump");
        string reason = null;

        if (Time.unscaledTime < cooldownUntil) reason = "cooldown";
        else if (SessionData.Instance == null || !SessionData.Instance.startedGame || !SessionData.Instance.play) reason = "game not running";
        else if (!fpc.enableMovement || fpc.ghostMovement || !fpc.clipping) reason = "movement disabled/ghost";
        else if (Player.Instance == null || Player.Instance.transitionActive || Player.Instance.inAirVent) reason = "transition or vent";
        else if (ModifiersController.Instance != null && ModifiersController.Instance.ratDetectiveActive) reason = "rat mode";
        else if (InteractionController.Instance != null && InteractionController.Instance.lockedInInteraction != null) reason = "locked in interaction";
        else if (StatusController.Instance != null && StatusController.Instance.disabledJump) reason = "jump disabled";
        else if (!jumpHeld) reason = "jump not held";
        else if (Plugin.RequireForward.Value && Forward(input) < 0.3f) reason = $"forward not held ({Forward(input):0.00})";
        // Airborne (mid-jump or falling), or a jump that hasn't been executed yet.
        else if (cc.isGrounded && !fpc.m_Jump) reason = "grounded, no jump pending";

        // Only report when the player is actually trying (holding jump), to keep the log readable.
        if (reason != null && jumpHeld && Plugin.DebugLogging.Value && Time.unscaledTime >= nextReasonLog)
        {
            nextReasonLog = Time.unscaledTime + 0.5f;
            Plugin.Logger.LogInfo("Mantle: not starting: " + reason);
        }
        return reason == null;
    }

    /// <summary>Forward input, read the way FirstPersonController.GetInput does: separate
    /// MoveForward/MoveBack actions on mouse and keyboard, MoveVertical on a controller.</summary>
    private static float Forward(Rewired.Player input)
    {
        if (InputController.Instance != null && !InputController.Instance.mouseInputMode)
            return input.GetAxis("MoveVertical");
        return input.GetAxis("MoveForward") + input.GetAxis("MoveBack");
    }

    private static float duration;

    private static void Begin(FirstPersonController fpc, CharacterController cc, Vector3 target, bool vault)
    {
        duration = vault ? Plugin.VaultDuration.Value : Plugin.Duration.Value;
        if (Player.Instance != null && Player.Instance.isRunning) duration *= Plugin.SprintSpeedup.Value;
        duration = Mathf.Max(0.1f, duration);
        CameraDip.Begin(duration, vault);
        startCenter = Ledge.Center(fpc.transform, cc);
        endCenter = target;
        // Rise straight up to just above the ledge, then move over it.
        liftCenter = new Vector3(startCenter.x, endCenter.y, startCenter.z);
        elapsed = 0f;
        stuckSteps = 0;
        active = true;
        fpc.m_Jump = false;
        if (Plugin.DebugLogging.Value)
            Plugin.Logger.LogInfo($"Mantle: from {startCenter} up to {liftCenter} onto {endCenter}");
    }

    private static void Step(FirstPersonController fpc, CharacterController cc, float dt)
    {
        elapsed += dt;
        var t = Mathf.Clamp01(elapsed / duration);

        // First 55% of the time rises, the rest moves forward; both eased.
        Vector3 desired;
        const float split = 0.55f;
        if (t < split)
            desired = Vector3.Lerp(startCenter, liftCenter, Mathf.SmoothStep(0f, 1f, t / split));
        else
            desired = Vector3.Lerp(liftCenter, endCenter, Mathf.SmoothStep(0f, 1f, (t - split) / (1f - split)));

        var before = Ledge.Center(fpc.transform, cc);
        cc.Move(desired - before);
        var after = Ledge.Center(fpc.transform, cc);

        fpc.m_MoveDir = Vector3.zero;
        Player.Instance?.UpdateMovementPhysics();

        // Blocked by something unexpected: give control back instead of fighting it.
        if ((desired - after).sqrMagnitude > 0.15f * 0.15f) stuckSteps++;
        else stuckSteps = 0;

        if (t >= 1f || stuckSteps >= 4)
        {
            if (stuckSteps >= 4 && Plugin.DebugLogging.Value) Plugin.Logger.LogInfo("Mantle: blocked, cancelled");
            active = false;
            fpc.m_Jumping = false;
            fpc.m_MoveDir = new Vector3(0f, -fpc.m_StickToGroundForce, 0f);
            cooldownUntil = Time.unscaledTime + 0.25f;
        }
    }
}


/// <summary>
/// Camera dip during a mantle: the view tips down and leans slightly as you haul yourself up, then
/// settles. Applied to the camera's parent (Player.camHeightParent), which the game leaves at zero
/// rotation outside scripted transitions, so it never fights mouse look. Runs per frame, not per
/// physics tick, so it stays smooth on high-refresh screens.
/// </summary>
[HarmonyPatch(typeof(Player), "Update")]
internal static class CameraDip
{
    private static bool active;
    private static float startTime, length, rollSign;
    private static bool isVault;

    public static void Begin(float duration, bool vault)
    {
        active = true;
        startTime = Time.time;
        length = duration;
        isVault = vault;
        rollSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
    }

    private static void Postfix(Player __instance)
    {
        if (!active) return;
        var parent = __instance.camHeightParent;
        if (parent == null || __instance.transitionActive)
        {
            active = false;
            return;
        }

        var t = Mathf.Clamp01((Time.time - startTime) / length);
        if (t >= 1f)
        {
            parent.localRotation = Quaternion.identity;
            active = false;
            return;
        }

        // Dip peaks early (while hauling up) and eases back as you come over the top.
        var dip = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
        var pitch = Plugin.DipAngle.Value * dip * (isVault ? 0.7f : 1f);
        var roll = Plugin.RollAngle.Value * rollSign * Mathf.Sin(Mathf.PI * t);
        parent.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }
}
