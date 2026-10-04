using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace SanitationDepartment;

/// <summary>
/// Working a released scene: bag the body, mop up the blood pool, scrub the spatter. Blood spatter is a
/// set of decals; scrubbing removes the ones near where you're looking, a few at a time. Once the body is
/// bagged, the pool is gone and nearly all the spots are scrubbed, the job completes.
/// </summary>
[HarmonyPatch(typeof(Player), "Update")]
internal static class CleaningPatch
{
    private static void Postfix()
    {
        try
        {
            if (SessionData.Instance == null || !SessionData.Instance.play) return;
            var focused = Cursor.lockState == CursorLockMode.Locked;
            if (focused && Plugin.SceneDebugKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.SceneDebugKey.Value))
                Scenes.SpawnScene(true);
            if (focused && Plugin.SceneStatusKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.SceneStatusKey.Value))
                Scenes.LogStatus();

            Cleaning.KeepBagsShaped();
            var scene = Scenes.At(Player.Instance.currentGameLocation);
            if (scene == null || scene.State != SceneState.Released)
            {
                Cleaning.Left();
                return;
            }
            Cleaning.InScene(scene, focused);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }
}

internal static class Cleaning
{
    private static int enteredScene = -1;
    private static float nextScrub, nextCheck, nextReport;
    private static int scrubbedSinceReport;
    private static readonly Dictionary<int, int> poolProgress = new();
    private static InteractablePreset bagPreset;

    private static float Radius => State.Mop ? Plugin.MopRadius.Value : Plugin.RagRadius.Value;

    public static void Left() => enteredScene = -1;

    private static float nextShapeCheck;

    /// <summary>
    /// The game recreates the bag's model from the plain bin bag whenever it reloads it (after loading a save,
    /// or coming back into range), sometimes before the mod knows it's a body bag. Reshape any that need it.
    /// </summary>
    public static void KeepBagsShaped()
    {
        if (Time.unscaledTime < nextShapeCheck) return;
        nextShapeCheck = Time.unscaledTime + 1f;
        foreach (var s in Scenes.All)
        {
            var bag = Scenes.FindBag(s);
            var obj = bag?.spawnedObject;
            if (obj != null && (obj.transform.localScale - Vector3.one).sqrMagnitude < 0.0001f) BodyBagShape.Apply(bag);
        }
    }

    public static void InScene(Scene scene, bool focused)
    {
        var addr = scene.Address;
        if (enteredScene != scene.AddressId)
        {
            enteredScene = scene.AddressId;
            var pools = Pools(addr).Count;
            var todo = new List<string>();
            if (!scene.Bagged) todo.Add($"bag the body ({Plugin.ActionKey.Value})");
            if (pools > 0) todo.Add("mop up the blood pool");
            todo.Add($"scrub {CountSpots(addr)} spots of blood (hold {Plugin.ActionKey.Value} and look at them)");
            Bag.Message("Cleanup job: " + string.Join(", ", todo) + ".");
        }

        var key = Plugin.ActionKey.Value;
        if (focused && Input.GetKeyDown(key) && TryBag(scene)) return;
        if (focused && Input.GetKey(key) && InteractionController.Instance?.carryingObject == null && Time.unscaledTime >= nextScrub)
        {
            nextScrub = Time.unscaledTime + (State.Mop ? 0.06f : 0.1f);
            Scrub(scene, addr);
        }

        if (Time.unscaledTime >= nextCheck)
        {
            nextCheck = Time.unscaledTime + 0.5f;
            if (scene.Bagged && Pools(addr).Count == 0 && CountSpots(addr) <= Allowed(scene)) Scenes.Complete(scene);
        }
    }

    /// <summary>Spots that may be left behind (the last specks in corners aren't worth hunting).</summary>
    private static int Allowed(Scene s) => Mathf.FloorToInt(s.InitialSpots * (1f - Mathf.Clamp01(Plugin.CleanThreshold.Value)));

    // ---------------------------------------------------------------- Blood

    /// <summary>Blood decals in the address, not counting those on the body itself.</summary>
    public static int CountSpots(NewAddress addr)
    {
        var n = 0;
        foreach (var sim in Spatter(addr))
            foreach (var ds in sim.decalsSpawned)
                if (ds != null && ds.parentID != SpatterSimulation.ParentID.human) n++;
        return n;
    }

    private static IEnumerable<SpatterSimulation> Spatter(NewAddress addr)
    {
        foreach (var room in addr.rooms)
        {
            if (room?.spatter == null) continue;
            foreach (var sim in room.spatter.ToArray())
                if (sim?.decalsSpawned != null) yield return sim;
        }
    }

    public static List<Interactable> Pools(NewAddress addr)
    {
        var result = new List<Interactable>();
        var poolPreset = PrefabControls.Instance.bloodPool;
        foreach (var room in addr.rooms)
        {
            if (room?.nodes == null) continue;
            foreach (var node in room.nodes)
            {
                if (node?.interactables == null) continue;
                foreach (var i in node.interactables)
                    if (i != null && i.preset == poolPreset) result.Add(i);
            }
        }
        return result;
    }

    public static bool IsPool(Interactable item) => item?.preset != null && item.preset == PrefabControls.Instance.bloodPool;

    private static void Scrub(Scene scene, NewAddress addr)
    {
        var cam = CameraController.Instance?.cam;
        if (cam == null) return;
        // Spots are picked along your line of sight rather than only where it hits a surface, so blood just
        // behind an edge (down the side of a fridge, inside an open door) can still be reached.
        var origin = cam.transform.position;
        var dir = cam.transform.forward;
        var radius = Radius;
        var reach = Physics.Raycast(origin, dir, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance + radius : 2.5f;

        // The blood pool first: it takes a while to mop.
        foreach (var pool in Pools(addr))
        {
            var pos = pool.spawnedObject != null ? pool.spawnedObject.transform.position : pool.wPos;
            if (DistanceFromSight(pos, origin, dir, reach + 0.5f) > (radius + 0.5f) * (radius + 0.5f)) continue;
            poolProgress.TryGetValue(pool.id, out var done);
            done++;
            var needed = State.Mop ? Plugin.PoolScrubsMop.Value : Plugin.PoolScrubsRag.Value;
            if (done < needed)
            {
                poolProgress[pool.id] = done;
                if (done % 5 == 1) Bag.Message($"Mopping up the blood pool... {Mathf.RoundToInt(100f * done / needed)}%");
                return;
            }
            poolProgress.Remove(pool.id);
            StopBleeding(scene.Victim);
            pool.SafeDelete(true);
            scene.Scrubbed += Plugin.PoolSpots.Value;
            Bag.Message("Blood pool mopped up.");
            return;
        }

        // Then spatter: remove the nearest spots within reach of the cloth.
        var near = new List<(SpatterSimulation sim, SpatterSimulation.DecalSpawnData ds, float d)>();
        foreach (var sim in Spatter(addr))
        {
            foreach (var ds in sim.decalsSpawned)
            {
                if (ds == null || ds.parentID == SpatterSimulation.ParentID.human) continue;
                var pos = ds.spawnedProjector != null ? ds.spawnedProjector.transform.position : ds.worldPos;
                var d = DistanceFromSight(pos, origin, dir, reach);
                if (d <= radius * radius) near.Add((sim, ds, d));
            }
        }
        if (near.Count == 0) return;

        var count = State.Mop ? 3 : 1;
        foreach (var (sim, ds, _) in near.OrderBy(n => n.d).Take(count))
            RemoveDecal(sim, ds);
        scene.Scrubbed += Math.Min(count, near.Count);
        scrubbedSinceReport += Math.Min(count, near.Count);

        if (Time.unscaledTime >= nextReport)
        {
            nextReport = Time.unscaledTime + 3f;
            var left = Math.Max(0, CountSpots(addr) - Allowed(scene));
            Bag.Message(left > 0 ? $"Scrubbing... {left} spots to go" : "That's the worst of the blood gone.");
        }
    }

    /// <summary>Squared distance from a point to the line of sight, or infinity if it's behind you or out of reach.</summary>
    private static float DistanceFromSight(Vector3 pos, Vector3 origin, Vector3 dir, float reach)
    {
        var v = pos - origin;
        var t = Vector3.Dot(v, dir);
        if (t < 0f || t > reach) return float.PositiveInfinity;
        return (v - dir * t).sqrMagnitude;
    }

    /// <summary>Takes one decal out of a spatter (what the game's own SpatterSimulation.Remove does for all of them).</summary>
    private static void RemoveDecal(SpatterSimulation sim, SpatterSimulation.DecalSpawnData ds)
    {
        if (ds.parentID == SpatterSimulation.ParentID.interactable && ds.i?.spawnedDecals != null) ds.i.spawnedDecals.Remove(ds);
        if (ds.spawnedProjector != null)
        {
            SpatterSimulation.DecalSpawnData.RecycleDecalProjector(ds.spawnedProjector);
            ds.spawnedProjector = null;
        }
        var list = sim.decalsSpawned;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i]?.Pointer != ds.Pointer) continue;
            list.RemoveAt(i);
            break;
        }
        if (list.Count == 0) sim.Remove();
    }

    /// <summary>Stops the body's wounds from feeding a new blood pool.</summary>
    private static void StopBleeding(Human victim)
    {
        if (victim?.currentWounds == null) return;
        foreach (var w in victim.currentWounds)
            if (w != null) w.bloodPoolAmount = 0f;
    }

    // ---------------------------------------------------------------- Body bag

    /// <summary>Looking at the body and pressing the action key zips it into a body bag.</summary>
    private static bool TryBag(Scene scene)
    {
        if (scene.Bagged) return false;
        var looking = InteractionController.Instance?.currentLookingAtInteractable?.interactable;
        var victim = scene.Victim;
        if (looking?.isActor == null || victim == null || looking.isActor.Pointer != victim.Pointer) return false;

        bagPreset ??= FindBagPreset();
        if (bagPreset == null)
        {
            Bag.Message("No body bag available (couldn't find the bag item).");
            return false;
        }

        var anchor = victim.outfitController?.GetBodyAnchor(CitizenOutfitController.CharacterAnchor.upperTorso);
        var pos = anchor != null ? anchor.position : victim.transform.position;
        if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var floor, 3f, Toolbox.Instance.playerMovementLayerMask, QueryTriggerInteraction.Ignore))
            pos = floor.point;
        var yaw = anchor != null ? anchor.eulerAngles.y : victim.transform.eulerAngles.y;

        StopBleeding(victim);
        victim.RemoveFromWorld(true);
        var bag = InteractableCreator.Instance.CreateWorldInteractable(bagPreset, null, null, null,
            pos + Vector3.up * 0.15f, new Vector3(0f, yaw, 0f), null, null, "");
        if (bag == null)
        {
            Plugin.Logger.LogError("Body bag creation failed");
            return false;
        }
        scene.BagId = bag.id;
        BodyBagShape.Apply(bag);
        Bag.Message("Body bagged. The coroner will collect it when the job's done.");
        return true;
    }

    private static InteractablePreset FindBagPreset()
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<InteractablePreset>())
            .Select(o => o.TryCast<InteractablePreset>()).Where(p => p != null).ToList();
        var bag = all.FirstOrDefault(p => p.name.Equals(Plugin.BodyBagItem.Value, StringComparison.OrdinalIgnoreCase))
                  ?? all.FirstOrDefault(p => p.name.IndexOf("binbag", StringComparison.OrdinalIgnoreCase) >= 0);
        Plugin.Logger.LogInfo($"Body bag item: {bag?.name ?? "none"} (bag-like items: " +
                              string.Join(", ", all.Where(p => p.name.IndexOf("bag", StringComparison.OrdinalIgnoreCase) >= 0).Select(p => p.name)) + ")");
        return bag;
    }
}

/// <summary>
/// Dragging a body is always an illegal action in the game. Moving the victim of your own released scene
/// is the job, so it's allowed there.
/// </summary>
[HarmonyPatch(typeof(RigidbodyDragObject), "Update")]
internal static class LegalDragPatch
{
    internal static bool Allowed;

    private static void Prefix(RigidbodyDragObject __instance)
    {
        var scene = Scenes.At(Player.Instance?.currentGameLocation);
        Allowed = scene != null && scene.State == SceneState.Released && __instance.ai?.human != null &&
                  __instance.ai.human.Pointer == scene.Victim?.Pointer;
    }

    private static void Postfix() => Allowed = false;
}

[HarmonyPatch(typeof(InteractionController), nameof(InteractionController.SetIllegalActionActive))]
internal static class LegalDragIllegalPatch
{
    private static bool Prefix(bool val) => !(val && LegalDragPatch.Allowed);
}

/// <summary>Stretches the bin bag into a body bag whenever it's spawned into the world.</summary>
[HarmonyPatch(typeof(Interactable), nameof(Interactable.OnSpawn))]
internal static class BodyBagShape
{
    private static void Postfix(Interactable __instance)
    {
        try
        {
            if (Scenes.IsBodyBag(__instance)) Apply(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e);
        }
    }

    /// <summary>Scales the model to body size: about 1.8 m long, 0.55 m wide, 0.3 m tall.</summary>
    public static void Apply(Interactable bag)
    {
        var obj = bag?.spawnedObject;
        if (obj == null) return;
        var t = obj.transform;
        var rotation = t.rotation;
        t.rotation = Quaternion.identity;
        t.localScale = Vector3.one;
        var renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            t.rotation = rotation;
            return;
        }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var size = bounds.size;
        t.localScale = new Vector3(0.55f / Mathf.Max(size.x, 0.01f), 0.3f / Mathf.Max(size.y, 0.01f), 1.8f / Mathf.Max(size.z, 0.01f));
        t.rotation = rotation;

        // Stretching scales around the model's centre, which lifts it off the floor: set it back down.
        bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        if (Physics.Raycast(bounds.center, Vector3.down, out var floor, 3f, Toolbox.Instance.playerMovementLayerMask, QueryTriggerInteraction.Ignore))
            t.position += Vector3.up * (floor.point.y + 0.01f - bounds.min.y);
    }
}

/// <summary>The stretched bin bag is called a body bag.</summary>
[HarmonyPatch(typeof(Interactable), nameof(Interactable.GetName))]
internal static class BodyBagNamePatch
{
    private static void Postfix(Interactable __instance, ref string __result)
    {
        if (Scenes.All.Count > 0 && Scenes.IsBodyBag(__instance)) __result = "Body bag";
    }
}
