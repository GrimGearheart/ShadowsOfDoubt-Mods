using UnityEngine;

namespace CityPlanner;

/// <summary>
/// Flooded basements (Basements / Flooded). The flooding is one sheet of water under the whole city at a fixed height,
/// which the game also checks to slow you down and play wading sounds. With the setting off, the sheet is hidden and
/// the water level is put far below the city, in every city (it's a play-time setting, so it needs City Planner
/// installed). Turning it back on restores both.
/// </summary>
internal static class Basements
{
    private const float Drained = -10000f;
    private static float? originalLevel;

    /// <summary>Called every frame: cheap unless something needs changing.</summary>
    public static void Tick()
    {
        var controls = CityControls.Instance;
        if (controls == null) return;
        var flooded = Plugin.FloodedBasements.Value;
        var water = controls.basementWaterTransform;
        if (!flooded)
        {
            if (controls.basementWaterLevel != Drained)
            {
                originalLevel ??= controls.basementWaterLevel;
                controls.basementWaterLevel = Drained;
            }
            if (water != null && water.gameObject.activeSelf) water.gameObject.SetActive(false);
        }
        else if (originalLevel != null)
        {
            controls.basementWaterLevel = originalLevel.Value;
            originalLevel = null;
            if (water != null) water.gameObject.SetActive(true);
        }
    }
}
