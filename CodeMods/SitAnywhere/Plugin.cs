using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace SitAnywhere;

/// <summary>
/// Sit on anything that's the right height: crates, benches, low walls, steps, desk edges, and the
/// edge of a roof or balcony with your legs over the drop. Looking at a spot like that puts "Sit"
/// on your Interact key; movement or Jump gets you back up. While seated you can pass time with
/// the game's own watch alarm, the same as on a chair.
///
/// The game's own chair sitting is tied to real furniture (Player.OnHide with the chair's
/// transitions), so this mod seats the player itself: the character controller is switched off
/// like during the game's transitions, and the player is placed so their eyes sit at seated height.
/// </summary>
[BepInPlugin("sodmods.sitanywhere", "Sit Anywhere", "1.0.0")]
public class Plugin : BasePlugin
{
    internal static ManualLogSource Logger;

    internal static ConfigEntry<float> MinSeatHeight;
    internal static ConfigEntry<float> MaxSeatHeight;
    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<bool> LedgeSitting;
    internal static ConfigEntry<float> LedgeMinDrop;
    internal static ConfigEntry<float> EyeAboveSeat;
    internal static ConfigEntry<float> SitTime;
    internal static ConfigEntry<float> StandTime;
    internal static ConfigEntry<float> LookLimit;
    internal static ConfigEntry<bool> TurnAround;
    internal static ConfigEntry<bool> PassTime;
    internal static ConfigEntry<bool> DebugLogging;

    public override void Load()
    {
        Logger = Log;
        MinSeatHeight = Config.Bind("Seats", "MinSeatHeight", 0.3f,
            "Lowest surface (metres above your feet) you can sit on.");
        MaxSeatHeight = Config.Bind("Seats", "MaxSeatHeight", 0.85f,
            "Highest surface (metres above your feet) you can sit on. Desks and tables are around 0.75.");
        Reach = Config.Bind("Seats", "Reach", 2.0f, "How far away (metres) you can look at a spot and sit there.");
        LedgeSitting = Config.Bind("Ledges", "Enabled", true,
            "Sit on the edge of roofs, balconies and low walls with your legs over the drop.");
        LedgeMinDrop = Config.Bind("Ledges", "MinDrop", 0.8f,
            "How far (metres) the ground has to fall away past an edge for it to count as a ledge.");
        EyeAboveSeat = Config.Bind("Seated", "EyeHeightAboveSeat", 0.75f,
            "Height of your eyes above the surface you're sitting on (metres).");
        SitTime = Config.Bind("Seated", "SitDownSeconds", 0.7f, "How long sitting down takes.");
        StandTime = Config.Bind("Seated", "StandUpSeconds", 0.5f, "How long getting up takes.");
        LookLimit = Config.Bind("Seated", "LookAroundDegrees", 120f,
            "How far you can turn your head left or right while seated. 180 = no limit.");
        TurnAround = Config.Bind("Seated", "TurnAround", true,
            "Turn around to sit on things in front of you, the way you'd sit on a bench. " +
            "Off = sit facing the way you were looking. Ledges always face out over the drop.");
        PassTime = Config.Bind("Seated", "PassTime", true,
            "Allow passing time with your watch alarm while seated, like on a chair.");
        DebugLogging = Config.Bind("Debug", "LogSeats", false, "Log seat detection results (for tuning and bug reports).");

        new Harmony("sodmods.sitanywhere").PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Sit Anywhere loaded");
        PhysicsSelfTest();
    }

    /// <summary>
    /// IL2CPP builds strip engine methods the game never calls. Call every physics query this mod
    /// uses once, so a missing one shows up in the log straight away.
    /// </summary>
    private void PhysicsSelfTest()
    {
        try
        {
            var far = new Vector3(0f, -10000f, 0f);
            Physics.Raycast(far, Vector3.down, out RaycastHit _, 1f, ~0, QueryTriggerInteraction.Ignore);
            Physics.OverlapCapsule(far, far + Vector3.up, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            Log.LogInfo("Physics self-test OK");
        }
        catch (Exception e)
        {
            Log.LogError("Physics self-test FAILED, sitting will not work: " + e.Message);
        }
    }
}
