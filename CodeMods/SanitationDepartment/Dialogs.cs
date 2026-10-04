using System;
using System.Linq;
using SOD.Common;
using SOD.Common.Helpers.DialogObjects;

namespace SanitationDepartment;

/// <summary>City Hall paperwork. Prices use the game's own dialog costs (greyed out when
/// unaffordable, charged only on success).</summary>
internal static class Dialogs
{
    public static void Register()
    {
        Lib.Dialogs.Builder("Sanitation_License")
            .SetText("I'd like a sanitation license.")
            .SetDialogLogic(new Logic(() => !State.Licensed, () =>
            {
                State.Licensed = true;
                State.Notify($"Sanitation License acquired. Trash and junk you pick up go in your bag ({State.BagCapacity} items); empty it at any bin with {Plugin.ActionKey.Value}.");
            }))
            .ModifyDialogOptions(p => { p.cost = Plugin.LicenseCost.Value; p.ranking = 4; })
            .AddResponse("You're licensed, and here's your bag. Every piece of trash or junk you bin earns you a little. Welcome aboard.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_Picker")
            .SetText("I'd like a litter picker.")
            .SetDialogLogic(new Logic(() => State.Licensed && !State.Picker, () =>
            {
                State.Picker = true;
                State.Notify("Litter picker: grab trash and junk from a few metres away.");
            }))
            .ModifyDialogOptions(p => { p.cost = Plugin.PickerCost.Value; p.ranking = 4; })
            .AddResponse("Saves your back. Mind you don't poke anyone with it.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_FullTime")
            .SetText("I want to transfer to sanitation full-time.")
            .SetDialogLogic(new Logic(() => State.Licensed && !State.FullTime && FullTime.Allowed, () =>
            {
                State.FullTime = true;
                State.Notify("You're on sanitation full-time. The Enforcers will handle the killers.");
            }))
            .ModifyDialogOptions(p => p.ranking = 5)
            .AddResponse("Done. The Enforcers can chase the killers; you keep the streets clean.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_Return")
            .SetText("I'm going back to detective work.")
            .SetDialogLogic(new Logic(() => State.FullTime, () =>
            {
                State.FullTime = false;
                State.Notify("Back on detective work. The city's killers are your problem again.", InterfaceControls.Icon.lookingGlass);
            }))
            .ModifyDialogOptions(p => p.ranking = 5)
            .AddResponse("Your old desk's still there. Your license stays valid if you want the odd shift.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_Certification")
            .SetText("I'd like to be certified for crime scene cleanup.")
            .SetDialogLogic(new Logic(() => State.Licensed && !State.Certified && FullTime.Allowed, () =>
            {
                State.Certified = true;
                State.Notify(Plugin.ScenesOnlyFullTime.Value
                    ? "Crime scene cleanup certified. While you're on sanitation full-time, the Enforcers will release scenes to you."
                    : "Crime scene cleanup certified. The Enforcers will release scenes to you.", InterfaceControls.Icon.skull);
            }))
            .ModifyDialogOptions(p => { p.cost = Plugin.CertificationCost.Value; p.ranking = 4; })
            .AddResponse("Not everyone has the stomach for it. When the Enforcers are done with a scene, it's yours: bag the body, scrub the blood, and the coroner does the rest.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_Mop")
            .SetText("I'd like a mop and bucket.")
            .SetDialogLogic(new Logic(() => State.Certified && !State.Mop, () =>
            {
                State.Mop = true;
                State.Notify("Mop and bucket: you scrub a wider area, faster.");
            }))
            .ModifyDialogOptions(p => { p.cost = Plugin.MopCost.Value; p.ranking = 4; })
            .AddResponse("Heavy-duty. Rinse it between jobs.", isSuccesful: true)
            .CreateAndRegister();

        Lib.Dialogs.Builder("Sanitation_Supervisor")
            .SetText("I'm here about the Route Supervisor position.")
            .SetDialogLogic(new Logic(() => State.Licensed && !State.Supervisor && State.Binned >= Plugin.SupervisorItems.Value, () =>
            {
                State.Supervisor = true;
                State.GlowOn = true;
                State.Notify($"Route Supervisor. Nearby trash now stands out (toggle with {Plugin.GlowToggleKey.Value}), and your bag holds {State.BagCapacity}.", InterfaceControls.Icon.star);
            }))
            .ModifyDialogOptions(p => { p.cost = Plugin.SupervisorCost.Value; p.ranking = 4; })
            .AddResponse("Your numbers speak for themselves. You'll learn to spot the mess before anyone else does.", isSuccesful: true)
            .CreateAndRegister();
    }

    /// <summary>City Hall staff who are currently at work (optionally limited to certain jobs).</summary>
    internal static bool IsCityHallDesk(Citizen citizen)
    {
        if (citizen == null || !citizen.isAtWork) return false;
        var job = citizen.job;
        var building = job?.employer?.placeOfBusiness?.building;
        if (building?.preset == null || building.preset.presetName != "CityHall") return false;
        var jobs = Plugin.DeskJobs.Value;
        if (string.IsNullOrWhiteSpace(jobs)) return true;
        var jobName = job.preset?.name;
        return jobName != null && jobs.Split(',').Any(j => j.Trim().Equals(jobName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A City Hall option that is shown when <paramref name="available"/> holds and always succeeds.</summary>
    private sealed class Logic : IDialogLogic
    {
        private readonly Func<bool> available;
        private readonly Action onSuccess;

        public Logic(Func<bool> available, Action onSuccess)
        {
            this.available = available;
            this.onSuccess = onSuccess;
        }

        public bool IsDialogShown(DialogPreset preset, Citizen saysTo, SideJob jobRef)
            => available() && IsCityHallDesk(saysTo);

        public DialogController.ForceSuccess ShouldDialogSucceedOverride(DialogController instance,
            EvidenceWitness.DialogOption dialog, Citizen saysTo, NewNode where, Actor saidBy)
            => DialogController.ForceSuccess.success;

        public void OnDialogExecute(DialogController instance, Citizen saysTo, Interactable saysToInteractable,
            NewNode where, Actor saidBy, bool success, NewRoom roomRef, SideJob jobRef)
        {
            if (success) onSuccess();
        }
    }
}
