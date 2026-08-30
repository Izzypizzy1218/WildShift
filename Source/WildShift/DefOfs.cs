using RimWorld;
using Verse;

namespace WildShift
{
    [DefOf]
    public static class WildShiftDefOf
    {
        public static HediffDef WildShift_Shapeshifter;
        public static HediffDef WildShift_Transformed;
        public static HediffDef WildShift_FieldShapeshifterMarker;
        public static AbilityDef WildShift_Shift;
        public static IncidentDef WildShift_ShapeshifterJoin;

        static WildShiftDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WildShiftDefOf));
        }
    }
}
