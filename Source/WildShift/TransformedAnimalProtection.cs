using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WildShift
{
    public static class TransformedAnimalProtection
    {
        public static bool IsOwnedPerson(Pawn body)
        {
            if (body?.Faction?.IsPlayer != true) return false;
            Pawn human = SharedFormIdentity.HumanFor(body);
            return human?.Faction?.IsPlayer == true;
        }

        public static bool SlaughterBlocked(Pawn body)
        {
            return WildShiftMod.Settings?.allowTransformedSlaughter == false && IsOwnedPerson(body);
        }

        public static bool SaleBlocked(Thing thing)
        {
            return WildShiftMod.Settings?.allowTransformedSale == false && IsOwnedPerson(thing as Pawn);
        }

        // Only a settings transition or completed load scans designations.
        // Normal gameplay queries use the individual pawn's active holder.
        public static void SettingsChanged()
        {
            if (Current.Game?.Maps == null) return;
            foreach (Map map in Current.Game.Maps)
            {
                if (map == null) continue;
                map.autoSlaughterManager?.Notify_ConfigChanged();
                if (WildShiftMod.Settings?.allowTransformedSlaughter != false) continue;
                if (map.designationManager == null) continue;
                List<Designation> designations = map.designationManager.AllDesignations;
                for (int i = designations.Count - 1; i >= 0; i--)
                {
                    Designation designation = designations[i];
                    if (designation.def == DesignationDefOf.Slaughter && SlaughterBlocked(designation.target.Thing as Pawn))
                        map.designationManager.RemoveDesignation(designation);
                }
            }
        }

        public static bool MentalSlaughterCandidate(Pawn pawn)
        {
            return pawn != null && pawn.IsAnimal && !SlaughterBlocked(pawn);
        }
    }

    public sealed class GameComponent_TransformedAnimalProtection : GameComponent
    {
        public GameComponent_TransformedAnimalProtection(Game game) { }
        public override void FinalizeInit()
        {
            LongEventHandler.ExecuteWhenFinished(TransformedAnimalProtection.SettingsChanged);
        }
    }

    [HarmonyPatch(typeof(Designator_Slaughter), "CanDesignateThing")]
    public static class Patch_ProtectedSlaughterDesignator
    {
        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (TransformedAnimalProtection.SlaughterBlocked(t as Pawn))
                __result = "WildShift_SlaughterBlocked".Translate();
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_Slaughter), "HasCheckbox")]
    public static class Patch_ProtectedSlaughterColumn
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        { if (TransformedAnimalProtection.SlaughterBlocked(pawn)) __result = false; }
    }

    [HarmonyPatch(typeof(DesignationManager), "AddDesignation")]
    public static class Patch_ProtectedSlaughterDesignation
    {
        public static bool Prefix(Designation newDes)
        {
            return newDes?.def != DesignationDefOf.Slaughter
                || !TransformedAnimalProtection.SlaughterBlocked(newDes.target.Thing as Pawn);
        }
    }

    [HarmonyPatch(typeof(AutoSlaughterManager), "CanEverAutoSlaughter")]
    public static class Patch_ProtectedAutoSlaughter
    {
        public static void Postfix(Pawn animal, ref bool __result)
        { if (TransformedAnimalProtection.SlaughterBlocked(animal)) __result = false; }
    }

    [HarmonyPatch(typeof(RimWorld.PawnUtility), "ShouldBeSlaughtered")]
    public static class Patch_ProtectedSlaughterEligibility
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        { if (TransformedAnimalProtection.SlaughterBlocked(pawn)) __result = false; }
    }

    [HarmonyPatch(typeof(JobDriver_Slaughter), "MakeNewToils")]
    public static class Patch_ProtectedSlaughterJob
    {
        public static void Prefix(JobDriver_Slaughter __instance)
        {
            __instance.AddEndCondition(delegate
            {
                Pawn victim = __instance.job?.targetA.Thing as Pawn;
                return TransformedAnimalProtection.SlaughterBlocked(victim)
                    ? JobCondition.Incompletable : JobCondition.Ongoing;
            });
        }
    }

    [HarmonyPatch(typeof(ExecutionUtility), "DoExecutionByCut")]
    public static class Patch_ProtectedSlaughterExecution
    {
        public static bool Prefix(Pawn executioner, Pawn victim)
        {
            return executioner?.CurJobDef != JobDefOf.Slaughter
                || !TransformedAnimalProtection.SlaughterBlocked(victim);
        }
    }

    [HarmonyPatch(typeof(SlaughtererMentalStateUtility), "FindAnimal")]
    public static class Patch_ProtectedMentalSlaughterTarget
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Pawn), "IsAnimal");
            MethodInfo predicate = AccessTools.Method(typeof(TransformedAnimalProtection), "MentalSlaughterCandidate");
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = predicate; }
                yield return instruction;
            }
        }
    }
}
