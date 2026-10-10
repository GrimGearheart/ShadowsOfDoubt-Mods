using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityPlanner;

/// <summary>
/// The link between the planner page and the game. While the game runs, City Planner answers on one port of this
/// PC only (127.0.0.1, never the network): it serves the planner page, reports whether the game is ready, and takes
/// plans the page sends, which are checked, written to cityplan.txt and used for the next city generated.
/// The New Game screen gets a "City Planner" button that opens the page in the player's browser.
/// </summary>
internal static class PlannerLink
{
    private static int port;
    private static string pagePath;
    private static readonly ConcurrentQueue<Action> mainThread = new();

    /// <summary>What the page is told (written on the main thread, read by the listener).</summary>
    internal static volatile string Status = "{\"ok\":true,\"state\":\"starting\"}";

    /// <summary>A city is being generated: the plan must not change until it's done.</summary>
    internal static volatile bool Generating;

    internal static string PageUrl => $"http://127.0.0.1:{port}/";

    public static void Start(int listenPort)
    {
        port = listenPort;
        pagePath = FindPage();
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            new Thread(() => Listen(listener)) { IsBackground = true, Name = "CityPlannerLink" }.Start();
            Plugin.Logger.LogInfo($"Planner page link ready at {PageUrl} (this PC only)" + (pagePath == null ? "; city-planner.html not found, so the page can't be opened from the game" : ""));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"Planner page link couldn't use port {port} ({e.Message}). Set another port under [Planner] in the config. Plans can still be pasted into cityplan.txt.");
        }
    }

    /// <summary>city-planner.html ships next to the mod's dll; mod managers may unpack it into a subfolder.</summary>
    private static string FindPage()
    {
        try
        {
            var dll = Directory.GetFiles(Paths.PluginPath, "CityPlanner.dll", SearchOption.AllDirectories).FirstOrDefault();
            var dir = dll == null ? Paths.PluginPath : Path.GetDirectoryName(dll);
            return Directory.GetFiles(dir, "city-planner.html", SearchOption.AllDirectories).FirstOrDefault()
                   ?? Directory.GetFiles(Paths.PluginPath, "city-planner.html", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static void OpenPage()
    {
        Plugin.Logger.LogInfo("Opening the planner page: " + PageUrl);
        Application.OpenURL(PageUrl);
    }

    // ------------------------------------------------------------------------------------------- Listener thread

    private static void Listen(TcpListener listener)
    {
        while (true)
        {
            try
            {
                var client = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Planner page link stopped: " + e.Message);
                return;
            }
        }
    }

    private static void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 5000;
                var stream = client.GetStream();
                if (!ReadRequest(stream, out var method, out var target, out var headers, out var body)) return;

                // Only pages on this PC may talk to the game: the planner opened from the game, or the planner file
                // opened from disk (its origin is "null"). Anything else, such as a website in the same browser, is refused.
                var host = headers.TryGetValue("host", out var h) ? h : "";
                var origin = headers.TryGetValue("origin", out var o) ? o : null;
                var ours = new[] { $"http://127.0.0.1:{port}", $"http://localhost:{port}" };
                if (!ours.Any(u => u == "http://" + host) || (origin != null && origin != "null" && !ours.Contains(origin)))
                {
                    Respond(stream, 403, "text/plain", "Only the City Planner page can talk to the game.", null);
                    return;
                }
                var cors = origin ?? "null";
                var path = target.Split('?')[0];
                if (method == "OPTIONS")
                    Respond(stream, 204, "text/plain", "", cors);
                else if (method == "GET" && (path == "/" || path == "/city-planner.html"))
                {
                    if (pagePath != null && File.Exists(pagePath)) Respond(stream, 200, "text/html; charset=utf-8", File.ReadAllText(pagePath), cors);
                    else Respond(stream, 404, "text/plain", "city-planner.html wasn't found in the City Planner mod folder. Reinstall the mod.", cors);
                }
                else if (method == "GET" && path == "/plan")
                {
                    // The plan the game is using, so the page can open on it. Empty if there's none yet.
                    var text = File.Exists(Plugin.PlanPath) ? File.ReadAllText(Plugin.PlanPath) : "";
                    Respond(stream, 200, "text/plain; charset=utf-8", text, cors);
                }
                else if (method == "GET" && path == "/status")
                    Respond(stream, 200, "application/json", Status, cors);
                else if (method == "POST" && path == "/plan")
                    Respond(stream, 200, "application/json", ReceivePlan(body), cors);
                else
                    Respond(stream, 404, "text/plain", "Not found", cors);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Planner page request failed: " + e.Message);
            }
        }
    }

    private static string ReceivePlan(string text)
    {
        Plan plan;
        string error;
        try
        {
            plan = Plan.Parse(text.Replace("\r", "").Split('\n'), out error);
        }
        catch (Exception e)
        {
            plan = null;
            error = e.Message;
        }
        if (plan == null) return $"{{\"ok\":false,\"error\":{Json(error ?? "the plan couldn't be read")}}}";

        // Saved first, so the plan survives a restart even if the game is mid-generation now.
        var tmp = Plugin.PlanPath + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, Plugin.PlanPath, true);
        mainThread.Enqueue(() =>
        {
            Plugin.NoteFileWritten();
            Plugin.Use(plan, "sent from the planner page");
        });
        var when = Generating ? "after" : "now";
        return $"{{\"ok\":true,\"size\":\"{plan.Width}x{plan.Height}\",\"blocks\":{Json(Blocks(plan))},\"label\":{Json(Sizes.Label(plan.Width, plan.Height))},\"streets\":{plan.Streets.Count},\"when\":\"{when}\"}}";
    }

    private static bool ReadRequest(NetworkStream stream, out string method, out string target,
        out System.Collections.Generic.Dictionary<string, string> headers, out string body)
    {
        method = target = body = null;
        headers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new MemoryStream();
        var one = new byte[4096];
        int headerEnd;
        while ((headerEnd = IndexOfBlankLine(buffer)) < 0)
        {
            var n = stream.Read(one, 0, one.Length);
            if (n <= 0 || buffer.Length > 32 * 1024) return false;
            buffer.Write(one, 0, n);
        }
        var all = buffer.ToArray();
        var lines = Encoding.ASCII.GetString(all, 0, headerEnd).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return false;
        method = first[0].ToUpperInvariant();
        target = first[1];
        foreach (var line in lines.Skip(1))
        {
            var i = line.IndexOf(':');
            if (i > 0) headers[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
        if (length < 0 || length > 512 * 1024) return false;
        var bodyBytes = new MemoryStream();
        bodyBytes.Write(all, headerEnd + 4, all.Length - headerEnd - 4);
        while (bodyBytes.Length < length)
        {
            var n = stream.Read(one, 0, one.Length);
            if (n <= 0) return false;
            bodyBytes.Write(one, 0, n);
        }
        body = Encoding.UTF8.GetString(bodyBytes.ToArray(), 0, length);
        return true;
    }

    private static int IndexOfBlankLine(MemoryStream ms)
    {
        var b = ms.GetBuffer();
        for (var i = 0; i + 3 < ms.Length; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }

    private static void Respond(NetworkStream stream, int code, string type, string content, string cors)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var reason = code switch { 200 => "OK", 204 => "No Content", 403 => "Forbidden", _ => "Not Found" };
        var head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n";
        if (cors != null)
            head += $"Access-Control-Allow-Origin: {cors}\r\nAccess-Control-Allow-Methods: GET, POST\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\n";
        var headBytes = Encoding.ASCII.GetBytes(head + "\r\n");
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    internal static string Blocks(Plan plan) => $"{plan.Width - 2} × {plan.Height - 2} blocks";

    internal static string Json(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "") + "\"";

    internal static bool TryDequeue(out Action action) => mainThread.TryDequeue(out action);
}

/// <summary>Runs on the game's main thread every frame: applies sent plans, keeps the status fresh, adds the button.</summary>
public class PlannerPump : MonoBehaviour
{
    public PlannerPump(IntPtr ptr) : base(ptr) { }

    private static float nextCheck;
    private static GameObject button;

    public void Update()
    {
        // The extra city sizes must exist before any save is loaded, not only once New Game is opened: a save finds its
        // city by size slot, and without the slot it looks for a 5 x 5 city and reports missing city data.
        if (Sizes.VanillaCount < 0 && CityControls.Instance != null && CityControls.Instance.citySizes != null)
        {
            Sizes.Ensure();
            Plugin.Logger.LogInfo($"City sizes ready at startup ({CityControls.Instance.citySizes.Count} in the list)");
        }
        // A sent plan waits until no planned city is being built.
        UpdateGenerating();
        while (!PlannerLink.Generating && PlannerLink.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Plugin.Logger.LogError("Applying a plan from the planner page failed: " + e); }
        }
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        UpdateStatus();
        if (button == null) button = MenuButton.TryCreate();
    }

    private static void UpdateGenerating()
    {
        if (!PlannerLink.Generating) return;
        // The plan is last used before the city is saved; the generator is gone if generation was abandoned.
        var cc = CityConstructor.Instance;
        if (cc == null || cc.loadState >= CityConstructor.LoadState.savingData || (SessionData.Instance != null && SessionData.Instance.startedGame))
            PlannerLink.Generating = false;
    }

    private static void UpdateStatus()
    {
        var playing = SessionData.Instance != null && SessionData.Instance.startedGame;
        var state = PlannerLink.Generating ? "generating" : playing ? "playing" : "menu";
        var plan = Plugin.Plan;
        var planJson = plan == null ? "null" : $"{{\"size\":\"{plan.Width}x{plan.Height}\",\"blocks\":{PlannerLink.Json(PlannerLink.Blocks(plan))},\"streets\":{plan.Streets.Count}}}";
        PlannerLink.Status = $"{{\"ok\":true,\"state\":\"{state}\",\"plan\":{planJson}}}";
    }
}

/// <summary>Generation starts: from here until it's done, a newly sent plan waits.</summary>
[HarmonyPatch(typeof(CityConstructor), nameof(CityConstructor.GenerateNewCity))]
internal static class GeneratingFlag
{
    private static void Prefix() => PlannerLink.Generating = true;
}

/// <summary>A "City Planner" button beside the city size on the New Game screen, opening the planner page.</summary>
internal static class MenuButton
{
    public static GameObject TryCreate()
    {
        try
        {
            var menu = MainMenuController.Instance;
            var size = menu?.citySizeDropdown;
            var template = menu?.pasteShareCodeButton ?? menu?.changeCityNameButton;
            if (size == null || template == null) return null;

            var sizeRect = size.GetComponent<RectTransform>();
            var go = UnityEngine.Object.Instantiate(template.gameObject, sizeRect.parent);
            go.name = "CityPlannerButton";
            var controller = go.GetComponent<ButtonController>();
            if (controller != null)
            {
                controller.SetInteractable(true);
                controller.useAutomaticText = false;
                if (controller.text != null) controller.text.text = "City Planner";
                if (controller.tooltip != null) controller.tooltip.enabled = false;
            }
            // A fresh click event drops the copied button's own action (pasting a share code).
            var button = go.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(PlannerLink.OpenPage)));
            }
            // Just right of the size list, the same height.
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = sizeRect.anchorMin;
            rect.anchorMax = sizeRect.anchorMax;
            rect.pivot = sizeRect.pivot;
            rect.sizeDelta = new Vector2(Mathf.Max(sizeRect.rect.width * 0.55f, rect.rect.width), sizeRect.rect.height);
            rect.anchoredPosition = sizeRect.anchoredPosition + new Vector2(sizeRect.rect.width * (1f - sizeRect.pivot.x) + rect.sizeDelta.x * rect.pivot.x + 12f, 0f);
            go.SetActive(true);
            Plugin.Logger.LogInfo($"City Planner button ({controller?.GetIl2CppType().Name ?? "no controller"}, copied from {template.name}) added beside the city size (size list {sizeRect.rect.width:0}x{sizeRect.rect.height:0} at {sizeRect.anchoredPosition}, in '{sizeRect.parent?.name}')");
            return go;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning("Couldn't add the City Planner button: " + e.Message);
            return new GameObject("CityPlannerButtonFailed"); // don't retry every half second
        }
    }
}

/// <summary>
/// Clicks on the City Planner button. The copied button's own click event isn't reliable, so the click is caught
/// where the game's buttons receive it, and the copied button's original action (pasting a share code) never runs.
/// </summary>
[HarmonyPatch(typeof(ButtonController), nameof(ButtonController.OnPointerClick))]
internal static class MenuButtonClick
{
    private static bool Prefix(ButtonController __instance, UnityEngine.EventSystems.PointerEventData eventData)
    {
        if (__instance == null || __instance.gameObject.name != "CityPlannerButton") return true;
        if (eventData == null || eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) PlannerLink.OpenPage();
        return false;
    }
}
