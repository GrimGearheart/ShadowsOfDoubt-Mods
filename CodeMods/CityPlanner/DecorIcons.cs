using System;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace CityPlanner;

/// <summary>
/// Material pictures for the planner's Decor tab. They're the game's own artwork (the decorating menu's icons), so
/// they're never shipped with the mod: once the game has loaded, each icon is drawn from the game's files into
/// BepInEx/cache/CityPlanner/icons on this PC, and the planner link serves them to the page.
/// </summary>
internal static class DecorIcons
{
    internal static string Dir => Path.Combine(Paths.CachePath, "CityPlanner", "icons");
    private static bool done;

    /// <summary>Called every frame until the game's materials are loaded; draws the icons once (skips ones already there).</summary>
    public static void EnsureExported()
    {
        if (done || Toolbox.Instance?.allMaterialGroups == null || Toolbox.Instance.allMaterialGroups.Count == 0) return;
        done = true;
        try
        {
            Directory.CreateDirectory(Dir);
            int made = 0;
            foreach (var m in Toolbox.Instance.allMaterialGroups)
            {
                if (m?.decorSprite == null) continue;
                var path = Path.Combine(Dir, FileName(m.name));
                if (File.Exists(path)) continue;
                if (SavePng(m.decorSprite.texture, m.decorSprite.textureRect, path, 96)) made++;
            }
            Plugin.Logger.LogInfo($"Decor icons ready in {Dir}" + (made > 0 ? $" ({made} drawn)" : ""));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Decor icons couldn't be drawn (the planner shows names instead): " + e.Message);
        }
    }

    /// <summary>The icon file for a material name; only letters, digits and _ - survive, so a request can't leave the folder.</summary>
    internal static string FileName(string material) =>
        string.Concat(material.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')) + ".png";

    /// <summary>Copies a region of a (possibly unreadable) texture through the GPU and saves it as a PNG, at most size px.</summary>
    private static bool SavePng(Texture source, Rect region, string path, int size)
    {
        if (source == null || region.width < 1 || region.height < 1) return false;
        var scale = Mathf.Min(1f, size / Mathf.Max(region.width, region.height));
        int w = Mathf.Max(1, Mathf.RoundToInt(region.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(region.height * scale));
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, rt, new Vector2(region.width / source.width, region.height / source.height),
                new Vector2(region.x / source.width, region.y / source.height));
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.Destroy(tex);
            return true;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
