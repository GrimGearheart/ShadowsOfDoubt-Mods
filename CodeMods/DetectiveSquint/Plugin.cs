using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace DetectiveSquint;

/// <summary>
/// Hold a key to narrow your eyes: the camera zooms in smoothly and mouse look slows down to match.
/// The game only sets the field of view on startup and when settings change (from Game.fov), so the
/// zoom is applied on top of that value and handed back exactly when released.
/// </summary>
[BepInPlugin("sodmods.detectivesquint", "Detective Squint", "1.0.2")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<KeyCode> SquintKey;
    internal static ConfigEntry<bool> Toggle;
    internal static ConfigEntry<float> Zoom;
    internal static ConfigEntry<float> Speed;
    internal static ConfigEntry<bool> ScaleSensitivity;

    public override void Load()
    {
        Logger = Log;
        SquintKey = Config.Bind("Controls", "SquintKey", KeyCode.Z, "Key to squint (zoom in).");
        Toggle = Config.Bind("Controls", "Toggle", false, "Press once to squint, again to stop, instead of holding the key.");
        Zoom = Config.Bind("Squint", "Zoom", 2.5f, "Magnification while squinting (2.5 = the view is 2.5x closer).");
        Speed = Config.Bind("Squint", "Speed", 6f, "How quickly the view zooms in and out (higher = snappier).");
        ScaleSensitivity = Config.Bind("Squint", "ScaleMouseSensitivity", true,
            "Slow mouse look down in proportion to the zoom, so aiming stays steady.");

        new Harmony("sodmods.detectivesquint").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Detective Squint loaded");
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class SquintPatch
{
    /// <summary>Current magnification (1 = normal view).</summary>
    internal static float Current = 1f;

    private static bool toggledOn;
    private static bool applied;

    private static void Postfix()
    {
        try
        {
            var cam = CameraController.Instance?.cam;
            if (cam == null || Game.Instance == null) return;

            var free = InFreeMovement();
            var key = Plugin.SquintKey.Value;
            bool wanted;
            if (Plugin.Toggle.Value)
            {
                if (free && key != KeyCode.None && Input.GetKeyDown(key)) toggledOn = !toggledOn;
                if (!free) toggledOn = false;
                wanted = toggledOn;
            }
            else
            {
                wanted = free && key != KeyCode.None && Input.GetKey(key);
            }

            var target = wanted ? Mathf.Max(1f, Plugin.Zoom.Value) : 1f;
            var t = 1f - Mathf.Exp(-Plugin.Speed.Value * Time.unscaledDeltaTime);
            Current = Mathf.Lerp(Current, target, t);
            if (Mathf.Abs(Current - target) < 0.005f) Current = target;

            if (Current > 1f)
            {
                cam.fieldOfView = Game.Instance.fov / Current;
                applied = true;
            }
            else if (applied)
            {
                // Hand the camera back exactly as the game set it.
                cam.fieldOfView = Game.Instance.fov;
                applied = false;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    /// <summary>First-person play only; not in menus, the map, documents, computers or conversations.</summary>
    private static bool InFreeMovement()
    {
        if (SessionData.Instance == null || !SessionData.Instance.play) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;
        if (InterfaceController.Instance != null && InterfaceController.Instance.desktopMode) return false;
        if (InteractionController.Instance != null && InteractionController.Instance.dialogMode) return false;
        return true;
    }
}

/// <summary>Scales look sensitivity by the zoom while the camera is turned, then restores it.</summary>
[HarmonyPatch(typeof(MouseLook), nameof(MouseLook.LookRotation))]
internal static class SensitivityPatch
{
    private static bool scaled;
    private static Vector2 savedMouse, savedController;

    private static void Prefix()
    {
        scaled = false;
        if (!Plugin.ScaleSensitivity.Value || SquintPatch.Current <= 1.001f || Game.Instance == null) return;
        savedMouse = Game.Instance.mouseSensitivity;
        savedController = Game.Instance.controllerSensitivity;
        Game.Instance.mouseSensitivity = savedMouse / SquintPatch.Current;
        Game.Instance.controllerSensitivity = savedController / SquintPatch.Current;
        scaled = true;
    }

    private static void Finalizer()
    {
        if (!scaled) return;
        Game.Instance.mouseSensitivity = savedMouse;
        Game.Instance.controllerSensitivity = savedController;
        scaled = false;
    }
}
