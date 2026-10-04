using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SanitationDepartment;

/// <summary>
/// Route Supervisor perk: a subtle outline on nearby trash you can actually see.
/// The game draws coloured outlines (used on people) for any renderer on layer 30, tinted by the
/// "_SelectionColor" property. Instead of moving the item's own meshes to that layer (which could
/// break picking it up), an invisible copy of each mesh is added as a child on the outline layer.
/// </summary>
[HarmonyPatch(typeof(Player), "Update")]
internal static class GlowPatch
{
    private const int OutlineLayer = 30;
    private static readonly Color GlowColour = new(0.62f, 0.71f, 0.54f, 0.6f);

    private static readonly Dictionary<int, List<GameObject>> glowing = new();
    private static float nextScan;

    private static void Postfix(Player __instance)
    {
        try
        {
            var key = Plugin.GlowToggleKey.Value;
            if (State.Supervisor && key != KeyCode.None && Input.GetKeyDown(key) && Cursor.lockState == CursorLockMode.Locked)
            {
                State.GlowOn = !State.GlowOn;
                State.Notify(State.GlowOn ? "Trash glow on" : "Trash glow off");
            }

            if (!State.Supervisor || !State.GlowOn || SessionData.Instance == null || !SessionData.Instance.play)
            {
                if (glowing.Count > 0) ClearAll();
                return;
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.3f;
            Scan(__instance);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Glow: " + e);
            ClearAll();
        }
    }

    private static void Scan(Player player)
    {
        var cam = CameraController.Instance?.cam;
        if (cam == null) return;

        var eye = cam.transform.position;
        var range = Plugin.GlowRange.Value;
        var keep = new HashSet<int>();

        // Everything with a collider nearby; trash and junk are picked out by the same rules as the tag.
        foreach (var collider in Physics.OverlapSphere(eye, range, ~0, QueryTriggerInteraction.Ignore))
        {
            var controller = collider != null ? collider.GetComponentInParent<InteractableController>() : null;
            var item = controller?.interactable;
            if (item == null || keep.Contains(item.id)) continue;
            var obj = item.spawnedObject;
            if (obj == null || item.inInventory != null) continue;
            var wanted = Trash.IsTrash(item) || Trash.IsJunk(item) || (Trash.IsSource(item) && Trash.SourceRefillLeft(item) <= 0f);
            if (!wanted || !Visible(eye, obj)) continue;

            keep.Add(item.id);
            if (!glowing.ContainsKey(item.id)) glowing[item.id] = AddGlow(obj);
        }

        // Remove glows for trash that was binned, picked up, moved out of range or out of sight.
        var stale = new List<int>();
        foreach (var id in glowing.Keys)
            if (!keep.Contains(id)) stale.Add(id);
        foreach (var id in stale) Remove(id);
    }

    private static bool Visible(Vector3 eye, GameObject obj)
    {
        var target = obj.transform.position + Vector3.up * 0.05f;
        var dir = target - eye;
        var dist = dir.magnitude;
        if (dist < 0.01f) return true;
        if (!Physics.Raycast(eye, dir / dist, out var hit, dist - 0.05f, Toolbox.Instance.playerMovementLayerMask,
                QueryTriggerInteraction.Ignore))
            return true;
        return hit.collider != null && hit.collider.transform.IsChildOf(obj.transform);
    }

    private static List<GameObject> AddGlow(GameObject obj)
    {
        var proxies = new List<GameObject>();
        foreach (var source in obj.GetComponentsInChildren<MeshRenderer>())
        {
            var filter = source.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || source.gameObject.layer == OutlineLayer) continue;

            var proxy = new GameObject("SanitationGlow");
            proxy.layer = OutlineLayer;
            proxy.transform.SetParent(source.transform, false);
            proxy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = proxy.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            var block = new MaterialPropertyBlock();
            block.SetColor("_SelectionColor", GlowColour);
            renderer.SetPropertyBlock(block);
            proxies.Add(proxy);
        }
        return proxies;
    }

    private static void Remove(int id)
    {
        if (!glowing.TryGetValue(id, out var proxies)) return;
        foreach (var p in proxies)
            if (p != null) UnityEngine.Object.Destroy(p);
        glowing.Remove(id);
    }

    private static void ClearAll()
    {
        foreach (var id in new List<int>(glowing.Keys)) Remove(id);
    }
}
