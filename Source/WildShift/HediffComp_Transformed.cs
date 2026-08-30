using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WildShift
{
    public class HediffCompProperties_Transformed : HediffCompProperties
    {
        public HediffCompProperties_Transformed()
        {
            compClass = typeof(HediffComp_Transformed);
        }
    }

    public class HediffComp_Transformed : HediffComp, IThingHolder
    {
        private ThingOwner<Pawn> storedPawns;
        private bool generatedHealthSanitized;

        public HediffComp_Transformed()
        {
            storedPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
        }

        public bool HasStoredPawn
        {
            get
            {
                return storedPawns != null && storedPawns.Count > 0;
            }
        }

        public Pawn StoredPawn
        {
            get
            {
                return storedPawns != null && storedPawns.Count > 0 ? storedPawns[0] : null;
            }
        }

        public IThingHolder ParentHolder
        {
            get
            {
                return parent != null ? parent.pawn : null;
            }
        }

        public void Store(Pawn human)
        {
            if (human == null)
            {
                Log.Error("[WildShift] Tried to store a null human pawn.");
                return;
            }

            if (storedPawns == null)
            {
                storedPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            }

            if (storedPawns.Count > 0)
            {
                Log.Error("[WildShift] Tried to store more than one human pawn in a transformed body.");
                return;
            }

            if (human.Spawned)
            {
                human.DeSpawn(DestroyMode.Vanish);
            }

            if (!storedPawns.TryAdd(human, false))
            {
                Log.Error("[WildShift] Failed to move the human pawn into the transformed body's ThingOwner.");
            }

            generatedHealthSanitized = true;
        }

        public Pawn ReleaseStoredPawn()
        {
            Pawn human = StoredPawn;
            if (human == null)
            {
                return null;
            }

            storedPawns.Remove(human);
            return human;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Deep.Look(ref storedPawns, "storedPawns", this);
            Scribe_Values.Look(ref generatedHealthSanitized, "generatedHealthSanitized", false);
            if (storedPawns == null)
            {
                storedPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Pawn animal = parent != null ? parent.pawn : null;
                if (!generatedHealthSanitized)
                {
                    TransformUtility.ClearGeneratedAnimalHealth(animal, WildShiftDefOf.WildShift_Transformed);
                }

                generatedHealthSanitized = true;
                TransformUtility.EnsureTransformedAnimalControl(animal, true);
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            if (storedPawns == null)
            {
                storedPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            }

            return storedPawns;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
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

            Pawn animal = parent != null ? parent.pawn : null;
            if (animal == null || animal.Faction != Faction.OfPlayer || !HasStoredPawn)
            {
                yield break;
            }

            TransformUtility.EnsureTransformedAnimalControl(animal, true);

            if (animal.Drafted)
            {
                yield return new Command_Target
                {
                    defaultLabel = "WildShift_CommandAttackLabel".Translate(),
                    defaultDesc = "WildShift_CommandAttackDesc".Translate(),
                    icon = TexCommand.AttackMelee,
                    targetingParams = TargetingParameters.ForAttackAny(),
                    action = delegate(LocalTargetInfo target)
                    {
                        TransformUtility.TryOrderMeleeAttack(animal, target);
                    }
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "WildShift_CommandRevertLabel".Translate(),
                defaultDesc = "WildShift_CommandRevertDesc".Translate(),
                action = delegate
                {
                    LongEventHandler.ExecuteWhenFinished(delegate
                    {
                        TransformUtility.RevertToHuman(animal);
                    });
                }
            };
        }

    }
}
