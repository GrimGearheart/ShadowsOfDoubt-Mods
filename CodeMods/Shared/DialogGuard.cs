using System;
using System.Reflection;
using HarmonyLib;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;
using UnityEngine;

namespace ProtectorateShared;

/// <summary>
/// Keeps mod dialog options working after loading a second save in the same game session. SOD.Common makes its
/// dialog presets with ScriptableObject.CreateInstance, and the game's Resources.UnloadUnusedAssets on loading can
/// destroy them; a destroyed preset left in a citizen's options makes SOD.Common's availability check throw, and
/// that empties the whole conversation menu. This file is compiled into each mod; running it more than once is
/// harmless.
/// </summary>
[HarmonyPatch]
internal static class DialogGuard
{
    private static BepInEx.Logging.ManualLogSource log;
    private static BepInEx.Logging.ManualLogSource Log => log ??= BepInEx.Logging.Logger.CreateLogSource("DialogGuard");

    private static readonly PropertyInfo ResponseInfo =
        typeof(DialogObject).GetProperty("ResponseInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// Marks every mod dialog (and its responses) as never-unload. Call after registering dialogs; it runs again
    /// when SOD.Common adds them to the game, to catch mods that registered later.
    /// </summary>
    [HarmonyPatch(typeof(DialogController), "Start")]
    [HarmonyPrefix]
    internal static void Protect()
    {
        foreach (var d in Lib.Dialogs.KnownModDialogs)
        {
            try
            {
                Keep(d.Preset);
                Keep(ResponseInfo?.GetValue(d) as UnityEngine.Object);
            }
            catch (Exception e)
            {
                Log.LogError(e.Message);
            }
        }
    }

    private static void Keep(UnityEngine.Object o)
    {
        if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }

    /// <summary>Drops options whose preset has been destroyed, so one dead entry can't take the menu down.</summary>
    [HarmonyPatch(typeof(EvidenceWitness), nameof(EvidenceWitness.GetDialogOptions),
        new[] { typeof(Il2CppSystem.Collections.Generic.List<Evidence.DataKey>) })]
    [HarmonyPostfix]
    private static void DropDead(Il2CppSystem.Collections.Generic.List<EvidenceWitness.DialogOption> __result)
    {
        if (__result == null) return;
        for (int i = __result.Count - 1; i >= 0; i--)
        {
            var p = __result[i]?.preset;
            // Unity's == also catches destroyed objects; the wrapper itself is still there.
            if (p is null || p != null) continue;
            Log.LogWarning($"Skipped a destroyed dialog option (msgID {p.msgID}, ranking {p.ranking})");
            __result.RemoveAt(i);
        }
    }
}
