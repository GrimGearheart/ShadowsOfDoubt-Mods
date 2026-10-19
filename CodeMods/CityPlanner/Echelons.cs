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
}
