using System;
using HarmonyLib;
using SOD.Common;

namespace Proprietor;

/// <summary>
/// Staff at your businesses greet you as their boss. Waiters, bar staff, receptionists and shopkeepers greet
/// customers with the game's "Welcome_Player01" line ("How can I help you?", or "What do you want?" from the rude
/// ones). For the people who work for you it is swapped for a plain, unenthusiastic "boss" line.
/// </summary>
// The game turns a dialogue message into the lines to speak with Human.ParseDDSMessage, both for conversations
// (how the welcome is triggered) and for direct speech, so the swap happens there.
[HarmonyPatch(typeof(Human), nameof(Human.ParseDDSMessage),
    new[] { typeof(string), typeof(Acquaintance), typeof(Il2CppSystem.Collections.Generic.List<int>), typeof(bool), typeof(Il2CppSystem.Object), typeof(bool) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
internal static class BossGreetingPatch
{
    private const string WelcomePlayer = "759119e7-ccc7-4b0c-a9df-b6fad5813d8f";
    private const string Prefix_ = "proprietor_boss_";

    private static readonly string[] Lines =
    {
        "Boss.",
        "Oh. Boss.",
        "Boss. Need something?",
        "What can I do for you, boss?",
        "Didn't see you come in, boss.",
        "Boss. Place is still standing.",
        "Boss. It's been one of those days.",
    };

    private static bool registered;

    /// <summary>
    /// Adds our lines to the game's text. Done the first time they're needed: the game's text isn't loaded yet
    /// when the mod starts.
    /// </summary>
    private static void Register()
    {
        if (registered) return;
        registered = true;
        for (var i = 0; i < Lines.Length; i++) Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + i, Lines[i]);
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "morning", "Morning, boss.");
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "afternoon", "Afternoon, boss.");
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "evening", "Evening, boss.");
    }

    private static void Postfix(Human __instance, string msgID, ref Il2CppSystem.Collections.Generic.List<string> __result)
    {
        if (msgID != WelcomePlayer) return;
        try
        {
            if (__instance == null || __instance.isPlayer || Store.Get(__instance.job?.employer) == null) return;
            Register();
            var lines = new Il2CppSystem.Collections.Generic.List<string>();
            var line = Pick();
            lines.Add(line);
            __result = lines;
            if (Plugin.LogEachSale.Value) Plugin.Logger.LogInfo($"Boss greeting from {__instance.GetCitizenName()}: {line}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Boss greeting failed: " + e.Message);
        }
    }

    /// <summary>Half the time a time-of-day greeting, otherwise one of the other lines.</summary>
    private static string Pick()
    {
        if (UnityEngine.Random.value < 0.5f)
        {
            var hour = SessionData.Instance.decimalClock;
            return Prefix_ + (hour >= 5f && hour < 12f ? "morning" : hour >= 12f && hour < 18f ? "afternoon" : "evening");
        }
        return Prefix_ + UnityEngine.Random.Range(0, Lines.Length);
    }
}
