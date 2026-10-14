using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_TransformedAnimalDraftGizmos
    {
        public static bool Prefix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance != null ? __instance.pawn : null;
            if (!TransformUtility.IsTransformedAnimal(pawn))
            {
                return true;
            }

            __result = GetTransformedDraftGizmos(pawn);
            return false;
        }

        public static void Postfix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance != null ? __instance.pawn : null;
            if (TransformUtility.IsTransformedAnimal(pawn))
            {
                return;
            }

            HediffComp_Shapeshifter shapeshifter = TransformUtility.TryGetShapeshifterComp(pawn);
            if (shapeshifter == null)
            {
                return;
            }

            Gizmo shiftGizmo = shapeshifter.CreateShiftGizmo();
            if (shiftGizmo != null)
            {
                __result = InsertAfterDraftGizmo(__result, shiftGizmo);
            }
        }

        private static IEnumerable<Gizmo> GetTransformedDraftGizmos(Pawn animal)
        {
            Command_Toggle command = new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                defaultLabel = animal.Drafted
                    ? "WildShift_CommandUndraftLabel".Translate()
                    : "WildShift_CommandDraftLabel".Translate(),
                defaultDesc = "WildShift_CommandDraftDesc".Translate(),
                icon = TexCommand.Draft,
                isActive = delegate
                {
                    return animal.Drafted;
                },
                toggleAction = delegate
                {
                    if (animal.drafter != null)
                    {
                        animal.drafter.Drafted = !animal.drafter.Drafted;
                    }
                },
                turnOnSound = SoundDefOf.DraftOn,
                turnOffSound = SoundDefOf.DraftOff,
                groupKeyIgnoreContent = 81729172,
                tutorTag = animal.Drafted ? "Undraft" : "Draft"
            };

            if (animal.Downed)
            {
                command.Disable("IsIncapped".Translate(animal.LabelShort, animal));
            }

            yield return command;
        }

        private static IEnumerable<Gizmo> InsertAfterDraftGizmo(
            IEnumerable<Gizmo> original,
            Gizmo shiftGizmo)
        {
            bool inserted = false;
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                    if (!inserted)
                    {
                        inserted = true;
                        yield return shiftGizmo;
                    }
                }
            }

            if (!inserted)
            {
                yield return shiftGizmo;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), "UseRangedAttack")]
    public static class Patch_TransformedUnarmedAttack
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // Native drafted menus assume an equipment tracker exists. Animal
            // forms without one are unarmed and can use native melee orders.
            if (pawn == null || pawn.equipment != null || !TransformUtility.IsTransformedAnimal(pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_TransformedDraftableAnimalsGizmos
    {
        // A passthrough postfix must run after the other mod's passthrough
        // postfix. A ref-result (void) postfix runs before that iterator.
        [HarmonyAfter("DraftableAnimals.Mod")]
        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            if (__instance == null || __instance.Faction != Faction.OfPlayer
                || TransformUtility.TryGetTransformedComp(__instance)?.HasStoredPawn != true)
                return __result;
            return WithoutDuplicateDraft(__result);
        }

        private static IEnumerable<Gizmo> WithoutDuplicateDraft(IEnumerable<Gizmo> original)
        {
            if (original == null) yield break;
            foreach (Gizmo gizmo in original)
            {
                // Releashed adds its own button and only removes vanilla's
                // description. Preserve WildShift's grouped button and every
                // unrelated command, including other mods' custom actions.
                if (gizmo is Command_Toggle command
                    && command.hotKey == KeyBindingDefOf.Command_ColonistDraft
                    && command.toggleAction?.Method.DeclaringType?.Assembly.GetName().Name == "DraftableAnimals")
                    continue;
                yield return gizmo;
            }
        }
    }
}
