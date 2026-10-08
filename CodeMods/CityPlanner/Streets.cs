using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CityPlanner;

/// <summary>
/// Painted streets. The game cuts the ground between buildings into pieces: one per gap between two neighbouring
/// city tiles, and one per crossing where four tiles meet. It then scores each piece by simulated foot traffic and
/// chains busy pieces into main streets, leaving quiet ones as back streets and alleys. When the plan has a
/// [Streets] section, that chaining step is done here instead: painted gaps get the type the plan gives them,
/// unpainted gaps go by foot traffic, and each street's busyness is set to suit its type (the game uses it to turn
/// buildings towards the busiest street and to put shops on busy streets).
/// </summary>
[HarmonyPatch(typeof(PathFinder), "CreateStreets")]
internal static class StreetsPatch
{
    // Busyness ranges per type. Vanilla main streets are 0.5 and up; the game reads lower as grubbier.
    private const float MainMin = 0.6f, BackMin = 0.25f, BackMax = 0.45f, AlleyMax = 0.2f;
    // Unpainted gaps: vanilla-like thresholds on the simulated foot traffic.
    private const float AutoMain = 0.5f, AutoAlley = 0.15f;

    private class Piece
    {
        public PathFinder.StreetChunk Chunk;
        public bool Junction;
        public Gap Gap;            // gaps only
        public int CornerX, CornerY; // junctions only: the tile at the crossing's south-west
        public StreetKind Kind;
        public bool Painted;
        public bool OwnedNorthSouth; // junctions: which road gets the crossing
        public List<Piece> Group;
    }

    private static bool Prefix()
    {
        if (!Plugin.Active || Plugin.Plan.Streets.Count == 0) return true;
        try
        {
            Build();
            return false;
        }
        catch (Exception e)
        {
            // Nothing has been created yet if the mapping fails, so the game can still lay out the streets itself.
            Plugin.Logger.LogError("Painted streets failed, using the game's own streets: " + e);
            foreach (var s in CityData.Instance.streetDirectory.ToArray()) UnityEngine.Object.Destroy(s.gameObject);
            CityData.Instance.streetDirectory.Clear();
            return true;
        }
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    private static void Build()
    {
        var plan = Plugin.Plan;
        var m = CityControls.Instance.tileMultiplier;
        var gaps = new Dictionary<Gap, Piece>();
        var junctions = new Dictionary<(int, int), Piece>();
        var loose = new List<Piece>();

        // 1. Work out which gap or crossing each piece of ground is, from its tiles' positions.
        var chunks = PathFinder.Instance.streetChunks;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var piece = new Piece { Chunk = chunk, Junction = chunk.isJunction };
            if (chunk.allTiles.Count == 0)
            {
                loose.Add(piece);
                continue;
            }
            int minX = int.MaxValue, minY = int.MaxValue;
            var onXEdge = true;
            for (var t = 0; t < chunk.allTiles.Count; t++)
            {
                var g = chunk.allTiles[t].globalTileCoord;
                minX = Math.Min(minX, g.x);
                minY = Math.Min(minY, g.y);
                var lx = ((g.x % m) + m) % m;
                if (lx != 0 && lx != m - 1) onXEdge = false;
            }
            if (piece.Junction)
            {
                piece.CornerX = FloorDiv(minX - (m - 1), m);
                piece.CornerY = FloorDiv(minY - (m - 1), m);
                if (!junctions.TryAdd((piece.CornerX, piece.CornerY), piece)) loose.Add(piece);
            }
            else
            {
                // A gap between east–west neighbours sits on a tile column boundary: its road runs north–south.
                piece.Gap = onXEdge
                    ? new Gap(true, FloorDiv(minX - (m - 1), m), FloorDiv(minY, m))
                    : new Gap(false, FloorDiv(minX, m), FloorDiv(minY - (m - 1), m));
                if (!gaps.TryAdd(piece.Gap, piece)) loose.Add(piece);
            }
        }

        // 2. Types: painted gaps as planned, the rest by foot traffic. A crossing takes the busiest type that meets it.
        foreach (var piece in gaps.Values)
        {
            piece.Painted = plan.Streets.TryGetValue(piece.Gap, out piece.Kind);
            if (!piece.Painted) piece.Kind = Auto(piece.Chunk);
        }
        foreach (var piece in loose) piece.Kind = Auto(piece.Chunk);
        foreach (var j in junctions.Values)
        {
            var around = Around(j, gaps).Where(p => p != null).ToList();
            j.Kind = around.Count > 0 ? around.Max(p => p.Kind) : Auto(j.Chunk);
            var ns = Same(j, Get(gaps, new Gap(true, j.CornerX, j.CornerY))) && Same(j, Get(gaps, new Gap(true, j.CornerX, j.CornerY + 1)));
            var ew = Same(j, Get(gaps, new Gap(false, j.CornerX, j.CornerY))) && Same(j, Get(gaps, new Gap(false, j.CornerX + 1, j.CornerY)));
            if (ns && ew) j.OwnedNorthSouth = LineLength(gaps, true, j.CornerX, j.CornerY, j.Kind) >= LineLength(gaps, false, j.CornerY, j.CornerX, j.Kind);
            else if (ns || ew) j.OwnedNorthSouth = ns;
            else j.OwnedNorthSouth = Same(j, Get(gaps, new Gap(true, j.CornerX, j.CornerY))) || Same(j, Get(gaps, new Gap(true, j.CornerX, j.CornerY + 1)));
        }
        var missing = plan.Streets.Keys.Count(k => !gaps.ContainsKey(k));
        if (missing > 0) Plugin.Logger.LogWarning($"{missing} painted gaps have no road in this city (the coastline ring has none) and were skipped");

        // 3. Group pieces into streets. Main and back streets run straight; a crossing belongs to one of the two roads,
        //    so the other one changes name there. Joined alleys become one street, so no dead-end walls split them.
        var groups = new List<List<Piece>>();
        int minLine = -1, maxLine = Math.Max(plan.Width, plan.Height) + 1;
        foreach (var northSouth in new[] { true, false })
        {
            for (var line = minLine; line <= maxLine; line++)
            {
                List<Piece> run = null;
                for (var pos = minLine; pos <= maxLine; pos++)
                {
                    // Along a north–south road at boundary x=line: gap (line, pos), then the crossing above it.
                    var gap = Get(gaps, new Gap(northSouth, northSouth ? line : pos, northSouth ? pos : line));
                    Step(ref run, gap, groups);
                    var j = Get(junctions, northSouth ? (line, pos) : (pos, line));
                    Step(ref run, j != null && j.OwnedNorthSouth == northSouth ? j : null, groups);
                }
            }
        }
        foreach (var start in gaps.Values.Concat(junctions.Values).Where(p => p.Kind == StreetKind.Alley && p.Group == null))
        {
            var group = new List<Piece>();
            var queue = new Queue<Piece>();
            start.Group = group;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                group.Add(p);
                foreach (var n in Neighbours(p, gaps, junctions))
                    if (n != null && n.Group == null && n.Kind == StreetKind.Alley)
                    {
                        n.Group = group;
                        queue.Enqueue(n);
                    }
            }
            groups.Add(group);
        }
        // Anything left over (a crossing whose roads both ended, an unrecognised piece) becomes a street of its own.
        foreach (var p in gaps.Values.Concat(junctions.Values).Concat(loose).Where(p => p.Group == null))
        {
            p.Group = new List<Piece> { p };
            groups.Add(p.Group);
        }

        // 4. Create the streets the way the game does, then let it lay their tiles.
        var counts = new Dictionary<StreetKind, int>();
        foreach (var group in groups)
        {
            var kind = group[0].Kind;
            var anchor = group[0].Chunk.anchorTile;
            var district = PathFinder.Instance.tileMap[anchor].cityTile.district;
            var street = UnityEngine.Object.Instantiate(PrefabControls.Instance.street, PrefabControls.Instance.cityContainer.transform)
                .GetComponent<StreetController>();
            street.Setup(district);
            if (!CityData.Instance.streetDirectory.Contains(street)) CityData.Instance.streetDirectory.Add(street);
            var footfall = 0f;
            foreach (var p in group)
            {
                for (var t = 0; t < p.Chunk.allTiles.Count; t++) street.AddTile(p.Chunk.allTiles[t]);
                street.AddChunk(p.Chunk);
                street.chunkSize++;
                footfall = Mathf.Max(footfall, p.Chunk.footfallNormalized);
            }
            switch (kind)
            {
                case StreetKind.Main:
                    street.normalizedFootfall = Mathf.Clamp(footfall, MainMin, 1f);
                    street.SetAsStreet();
                    break;
                case StreetKind.Back:
                    street.normalizedFootfall = Mathf.Clamp(footfall, BackMin, BackMax);
                    street.SetAsBackstreet();
                    break;
                default:
                    street.normalizedFootfall = Mathf.Min(footfall, AlleyMax);
                    street.SetAsAlley();
                    break;
            }
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        foreach (var street in CityData.Instance.streetDirectory) street.LoadStreetTiles();

        var painted = gaps.Values.Count(p => p.Painted);
        Plugin.Logger.LogInfo($"Painted streets: {painted} of {gaps.Count} gaps painted, {junctions.Count} crossings; " +
                              $"{counts.GetValueOrDefault(StreetKind.Main)} main streets, {counts.GetValueOrDefault(StreetKind.Back)} back streets, " +
                              $"{counts.GetValueOrDefault(StreetKind.Alley)} alleys" + (loose.Count > 0 ? $", {loose.Count} unrecognised pieces" : ""));
    }

    private static StreetKind Auto(PathFinder.StreetChunk chunk) =>
        chunk.footfallNormalized >= AutoMain ? StreetKind.Main : chunk.footfallNormalized < AutoAlley ? StreetKind.Alley : StreetKind.Back;

    private static T Get<TK, T>(Dictionary<TK, T> d, TK key) where T : class => d.TryGetValue(key, out var v) ? v : null;

    private static bool Same(Piece a, Piece b) => b != null && b.Kind == a.Kind;

    /// <summary>The four gaps meeting at a crossing: south and north (north–south road), west and east.</summary>
    private static IEnumerable<Piece> Around(Piece j, Dictionary<Gap, Piece> gaps)
    {
        yield return Get(gaps, new Gap(true, j.CornerX, j.CornerY));
        yield return Get(gaps, new Gap(true, j.CornerX, j.CornerY + 1));
        yield return Get(gaps, new Gap(false, j.CornerX, j.CornerY));
        yield return Get(gaps, new Gap(false, j.CornerX + 1, j.CornerY));
    }

    private static IEnumerable<Piece> Neighbours(Piece p, Dictionary<Gap, Piece> gaps, Dictionary<(int, int), Piece> junctions)
    {
        if (p.Junction) return Around(p, gaps);
        var g = p.Gap;
        // A north–south gap (X, Y) has crossings at its south (X, Y-1) and north (X, Y) ends; east–west likewise.
        return g.Vertical
            ? new[] { Get(junctions, (g.X, g.Y - 1)), Get(junctions, (g.X, g.Y)) }
            : new[] { Get(junctions, (g.X - 1, g.Y)), Get(junctions, (g.X, g.Y)) };
    }

    /// <summary>How many gaps of one type run unbroken along a road through a crossing (to pick who gets it).</summary>
    private static int LineLength(Dictionary<Gap, Piece> gaps, bool northSouth, int line, int pos, StreetKind kind)
    {
        var n = 0;
        for (var p = pos; Get(gaps, new Gap(northSouth, northSouth ? line : p, northSouth ? p : line))?.Kind == kind; p--) n++;
        for (var p = pos + 1; Get(gaps, new Gap(northSouth, northSouth ? line : p, northSouth ? p : line))?.Kind == kind; p++) n++;
        return n;
    }

    /// <summary>Walks one step along a road: extends the current street or starts a new one.</summary>
    private static void Step(ref List<Piece> run, Piece p, List<List<Piece>> groups)
    {
        if (p == null || p.Kind == StreetKind.Alley || p.Group != null)
        {
            run = null;
            return;
        }
        if (run == null || run[0].Kind != p.Kind)
        {
            run = new List<Piece>();
            groups.Add(run);
        }
        run.Add(p);
        p.Group = run;
    }
}
