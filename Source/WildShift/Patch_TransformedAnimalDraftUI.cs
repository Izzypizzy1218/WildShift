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

    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    public static class Patch_TransformedAnimalDirectRightClickAttack
    {
        public static bool Prefix()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null
                || currentEvent.type != EventType.MouseDown
                || currentEvent.button != 1
                || Find.Selector == null)
            {
                return true;
            }

            List<Pawn> selectedPawns = Find.Selector.SelectedPawns;
            if (selectedPawns == null || selectedPawns.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < selectedPawns.Count; i++)
            {
                Pawn pawn = selectedPawns[i];
                if (!TransformUtility.IsTransformedAnimal(pawn) || !pawn.Drafted)
                {
                    return true;
                }
            }

            Thing attackTarget = FindAttackTarget(selectedPawns);
            if (attackTarget == null)
            {
                return true;
            }

            bool orderedAny = false;
            for (int i = 0; i < selectedPawns.Count; i++)
            {
                orderedAny |= TransformUtility.TryOrderMeleeAttack(selectedPawns[i], attackTarget);
            }

            if (!orderedAny)
            {
                return true;
            }

            FleckMaker.Static(attackTarget.Position, attackTarget.Map, FleckDefOf.FeedbackMelee, 1f);
            currentEvent.Use();
            return false;
        }

        private static Thing FindAttackTarget(List<Pawn> selectedAnimals)
        {
            foreach (LocalTargetInfo candidate in GenUI.TargetsAtMouse(TargetingParameters.ForAttackAny(), true, null))
            {
                Thing thing = candidate.Thing;
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    continue;
                }

                bool isSelectedAnimal = false;
                for (int i = 0; i < selectedAnimals.Count; i++)
                {
                    if (thing == selectedAnimals[i])
                    {
                        isSelectedAnimal = true;
                        break;
                    }
                }

                if (!isSelectedAnimal)
                {
                    return thing;
                }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_DraftedAttack), "GetOptionsFor", new[] { typeof(Thing), typeof(FloatMenuContext) })]
    public static class Patch_TransformedAnimalSuppressAttackMenu
    {
        public static bool Prefix(ref FloatMenuContext context, ref IEnumerable<FloatMenuOption> __result)
        {
            if (context == null)
            {
                return true;
            }

            int transformedCount = 0;
            List<Pawn> ordinaryPawns = null;
            foreach (Pawn pawn in context.ValidSelectedPawns)
            {
                if (TransformUtility.IsTransformedAnimal(pawn))
                {
                    transformedCount++;
                }
                else
                {
                    if (ordinaryPawns == null)
                    {
                        ordinaryPawns = new List<Pawn>();
                    }

                    ordinaryPawns.Add(pawn);
                }
            }

            if (transformedCount == 0)
            {
                return true;
            }

            if (ordinaryPawns != null && ordinaryPawns.Count > 0)
            {
                context = new FloatMenuContext(ordinaryPawns, context.clickPosition, context.map);
                return true;
            }

            // Direct right click above owns transformed-animal attacks. Suppress
            // the old menu option so "Move here / Attack" is never presented.
            __result = EmptyFloatMenuOptions.Instance;
            return false;
        }

        private static class EmptyFloatMenuOptions
        {
            public static readonly IEnumerable<FloatMenuOption> Instance = new FloatMenuOption[0];
        }
    }
}
