using System;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace SitAnywhere;

internal enum Phase
{
    None,
    SittingDown,
    Seated,
    StandingUp,
}

/// <summary>
/// The player's seated state. Sitting switches off the character controller and gravity the same
/// way the game's own transitions (chairs, hiding places) do, then holds the player so their eyes
/// stay at the seat's eye position. Standing walks them back to where they were standing.
/// </summary>
internal static class Seated
{
    public static Phase Phase;
    public static Seat Current;
    /// <summary>Fall damage guard: the game counts any downward move with no ground as falling.</summary>
    public static float GraceUntil;

    private static Vector3 standPos, fromPos;
    private static Quaternion fromRot, toRot;
    private static float fromPitch, toPitch;
    private static float startTime, duration;
    private static bool movementReleased;
    private static IntPtr seatedPlayer;

    public static bool Active => Phase != Phase.None;

    public static void SitDown(Player p, Seat seat)
    {
        Current = seat;
        standPos = p.transform.position;
        fromPos = standPos;
        fromRot = p.transform.rotation;
        toRot = Quaternion.Euler(0f, seat.Yaw, 0f);
        fromPitch = SignedAngle(p.fps.m_Camera.transform.localEulerAngles.x);
        toPitch = seat.Pitch;
        seatedPlayer = p.Pointer;

        p.EnablePlayerMovement(false, false);
        p.fps.m_StickToGroundForce = 0f;
        p.fps.m_GravityMultiplier = 0f;
        p.fps.m_MoveDir = Vector3.zero;
        p.fps.m_Jump = false;
        p.EnableCharacterController(false);
        p.fps.enableHeadBob = false;
        p.fps.m_UseJumpBob = false;
        p.fps.enableLook = false;

        Begin(Phase.SittingDown, Plugin.SitTime.Value);
        movementReleased = false;
        if (Plugin.DebugLogging.Value)
            Plugin.Logger.LogInfo($"Sit: {(seat.Ledge ? "ledge" : "seat")} eye {seat.Eye} yaw {seat.Yaw:0}");
        Prompts.Refresh();
    }

    public static void StandUp(Player p)
    {
        if (Phase is Phase.None or Phase.StandingUp) return;
        StopPassingTime(p);
        // Keep looking the way you are; just rise back to where you stood.
        p.fps.m_MouseLook.Init(p.transform, p.fps.m_Camera.transform);
        p.fps.enableLook = true;
        fromPos = p.transform.position;
        Begin(Phase.StandingUp, Plugin.StandTime.Value);
        Prompts.Refresh();
    }

    /// <summary>Gives control straight back, e.g. when the game teleports the player.</summary>
    public static void EndNow(Player p, bool restoreController)
    {
        if (!Active) return;
        StopPassingTime(p);
        Phase = Phase.None;
        if (restoreController) Restore(p);
        Prompts.Refresh();
    }

    /// <summary>
    /// Back on your feet at once, where you stood before sitting. Used when the game is about to
    /// move the player itself (talking to someone, a punch, a computer, lockpicking): its moves
    /// start from, and return to, wherever the player is, so they have to start from standing.
    /// </summary>
    public static void StandNow(Player p)
    {
        if (!Active) return;
        StopPassingTime(p);
        Phase = Phase.None;
        p.transform.position = standPos;
        p.fps.m_MouseLook.Init(p.transform, p.fps.m_Camera.transform);
        Restore(p);
        Prompts.Refresh();
    }

    /// <summary>The game has already taken over the player: just let go.</summary>
    public static void Forget()
    {
        Phase = Phase.None;
        Prompts.Refresh();
    }

    public static void Tick(Player p)
    {
        if (!Active) return;
        if (p.Pointer != seatedPlayer)
        {
            // A different save was loaded; this player was never seated.
            Phase = Phase.None;
            return;
        }
        if (p.transitionActive)
        {
            Forget();
            return;
        }
        if (p.playerKOInProgress || p.inAirVent ||
            (CutSceneController.Instance != null && CutSceneController.Instance.cutSceneActive))
        {
            StandNow(p);
            return;
        }
        // Setting a route on the map and auto-travelling: get up so you can walk.
        if (p.autoTravelActive && Phase is Phase.Seated or Phase.SittingDown)
        {
            StandUp(p);
            return;
        }

        GraceUntil = Time.time + 0.5f;
        var fps = p.fps;
        fps.fallCount = 0f;

        var k = duration <= 0f ? 1f : Mathf.Clamp01((Time.time - startTime) / duration);
        var e = Mathf.SmoothStep(0f, 1f, k);
        switch (Phase)
        {
            case Phase.SittingDown:
                p.transform.position = Vector3.Lerp(fromPos, SeatOrigin(p), e);
                p.transform.rotation = Quaternion.Slerp(fromRot, toRot, e);
                fps.m_Camera.transform.localRotation = Quaternion.Euler(Mathf.Lerp(fromPitch, toPitch, e), 0f, 0f);
                if (k >= 1f)
                {
                    fps.m_MouseLook.Init(p.transform, fps.m_Camera.transform);
                    fps.enableLook = true;
                    Phase = Phase.Seated;
                    Prompts.Refresh();
                }
                break;

            case Phase.Seated:
                p.transform.position = SeatOrigin(p);
                if (WantsToStand()) StandUp(p);
                break;

            case Phase.StandingUp:
                p.transform.position = Vector3.Lerp(fromPos, standPos, e);
                if (k >= 1f)
                {
                    Phase = Phase.None;
                    Restore(p);
                    Prompts.Refresh();
                }
                break;
        }
    }

    /// <summary>Movement or Jump stands you up. Movement has to be let go of after sitting first,
    /// so walking up to a seat while still holding forward doesn't pop you straight back up.</summary>
    private static bool WantsToStand()
    {
        var input = InputController.Instance?.player;
        if (input == null) return false;
        if (input.GetButtonDown("Jump")) return true;

        float x, z;
        if (InputController.Instance.mouseInputMode)
        {
            x = input.GetAxis("MoveLeft") + input.GetAxis("MoveRight");
            z = input.GetAxis("MoveBack") + input.GetAxis("MoveForward");
        }
        else
        {
            x = input.GetAxis("MoveHorizontal");
            z = input.GetAxis("MoveVertical");
        }
        var amount = new Vector2(x, z).magnitude;
        if (amount < 0.2f) movementReleased = true;
        return movementReleased && amount > 0.5f;
    }

    /// <summary>Where the player's origin goes so the eyes land on the seat's eye position. Uses
    /// the camera's current height, so it holds whatever the game does with camera height.</summary>
    private static Vector3 SeatOrigin(Player p) => Current.Eye - Vector3.up * p.camHeightParent.localPosition.y;

    private static void Restore(Player p)
    {
        p.EnableCharacterController(true);
        p.fps.m_StickToGroundForce = 7f;
        p.fps.m_GravityMultiplier = 2f;
        p.fps.enableHeadBob = true;
        p.fps.m_UseJumpBob = true;
        p.fps.enableLook = true;
        p.fps.fallCount = 0f;
        p.fps.lastY = p.transform.position.y;
        p.EnablePlayerMovement(true);
        GraceUntil = Time.time + 0.5f;
    }

    private static void StopPassingTime(Player p)
    {
        if (p.setAlarmMode) p.SetSettingAlarmMode(false);
        if (p.spendingTimeMode) p.SetSpendingTimeMode(false);
        p.spendingTimeDelay = 0f;
    }

    private static void Begin(Phase phase, float seconds)
    {
        Phase = phase;
        startTime = Time.time;
        duration = Mathf.Max(0f, seconds);
    }

    private static float SignedAngle(float a) => a > 180f ? a - 360f : a;
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class SeatedTickPatch
{
    private static float nextErrorLog;

    private static void Postfix(Player __instance)
    {
        try
        {
            if (SessionData.Instance == null || !SessionData.Instance.play) return;
            Seated.Tick(__instance);
            if (!Seated.Active) Prompts.Scan(__instance);
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

/// <summary>Keeps the fall counter at zero while seated and just after, so getting up never
/// counts as landing from a fall.</summary>
[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class NoFallDamagePatch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (!Seated.Active && Time.time > Seated.GraceUntil) return;
        __instance.fallCount = 0f;
        __instance.lastY = __instance.transform.position.y;
    }
}

/// <summary>Keeps your head turning within a natural range while seated.</summary>
[HarmonyPatch(typeof(MouseLook), nameof(MouseLook.LookRotation))]
internal static class LookLimitPatch
{
    private static void Postfix(MouseLook __instance, Transform character)
    {
        if (Seated.Phase != Phase.Seated) return;
        var limit = Plugin.LookLimit.Value;
        if (limit >= 180f || Player.Instance == null || character != Player.Instance.transform) return;

        var yaw = character.eulerAngles.y;
        var offset = Mathf.DeltaAngle(Seated.Current.Yaw, yaw);
        if (Mathf.Abs(offset) <= limit) return;
        var clamped = Quaternion.Euler(0f, Seated.Current.Yaw + Mathf.Clamp(offset, -limit, limit), 0f);
        character.rotation = clamped;
        __instance.m_CharacterTargetRot = character.localRotation;
    }
}

/// <summary>No crouching (or lying down) while seated.</summary>
[HarmonyPatch(typeof(Actor), nameof(Actor.SetCrouched))]
internal static class NoCrouchPatch
{
    private static bool Prefix(Actor __instance) => !(Seated.Active && __instance.isPlayer);
}

/// <summary>Getting hurt gets you up.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.RecieveDamage))]
internal static class DamagePatch
{
    private static void Postfix(Player __instance, float amount)
    {
        if (amount > 0.01f && Seated.Phase is Phase.Seated or Phase.SittingDown) Seated.StandUp(__instance);
    }
}

/// <summary>
/// The game's own player moves (talking, attacks and blocks, computers, lockpicking, door peeks,
/// hiding) remember where the player was and put them back there afterwards with collision on.
/// Starting one while seated would leave you standing inside the seat or the roof, so stand first.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.TransformPlayerController))]
internal static class GameTransitionPatch
{
    private static void Prefix(Player __instance)
    {
        try
        {
            Seated.StandNow(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

/// <summary>The game moving the player (fast travel, loading, being arrested) ends sitting at once.</summary>
[HarmonyPatch(typeof(Player), nameof(Player.Teleport))]
internal static class TeleportPatch
{
    private static void Prefix(Player __instance) => Seated.EndNow(__instance, restoreController: true);
}

[HarmonyPatch(typeof(Player), nameof(Player.SetPosition))]
internal static class SetPositionPatch
{
    private static void Prefix(Player __instance) => Seated.EndNow(__instance, restoreController: true);
}
