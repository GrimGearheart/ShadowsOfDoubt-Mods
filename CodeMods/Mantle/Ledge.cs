using UnityEngine;

namespace Mantle;

/// <summary>
/// Ledge detection. Finds what's in front of the player and decides between:
/// - Mantle: a surface deep enough to stand on (a dumpster, a crate, a low roof) — climb onto it.
/// - Vault: a thin top with lower ground behind it (a fence, a railing) — go over it and drop down.
/// </summary>
internal static class Ledge
{
    private const float Skin = 0.02f;
    /// <summary>How far above the top surface the capsule is carried, to clear lips and rims.</summary>
    private const float Clearance = 0.15f;
    /// <summary>Distances past the wall face (metres) at which the top surface is probed.</summary>
    private static readonly float[] Depths = { 0.04f, 0.12f, 0.25f, 0.4f, 0.6f, 0.85f };
    /// <summary>Radius of the sphere used to find thin tops (fence boards, railings).</summary>
    private const float ThinProbeRadius = 0.06f;

    public static Vector3 Center(Transform t, CharacterController cc) => t.TransformPoint(cc.center);

    public static bool TryFind(Transform t, CharacterController cc, out Vector3 target, out bool vault)
    {
        target = default;
        vault = false;
        var mask = Toolbox.Instance.playerMovementLayerMask;
        var center = Center(t, cc);
        var feet = center - Vector3.up * (cc.height * 0.5f);
        var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) return false;

        var minH = Plugin.MinHeight.Value;
        var maxH = Plugin.MaxHeight.Value;
        var reach = cc.radius + Plugin.Reach.Value;
        var minNormal = Mathf.Cos(Plugin.MaxSlope.Value * Mathf.Deg2Rad);

        // 1. Something in front, at least MinHeight tall.
        var low = feet + Vector3.up * (minH + 0.05f);
        if (!Physics.Raycast(low, fwd, out var wall, reach, mask, QueryTriggerInteraction.Ignore)) return Fail("no wall");
        if (Mathf.Abs(wall.normal.y) > 0.5f) return Fail("not a wall");

        // 2. Probe straight down at increasing depths past the wall face to map the top.
        var probeY = feet.y + maxH + 0.1f;
        float topY = float.NaN, topDepth = 0f, lastSameDepth = 0f;
        var anyClearStart = false;
        var debug = Plugin.DebugLogging.Value;
        var profile = debug ? new System.Text.StringBuilder() : null;

        // 2a. Right at the face, a small sphere instead of a thin ray: head-on, a fence board is only
        // a few centimetres thick and a ray just past the face would miss its top entirely.
        var faceProbe = wall.point + fwd * 0.02f;
        var faceStart = new Vector3(faceProbe.x, probeY, faceProbe.z);
        if (!Physics.CheckSphere(faceStart, ThinProbeRadius, mask, QueryTriggerInteraction.Ignore))
        {
            anyClearStart = true;
            if (Physics.SphereCast(faceStart, ThinProbeRadius, Vector3.down, out var edge, maxH + 0.6f, mask, QueryTriggerInteraction.Ignore))
            {
                var h = edge.point.y - feet.y;
                if (h >= minH && h <= maxH)
                {
                    topY = edge.point.y;
                    topDepth = lastSameDepth = 0.02f;
                }
            }
        }

        // 2b. Further in, rays: find the top if the face probe didn't, then see how deep it goes.
        foreach (var depth in Depths)
        {
            var p = wall.point + fwd * depth;
            var start = new Vector3(p.x, probeY, p.z);
            if (Physics.CheckSphere(start, 0.03f, mask, QueryTriggerInteraction.Ignore))
            {
                profile?.Append($" {depth:0.00}:inside");
                // Still inside something taller than reach; only fatal if it's right at the face.
                if (float.IsNaN(topY)) continue;
                break;
            }
            anyClearStart = true;
            var found = Physics.Raycast(start, Vector3.down, out var hit, maxH + 0.6f, mask, QueryTriggerInteraction.Ignore);
            profile?.Append(found ? $" {depth:0.00}:{hit.point.y - feet.y:0.00}" : $" {depth:0.00}:miss");
            if (float.IsNaN(topY))
            {
                if (!found || hit.normal.y < minNormal) continue;
                var h = hit.point.y - feet.y;
                if (h < minH || h > maxH) continue;
                topY = hit.point.y;
                topDepth = lastSameDepth = depth;
            }
            else if (found && Mathf.Abs(hit.point.y - topY) < 0.2f && hit.normal.y >= minNormal)
            {
                lastSameDepth = depth; // the top continues: something you can stand on
            }
            else if (found && hit.point.y > topY && hit.point.y - feet.y <= maxH && hit.normal.y >= minNormal)
            {
                // A higher surface just behind: what we found was a lip or bulge on the front
                // (a vending machine's panel, a window sill). Step up to the real top.
                topY = hit.point.y;
                topDepth = lastSameDepth = depth;
            }
            else
            {
                break; // the top ends here: thin obstacle
            }
        }
        if (float.IsNaN(topY)) return Fail(anyClearStart ? "no usable top surface" : "taller than reach");
        var height = topY - feet.y;
        lastBlocker = "nothing";
        var obstacle = !debug ? "" : $"{wall.collider.name} size {wall.collider.bounds.size.x:0.00}x{wall.collider.bounds.size.y:0.00}x{wall.collider.bounds.size.z:0.00}";

        // Positions are carried Clearance above the top, then the controller settles them down.
        var carryY = topY + cc.height * 0.5f + cc.skinWidth + Skin + Clearance;
        var liftCenter = new Vector3(center.x, carryY, center.z);
        if (!PathClear(center, liftCenter, cc, mask)) return Fail($"no headroom (hit {lastBlocker})");

        // 3a. Mantle: deep enough to stand on, with room to stand.
        var standDepth = Mathf.Max(topDepth, cc.radius + 0.05f);
        if (lastSameDepth >= standDepth)
        {
            var p = wall.point + fwd * standDepth;
            var stand = new Vector3(p.x, carryY, p.z);
            if (!Blocked(stand, cc, mask) && PathClear(liftCenter, stand, cc, mask))
            {
                target = stand;
                Log($"mantle onto ledge, height {height:0.00}m");
                return true;
            }
        }

        // 3b. Vault: go over the top and come down on the far side.
        if (Plugin.EnableVault.Value)
        {
            var p = wall.point + fwd * (lastSameDepth + cc.radius + 0.15f);
            var over = new Vector3(p.x, carryY, p.z);
            // There must be ground on the far side within a safe drop.
            if (!Physics.Raycast(over, Vector3.down, out var landing, cc.height * 0.5f + Plugin.MaxVaultDrop.Value + height,
                    mask, QueryTriggerInteraction.Ignore))
                return Fail($"vault: no ground on the far side (height {height:0.00}m, obstacle {obstacle}, profile{profile})");
            if (topY - landing.point.y > Plugin.MaxVaultDrop.Value)
                return Fail($"vault: drop too far ({topY - landing.point.y:0.00}m, landing on {landing.collider.name} layer {landing.collider.gameObject.layer})");
            if (!Blocked(over, cc, mask) && PathClear(liftCenter, over, cc, mask))
            {
                target = over;
                vault = true;
                Log($"vault over obstacle, height {height:0.00}m, depth {lastSameDepth:0.00}m, landing on {landing.collider.name} (layer {landing.collider.gameObject.layer})");
                return true;
            }
        }
        return Fail((lastSameDepth >= standDepth ? "no room to stand" : "too thin to stand on, can't vault") +
                    $" (height {height:0.00}m, depth {lastSameDepth:0.00}m, blocked by {lastBlocker}, obstacle {obstacle}, profile{profile})");
    }

    /// <summary>Last thing a space check ran into, for debug logging.</summary>
    private static string lastBlocker = "";

    /// <summary>True if a collider belongs to the player (their own capsule, held items, etc.).</summary>
    private static bool IsPlayer(Collider c, CharacterController cc)
        => c == null || c == cc || c.transform.IsChildOf(cc.transform) || cc.transform.IsChildOf(c.transform);

    /// <summary>
    /// Sweeps the capsule from one point to another as a series of overlap checks.
    /// (Physics.CapsuleCastAll is stripped from the game's IL2CPP build; OverlapCapsule is used
    /// by the game itself, so it's guaranteed to exist.)
    /// </summary>
    private static bool PathClear(Vector3 from, Vector3 to, CharacterController cc, int mask)
    {
        var distance = Vector3.Distance(from, to);
        var steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.1f));
        for (int i = 1; i <= steps; i++)
        {
            var point = Vector3.Lerp(from, to, (float)i / steps);
            if (Overlaps(point, cc, mask, cc.radius * 0.9f)) return false;
        }
        return true;
    }

    private static Vector3 Bottom(Vector3 center, CharacterController cc) => center - Vector3.up * (cc.height * 0.5f - cc.radius);
    private static Vector3 TopPoint(Vector3 center, CharacterController cc) => center + Vector3.up * (cc.height * 0.5f - cc.radius);

    private static bool Blocked(Vector3 center, CharacterController cc, int mask)
        => Overlaps(center, cc, mask, cc.radius * 0.95f);

    private static bool Overlaps(Vector3 center, CharacterController cc, int mask, float radius)
    {
        var overlaps = Physics.OverlapCapsule(Bottom(center, cc), TopPoint(center, cc), radius, mask, QueryTriggerInteraction.Ignore);
        foreach (var c in overlaps)
        {
            if (IsPlayer(c, cc)) continue;
            lastBlocker = c.name;
            return true;
        }
        return false;
    }

    private static void Log(string message)
    {
        if (Plugin.DebugLogging.Value) Plugin.Logger.LogInfo("Mantle: " + message);
    }

    private static bool Fail(string reason)
    {
        Log(reason);
        return false;
    }
}
