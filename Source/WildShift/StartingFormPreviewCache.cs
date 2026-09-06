using System.Runtime.CompilerServices;
using Verse;

namespace WildShift
{
    // Owned by one scenario part. Weak keys do not retain discarded reroll pawns.
    // Track only scenario-created markers; leaving the slot discards the form.
    public sealed class StartingFormPreviewCache
    {
        private sealed class Entry
        {
            public Hediff hediff;
        }

        private readonly ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();

        public void Activate(Pawn pawn)
        {
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter);
            if (existing != null)
            {
                return;
            }

            // No fixed scenario fallback: the ordinary pool must reroll too,
            // not just the racial preference branch. Redraws return above.
            HediffComp_Shapeshifter comp = TransformUtility.AddOrGetShapeshifter(pawn, null, true);
            if (comp != null)
            {
                entries.Remove(pawn);
                entries.Add(pawn, new Entry { hediff = comp.parent });
            }
        }

        public void Deactivate(Pawn pawn)
        {
            Entry entry;
            if (entries.TryGetValue(pawn, out entry)
                && pawn.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_Shapeshifter) == entry.hediff)
            {
                pawn.health.RemoveHediff(entry.hediff);
            }
            entries.Remove(pawn);
        }
    }
}
