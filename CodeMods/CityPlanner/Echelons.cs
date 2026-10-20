using HarmonyLib;

namespace CityPlanner;

/// <summary>
/// Echelon floors ([City] Echelons = off). Some towers mark their top floors as Echelon floors when they're built: a
/// fixed luxury look, and a flag saved on each floor that the game uses while playing (you're trespassing there without
/// the right sync disk or a guest pass). The game has a switch for all of it (Game.allowEchelons, on, not in any menu).
/// For a planned city with Echelons off, the switch is off while the city is made, so those floors are built and saved
/// as ordinary floors; it goes back on once the city is saved, so other cities are untouched.
/// </summary>
internal static class Echelons
{
    private static bool switchedOff;

    /// <summary>Called every generation step.</summary>
    public static void Tick(CityConstructor constructor)
    {
        if (!Plugin.Active || Plugin.Plan.Echelons)
        {
            Restore();
            return;
        }
        if (constructor.loadState >= CityConstructor.LoadState.savingData)
        {
            Restore();
            return;
        }
        if (switchedOff || Game.Instance == null) return;
        Game.Instance.allowEchelons = false;
        switchedOff = true;
        Plugin.Logger.LogInfo("Echelons off for this city: the towers' top floors are built as ordinary floors");
    }

    public static void Restore()
    {
        if (!switchedOff) return;
        if (Game.Instance != null) Game.Instance.allowEchelons = true;
        switchedOff = false;
    }

    /// <summary>
    /// The laser screen at the entrance to the Echelons. It's part of an object every floor of those towers has, and when
    /// it appears (as you get near) it switches itself on by floor number, not by whether the floor is an Echelon floor.
    /// So on a city built without Echelons it would still light up on what are now ordinary floors. Here it removes
    /// itself, as it does below the Echelons, when its floor isn't an Echelon floor. Needs City Planner installed; cities
    /// with Echelons (any city the game makes) are unaffected, since their Echelon floors are marked as such.
    /// </summary>
    [HarmonyPatch(typeof(EchelonsLaserScreenController), nameof(EchelonsLaserScreenController.Setup))]
    private static class LaserScreenPatch
    {
        private static bool Prefix(EchelonsLaserScreenController __instance, InteractableController newController)
        {
            var floor = newController?.interactable?.node?.gameLocation?.thisAsAddress?.floor;
            if (floor == null || floor.isEchelons || SessionData.Instance == null || SessionData.Instance.isFloorEdit) return true;
            var building = newController.interactable.node.building;
            if (building?.preset == null || !building.preset.buildingFeaturesEchelonFloors || floor.floor < building.preset.echelonFloorStart) return true;
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
