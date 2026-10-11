using System;
using System.Collections.Generic;
using HarmonyLib;
using SOD.Common;

namespace DetectiveLicense;

/// <summary>
/// Public records for licensed investigators. The clerks' computers in the City Hall lobby get the citizen database
/// (the same program as the enforcer offices) while the player holds a license; a clerk hands over their login, and
/// every printout made there costs Records.PrintCost. Enforcer and hospital computers are untouched.
/// </summary>
[HarmonyPatch]
internal static class Records
{
    private const string LobbyPreset = "CityHallLobby";
    private const string DatabaseAppName = "GovDatabase";

    internal static bool IsLobby(NewAddress address) => address?.addressPreset != null && address.addressPreset.name == LobbyPreset;

    private static NewAddress AddressOf(ComputerController cc) => cc?.ic?.interactable?.node?.gameLocation?.thisAsAddress;

    /// <summary>The people who can log in at the lobby desks (each desk's owners), the clerk asked first if they have one.</summary>
    internal static Human DeskLogin(Human clerk)
    {
        var owners = new List<Human>();
        var building = clerk?.job?.employer?.placeOfBusiness?.building;
        if (building == null) return null;
        foreach (var fkv in building.floors)
        {
            foreach (var address in fkv.Value.addresses)
            {
                if (!IsLobby(address)) continue;
                foreach (var room in address.rooms)
                foreach (var node in room.nodes)
                foreach (var i in node.interactables)
                {
                    if (i?.preset == null || !i.preset.isComputer || i.furnitureParent == null) continue;
                    foreach (var kv in i.furnitureParent.ownerMap)
                    {
                        var h = kv.Key.human;
                        if (h == null || h.passcode == null) continue;
                        if (h.humanID == clerk.humanID) return h;
                        if (!owners.Exists(o => o.humanID == h.humanID)) owners.Add(h);
                    }
                }
            }
        }
        return owners.Count > 0 ? owners[0] : null;
    }

    /// <summary>
    /// The database only installs where its preset's address list allows (enforcer offices, hospital wards). The
    /// lobby is on that list exactly while the player is licensed. The list is shared by every computer, but only
    /// lobby addresses are ever added or removed.
    /// </summary>
    [HarmonyPatch(typeof(DesktopApp), nameof(DesktopApp.UpdateIcons))]
    [HarmonyPrefix]
    private static void Install(DesktopApp __instance)
    {
        try
        {
            var address = AddressOf(__instance.controller);
            if (!IsLobby(address)) return;
            var apps = __instance.controller.ic.interactable.preset.additionalApps;
            foreach (var app in apps)
            {
                if (app == null || app.name != DatabaseAppName) continue;
                var allowed = app.onlyInAddresses;
                var listed = allowed.Contains(address.addressPreset);
                if (Plugin.Licensed && !listed) allowed.Add(address.addressPreset);
                else if (!Plugin.Licensed && listed) allowed.Remove(address.addressPreset);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("Records install failed: " + e);
        }
    }

    /// <summary>Lobby printouts cost money: refused with the error beep when you can't pay, charged once printed.</summary>
    [HarmonyPatch(typeof(DatabaseApp), nameof(DatabaseApp.OnPrintEntry))]
    [HarmonyPrefix]
    private static bool BeforePrint(DatabaseApp __instance, out bool __state)
    {
        __state = false;
        var cc = __instance.controller;
        if (!IsLobby(AddressOf(cc))) return true;
        var cost = Plugin.PrintCost.Value;
        // Only a print that will actually happen is charged (the game beeps and does nothing otherwise).
        __state = cost > 0 && cc.printedDocument == null && cc.printTimer <= 0f;
        if (__state && GameplayController.Instance.money < cost)
        {
            __state = false;
            AudioController.Instance.PlayWorldOneShot(AudioControls.Instance.computerInvalidPasscode, Player.Instance,
                cc.ic.interactable.node, cc.ic.interactable.wPos);
            Lib.GameMessage.Broadcast($"Records printout: {cost} crows", icon: InterfaceControls.Icon.money,
                color: InterfaceControls.Instance.messageRed);
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(DatabaseApp), nameof(DatabaseApp.OnPrintEntry))]
    [HarmonyPostfix]
    private static void AfterPrint(DatabaseApp __instance, bool __state)
    {
        if (!__state || __instance.controller.printedDocument == null) return;
        var cost = Plugin.PrintCost.Value;
        GameplayController.Instance.AddMoney(-cost, true, "public records");
        Plugin.Logger.LogInfo($"Records printout: charged {cost}");
    }
}
