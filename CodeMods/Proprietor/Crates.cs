using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Proprietor;

/// <summary>A delivery crate waiting to be unpacked.</summary>
public class Crate
{
    public int RoomId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    /// <summary>Hours of staff time it still needs before it's unpacked.</summary>
    public float HoursLeft { get; set; }
}

/// <summary>
/// Deliveries you can see. When an order arrives, a crate turns up on a clear patch of floor in the business's
/// stockroom (else its back room, else its kitchen; never out front), and the staff unpack it over a few hours of
/// their shifts, after which it's gone. The stock itself is on the books the moment it arrives.
///
/// Crates are never written into the save: the game is told they aren't worth saving, and the mod puts them back
/// when the save loads. Without the mod, a save holds no crates.
/// </summary>
[HarmonyPatch]
internal static class Crates
{
    private const int MaxPerBusiness = 2;
    private static readonly string[] PresetNames = { "WoodenCrate", "SmallCrate", "CardboardBoxLarge" };
    private static readonly string[][] RoomPreference =
    {
        new[] { "storeroom", "stockroom", "storage" }, new[] { "backroom", "back room" }, new[] { "kitchen" }
    };

    private static readonly Dictionary<Crate, Interactable> spawned = new();
    private static readonly HashSet<int> ids = new();
    private static InteractablePreset preset;

    /// <summary>An order arrived: a crate turns up (at most a couple at a time; staff stack the rest).</summary>
    public static void Delivered(Business b, Company c)
    {
        if (!Plugin.DeliveryCrates.Value || c?.address == null) return;
        if (b.Crates.Count >= MaxPerBusiness)
        {
            // More work for the crates already waiting.
            b.Crates[^1].HoursLeft += 1f;
            return;
        }
        var spot = FindSpot(c, b);
        if (spot == null) return;
        var (room, pos) = spot.Value;
        var crate = new Crate
        {
            RoomId = room.roomID, X = pos.x, Y = pos.y, Z = pos.z,
            Yaw = UnityEngine.Random.Range(0f, 360f),
            HoursLeft = UnityEngine.Random.Range(2, 5)
        };
        if (Spawn(c, crate))
        {
            b.Crates.Add(crate);
            Plugin.Logger.LogInfo($"{b.Name}: delivery crate left in {room.name}");
        }
    }

    /// <summary>Every hour, staff on shift chip away at the crates; unpacked ones disappear.</summary>
    public static void Hourly()
    {
        foreach (var b in Store.Owned.Values)
        {
            if (b.Crates.Count == 0) continue;
            var c = Store.FindCompany(b.CompanyId);
            if (c == null || (c.currentStaff?.Count ?? 0) == 0) continue;
            var crate = b.Crates[0];
            crate.HoursLeft -= 1f;
            if (crate.HoursLeft > 0f) continue;
            Despawn(crate);
            b.Crates.RemoveAt(0);
            Plugin.Logger.LogInfo($"{b.Name}: staff unpacked a delivery crate");
        }
    }

    /// <summary>After a save loads: put back the crates still waiting.</summary>
    public static void Restore()
    {
        spawned.Clear();
        ids.Clear();
        foreach (var b in Store.Owned.Values)
        {
            var c = Store.FindCompany(b.CompanyId);
            if (c == null) { b.Crates.Clear(); continue; }
            foreach (var crate in b.Crates.ToList())
                if (!Spawn(c, crate)) b.Crates.Remove(crate);
        }
    }

    /// <summary>A business sold: its crates go with it.</summary>
    public static void Remove(Business b)
    {
        foreach (var crate in b.Crates) Despawn(crate);
        b.Crates.Clear();
    }

    public static void Forget()
    {
        spawned.Clear();
        ids.Clear();
    }

    // ---- The game's side ----

    private static bool Spawn(Company c, Crate crate)
    {
        try
        {
            preset ??= FindPreset();
            if (preset == null) return false;
            var obj = InteractableCreator.Instance.CreateWorldInteractable(preset, c.director, null, null,
                new Vector3(crate.X, crate.Y, crate.Z), new Vector3(0f, crate.Yaw, 0f), null, null, "");
            if (obj == null) return false;
            spawned[crate] = obj;
            ids.Add(obj.id);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't place a delivery crate: " + e.Message);
            return false;
        }
    }

    private static void Despawn(Crate crate)
    {
        if (!spawned.TryGetValue(crate, out var obj)) return;
        spawned.Remove(crate);
        try
        {
            ids.Remove(obj.id);
            obj.Delete();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't remove a delivery crate: " + e.Message);
        }
    }

    private static InteractablePreset FindPreset()
    {
        var all = Toolbox.Instance.objectPresetDictionary;
        foreach (var name in PresetNames)
            foreach (var kv in all)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Logger.LogInfo($"Delivery crates use '{kv.Key}'");
                    return kv.Value;
                }
        Plugin.Logger.LogWarning("No crate object found; deliveries won't show crates");
        return null;
    }

    /// <summary>A clear floor tile near the middle of the best room for deliveries, not where a crate already sits.</summary>
    private static (NewRoom, Vector3)? FindSpot(Company c, Business b)
    {
        var rooms = new List<NewRoom>();
        foreach (var r in c.address.rooms) if (r != null && r.nodes.Count > 0) rooms.Add(r);
        if (rooms.Count == 0) return null;

        // Only staff rooms: never out front with the customers.
        var ranked = rooms.Where(r => Rank(r) < RoomPreference.Length).OrderBy(Rank).ToList();
        if (ranked.Count == 0)
        {
            Plugin.Logger.LogInfo($"{c.name}: no stockroom, back room or kitchen for delivery crates (rooms: " +
                                  string.Join(", ", rooms.Select(r => $"{r.name} [{r.preset?.name}]")) + ")");
            return null;
        }
        var taken = b.Crates.Select(k => new Vector3(k.X, k.Y, k.Z)).ToList();
        bool Usable(NewNode n) =>
            n != null && !n.isObstacle && !n.noPassThrough && !n.isIndoorsEntrance &&
            (n.floorType == NewNode.FloorTileType.floorAndCeiling || n.floorType == NewNode.FloorTileType.floorOnly) &&
            !taken.Any(t => Vector3.Distance(t, n.position) < 1f);

        // First choice: an empty tile near the middle of the room.
        foreach (var room in ranked)
        {
            NewNode best = null;
            var bestDist = float.MaxValue;
            foreach (var n in room.nodes)
            {
                if (!Usable(n) || !n.allowNewFurniture || n.individualFurniture.Count > 0) continue;
                var d = Vector3.Distance(n.position, room.middleRoomPosition);
                if (d < bestDist) { bestDist = d; best = n; }
            }
            if (best != null) return (room, best.position);
        }

        // Cramped rooms (kitchens are full of counters): a tile people can walk on, as far from the doors as possible
        // so it isn't in the way.
        foreach (var room in ranked)
        {
            var doors = Nodes(room).Where(n => n != null && n.isIndoorsEntrance).Select(n => n.position).ToList();
            NewNode best = null;
            var bestScore = float.MinValue;
            foreach (var n in room.nodes)
            {
                if (!Usable(n) || n.walkableNodeSpace.Count == 0) continue;
                var score = doors.Count > 0
                    ? doors.Min(d => Vector3.Distance(d, n.position))
                    : -Vector3.Distance(n.position, room.middleRoomPosition);
                // Keep clear of furniture (a safe, a cabinet) where possible, so nothing gets blocked or overlapped.
                if (n.individualFurniture.Count > 0) score -= 100f;
                if (score > bestScore) { bestScore = score; best = n; }
            }
            if (best != null) return (room, best.position);
        }

        Plugin.Logger.LogInfo($"{c.name}: no room for a delivery crate in " + string.Join(", ", ranked.Select(r =>
            $"{r.name} ({r.nodes.Count} tiles, {Nodes(r).Count(n => Usable(n))} floor, {Nodes(r).Count(n => Usable(n) && n.walkableNodeSpace.Count > 0)} walkable)")));
        return null;
    }

    private static List<NewNode> Nodes(NewRoom r)
    {
        var list = new List<NewNode>();
        foreach (var n in r.nodes) list.Add(n);
        return list;
    }

    /// <summary>Stockroom first, then back room, then kitchen; anything else isn't for crates.</summary>
    private static int Rank(NewRoom r)
    {
        var name = ((r.preset?.name ?? "") + " " + (r.roomType?.name ?? "") + " " + (r.name ?? "")).ToLowerInvariant();
        for (var i = 0; i < RoomPreference.Length; i++)
            if (RoomPreference[i].Any(name.Contains)) return i;
        return RoomPreference.Length;
    }

    /// <summary>Crates are never saved with the game (the mod puts them back on load).</summary>
    [HarmonyPatch(typeof(Interactable), nameof(Interactable.IsSaveStateEligable))]
    [HarmonyPostfix]
    private static void NotSaved(Interactable __instance, ref bool __result)
    {
        if (__result && ids.Count > 0 && ids.Contains(__instance.id)) __result = false;
    }
}
