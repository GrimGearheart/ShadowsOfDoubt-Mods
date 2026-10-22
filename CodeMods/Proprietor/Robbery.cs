using System;
using UnityEngine;

namespace Proprietor;

/// <summary>
/// A full till is a target. Once a business's till holds more than Robbery.SafeLimit, every night there's a chance
/// someone breaks in (or, at a place that never closes, holds up the counter in the small hours) and takes most of
/// it. The chance climbs from nothing at SafeLimit to NightlyChance at DangerLimit, and keeps climbing past it. Never
/// while you're inside. The manager tells you in the morning report.
/// </summary>
internal static class Robbery
{
    private const int NightHour = 3;

    public static void Hourly(int hour)
    {
        if (hour != NightHour || Plugin.RobberyChance.Value <= 0f) return;
        foreach (var b in Store.Owned.Values)
        {
            try
            {
                Night(b);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"Robbery check at {b.Name} failed: " + e.Message);
            }
        }
    }

    private static void Night(Business b)
    {
        var safe = Plugin.RobberySafe.Value;
        var danger = Math.Max(safe + 1, Plugin.RobberyDanger.Value);
        if (b.Till <= safe) return;
        var c = Store.FindCompany(b.CompanyId);
        if (c?.address == null) return;
        // Not with the owner in the building.
        var here = Player.Instance?.currentGameLocation?.thisAsAddress;
        if (here != null && here.Pointer == c.address.Pointer) return;

        var risk = Mathf.Clamp((b.Till - safe) / (float)(danger - safe), 0f, 2f) * Plugin.RobberyChance.Value;
        if (UnityEngine.Random.value > risk) return;

        var share = UnityEngine.Random.Range(Plugin.RobberyMinShare.Value, Plugin.RobberyMaxShare.Value);
        var stolen = Mathf.Clamp(Mathf.RoundToInt(b.Till * share / 10f) * 10, 0, b.Till);
        if (stolen <= 0) return;
        b.Till -= stolen;
        b.Notes.Insert(0, c.openForBusinessDesired
            ? $"Someone held up the counter around {NightHour}am and walked off with {Store.Currency}{stolen} from the till. Nobody was hurt."
            : $"We got hit last night. Somebody broke in and cleaned out {Store.Currency}{stolen} from the till.");
        b.BadNews = true;
        Plugin.Logger.LogInfo($"{b.Name}: robbed of ¢{stolen} (risk {risk:P0}); till now ¢{b.Till}");
    }
}
