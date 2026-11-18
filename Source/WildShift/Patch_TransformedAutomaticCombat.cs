using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Keep automatic attacks on the colonist policy for controlled animal forms.
    // The native automatic entry points use !IsColonist to permit manual-only
    // equipment/apparel on animals. A player person's animal form should use
    // the colonist policy here while explicit verb orders remain untouched.
    public static class TransformedAutomaticCombat
    {
        [ThreadStatic] private static Pawn automaticActor;

        // Harmony can run a finalizer even when an earlier foreign prefix
        // skipped or threw before ours. A default state has nothing to undo.
        public struct ScopeState
        {
            public Pawn previous;
            public bool entered;
        }

        public static void Enter(Pawn actor, out Pawn previous)
        {
            // Assign the saved state before eligibility checks. A finalizer
            // can therefore restore the outer context even if a mod throws.
            previous = automaticActor;
            automaticActor = null;
            if (IsControlledForm(actor)) automaticActor = actor;
        }

        public static void Restore(Pawn previous)
        {
            automaticActor = previous;
        }

        public static void Filter(Pawn actor, ref bool allowManualCastWeapons)
        {
            if (automaticActor != null && ReferenceEquals(actor, automaticActor))
                allowManualCastWeapons = false;
        }

        private static bool IsControlledForm(Pawn actor)
        {
            // Match WildShift's player-control eligibility without querying
            // the patched IsColonistPlayerControlled getter recursively.
            // Requiring a valid live original excludes an obsolete shell.
            return actor != null && !actor.Destroyed && actor.health != null && !actor.Dead
                && actor.Spawned && actor.Faction?.IsPlayer == true
                && actor.RaceProps?.Animal == true && actor.MentalStateDef == null
                && SharedFormIdentity.HumanFor(actor) != null;
        }
    }

    [HarmonyPatch(typeof(Pawn), "get_CurrentEffectiveVerb")]
    public static class Patch_TransformedAutomaticEffectiveVerb
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn __instance, out TransformedAutomaticCombat.ScopeState __state)
        {
            __state = new TransformedAutomaticCombat.ScopeState { entered = true };
            // Enter writes previous before any eligibility getter can throw.
            TransformedAutomaticCombat.Enter(__instance, out __state.previous);
        }

        public static void Finalizer(TransformedAutomaticCombat.ScopeState __state)
        {
            if (__state.entered) TransformedAutomaticCombat.Restore(__state.previous);
        }
    }

    [HarmonyPatch(typeof(Pawn), "TryStartAttack", new[] { typeof(LocalTargetInfo) })]
    public static class Patch_TransformedAutomaticStartAttack
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn __instance, out TransformedAutomaticCombat.ScopeState __state)
        {
            __state = new TransformedAutomaticCombat.ScopeState { entered = true };
            TransformedAutomaticCombat.Enter(__instance, out __state.previous);
        }

        public static void Finalizer(TransformedAutomaticCombat.ScopeState __state)
        {
            if (__state.entered) TransformedAutomaticCombat.Restore(__state.previous);
        }
    }

    [HarmonyPatch(typeof(Pawn), "TryGetAttackVerb", new[] { typeof(Thing), typeof(bool), typeof(bool) })]
    public static class Patch_TransformedAutomaticVerbSelection
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn __instance, ref bool allowManualCastWeapons)
        {
            TransformedAutomaticCombat.Filter(__instance, ref allowManualCastWeapons);
        }
    }
}
