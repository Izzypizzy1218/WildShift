using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class SapientAnimalsCompatibility
    {
        private static readonly Type Helpers = AccessTools.TypeByName("BigAndSmall.HumanlikeAnimals");
        private static readonly MethodInfo Source = Helpers == null ? null : AccessTools.Method(Helpers, "AnimalSourceFor", new[] { typeof(ThingDef) });
        private static readonly MethodInfo Lookup = Helpers == null ? null : AccessTools.Method(Helpers, "GetHumanlikeAnimalFor", new[] { typeof(ThingDef) });
        private static readonly Type Entry = AccessTools.TypeByName("BigAndSmall.HumanlikeAnimal");
        private static readonly FieldInfo Kind = Entry == null ? null : AccessTools.Field(Entry, "animalKind");

        public static ThingDef AnimalSource(ThingDef race)
        {
            if (race?.race == null || !race.race.Humanlike || Source == null) return null;
            return Source.Invoke(null, new object[] { race }) as ThingDef;
        }

        public static PawnKindDef OriginalKind(Pawn pawn)
        {
            if (pawn?.RaceProps == null || !pawn.RaceProps.Humanlike || Lookup == null || Kind == null) return null;
            object entry = Lookup.Invoke(null, new object[] { pawn.def });
            var kind = entry == null ? null : Kind.GetValue(entry) as PawnKindDef;
            return kind?.race?.race != null && kind.race.race.Animal && !kind.race.race.IsMechanoid
                && !GeneticsCompatibility.Excluded(kind.race) ? kind : null;
        }

        public sealed class SwapState
        {
            public Pawn oldPawn;
            public PawnKindDef kind;
            public HediffComp_Transformed storage;
            public bool latent, selected, drafted;
            public GeneticFormState genetics;
        }

        public static void Install(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("BigAndSmall.RaceMorpher");
            if (type == null) return;
            var method = AccessTools.DeclaredMethod(type, "SwapAnimalToSapientVersion", new[] { typeof(Pawn) });
            if (method == null || method.ReturnType != typeof(Pawn))
            { Log.Warning("[WildShift] B&S sapient animal API was not found; compatibility needs verification."); return; }
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(SapientAnimalsCompatibility), nameof(BeforeSwap)),
                postfix: new HarmonyMethod(typeof(SapientAnimalsCompatibility), nameof(AfterSwap)));
        }

        public static bool BeforeSwap(Pawn __0, ref Pawn __result, out SwapState __state)
        {
            __state = null;
            if (__0 == null) return true;
            var storage = TransformUtility.TryGetTransformedComp(__0);
            bool latent = __0.health.hediffSet.HasHediff(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
            GeneticFormState genetics = GeneticsCompatibility.Capture(__0);
            if (storage == null && !latent && genetics == null) return true;
            // B&S replaces and destroys the source. Empty its apparel, equipment
            // and carried object too (the native routine only handles inventory).
            if (!__0.Spawned || !(storage?.StoredPawn != null
                ? FormPossessions.TryUnload(__0, storage.StoredPawn) : AwakeningPossessions.TryDropAll(__0)))
            {
                __result = __0;
                Messages.Message("WildShift_AwakenDropFailed".Translate(), __0, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            __state = new SwapState { oldPawn = __0, kind = __0.kindDef, storage = storage, latent = latent,
                selected = Find.Selector.IsSelected(__0), drafted = __0.Drafted, genetics = genetics };
            return true;
        }

        public static void AfterSwap(Pawn __result, SwapState __state)
        {
            if (__state == null || __result == null || __result == __state.oldPawn || __result.Destroyed) return;
            GeneticsCompatibility.Restore(__result, __state.genetics);
            if (__state.storage != null)
            {
                // B&S migrates the actual hediff; the ThingOwner remains attached
                // to the same comp, preserving the original pawn and its identity.
                if (TransformUtility.TryGetTransformedComp(__result) != __state.storage)
                { Log.Error("[WildShift] B&S did not migrate transformed storage as expected."); return; }
                TransformUtility.EnsureTransformedAnimalControl(__result, true);
                if (__state.drafted && __result.drafter != null) __result.drafter.Drafted = true;
                var owner = TransformUtility.TryGetShapeshifterComp(__state.storage.StoredPawn);
                if (owner != null && __state.genetics != null) owner.geneticForm = __state.genetics;
            }
            else if (__state.latent)
            {
                // Cogni-Fi already created a sapient identity. Keep that pawn;
                // never generate another human through the latent-animal button.
                var owner = TransformUtility.AddOrGetShapeshifter(__result, __state.kind);
                if (owner != null)
                {
                    owner.geneticForm = __state.genetics;
                    var latent = __result.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
                    if (latent != null) __result.health.RemoveHediff(latent);
                }
            }
            if (__state.selected) { Find.Selector.Deselect(__state.oldPawn); Find.Selector.Select(__result); }
            Patch_GameEnder.InvalidateCache();
            Find.ColonistBar?.MarkColonistsDirty();
        }
    }
}
