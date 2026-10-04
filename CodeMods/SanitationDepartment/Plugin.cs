using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using SOD.Common;
using SOD.Common.Helpers;
using UnityEngine;

namespace SanitationDepartment;

/// <summary>
/// Sanitation Department: turns the city's trash into a job.
/// - Sanitation License (City Hall): trash items get a "Trash" tag and binning them pays.
/// - Transfer to Sanitation full-time (sandbox only): no new murders. Returning is free and instant.
/// - Route Supervisor (offered after binning 100 items): a subtle glow on nearby visible trash.
/// - Crime scene cleanup certification: case-free death scenes, released by the Enforcers for cleaning.
/// </summary>
[BepInPlugin(Guid, "Sanitation Department", "1.1.1")]
[BepInDependency("Venomaus.SOD.Common")]
public class Plugin : BasePlugin
{
    public const string Guid = "sodmods.sanitationdepartment";

    internal static ManualLogSource Logger;

    internal static ConfigEntry<int> LicenseCost;
    internal static ConfigEntry<int> PayPerItem;
    internal static ConfigEntry<int> SupervisorCost;
    internal static ConfigEntry<int> SupervisorItems;
    internal static ConfigEntry<float> GlowRange;
    internal static ConfigEntry<KeyCode> GlowToggleKey;
    internal static ConfigEntry<bool> TagWithStreetCleanerDisk;
    internal static ConfigEntry<string> DeskJobs;
    internal static ConfigEntry<KeyCode> ActionKey;
    internal static ConfigEntry<string> NotJunk;
    internal static ConfigEntry<string> TrashSources;
    internal static ConfigEntry<float> SourceRefillHours;
    internal static ConfigEntry<KeyCode> KeepKey;
    internal static ConfigEntry<bool> LogItems;
    internal static ConfigEntry<int> BulkyPay;
    internal static ConfigEntry<int> BreakdownPieces;
    internal static ConfigEntry<int> BagSize;
    internal static ConfigEntry<int> SupervisorBagSize;
    internal static ConfigEntry<int> PickerCost;
    internal static ConfigEntry<float> PickerReach;
    internal static ConfigEntry<int> CertificationCost;
    internal static ConfigEntry<int> MopCost;
    internal static ConfigEntry<int> SceneFee;
    internal static ConfigEntry<int> PayPerSpot;
    internal static ConfigEntry<int> PoolSpots;
    internal static ConfigEntry<float> CleanThreshold;
    internal static ConfigEntry<float> SceneIntervalMin;
    internal static ConfigEntry<float> SceneIntervalMax;
    internal static ConfigEntry<int> MaxScenes;
    internal static ConfigEntry<bool> ScenesOnlyFullTime;
    internal static ConfigEntry<float> DiscoveryHours;
    internal static ConfigEntry<float> InvestigationHours;
    internal static ConfigEntry<float> AccessHours;
    internal static ConfigEntry<float> RagRadius;
    internal static ConfigEntry<float> MopRadius;
    internal static ConfigEntry<int> PoolScrubsRag;
    internal static ConfigEntry<int> PoolScrubsMop;
    internal static ConfigEntry<string> BodyBagItem;
    internal static ConfigEntry<KeyCode> SceneDebugKey;
    internal static ConfigEntry<KeyCode> SceneStatusKey;

    public override void Load()
    {
        Logger = Log;
        LicenseCost = Config.Bind("License", "Cost", 150, "Price of a Sanitation License, in crows.");
        PayPerItem = Config.Bind("License", "PayPerItem", 2,
            "Crows paid per item of trash binned while licensed (the Street Cleaner sync disk pays more if installed; the higher value is used).");
        SupervisorItems = Config.Bind("RouteSupervisor", "ItemsRequired", 100, "Items you must bin before City Hall offers the Route Supervisor certificate.");
        SupervisorCost = Config.Bind("RouteSupervisor", "Cost", 500, "Price of the Route Supervisor certificate, in crows.");
        GlowRange = Config.Bind("RouteSupervisor", "GlowRange", 8f, "How far away (metres) trash is highlighted.");
        GlowToggleKey = Config.Bind("RouteSupervisor", "GlowToggleKey", KeyCode.G, "Key to switch the trash glow on and off.");
        TagWithStreetCleanerDisk = Config.Bind("Tag", "ShowWithStreetCleanerDisk", true,
            "Also show the Trash tag to players with the game's Street Cleaner sync disk perk, even without the license.");
        ActionKey = Config.Bind("Controls", "ActionKey", KeyCode.B,
            "Look at a bin and press to empty your bag; press while carrying bulky junk to break it down into the bag. At a crime scene: press at the body to bag it, hold while looking at blood to scrub.");
        KeepKey = Config.Bind("Controls", "KeepKey", KeyCode.LeftShift,
            "Hold while picking up trash or junk to put it in an inventory slot instead of the bag (to keep it).");
        LogItems = Config.Bind("Debug", "LogItems", false, "Log how items you look at are classified (for tuning and bug reports).");
        NotJunk = Config.Bind("Items", "NotJunk", "TrafficCone,Barrel,PlasticStool,SmallCrate",
            "Comma-separated item types that are never junk (street furniture etc.).");
        TrashSources = Config.Bind("Items", "TrashSources", "Dustbin",
            "Comma-separated item types you collect trash FROM (look at one and press the action key) instead of binning them.");
        SourceRefillHours = Config.Bind("Items", "SourceRefillHours", 12f,
            "In-game hours before an emptied dustbin has trash to collect again (1 in-game hour is about 6.5 real minutes).");
        BulkyPay = Config.Bind("License", "BulkyPay", 5, "Crows for tossing a bulky junk item (e.g. a cardboard box) into a bin or dumpster whole.");
        BreakdownPieces = Config.Bind("Bag", "BreakdownPieces", 3, "How many bag items a broken-down bulky item becomes.");
        BagSize = Config.Bind("Bag", "Capacity", 15, "Items your bag holds.");
        SupervisorBagSize = Config.Bind("Bag", "SupervisorCapacity", 30, "Items your bag holds once you're a Route Supervisor.");
        PickerCost = Config.Bind("LitterPicker", "Cost", 75, "Price of a litter picker at City Hall.");
        PickerReach = Config.Bind("LitterPicker", "Reach", 4f, "How far (metres) you can grab trash and junk with the litter picker.");
        CertificationCost = Config.Bind("CrimeScenes", "CertificationCost", 250, "Price of crime scene cleanup certification at City Hall (requires the Sanitation License).");
        MopCost = Config.Bind("CrimeScenes", "MopCost", 100, "Price of a mop and bucket at City Hall (scrubs a wider area, faster).");
        SceneFee = Config.Bind("CrimeScenes", "SceneFee", 150, "Crows paid for finishing a crime scene.");
        PayPerSpot = Config.Bind("CrimeScenes", "PayPerSpot", 1, "Crows per spot of blood scrubbed, paid when the scene is finished.");
        PoolSpots = Config.Bind("CrimeScenes", "PoolSpots", 20, "How many spots a mopped-up blood pool counts as, for pay.");
        CleanThreshold = Config.Bind("CrimeScenes", "CleanThreshold", 0.9f, "Share of the blood that must be scrubbed for the scene to count as clean (0-1).");
        SceneIntervalMin = Config.Bind("CrimeScenes", "IntervalMinHours", 10f, "Fewest in-game hours between new scenes.");
        SceneIntervalMax = Config.Bind("CrimeScenes", "IntervalMaxHours", 18f, "Most in-game hours between new scenes.");
        MaxScenes = Config.Bind("CrimeScenes", "MaxOpenScenes", 1, "Scenes that can be open at once.");
        ScenesOnlyFullTime = Config.Bind("CrimeScenes", "OnlyWhenFullTime", true,
            "Only post scenes while you're on sanitation full-time (when off, murders give the Enforcers enough to do).");
        DiscoveryHours = Config.Bind("CrimeScenes", "DiscoveryHours", 0.5f, "Most in-game hours before a neighbour reports a death (they may notice sooner).");
        InvestigationHours = Config.Bind("CrimeScenes", "InvestigationHours", 2f, "In-game hours the Enforcers hold a scene, after their search, before releasing it for cleaning.");
        AccessHours = Config.Bind("CrimeScenes", "AccessHours", 24f, "How long a released scene lets you in without trespassing.");
        RagRadius = Config.Bind("CrimeScenes", "RagRadius", 0.45f, "Scrubbing reach around the point you look at, without the mop (metres).");
        MopRadius = Config.Bind("CrimeScenes", "MopRadius", 0.9f, "Scrubbing reach with the mop and bucket (metres).");
        PoolScrubsRag = Config.Bind("CrimeScenes", "PoolScrubsRag", 25, "Scrubbing ticks to mop up a blood pool without the mop.");
        PoolScrubsMop = Config.Bind("CrimeScenes", "PoolScrubsMop", 10, "Scrubbing ticks to mop up a blood pool with the mop.");
        BodyBagItem = Config.Bind("CrimeScenes", "BodyBagItem", "BinBag", "Item the body bag is made from (stretched to size).");
        SceneDebugKey = Config.Bind("Debug", "SceneKey", KeyCode.None, "Testing: create a crime scene somewhere in the city right now.");
        SceneStatusKey = Config.Bind("Debug", "SceneStatusKey", KeyCode.None, "Testing: write every crime scene's status to the log.");
        DeskJobs = Config.Bind("CityHall", "DeskJobs", "",
            "Comma-separated job names at City Hall who handle sanitation paperwork. Empty = any City Hall employee at work.");

        Lib.SaveGame.OnAfterLoad += (_, args) =>
        {
            State.Load(args);
            Scenes.Load(args);
            Log.LogInfo($"Save loaded: license={State.Licensed} fullTime={State.FullTime} binned={State.Binned} supervisor={State.Supervisor}");
        };
        Lib.SaveGame.OnAfterSave += (_, args) =>
        {
            State.Save(args);
            Scenes.Save(args);
        };
        Lib.SaveGame.OnBeforeNewGame += (_, _) =>
        {
            State.Reset();
            Scenes.All.Clear();
            Scenes.NextSceneAt = -1f;
        };

        Dialogs.Register();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Sanitation Department loaded");
    }
}

/// <summary>Per-save progress, stored next to each save file via SOD.Common.</summary>
internal static class State
{
    private const string FileName = "sanitation.txt";

    internal static bool Licensed;
    internal static bool FullTime;
    internal static int Binned;
    internal static bool Supervisor;
    internal static bool GlowOn = true;
    internal static int BagCount;
    /// <summary>When each dustbin (by item id) was last emptied, in game hours.</summary>
    internal static readonly System.Collections.Generic.Dictionary<int, float> SourceEmptied = new();
    internal static bool Picker;
    internal static bool Certified;
    internal static bool Mop;
    internal static int ScenesCleaned;

    internal static int BagCapacity => Supervisor ? Plugin.SupervisorBagSize.Value : Plugin.BagSize.Value;

    /// <summary>Counts disposed items toward Route Supervisor and announces the promotion.</summary>
    internal static void AddBinned(int items)
    {
        if (items <= 0) return;
        var needed = Plugin.SupervisorItems.Value;
        var before = Binned;
        Binned += items;
        Plugin.Logger.LogInfo($"Binned {items} (total {Binned})");
        if (!Supervisor && before < needed && Binned >= needed)
            Notify($"{needed} items binned. City Hall has a promotion for you: Route Supervisor.", InterfaceControls.Icon.star);
    }

    internal static void Reset()
    {
        Licensed = FullTime = Supervisor = Picker = Certified = Mop = false;
        Binned = BagCount = ScenesCleaned = 0;
        SourceEmptied.Clear();
        GlowOn = true;
    }

    internal static void Load(SaveGameArgs args)
    {
        Reset();
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                var value = parts[1].Trim();
                switch (parts[0].Trim())
                {
                    case "licensed": Licensed = value == "true"; break;
                    case "fulltime": FullTime = value == "true"; break;
                    case "binned": int.TryParse(value, out Binned); break;
                    case "supervisor": Supervisor = value == "true"; break;
                    case "glow": GlowOn = value != "false"; break;
                    case "bag": int.TryParse(value, out BagCount); break;
                    case "picker": Picker = value == "true"; break;
                    case "certified": Certified = value == "true"; break;
                    case "mop": Mop = value == "true"; break;
                    case "scenes": int.TryParse(value, out ScenesCleaned); break;
                    case "dustbins":
                        foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kv = pair.Split(':');
                            if (kv.Length == 2 && int.TryParse(kv[0], out var id) &&
                                float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                                SourceEmptied[id] = t;
                        }
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to load sanitation state: " + e);
        }
    }

    internal static void Save(SaveGameArgs args)
    {
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            File.WriteAllLines(path, new[]
            {
                $"licensed={(Licensed ? "true" : "false")}",
                $"fulltime={(FullTime ? "true" : "false")}",
                $"binned={Binned}",
                $"supervisor={(Supervisor ? "true" : "false")}",
                $"glow={(GlowOn ? "true" : "false")}",
                $"bag={BagCount}",
                $"picker={(Picker ? "true" : "false")}",
                $"certified={(Certified ? "true" : "false")}",
                $"mop={(Mop ? "true" : "false")}",
                $"scenes={ScenesCleaned}",
                "dustbins=" + string.Join(";", System.Linq.Enumerable.Select(SourceEmptied,
                    kv => $"{kv.Key}:{kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            });
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to save sanitation state: " + e);
        }
    }

    internal static void Notify(string message, InterfaceControls.Icon icon = InterfaceControls.Icon.trash)
    {
        Lib.GameMessage.Broadcast(message, icon: icon, color: InterfaceControls.Instance.messageGreen);
        Plugin.Logger.LogInfo(message);
    }
}
