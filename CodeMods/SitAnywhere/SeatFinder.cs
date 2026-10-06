using UnityEngine;

namespace SitAnywhere;

/// <summary>Where and how the player would sit.</summary>
internal struct Seat
{
    /// <summary>World position of the player's eyes once seated.</summary>
    public Vector3 Eye;
    /// <summary>Facing direction once seated, in degrees around the vertical axis.</summary>
    public float Yaw;
    /// <summary>Camera pitch once seated (positive = looking down).</summary>
    public float Pitch;
    /// <summary>Sitting on an edge with legs over a drop.</summary>
    public bool Ledge;
}

/// <summary>
/// Works out from what the player is looking at whether there's somewhere to sit:
/// a flat top at seat height (sit on its near edge, facing back the way you came), or a ledge with
/// a drop beyond it, either seat height or at your feet (sit on the edge facing out).
/// Only uses physics queries the game itself makes, as IL2CPP strips the rest.
/// </summary>
internal static class SeatFinder
{
    private const float Step = 0.05f;
    private const float FlatNormal = 0.85f;
    private const float SameTopTolerance = 0.04f;
    private const float HeadRadius = 0.17f;

    public static string LastFailure;

    public static bool TryFind(Player player, out Seat seat)
    {
        seat = default;
        LastFailure = null;
        var cam = CameraController.Instance != null ? CameraController.Instance.cam : null;
        if (cam == null) return Fail("no camera");

        var mask = (int)Toolbox.Instance.playerMovementLayerMask;
        var t = player.transform;
        var origin = cam.transform.position;
        var forward = cam.transform.forward;
        var feetY = t.position.y - player.GetPlayerHeightNormal() * 0.5f;

        if (!Physics.Raycast(origin, forward, out var hit, Plugin.Reach.Value, mask, QueryTriggerInteraction.Ignore))
            return Fail("not looking at anything");
        if (IsExcluded(hit.collider, t)) return Fail("looking at a person");

        var dir = Flat(forward);
        if (dir.sqrMagnitude < 0.01f) dir = Flat(t.forward);
        dir.Normalize();

        // Looking at the side of something: sample its top just behind the face we hit.
        var sample = hit.point;
        if (hit.normal.y < 0.7f) sample += dir * 0.08f;

        var maxSeat = Plugin.MaxSeatHeight.Value;
        if (!TopAt(sample, feetY + maxSeat + 0.3f, maxSeat + 0.6f, t, mask, out var topY))
            return Fail("no flat top");
        var h = topY - feetY;

        if (h >= Plugin.MinSeatHeight.Value && h <= maxSeat)
            return SeatHeight(player, t, mask, sample, topY, feetY, dir, origin, out seat);
        if (Mathf.Abs(h) <= 0.15f && Plugin.LedgeSitting.Value)
            return FloorEdge(player, t, mask, sample, topY, dir, origin, out seat);
        return Fail($"wrong height ({h:0.00}m)");
    }

    /// <summary>A crate, bench, low wall or desk: sit on the edge nearest you, or on top facing
    /// out if there's a drop on the far side.</summary>
    private static bool SeatHeight(Player player, Transform t, int mask, Vector3 sample, float topY, float feetY,
        Vector3 dir, Vector3 camPos, out Seat seat)
    {
        seat = default;
        var near = March(sample, -dir, 1.2f, topY, t, mask, out _);
        var far = March(sample, dir, 1.5f, topY, t, mask, out var farOpen);
        var depth = Flat(far - near).magnitude;
        if (depth < 0.15f) return Fail($"too thin ({depth:0.00}m)");
        if (Flat(near - t.position).magnitude > 1.3f) return Fail("too far away");

        var ledge = Plugin.LedgeSitting.Value && depth <= 0.9f && farOpen &&
                    DropBeyond(far + dir * 0.15f, topY, Mathf.Min(feetY, topY) - Plugin.LedgeMinDrop.Value, t, mask);
        Vector3 hips, facing;
        if (ledge)
        {
            hips = far - dir * Mathf.Min(0.15f, depth * 0.5f);
            facing = dir;
            if (!LegRoom(far, topY, dir, t, mask)) return Fail("no room for legs");
        }
        else
        {
            hips = near + dir * Mathf.Min(0.2f, depth * 0.5f);
            facing = Plugin.TurnAround.Value ? -dir : dir;
        }
        hips.y = topY;
        return Finish(t, mask, hips, facing, ledge, camPos, out seat);
    }

    /// <summary>The edge of a roof, balcony or landing at your feet: sit with legs over the drop.</summary>
    private static bool FloorEdge(Player player, Transform t, int mask, Vector3 sample, float topY, Vector3 dir,
        Vector3 camPos, out Seat seat)
    {
        seat = default;
        var edge = March(sample, dir, 1.0f, topY, t, mask, out var open);
        if (!open) return Fail("floor, no edge nearby");
        if (!DropBeyond(edge + dir * 0.15f, topY, topY - Plugin.LedgeMinDrop.Value, t, mask))
            return Fail("edge without a drop");
        if (!LegRoom(edge, topY, dir, t, mask)) return Fail("no room for legs");
        var hips = edge - dir * 0.1f;
        hips.y = topY;
        if (Flat(hips - t.position).magnitude > Plugin.Reach.Value) return Fail("too far away");
        return Finish(t, mask, hips, dir, true, camPos, out seat);
    }

    private static bool Finish(Transform t, int mask, Vector3 hips, Vector3 facing, bool ledge, Vector3 camPos, out Seat seat)
    {
        seat = default;
        var eye = hips + Vector3.up * Plugin.EyeAboveSeat.Value + facing * 0.05f;

        // Room for the upper body and head above the seat.
        var bottom = hips + Vector3.up * (HeadRadius + 0.2f);
        var top = eye + Vector3.up * 0.05f;
        foreach (var c in Physics.OverlapCapsule(bottom, top, HeadRadius, mask, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.transform.IsChildOf(t)) continue;
            return Fail("no headroom (" + c.name + ")");
        }

        // Nothing between you and the seat (walls, windows).
        var toEye = eye - camPos;
        if (Physics.Raycast(camPos, toEye.normalized, out var block, toEye.magnitude, mask, QueryTriggerInteraction.Ignore) &&
            !block.collider.transform.IsChildOf(t))
            return Fail("seat is blocked (" + block.collider.name + ")");

        seat = new Seat
        {
            Eye = eye,
            Yaw = Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y,
            Pitch = ledge ? 20f : 5f,
            Ledge = ledge,
        };
        return true;
    }

    /// <summary>Walks from <paramref name="from"/> along <paramref name="dir"/> while the same flat
    /// top continues underneath. Returns the last point on it; <paramref name="leftTop"/> is true if
    /// the top ended within <paramref name="max"/>.</summary>
    private static Vector3 March(Vector3 from, Vector3 dir, float max, float topY, Transform t, int mask, out bool leftTop)
    {
        var last = from;
        for (var d = Step; d <= max; d += Step)
        {
            var p = from + dir * d;
            if (!SameTop(p, topY, t, mask))
            {
                leftTop = true;
                return last;
            }
            last = p;
        }
        leftTop = false;
        return last;
    }

    private static bool SameTop(Vector3 p, float topY, Transform t, int mask)
    {
        var start = new Vector3(p.x, topY + 0.25f, p.z);
        if (!Physics.Raycast(start, Vector3.down, out var hit, 0.35f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (IsExcluded(hit.collider, t)) return false;
        return hit.normal.y >= FlatNormal && Mathf.Abs(hit.point.y - topY) <= SameTopTolerance;
    }

    private static bool TopAt(Vector3 p, float fromY, float distance, Transform t, int mask, out float topY)
    {
        topY = 0f;
        var start = new Vector3(p.x, fromY, p.z);
        if (!Physics.Raycast(start, Vector3.down, out var hit, distance, mask, QueryTriggerInteraction.Ignore)) return false;
        if (IsExcluded(hit.collider, t) || hit.normal.y < FlatNormal) return false;
        topY = hit.point.y;
        return true;
    }

    /// <summary>True if, just past an edge, nothing is above <paramref name="lowestY"/>.</summary>
    private static bool DropBeyond(Vector3 p, float topY, float lowestY, Transform t, int mask)
    {
        var start = new Vector3(p.x, topY + 0.25f, p.z);
        var distance = start.y - lowestY;
        if (distance <= 0f) return false;
        if (!Physics.Raycast(start, Vector3.down, out var hit, distance, mask, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform.IsChildOf(t);
    }

    /// <summary>Room for dangling legs: nothing just past the edge, below the seat.</summary>
    private static bool LegRoom(Vector3 edge, float topY, Vector3 dir, Transform t, int mask)
    {
        var start = new Vector3(edge.x, topY - 0.25f, edge.z) - dir * 0.05f;
        return !Physics.Raycast(start, dir, out _, 0.5f, mask, QueryTriggerInteraction.Ignore);
    }

    private static bool IsExcluded(Collider c, Transform player)
    {
        if (c == null) return true;
        if (c.transform.IsChildOf(player)) return true;
        return c.GetComponentInParent<Actor>() != null;
    }

    private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    private static bool Fail(string reason)
    {
        LastFailure = reason;
        return false;
    }
}
