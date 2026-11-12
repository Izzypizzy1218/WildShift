using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WildShift
{
    // Animal think trees do not contain the human meditation job giver. This
    // explicit order uses the native meditation driver without running another
    // person's work/AI tree or granting meditation to ordinary animals.
    public static class SharedFormMeditation
    {
        public static bool Eligible(Pawn body)
        {
            Pawn human=SharedFormIdentity.HumanFor(body);
            return ModsConfig.RoyaltyActive &&human!=null&&body.Faction?.IsPlayer==true
                &&!human.IsPrisoner&&!body.IsPrisoner&&body.IsColonistPlayerControlled
                &&body.psychicEntropy!=null&&body.HasPsylink
                &&body.psychicEntropy.PsychicSensitivity>float.Epsilon;
        }

        public static bool CanOrder(Pawn body)
        {
            return Eligible(body)&&body.Spawned&&!body.Dead&&!body.Downed&&!body.InMentalState
                &&body.jobs!=null&&MeditationUtility.CanMeditateNow(body)
                &&MeditationUtility.SafeEnvironmentalConditions(body,body.Position,body.Map);
        }

        public static bool Order(Pawn body)
        {
            if(!CanOrder(body))return false;
            // Focus choice uses the active body's location and the original
            // person's native focus permissions (patched below).
            LocalTargetInfo focus=MeditationUtility.BestFocusAt(body.Position,body);
            Job job=JobMaker.MakeJob(JobDefOf.Meditate,body.Position,LocalTargetInfo.Invalid,focus);
            job.ignoreJoyTimeAssignment=true;
            job.playerForced=true;
            if(body.Drafted)body.drafter.Drafted=false;
            body.jobs.TryTakeOrderedJob(job,JobTag.Misc);
            return body.CurJob==job;
        }

        public static IEnumerable<Gizmo> Gizmos(IEnumerable<Gizmo> native,Pawn body)
        {
            foreach(Gizmo gizmo in native)yield return gizmo;
            if(!Eligible(body))yield break;
            var command=new Command_Action
            {
                defaultLabel="WildShift_Meditate".Translate(),
                defaultDesc="WildShift_MeditateDesc".Translate(),
                icon=ThingDefOf.MeditationSpot.uiIcon,
                action=delegate
                {
                    if(!Order(body))Messages.Message("WildShift_MeditationUnavailable".Translate(),body,MessageTypeDefOf.RejectInput,false);
                }
            };
            if(!CanOrder(body))command.Disable("WildShift_MeditationUnavailable".Translate());
            yield return command;
        }
    }

    [HarmonyPatch(typeof(Pawn),"GetGizmos")]
    public static class Patch_SharedMeditationCommand
    {
        public static void Postfix(Pawn __instance,ref IEnumerable<Gizmo> __result)
        {
            if(SharedFormMeditation.Eligible(__instance))__result=SharedFormMeditation.Gizmos(__result,__instance);
        }
    }

    [HarmonyPatch(typeof(MeditationFocusTypeAvailabilityCache),"PawnCanUse")]
    public static class Patch_SharedMeditationFocusIdentity
    {
        public static void Prefix(ref Pawn p){p=SharedFormSystems.Canonical(p);}
    }
}
