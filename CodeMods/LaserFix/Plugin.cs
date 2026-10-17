using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace LaserFix;

/// <summary>
/// Laser Fix: security camera lasers and laser grids stop at window glass. The game measures a laser with the same
/// "what can a person see?" ray that citizens use, which deliberately ignores the Transparent layer that window glass
/// is on, so beams shone straight through windows and out into the street. After the game sizes a beam, it's checked
/// for window glass along its length and, if it meets some, the drawn beam and its glow end there. Only the drawing
/// changes: what the laser detects (and whether it trips the alarm) is exactly as before.
/// </summary>
[BepInPlugin("sodmods.laserfix", "Laser Fix", "1.0.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;
    private static int glassMask;

    public override void Load()
    {
        Logger = Log;
        var layer = LayerMask.NameToLayer("Transparent");
        glassMask = 1 << (layer >= 0 ? layer : 25);
        new Harmony("sodmods.laserfix").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo($"Laser Fix loaded (window glass on layer {(layer >= 0 ? layer : 25)})");
    }

    /// <summary>Ends a beam at the first window glass along it. The beam runs from its origin along its forward.</summary>
    internal static void StopAtGlass(Transform beam, Transform origin, Light glow)
    {
        if (beam == null || origin == null) return;
        var length = beam.lossyScale.z;
        if (length <= 0.05f) return;
        if (!Physics.Raycast(new Ray(origin.position, origin.forward), out var hit, length, glassMask, QueryTriggerInteraction.Collide)) return;
        if (!IsGlass(hit.transform)) return;
        // Same way the game sizes it: scale while detached, so the length is in world units.
        var parent = beam.parent;
        beam.parent = null;
        beam.localScale = new Vector3(beam.localScale.x, beam.localScale.y, hit.distance);
        beam.parent = parent;
        if (glow != null) glow.range = hit.distance;
    }

    /// <summary>Window glass only: other see-through things on the same layer are left alone.</summary>
    private static bool IsGlass(Transform t)
    {
        for (var i = 0; i < 3 && t != null; i++, t = t.parent)
        {
            var name = t.name;
            if (name.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }
}

/// <summary>Security camera lasers.</summary>
[HarmonyPatch(typeof(Toolbox), nameof(Toolbox.HandleLaserBehaviour))]
internal static class CameraLaserPatch
{
    private static void Postfix(GameObject laser, Light laserLight)
    {
        try
        {
            if (laser != null) Plugin.StopAtGlass(laser.transform, laser.transform, laserLight);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

/// <summary>Laser grids (they sweep, and are sized every frame the same way).</summary>
[HarmonyPatch(typeof(LaserGridController), nameof(LaserGridController.Update))]
internal static class LaserGridPatch
{
    private static void Postfix(LaserGridController __instance)
    {
        try
        {
            if (__instance != null && __instance.laser != null && __instance.laserParent != null)
                Plugin.StopAtGlass(__instance.laser, __instance.laserParent, null);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}
