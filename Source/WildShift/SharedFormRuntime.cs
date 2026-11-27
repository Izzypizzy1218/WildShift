using HarmonyLib;
using System.Runtime.CompilerServices;
using Verse;

namespace WildShift
{
    public static class SharedFormRuntime
    {
        // Ordinary pawns never acquire a runtime binding. Avoid searching
        // their complete health list on both global Pawn tick callbacks.
        // Weak keys retain neither discarded bodies nor previous games.
        private static readonly ConditionalWeakTable<Pawn, object> initializedBodies
            = new ConditionalWeakTable<Pawn, object>();
        private static readonly object initialized = new object();

        private static object Initialized(Pawn ignored) { return initialized; }

        public static void Initialize(Pawn body)
        {
            if (body == null) return;
            // Reinitialization revokes the old registration first. Register
            // only after every module finishes against the same live owner.
            initializedBodies.Remove(body);
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            if (marker != null && ReferenceEquals(marker.runtimeBody, body)) marker.runtimeBody = null;
            Pawn human = marker == null ? null : SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
            if (human == null) return;
            try
            {
                SharedFormAnatomy.Invalidate(body);
                SharedFormIdentity.Initialize(body);
                SharedFormBiology.Initialize(body);
                SharedFormThoughts.Initialize(body);
                SharedFormSystems.Initialize(body);
                SharedFormGear.Initialize(body);
                // Projected health can trigger a native equipment drop.
                // Its unequip callback needs the active body's ability tracker.
                SharedFormAbilities.Initialize(body);
                SharedFormHealth.Initialize(body);
                if (SharedFormIdentity.HumanFor(body) == human)
                {
                    initializedBodies.GetValue(body, Initialized);
                    marker.runtimeBody = body;
                }
            }
            catch
            {
                initializedBodies.Remove(body);
                if (ReferenceEquals(marker.runtimeBody, body)) marker.runtimeBody = null;
                throw;
            }
        }

        public static void Release(Pawn body)
        {
            if (body == null) return;
            initializedBodies.Remove(body);
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            if (marker != null && ReferenceEquals(marker.runtimeBody, body)) marker.runtimeBody = null;
            SharedFormAnatomy.Invalidate(body);
            SharedFormAbilities.Release(body);
            SharedFormGear.Release(body);
            SharedFormSystems.Release(body);
            SharedFormHealth.Release(body);
            SharedFormBiology.Release(body);
        }

        public static void Tick(Pawn body)
        {
            if (!TransformUtility.MightBeTransformed(body)) return;
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            if (marker == null) return;
            if (!ReferenceEquals(marker.runtimeBody, body))
            {
                object registration;
                if (!initializedBodies.TryGetValue(body, out registration)) return;
                marker.runtimeBody = body;
            }
            Pawn human = SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
            if (human == null) return;
            SharedFormBiology.TickForSource(body, human, marker);
            SharedFormGear.TickForSource(body, human, marker);
        }

        public static void TickInterval(Pawn body, int delta)
        {
            if (!TransformUtility.MightBeTransformed(body)) return;
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            if (marker == null) return;
            if (!ReferenceEquals(marker.runtimeBody, body))
            {
                object registration;
                if (!initializedBodies.TryGetValue(body, out registration)) return;
                marker.runtimeBody = body;
            }
            Pawn human = SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
            if (human == null) return;
            SharedFormBiology.TickIntervalForSource(body, human, delta, marker);
            SharedFormSystems.TickForSource(body, human, delta, marker);
            SharedFormGear.TickIntervalForSource(body, human, delta, marker);
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_SharedFormRuntimeTick
    { public static void Postfix(Pawn __instance) { SharedFormRuntime.Tick(__instance); } }

    [HarmonyPatch(typeof(Pawn), "TickInterval")]
    public static class Patch_SharedFormRuntimeInterval
    { public static void Postfix(Pawn __instance, int delta) { SharedFormRuntime.TickInterval(__instance, delta); } }
}
