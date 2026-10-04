using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProtectorateShared;

/// <summary>
/// The "Credentials" card on the pause menu, shared by The Protectorate's mods. This file is compiled into
/// each mod; the card itself is one panel found by name, and each mod keeps its own section in it, so it
/// works with any combination of the mods installed.
/// </summary>
internal static class CredentialsCard
{
    private const string RootName = "ProtectorateCredentials";
    private const string HeaderName = "00_Header";

    /// <summary>Shows or hides the card and rewrites this mod's section. Call whenever the menu changes.</summary>
    /// <param name="sectionKey">Sorts sections and names this mod's child, e.g. "20_Sanitation".</param>
    /// <param name="title">Section heading.</param>
    /// <param name="lines">Section lines; an empty list hides the section.</param>
    public static void Refresh(string sectionKey, string title, Func<List<string>> lines)
    {
        var menu = MainMenuController.Instance;
        var container = menu?.mainMenuContainer;
        if (container == null) return;

        var paused = menu.mainMenuActive && SessionData.Instance != null && SessionData.Instance.startedGame &&
                     menu.currentComponent != null && menu.currentComponent.component == MainMenuController.Component.mainMenuButtons;
        var root = container.Find(RootName);
        if (!paused)
        {
            if (root != null) root.gameObject.SetActive(false);
            return;
        }

        root ??= CreateRoot(container, menu);
        var section = root.Find(sectionKey) ?? CreateText(root, sectionKey, menu, 17f).transform;
        var content = lines();
        var text = section.GetComponent<TextMeshProUGUI>();
        if (content.Count == 0)
        {
            section.gameObject.SetActive(false);
        }
        else
        {
            text.text = $"<b><color=#E8B04B>{title}</color></b>\n" + string.Join("\n", content);
            section.gameObject.SetActive(true);
        }

        // Keep sections in key order, and only show the card when some section has something in it.
        var any = false;
        var keys = new List<string>();
        for (var i = 0; i < root.childCount; i++) keys.Add(root.GetChild(i).name);
        keys.Sort(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            var child = root.Find(keys[i]);
            child.SetSiblingIndex(i);
            if (keys[i] != HeaderName && child.gameObject.activeSelf) any = true;
        }
        root.SetAsLastSibling();
        root.gameObject.SetActive(any);
    }

    private static Transform CreateRoot(Transform container, MainMenuController menu)
    {
        var go = new GameObject(RootName);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(container, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-48f, 48f);

        var background = go.AddComponent<Image>();
        background.color = new Color(0.08f, 0.07f, 0.11f, 0.82f);
        background.raycastTarget = false;

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset { left = 20, right = 20, top = 14, bottom = 16 };
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = CreateText(go.transform, HeaderName, menu, 20f);
        header.text = "<b>CREDENTIALS</b>";
        header.color = new Color(0.95f, 0.93f, 0.88f, 1f);
        return go.transform;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, MainMenuController menu, float size)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        if (menu.buildText != null) text.font = menu.buildText.font;
        text.fontSize = size;
        text.color = new Color(0.86f, 0.84f, 0.8f, 1f);
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.richText = true;
        go.AddComponent<LayoutElement>().minWidth = 300f;
        return text;
    }

    /// <summary>"2h 15m" from a span of in-game hours.</summary>
    public static string Hours(float hours)
    {
        hours = Mathf.Max(0f, hours);
        var h = Mathf.FloorToInt(hours);
        var m = Mathf.FloorToInt((hours - h) * 60f);
        return h > 0 ? $"{h}h {m:00}m" : $"{m}m";
    }
}
