using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    public class HediffCompProperties_Shapeshifter : HediffCompProperties
    {
        public HediffCompProperties_Shapeshifter()
        {
            compClass = typeof(HediffComp_Shapeshifter);
        }
    }

    public class HediffComp_Shapeshifter : HediffComp
    {
        public PawnKindDef assignedKind;
        private int lastShiftTick = -999999;
        private bool legacyAbilitiesRemoved;

        private Pawn ParentPawn
        {
            get
            {
                return parent != null ? parent.pawn : null;
            }
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (assignedKind == null)
                {
                    return null;
                }

                return "WildShift_LabelAssignedKind".Translate(assignedKind.LabelCap);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Defs.Look(ref assignedKind, "assignedKind");
            Scribe_Values.Look(ref lastShiftTick, "lastShiftTick", -999999);
            Scribe_Values.Look(ref legacyAbilitiesRemoved, "legacyAbilitiesRemoved", false);
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            EnsureAssignedKind();
            RemoveLegacyAbilities();
        }

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            IEnumerable<Gizmo> baseGizmos = base.CompGetGizmos();
            if (baseGizmos != null)
            {
                foreach (Gizmo gizmo in baseGizmos)
                {
                    yield return gizmo;
                }
            }

            Pawn pawn = ParentPawn;
            if (pawn == null || pawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            RemoveLegacyAbilities();

            if (pawn.drafter == null || !pawn.drafter.Drafted)
            {
                yield break;
            }

            string disabledReason;
            bool disabled = ShiftGizmoDisabled(pawn, out disabledReason);
            string kindLabel = assignedKind != null ? assignedKind.LabelCap.ToString() : "???";
            Command_Action command = new Command_Action
            {
                defaultLabel = "WildShift_CommandShiftLabel".Translate(),
                defaultDesc = "WildShift_CommandShiftDesc".Translate(kindLabel),
                action = delegate
                {
                    TryShiftFromGizmo(pawn);
                }
            };

            if (disabled)
            {
                command.Disable(disabledReason);
            }

            yield return command;
        }

        public void EnsureAssignedKind()
        {
            if (assignedKind != null && AnimalPool.IsEligible(assignedKind))
            {
                return;
            }

            assignedKind = AnimalPool.RandomEligibleKind();
            if (assignedKind == null)
            {
                Log.Warning("[WildShift] No eligible animal kind could be assigned to a shapeshifter.");
            }
        }

        public void RemoveLegacyAbilities()
        {
            if (legacyAbilitiesRemoved)
            {
                return;
            }

            Pawn pawn = ParentPawn;
            if (pawn == null || pawn.abilities == null || WildShiftDefOf.WildShift_Shift == null)
            {
                return;
            }

            int safety = 0;
            while (pawn.abilities.GetAbility(WildShiftDefOf.WildShift_Shift) != null && safety < 16)
            {
                pawn.abilities.RemoveAbility(WildShiftDefOf.WildShift_Shift);
                safety++;
            }

            legacyAbilitiesRemoved = true;
        }

        private bool ShiftGizmoDisabled(Pawn pawn, out string reason)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned)
            {
                reason = "WildShift_ShiftDisabledBadPawn".Translate();
                return true;
            }

            EnsureAssignedKind();
            if (assignedKind == null)
            {
                reason = "WildShift_ShiftDisabledNoKind".Translate();
                return true;
            }

            int remaining = CooldownTicksRemaining();
            if (remaining > 0)
            {
                reason = "WildShift_ShiftDisabledCooldown".Translate((remaining / 2500f).ToString("0.#"));
                return true;
            }

            reason = null;
            return false;
        }

        private void TryShiftFromGizmo(Pawn pawn)
        {
            string reason;
            if (ShiftGizmoDisabled(pawn, out reason))
            {
                if (!reason.NullOrEmpty())
                {
                    Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                }

                return;
            }

            PawnKindDef kind = assignedKind;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (TransformUtility.TransformToAnimal(pawn, kind))
                {
                    lastShiftTick = Find.TickManager.TicksGame;
                }
            });
        }

        private int CooldownTicksRemaining()
        {
            if (lastShiftTick <= -999000 || WildShiftMod.Settings == null || Find.TickManager == null)
            {
                return 0;
            }

            int elapsed = Find.TickManager.TicksGame - lastShiftTick;
            int remaining = WildShiftMod.Settings.ShiftCooldownTicks - elapsed;
            return remaining > 0 ? remaining : 0;
        }
    }
}
