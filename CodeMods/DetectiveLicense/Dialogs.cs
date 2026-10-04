using System;
using System.Linq;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;

namespace DetectiveLicense;

/// <summary>The two City Hall dialog options. Prices use the game's own dialog cost, so the option
/// greys out when unaffordable and money is only taken when the request succeeds.</summary>
internal static class Dialogs
{
    private static readonly System.Collections.Generic.HashSet<int> LoggedStaff = new();

    public static void Register()
    {
        Lib.Dialogs.Builder("DetectiveLicense_Permit")
            .SetText("I need a crime scene permit.")
            .SetDialogLogic(new PermitLogic())
            .ModifyDialogOptions(p =>
            {
                p.cost = Plugin.PermitCost.Value;
                p.ranking = 6;
            })
            .AddResponse("Here's your permit. Don't touch anything you don't need to.", isSuccesful: true)
            .AddResponse("There aren't any open murder cases right now, Detective.", isSuccesful: false)
            .CreateAndRegister();

        Lib.Dialogs.Builder("DetectiveLicense_License")
            .SetText("I'd like a private investigator's license.")
            .SetDialogLogic(new LicenseLogic())
            .ModifyDialogOptions(p =>
            {
                p.cost = Plugin.LicenseCost.Value;
                p.ranking = 5;
            })
            .AddResponse("All in order. Welcome to the profession, Detective.", isSuccesful: true)
            .CreateAndRegister();
    }

    /// <summary>City Hall staff who are currently at work (optionally limited to certain jobs).</summary>
    internal static bool IsCityHallDesk(Citizen citizen)
    {
        if (citizen == null) return false;
        var job = citizen.job;
        var building = job?.employer?.placeOfBusiness?.building;
        if (building?.preset == null || building.preset.presetName != "CityHall") return false;

        // Diagnostic, once per citizen: helps tune DeskJobs to the city's actual City Hall staff.
        if (LoggedStaff.Add(citizen.humanID))
            Plugin.Logger.LogDebug($"City Hall staff: {citizen.GetCitizenName()} job={job.preset?.name} atWork={citizen.isAtWork}");
        if (!citizen.isAtWork) return false;

        var jobs = Plugin.DeskJobs.Value;
        if (string.IsNullOrWhiteSpace(jobs)) return true;
        var jobName = job.preset?.name;
        return jobName != null && jobs.Split(',').Any(j => j.Trim().Equals(jobName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PermitLogic : IDialogLogic
    {
        // A license already covers crime scenes, so licensed players don't need permits.
        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef)
            => !Plugin.Licensed && IsCityHallDesk(saysTo);

        // Refuse (and therefore don't charge) when there is nothing to cover.
        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => CaseLocations.Get().Count > 0 ? DialogController.ForceSuccess.success : DialogController.ForceSuccess.fail;

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (!success) return;
            var hours = Plugin.PermitHours.Value;
            var count = CaseLocations.IssuePermit(hours);
            Lib.GameMessage.Broadcast($"Crime scene permit: {count} location{(count == 1 ? "" : "s")}, {hours:0.#} hours",
                icon: InterfaceControls.Icon.key, color: InterfaceControls.Instance.messageGreen);
            Plugin.Logger.LogInfo($"Permit issued for {count} case location(s), {hours} hours");
        }
    }

    private sealed class LicenseLogic : IDialogLogic
    {
        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef)
            => !Plugin.Licensed && IsCityHallDesk(saysTo);

        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => DialogController.ForceSuccess.success;

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (!success) return;
            Plugin.Licensed = true;
            CaseLocations.Invalidate();
            Lib.GameMessage.Broadcast("Private investigator's license acquired",
                icon: InterfaceControls.Icon.star, color: InterfaceControls.Instance.messageGreen);
            Plugin.Logger.LogInfo("License acquired");
        }
    }
}
