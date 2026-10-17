using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace LaserFix;

/// <summary>
/// Laser Fix: security camera lasers and laser grids stay inside. The game measures a laser with the same "what can a
/// person see?" ray that citizens use, which deliberately ignores the Transparent layer that window glass is on, so
/// beams shone straight through windows and out into the street. After the game sizes a beam it is checked twice:
/// for window glass along it, and for the point where it would leave the building (some windows, like stained-glass
/// panels, have nothing a ray can hit). The drawn beam and its glow end at whichever comes first. Only the drawing
/// changes: what the laser detects (and whether it trips the alarm) is exactly as before.
/// </summary>
[BepInPlugin("sodmods.laserfix", "Laser Fix", "1.0.1")]
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

    private const float Step = 0.25f;

    /// <summary>
    /// Ends a beam at the first window glass along it, or where it would leave the building, whichever is nearer. The
    /// beam runs from its origin along its forward.
    /// </summary>
    internal static void StopAtGlass(Transform beam, Transform origin, Light glow)
    {
        if (beam == null || origin == null) return;
        var length = beam.lossyScale.z;
        if (length <= 0.05f) return;
        var from = origin.position;
        var dir = origin.forward;
        var end = length;
        if (Physics.Raycast(new Ray(from, dir), out var hit, length, glassMask, QueryTriggerInteraction.Collide) && IsGlass(hit.transform))
            end = hit.distance;
        // Lasers inside a building never reach the street: walk the beam and stop where it would step outside.
        if (IsOutside(from) == false)
        {
            for (var d = Step; d < end; d += Step)
            {
                if (IsOutside(from + dir * d) != true) continue;
                // Narrow down to the wall between the last inside point and this one.
                float inside = d - Step, outside = d;
                for (var i = 0; i < 6; i++)
                {
                    var m = (inside + outside) * 0.5f;
                    if (IsOutside(from + dir * m) == true) outside = m;
                    else inside = m;
                }
                end = Mathf.Min(end, inside);
                break;
            }
        }
        if (end >= length - 0.01f) return;
        // Same way the game sizes it: scale while detached, so the length is in world units.
        var parent = beam.parent;
        beam.parent = null;
        beam.localScale = new Vector3(beam.localScale.x, beam.localScale.y, end);
        beam.parent = parent;
        if (glow != null) glow.range = end;
    }

    /// <summary>Whether a point is outdoors (a street, alley or rooftop), from the game's own map; null if it's off the map.</summary>
    internal static bool? IsOutside(Vector3 position)
    {
        var map = PathFinder.Instance?.nodeMap;
        if (map == null || CityData.Instance == null) return null;
        if (!map.TryGetValue(CityData.Instance.RealPosToNodeInt(position), out var node) || node?.gameLocation == null) return null;
        return node.gameLocation.isOutside;
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
