using System.Runtime.CompilerServices;
using Verse;

namespace WildShift
{
    // Owned by one scenario part. Weak keys do not retain discarded reroll pawns.
    // Retain the original hediff rather than rolling a new form on slot changes.
    public sealed class StartingFormPreviewCache
    {
        private sealed class Entry
        {
            public Hediff hediff;
            public bool addedByScenario;
        }

        private readonly ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();

        public void Activate(Pawn pawn, PawnKindDef fallback)
        {
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            Entry entry;
            if (existing != null)
            {
                return;
            }

            if (entries.TryGetValue(pawn, out entry))
            {
                pawn.health.AddHediff(entry.hediff);
                return;
            }

            HediffComp_Shapeshifter comp = TransformUtility.AddOrGetShapeshifter(pawn, fallback, true);
            if (comp != null)
            {
                entries.Add(pawn, new Entry { hediff = comp.parent, addedByScenario = true });
            }
        }

        public void Deactivate(Pawn pawn)
        {
            Entry entry;
            if (entries.TryGetValue(pawn, out entry) && entry.addedByScenario
                && pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter) == entry.hediff)
            {
                pawn.health.RemoveHediff(entry.hediff);
            }
        }
    }
}
