using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SOD.Common;
using SOD.Common.Helpers;
using UnityEngine;

namespace SanitationDepartment;

internal enum SceneState
{
    /// <summary>Dead, not yet reported.</summary>
    Undiscovered,
    /// <summary>Reported; the Enforcers are responding, searching or guarding.</summary>
    Investigating,
    /// <summary>Released to the player. The game's own cleanup is held until the scene is clean.</summary>
    Released,
    /// <summary>Clean and paid. Waiting for the game's cleanup (the coroner) once the player leaves.</summary>
    Done,
}

internal class Scene
{
    public int AddressId;
    public int VictimId;
    public SceneState State;
    public float DiedAt;
    /// <summary>Blood spots when the scene was released.</summary>
    public int InitialSpots;
    /// <summary>Spots scrubbed so far (paid on completion).</summary>
    public int Scrubbed;
    /// <summary>The body bag item, once the body is bagged (0 = not bagged).</summary>
    public int BagId;

    public bool Bagged => BagId != 0;

    public NewAddress Address => CityData.Instance.addressDictionary.TryGetValue(AddressId, out var a) ? a : null;

    public Human Victim => CityData.Instance.GetHuman(VictimId, out var h) ? h : null;

    public string Name => Address?.name ?? $"address {AddressId}";
}

/// <summary>
/// Crime scene cleanup jobs. A scene is a resident found dead at home with no murder case behind it
/// (the same kind of death the game records when the player kills someone), so the Enforcers'
/// crime-scene routine runs on it but no case, killer or evidence exists. After their investigation
/// the scene is released to the player instead of the game's invisible auto-cleanup.
/// </summary>
internal static class Scenes
{
    private const string FileName = "cleanup.txt";

    internal static readonly List<Scene> All = new();
    internal static float NextSceneAt = -1f;

    private static MurderWeaponPreset weapon;

    internal static Scene At(NewGameLocation loc)
    {
        var id = loc?.thisAsAddress?.id ?? -1;
        return id < 0 ? null : All.Find(s => s.AddressId == id);
    }

    internal static Interactable FindBag(Scene s)
    {
        if (!s.Bagged) return null;
        if (CityData.Instance.savableInteractableDictionary.TryGetValue(s.BagId, out var bag) && bag != null) return bag;
        foreach (var i in CityData.Instance.interactableDirectory)
            if (i != null && i.id == s.BagId) return i;
        return null;
    }

    internal static bool IsBodyBag(Interactable item) => item != null && All.Any(s => s.Bagged && s.BagId == item.id);

    // ---------------------------------------------------------------- Generating scenes

    /// <summary>Scenes are posted to certified cleaners on sanitation full-time (or always, if configured).</summary>
    private static bool Generating =>
        State.Certified && FullTime.Allowed && (State.FullTime || !Plugin.ScenesOnlyFullTime.Value);

    private static void ScheduleNext() =>
        NextSceneAt = SessionData.Instance.gameTime +
                      UnityEngine.Random.Range(Plugin.SceneIntervalMin.Value, Math.Max(Plugin.SceneIntervalMin.Value, Plugin.SceneIntervalMax.Value));

    private static void GeneratorTick()
    {
        if (!Generating)
        {
            NextSceneAt = -1f;
            return;
        }
        if (NextSceneAt < 0f)
        {
            ScheduleNext();
            return;
        }
        if (SessionData.Instance.gameTime < NextSceneAt) return;
        if (All.Count(s => s.State != SceneState.Done) < Plugin.MaxScenes.Value) SpawnScene(false);
        ScheduleNext();
    }

    public static void SpawnScene(bool announce)
    {
        var victim = PickVictim();
        if (victim == null)
        {
            if (announce) State.Notify("[Debug] No suitable scene right now.");
            return;
        }

        var home = victim.home;
        Kill(victim);
        All.Add(new Scene
        {
            AddressId = home.id,
            VictimId = victim.humanID,
            State = SceneState.Undiscovered,
            DiedAt = SessionData.Instance.gameTime,
        });
        Plugin.Logger.LogInfo($"Scene created: {victim.GetCitizenName()} at {home.name}");
        if (announce) State.Notify($"[Debug] Scene created at {home.name}.");
    }

    /// <summary>A resident at home alone, away from the player, not tied to a case or the Enforcers.</summary>
    private static Citizen PickVictim()
    {
        var player = Player.Instance;
        var murders = MurderController.Instance;
        var candidates = CityData.Instance.citizenDirectory.ToArray().Where(c =>
            c != null && !c.isDead && !c.removedFromWorld && !c.isPlayer && c.ai != null && !c.isEnforcer &&
            c.home != null && c.home.residence != null && c.home.company == null && c.currentGameLocation == c.home &&
            c.home.building != player.currentBuilding && c.home != player.home &&
            At(c.home) == null && !GameplayController.Instance.enforcerCalls.ContainsKey(c.home) &&
            c.home.currentOccupants.ToArray().All(o => o == c || o.isDead) &&
            !murders.activeMurders.ToArray().Any(m => m.victim == c || m.murderer == c) &&
            murders.currentMurderer != c).ToList();
        return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private static void Kill(Citizen victim)
    {
        victim.SetHealth(0f);
        victim.ai.SetKO(true);

        // A death record without a murder case. The game's own constructor assumes a case exists, so the
        // record is filled in here the way it would fill it (the loader does the same for saved deaths).
        var death = new Human.Death(IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Human.Death>.NativeClassPtr))
        {
            isDead = true,
            murder = -1,
            victim = victim.humanID,
            killer = victim.humanID,
            location = victim.currentNodeCoord,
            time = SessionData.Instance.gameTime,
        };
        death.timeOfDeathRange = Toolbox.Instance.CreateTimeRange(death.time, GameplayControls.Instance.timeOfDeathAccuracy, false, true, 15);
        if (victim.currentRoom != null) victim.currentRoom.containsDead = true;
        victim.death = death;
        victim.Murder(victim, false, null, null, 0f);
        Bloody(victim);
    }

    /// <summary>Wounds (which grow a blood pool over time) and spatter that never fades on its own.</summary>
    private static void Bloody(Citizen victim)
    {
        try
        {
            weapon ??= FindWeapon();
            var chest = victim.transform.position + Vector3.up * 1.1f;
            if (weapon?.entryWound != null)
            {
                victim.CreateWoundClosestToPoint(chest, Vector3.up, weapon.entryWound, weapon);
                victim.CreateWoundClosestToPoint(chest + Vector3.up * 0.15f, Vector3.up, weapon.entryWound, weapon);
            }

            var forward = weapon?.forwardSpatter ?? GameplayControls.Instance.bleedingSpatter;
            var back = weapon?.backSpatter ?? GameplayControls.Instance.bleedingSpatter;
            var local = new Vector3(0f, 1.1f, 0f);
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left + Vector3.down, Vector3.down })
                new SpatterSimulation(victim, local, dir, forward, SpatterSimulation.EraseMode.neverOrManual, 1f, false);
            new SpatterSimulation(victim, local, Vector3.back, back, SpatterSimulation.EraseMode.neverOrManual, 1f, false);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Blood failed: " + e);
        }
    }

    private static MurderWeaponPreset FindWeapon()
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<MurderWeaponPreset>())
            .Select(o => o.TryCast<MurderWeaponPreset>()).Where(w => w != null && w.entryWound != null).ToList();
        return all.FirstOrDefault(w => w.name.IndexOf("knife", StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
    }

    // ---------------------------------------------------------------- Advancing scenes

    /// <summary>Every world tick: neighbours report bodies, investigations end, finished scenes get collected.</summary>
    public static void Tick()
    {
        GeneratorTick();
        var now = SessionData.Instance.gameTime;
        foreach (var s in All.ToList())
        {
            var addr = s.Address;
            var victim = s.Victim;
            if (addr == null || victim == null || (victim.removedFromWorld && !s.Bagged && s.State != SceneState.Done))
            {
                Plugin.Logger.LogWarning($"Dropping scene at {s.Name}: address or body gone");
                All.Remove(s);
                continue;
            }

            switch (s.State)
            {
                case SceneState.Undiscovered:
                    if (victim.death != null && victim.death.reported)
                        Investigating(s, addr);
                    else if (now >= s.DiedAt + Plugin.DiscoveryHours.Value)
                        Report(s, addr, victim);
                    break;

                case SceneState.Investigating:
                    var calls = GameplayController.Instance.enforcerCalls;
                    if (!calls.ContainsKey(addr))
                    {
                        Release(s);
                        break;
                    }
                    // The game keeps a crime scene open for its own (long) crimeSceneLength; our investigation ends sooner.
                    var call = calls[addr];
                    var due = call.arrivalTime + GameplayControls.Instance.crimeSceneSearchLength + Plugin.InvestigationHours.Value;
                    if (call.state == GameplayController.EnforcerCallState.arrived && call.arrivalTime > 0f && now > due)
                    {
                        call.isCrimeScene = true;
                        call.state = GameplayController.EnforcerCallState.completed;
                    }
                    break;

                case SceneState.Done:
                    // The game's cleanup has run once the address has left its queue.
                    if (!GameplayController.Instance.crimeSceneCleanups.Contains(addr)) Collected(s);
                    break;
            }
        }
    }

    private static void Report(Scene s, NewAddress addr, Human victim)
    {
        var finder = CityData.Instance.citizenDirectory.ToArray()
            .Where(h => h != null && h != victim && !h.isDead && h.ai != null && !h.isEnforcer)
            .OrderBy(h => h.currentBuilding == addr.building ? 0 : 1).FirstOrDefault();
        GameplayController.Instance.CallEnforcers(addr, false, false, 0f);
        if (finder != null) victim.death.SetReported(finder, Human.Death.ReportType.smell);
        Investigating(s, addr);
    }

    /// <summary>A heads-up for certified cleaners: there'll be a job here once the Enforcers are done.</summary>
    private static void Investigating(Scene s, NewAddress addr)
    {
        s.State = SceneState.Investigating;
        if (State.Certified) State.Notify($"Enforcers responding to a death at {addr.name}. Cleanup crew on standby.", InterfaceControls.Icon.skull);
    }

    public static void Release(Scene s)
    {
        s.State = SceneState.Released;
        var addr = s.Address;
        if (addr == null) return;
        s.InitialSpots = Cleaning.CountSpots(addr);
        GameplayController.Instance.AddGuestPass(addr, Plugin.AccessHours.Value);
        var text = $"Cleanup job: {addr.name} has been released by the Enforcers. Bag the body and scrub the blood.";
        InterfaceController.Instance.NewGameMessage(InterfaceController.GameMessageType.notification, 0, text,
            InterfaceControls.Icon.skull, AudioControls.Instance.enforcerScannerMsg);
        Plugin.Logger.LogInfo($"{text} ({s.InitialSpots} spots)");
    }

    /// <summary>The scene is clean: pay up and hand it to the game's own cleanup (the coroner).</summary>
    public static void Complete(Scene s)
    {
        var addr = s.Address;
        s.State = SceneState.Done;
        var pay = Plugin.SceneFee.Value + s.Scrubbed * Plugin.PayPerSpot.Value;
        if (pay > 0) GameplayController.Instance.AddMoney(pay, true, Trash.Reason);
        State.ScenesCleaned++;
        addr.loggedAsCrimeScene = SessionData.Instance.gameTime - GameplayControls.Instance.crimeSceneCleanupDelay - 0.01f;
        if (!GameplayController.Instance.crimeSceneCleanups.Contains(addr)) GameplayController.Instance.crimeSceneCleanups.Add(addr);
        State.Notify($"{addr.name} is clean. {CityControls.Instance.cityCurrency}{pay} paid. The coroner collects the body bag once you've left.",
            InterfaceControls.Icon.star);
    }

    /// <summary>The game's cleanup has run (it removes the body and, if nobody else lives there, clears the flat).</summary>
    private static void Collected(Scene s)
    {
        Scenes.FindBag(s)?.SafeDelete(true);
        All.Remove(s);
        Plugin.Logger.LogInfo($"Scene at {s.Name} collected");
    }

    // ---------------------------------------------------------------- Saving

    internal static void Load(SaveGameArgs args)
    {
        All.Clear();
        NextSceneAt = -1f;
        try
        {
            var path = Lib.SaveGame.GetSaveGameDataPath(args, FileName);
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith("next="))
                {
                    float.TryParse(line[5..], NumberStyles.Float, CultureInfo.InvariantCulture, out NextSceneAt);
                    continue;
                }
                var p = line.Split(',');
                if (p.Length < 7 || !int.TryParse(p[0], out var addr) || !int.TryParse(p[1], out var victim) ||
                    !Enum.TryParse<SceneState>(p[2], out var state) ||
                    !float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var died)) continue;
                int.TryParse(p[4], out var initial);
                int.TryParse(p[5], out var scrubbed);
                int.TryParse(p[6], out var bag);
                All.Add(new Scene
                {
                    AddressId = addr, VictimId = victim, State = state, DiedAt = died,
                    InitialSpots = initial, Scrubbed = scrubbed, BagId = bag,
                });
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to load cleanup scenes: " + e);
        }
    }

    internal static void Save(SaveGameArgs args)
    {
        try
        {
            var lines = new List<string> { "next=" + NextSceneAt.ToString(CultureInfo.InvariantCulture) };
            lines.AddRange(All.Select(s =>
                $"{s.AddressId},{s.VictimId},{s.State},{s.DiedAt.ToString(CultureInfo.InvariantCulture)},{s.InitialSpots},{s.Scrubbed},{s.BagId}"));
            File.WriteAllLines(Lib.SaveGame.GetSaveGameDataPath(args, FileName), lines);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Failed to save cleanup scenes: " + e);
        }
    }

    internal static void LogStatus()
    {
        var now = SessionData.Instance.gameTime;
        Plugin.Logger.LogInfo($"--- {All.Count} scene(s), game time {now:0.00}, next scene at {NextSceneAt:0.00}, generating={Generating} ---");
        foreach (var s in All)
        {
            var addr = s.Address;
            var calls = GameplayController.Instance.enforcerCalls;
            var call = addr != null && calls.ContainsKey(addr) ? calls[addr] : null;
            Plugin.Logger.LogInfo(
                $"{s.Name}: {s.State}, victim {s.Victim?.GetCitizenName()}, died {now - s.DiedAt:0.00}h ago, " +
                $"call={(call == null ? "none" : $"{call.state} arrived {now - call.arrivalTime:0.00}h ago guard={call.guard}")}, " +
                $"spots {(addr != null ? Cleaning.CountSpots(addr) : -1)}/{s.InitialSpots}, pools {(addr != null ? Cleaning.Pools(addr).Count : -1)}, " +
                $"scrubbed {s.Scrubbed}, bag {s.BagId}, inGameQueue={GameplayController.Instance.crimeSceneCleanups.Contains(addr)}");
        }
    }
}

/// <summary>
/// The game's world tick. It walks every Enforcer call through respond, search, guard and complete, then
/// queues finished crime scenes for its invisible cleanup. Our scenes are pulled out of that queue so they
/// wait for the player.
/// </summary>
[HarmonyPatch(typeof(CitizenBehaviour), nameof(CitizenBehaviour.GameWorldCheck))]
internal static class WorldTickPatch
{
    private static void Prefix() => Hold();

    private static void Postfix()
    {
        try
        {
            Hold();
            Scenes.Tick();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    private static void Hold()
    {
        try
        {
            var queue = GameplayController.Instance?.crimeSceneCleanups;
            if (queue == null || Scenes.All.Count == 0) return;

            // The game's cleanup waits 5 hours from when a scene was logged, and can run in the same tick that
            // queues it (after a long sleep, say), before we get to pull it out. Keeping the logged time current
            // means that wait never runs out while one of our scenes is open.
            var now = SessionData.Instance.gameTime;
            foreach (var s in Scenes.All)
            {
                if (s.State == SceneState.Done) continue;
                var addr = s.Address;
                if (addr != null && addr.loggedAsCrimeScene < now) addr.loggedAsCrimeScene = now;
            }

            for (var i = queue.Count - 1; i >= 0; i--)
            {
                var s = Scenes.At(queue[i]);
                if (s == null || s.State == SceneState.Done) continue;
                queue.RemoveAt(i);
                if (s.State != SceneState.Released) Scenes.Release(s);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

/// <summary>
/// When the game clears out a dead tenant's flat it leaves some furniture sets behind. For our scenes the
/// landlord clears out everything.
/// </summary>
[HarmonyPatch(typeof(NewGameLocation), nameof(NewGameLocation.RemoveAllInhabitantFurniture))]
internal static class ClearFlatPatch
{
    private static void Prefix(NewGameLocation __instance, ref bool removeSkipAddressInhabitantsFurniture)
    {
        if (Scenes.At(__instance)?.State == SceneState.Done) removeSkipAddressInhabitantsFurniture = true;
    }
}
