using HarmonyLib;

namespace SanitationDepartment;

/// <summary>
/// Full-time sanitation (sandbox only): no new murders. New murders only start when the game's
/// cooldown between murders reaches zero, so the cooldown is held while no murder is in progress.
/// A killer already mid-routine is left to finish, so nothing freezes in a half-done state.
/// Returning to detective work simply stops holding the cooldown.
/// </summary>
[HarmonyPatch(typeof(MurderController), nameof(MurderController.Tick))]
internal static class QuietCityPatch
{
    private static void Prefix(MurderController __instance)
    {
        if (!State.FullTime || !FullTime.Allowed) return;
        var current = __instance.GetCurrentMurder();
        if (current != null && FullTime.InProgress(current.state)) return;
        if (__instance.pauseBetweenMurders < 1f) __instance.pauseBetweenMurders = 1f;
    }
}

internal static class FullTime
{
    /// <summary>The story campaign is scripted around its case, so the transfer is sandbox-only.</summary>
    public static bool Allowed => Game.Instance != null && Game.Instance.sandboxMode;

    public static bool InProgress(MurderController.MurderState state)
        => state is MurderController.MurderState.acquireEuipment or MurderController.MurderState.research
            or MurderController.MurderState.waitForLocation or MurderController.MurderState.travellingTo
            or MurderController.MurderState.executing or MurderController.MurderState.post
            or MurderController.MurderState.escaping;
}
