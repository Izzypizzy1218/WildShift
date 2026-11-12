using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WildShift
{
    // Owns only the inactive animal. While that animal is active this owner is
    // empty, and its transformed comp owns the human instead. Never nest the
    // animal in its own preserved human: that would create a deep-save cycle.
    public sealed class AnimalFormStorage : IExposable, IThingHolder, ISuspendableThingHolder
    {
        private Pawn human;
        private ThingOwner<Pawn> forms;
        private PawnKindDef sourceKind;

        public AnimalFormStorage(Pawn human)
        {
            this.human = human;
            forms = NewOwner();
        }

        private ThingOwner<Pawn> NewOwner()
        {
            var owner = new ThingOwner<Pawn>(this, false, LookMode.Deep);
            owner.dontTickContents = true;
            return owner;
        }

        public Pawn StoredForm { get { return forms != null && forms.Count > 0 ? forms[0] : null; } }
        public bool HasStoredForm { get { return StoredForm != null; } }
        public IThingHolder ParentHolder { get { return human; } }
        public bool IsContentsSuspended { get { return true; } }

        internal void BindOwner(Pawn owner)
        {
            // A Hediff has no pawn during LoadingVars; HediffSet assigns it
            // while resolving references. The owning comp rebinds us once the
            // actual human exists, including when another mod moves its Hediff.
            if (owner != null) human = owner;
        }

        public bool Matches(PawnKindDef kind)
        {
            Pawn form = StoredForm;
            PawnKindDef original = sourceKind ?? TransformUtility.TryGetTransformedComp(form)?.sourceKind ?? form?.kindDef;
            return form != null && kind != null && original != null && original.race == kind.race;
        }

        public Pawn TryTake(PawnKindDef kind)
        {
            Pawn form = StoredForm;
            if (form == null || form.Dead || form.Destroyed || !Matches(kind)) return null;
            HediffComp_Transformed transformed = TransformUtility.TryGetTransformedComp(form);
            if (transformed == null || transformed.HasStoredPawn) return null;
            bool detaching = false;
            try
            {
                // Keep ownership until preparation succeeds. This also lets
                // the death prefix recognize an inactive body during callbacks.
                if (!TransformUtility.PrepareStoredAnimal(form, human)) return null;
                form.mindState?.mentalStateHandler?.Reset();
                if (form.mindState != null) form.mindState.duty = null;
                transformed.enemyCombatForm = false;
                transformed.enemyReturnPending = false;
                detaching = true;
                if (!forms.Remove(form)) return null;
                return form;
            }
            catch (Exception ex)
            {
                // Removal notifications may throw after the owner has already
                // detached the body. Continue with that exact committed body.
                if (detaching && form.holdingOwner == null && !forms.Contains(form)) return form;
                Log.Error("[WildShift] Could not prepare the preserved animal; retaining its original body: " + ex);
                return null;
            }
        }

        public bool TryStore(Pawn form, PawnKindDef kind)
        {
            if (form == null || form == human || form.Destroyed || form.Dead) return false;
            HediffComp_Transformed transformed = TransformUtility.TryGetTransformedComp(form);
            if (transformed == null || transformed.HasStoredPawn) return false;
            if (forms == null) forms = NewOwner();
            if (forms.Count > 0 && !forms.Contains(form)) return false;
            SharedFormRuntime.Release(form);
            // Release the live human-skill view immediately. This body is now
            // inactive, so direct consumers must see its own preserved records
            // both before saving and after a fresh native reload.
            (form.skills as SharedPawnSkillTracker)?.Bind();
            // Projections belong to the active identity, never to the dormant
            // body's saved health. The real drug state stays on the human.
            var hediffs = form.health != null ? form.health.hediffSet?.hediffs : null;
            if (hediffs != null)
                foreach (Hediff hediff in new List<Hediff>(hediffs))
                    if (hediff.def != null && hediff.def.defName == SharedFormHealth.ProjectionDefName) form.health.RemoveHediff(hediff);
            if (!FormTransferUtility.TryStore(form, forms)) return false;
            sourceKind = kind ?? transformed.sourceKind ?? form.kindDef;
            transformed.sourceKind = sourceKind;
            transformed.enemyCombatForm = false;
            transformed.enemyReturnPending = false;
            return true;
        }

        public static bool IsStored(Pawn form)
        {
            var owner = form != null ? form.ParentHolder as AnimalFormStorage : null;
            return owner != null && owner.StoredForm == form;
        }

        public static bool TryAdoptAwakened(Pawn form, Pawn human, PawnKindDef kind)
        {
            HediffComp_Shapeshifter shifter = TransformUtility.TryGetShapeshifterComp(human);
            if (shifter == null || shifter.FormStorage.HasStoredForm) return false;
            Hediff added = null;
            HediffComp_Transformed transformed = TransformUtility.TryGetTransformedComp(form);
            if (transformed == null)
            {
                added = HediffMaker.MakeHediff(WildShiftDefOf.WildShift_Transformed, form);
                form.health.AddHediff(added);
                transformed = added.TryGetComp<HediffComp_Transformed>();
            }
            if (transformed == null || !shifter.FormStorage.TryStore(form, kind))
            {
                // A rejected adoption must remain a valid latent animal so the
                // generated human can roll back and acquisition can retry.
                if (added != null) form.health.RemoveHediff(added);
                return false;
            }
            transformed.sourceKind = kind;
            Hediff latent = form.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
            if (latent != null)
            {
                try { form.health.RemoveHediff(latent); }
                catch (Exception ex)
                {
                    // Adoption already committed. The transformed comp also
                    // rejects latent awakening, so a failing marker callback
                    // cannot justify deleting this successfully preserved body.
                    Log.Error("[WildShift] Could not clear the consumed latent marker: " + ex);
                }
            }
            return true;
        }

        public ThingOwner GetDirectlyHeldThings() { return forms; }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref forms, "forms", this);
            Scribe_Defs.Look(ref sourceKind, "sourceKind");
            if (forms == null) forms = NewOwner();
            forms.dontTickContents = true;
        }
    }
}
