using System;
using System.Collections.Generic;
using HarmonyLib;
using ProtectorateShared;

namespace SanitationDepartment;

/// <summary>This mod's sections on the pause menu's Credentials card.</summary>
[HarmonyPatch]
internal static class Credentials
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.SetMenuComponent), new[] { typeof(MainMenuController.Component) })]
    private static void OnMenuComponent() => Refresh();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.EnableMainMenu))]
    private static void OnMenu() => Refresh();

    private static void Refresh()
    {
        try
        {
            CredentialsCard.Refresh("20_Sanitation", "Sanitation", Sanitation);
            CredentialsCard.Refresh("30_CrimeScenes", "Crime Scene Cleanup", CrimeScenes);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Credentials card: " + e);
        }
    }

    private static List<string> Sanitation()
    {
        var lines = new List<string>();
        if (!State.Licensed) return lines;
        lines.Add("Sanitation License");
        lines.Add($"Trash bag: {State.BagCount}/{State.BagCapacity}");
        if (State.Picker) lines.Add("Litter picker");
        if (State.Supervisor) lines.Add("Route Supervisor");
        else if (State.Binned >= Plugin.SupervisorItems.Value) lines.Add("Route Supervisor promotion waiting at City Hall");
        else lines.Add($"Items binned: {State.Binned}/{Plugin.SupervisorItems.Value} toward Route Supervisor");
        if (State.FullTime) lines.Add("On sanitation full-time (no new murders)");
        return lines;
    }

    private static List<string> CrimeScenes()
    {
        var lines = new List<string>();
        if (!State.Certified) return lines;
        lines.Add("Certified for crime scene cleanup");
        if (State.Mop) lines.Add("Mop and bucket");
        lines.Add($"Scenes cleaned: {State.ScenesCleaned}");
        var now = SessionData.Instance.gameTime;
        if (Plugin.ScenesOnlyFullTime.Value && !State.FullTime) lines.Add("New scenes are posted while you're on full-time");
        foreach (var s in Scenes.All)
        {
            var addr = s.Address;
            if (addr == null) continue;
            switch (s.State)
            {
                case SceneState.Investigating:
                    var calls = GameplayController.Instance.enforcerCalls;
                    var call = calls.ContainsKey(addr) ? calls[addr] : null;
                    if (call == null || call.arrivalTime <= 0f)
                    {
                        lines.Add($"{s.Name}: Enforcers on their way");
                    }
                    else
                    {
                        var release = call.arrivalTime + GameplayControls.Instance.crimeSceneSearchLength + Plugin.InvestigationHours.Value;
                        lines.Add($"{s.Name}: Enforcers investigating, released in about {CredentialsCard.Hours(release - now)}");
                    }
                    break;

                case SceneState.Released:
                    var job = $"Open job: {s.Name}";
                    if (s.Bagged) job += ", body bagged";
                    var passes = GameplayController.Instance.guestPasses;
                    if (passes.ContainsKey(addr) && passes[addr].x > now)
                        job += $" (access {CredentialsCard.Hours(passes[addr].x - now)})";
                    lines.Add(job);
                    break;

                case SceneState.Done:
                    lines.Add($"{s.Name}: clean, waiting for the coroner");
                    break;
            }
        }
        return lines;
    }
}
