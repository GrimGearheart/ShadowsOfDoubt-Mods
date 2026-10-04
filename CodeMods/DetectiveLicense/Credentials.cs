using System;
using System.Collections.Generic;
using HarmonyLib;
using ProtectorateShared;

namespace DetectiveLicense;

/// <summary>This mod's section on the pause menu's Credentials card.</summary>
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
            CredentialsCard.Refresh("10_Detective", "Detective", Lines);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Credentials card: " + e);
        }
    }

    private static List<string> Lines()
    {
        var lines = new List<string>();
        if (Plugin.Licensed) lines.Add("Private investigator's license");

        // A permit is a guest pass on open-case locations; list the ones still running.
        var now = SessionData.Instance.gameTime;
        var passes = GameplayController.Instance.guestPasses;
        foreach (var address in CaseLocations.Get())
        {
            if (!passes.ContainsKey(address)) continue;
            var expires = passes[address].x;
            if (expires > now) lines.Add($"Crime scene permit: {address.name} ({CredentialsCard.Hours(expires - now)} left)");
        }
        return lines;
    }
}
