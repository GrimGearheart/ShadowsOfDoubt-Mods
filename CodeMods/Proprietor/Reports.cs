using System;
using System.Collections.Generic;
using System.Linq;
using SOD.Common;

namespace Proprietor;

/// <summary>A manager's daily report, sent to the player as a vmail.</summary>
public class Report
{
    public string Id { get; set; }
    public float Time { get; set; }
    public int SenderId { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
}

/// <summary>
/// Every morning each manager vmails the owner a report: wages, deliveries, restocking, what's low, and how the
/// regulars seem. Each report is its own little DDS tree (subject block + body block), registered with the game when
/// it's sent and again whenever the save loads. The game's vmail app breaks on a thread whose tree it doesn't know,
/// so report threads are taken out of the game's save while it saves and put straight back: without this mod a save
/// holds no trace of them.
/// </summary>
internal static class Reports
{
    private const float KeepHours = 7 * 24f;
    private const string Prefix = "proprietor_report_";
    private static readonly Dictionary<string, (StateSaveData.MessageThreadSave Thread, Human Sender)> posted = new();

    /// <summary>Sends today's report for a business from its manager.</summary>
    public static void Send(Business b, Company c, string body)
    {
        var now = SessionData.Instance.gameTime;
        var sender = Manager(c);
        var r = new Report
        {
            Id = $"{b.CompanyId}_{(int)(now * 60)}",
            Time = now,
            SenderId = sender?.humanID ?? -1,
            Subject = $"{b.Name} - daily report",
            Body = body
        };
        b.Reports.Add(r);
        foreach (var old in b.Reports.Where(x => now - x.Time > KeepHours).ToList())
        {
            Remove(old.Id);
            b.Reports.Remove(old);
        }
        Post(r);
    }

    /// <summary>After a save loads: every kept report goes back in the inbox.</summary>
    public static void Restore()
    {
        posted.Clear();
        var n = 0;
        foreach (var b in Store.Owned.Values)
            foreach (var r in b.Reports)
                if (Post(r)) n++;
        if (n > 0) Plugin.Logger.LogInfo($"Reports: {n} manager report(s) back in your vmail");
    }

    /// <summary>Takes the report threads out of the game (before it saves, or before another save loads).</summary>
    public static void Detach()
    {
        foreach (var id in posted.Keys.ToList()) Unlink(id);
    }

    /// <summary>Puts them back after saving.</summary>
    public static void Reattach()
    {
        foreach (var (thread, sender) in posted.Values) Link(thread, sender);
    }

    /// <summary>Forgets every report thread (before another save or a new game loads).</summary>
    public static void Clear()
    {
        Detach();
        posted.Clear();
    }

    // ---- The game's side ----

    private static bool Post(Report r)
    {
        try
        {
            var player = Player.Instance;
            if (player == null) return false;
            Human sender = null;
            if (r.SenderId >= 0) CityData.Instance.GetHuman(r.SenderId, out sender);
            sender ??= player;
            var tree = Register(r);
            var thread = Toolbox.Instance.NewVmailThread(sender, player, null, null, null, tree, r.Time, 999,
                StateSaveData.CustomDataSource.sender, -1);
            if (thread == null) return false;
            posted[r.Id] = (thread, sender);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't send a manager's report: " + e.Message);
            return false;
        }
    }

    /// <summary>Makes the report's tree, message and text blocks known to the game. Returns the tree id.</summary>
    private static string Register(Report r)
    {
        var p = Prefix + r.Id;
        var tb = Toolbox.Instance;
        Lib.DdsStrings.AddOrUpdate("dds.blocks", p + "_s", r.Subject);
        Lib.DdsStrings.AddOrUpdate("dds.blocks", p + "_b", r.Body);
        foreach (var id in new[] { p + "_s", p + "_b" })
            if (!tb.allDDSBlocks.ContainsKey(id))
                tb.allDDSBlocks.Add(id, new DDSSaveClasses.DDSBlockSave { id = id, name = id });

        if (!tb.allDDSMessages.ContainsKey(p + "_m"))
        {
            var msg = new DDSSaveClasses.DDSMessageSave { id = p + "_m", name = p + "_m" };
            msg.blocks.Add(new DDSSaveClasses.DDSBlockCondition { blockID = p + "_s", instanceID = p + "_si", alwaysDisplay = true });
            msg.blocks.Add(new DDSSaveClasses.DDSBlockCondition { blockID = p + "_b", instanceID = p + "_bi", alwaysDisplay = true });
            tb.allDDSMessages.Add(msg.id, msg);
        }

        var treeId = p + "_t";
        if (!tb.allDDSTrees.ContainsKey(treeId))
        {
            var settings = new DDSSaveClasses.DDSMessageSettings { msgID = p + "_m", instanceID = p + "_i", saidBy = 0, saidTo = 1 };
            var tree = new DDSSaveClasses.DDSTreeSave
            {
                id = treeId,
                name = treeId,
                treeType = DDSSaveClasses.TreeType.vmail,
                startingMessage = settings.instanceID
            };
            tree.messages.Add(settings);
            tree.messageRef = new Il2CppSystem.Collections.Generic.Dictionary<string, DDSSaveClasses.DDSMessageSettings>();
            tree.messageRef.Add(settings.instanceID, settings);
            tb.allDDSTrees.Add(treeId, tree);
        }
        return treeId;
    }

    private static void Remove(string id)
    {
        Unlink(id);
        posted.Remove(id);
    }

    private static void Unlink(string id)
    {
        if (!posted.TryGetValue(id, out var p)) return;
        try
        {
            var t = p.Thread;
            GameplayController.Instance.messageThreads.Remove(t.threadID);
            p.Sender?.messageThreadsStarted.Remove(t);
            p.Sender?.messageThreadFeatures.Remove(t);
            Player.Instance?.messageThreadFeatures.Remove(t);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't take a report out of vmail: " + e.Message);
        }
    }

    private static void Link(StateSaveData.MessageThreadSave t, Human sender)
    {
        try
        {
            if (!GameplayController.Instance.messageThreads.ContainsKey(t.threadID))
                GameplayController.Instance.messageThreads.Add(t.threadID, t);
            if (sender != null && !sender.messageThreadsStarted.Contains(t)) sender.messageThreadsStarted.Add(t);
            if (sender != null && !sender.messageThreadFeatures.Contains(t)) sender.messageThreadFeatures.Add(t);
            var player = Player.Instance;
            if (player != null && !player.messageThreadFeatures.Contains(t)) player.messageThreadFeatures.Add(t);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Couldn't put a report back in vmail: " + e.Message);
        }
    }

    /// <summary>Who signs the report: the manager (the original owner), else anyone on the staff.</summary>
    public static Human Manager(Company c)
    {
        if (c == null) return null;
        if (c.director != null && !c.director.isDead) return c.director;
        foreach (var occ in c.companyRoster)
            if (occ?.employee != null && !occ.employee.isDead) return occ.employee;
        return null;
    }
}
