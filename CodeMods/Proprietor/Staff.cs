using System;
using HarmonyLib;
using SOD.Common;

namespace Proprietor;

/// <summary>
/// Staff at your businesses greet you as their boss. Waiters, bar staff, receptionists and shopkeepers greet
/// customers with the game's "Welcome_Player01" line ("How can I help you?", or "What do you want?" from the rude
/// ones). For the people who work for you it is swapped for a plain, unenthusiastic "boss" line.
/// </summary>
[HarmonyPatch(typeof(SpeechController), nameof(SpeechController.Speak),
    new[] { typeof(string), typeof(bool), typeof(bool), typeof(Human), typeof(SideJob), typeof(Human.InteractionDialogInstance) })]
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

    public static void Register()
    {
        for (var i = 0; i < Lines.Length; i++) Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + i, Lines[i]);
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "morning", "Morning, boss.");
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "afternoon", "Afternoon, boss.");
        Lib.DdsStrings.AddOrUpdate("dds.blocks", Prefix_ + "evening", "Evening, boss.");
    }

    private static bool Prefix(SpeechController __instance, string ddsMessage, bool shout, bool interupt)
    {
        if (ddsMessage != WelcomePlayer) return true;
        try
        {
            var human = __instance.actor?.TryCast<Human>();
            if (human == null || Store.Get(human.job?.employer) == null) return true;
            __instance.Speak("dds.blocks", Pick(), true, shout, interupt);
            return false;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Boss greeting failed: " + e.Message);
            return true;
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
