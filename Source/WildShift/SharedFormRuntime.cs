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
            Pawn human = SharedFormIdentity.HumanFor(body);
            if (human == null) return;
            try
            {
                SharedFormIdentity.Initialize(body);
                SharedFormBiology.Initialize(body);
                SharedFormThoughts.Initialize(body);
                SharedFormSystems.Initialize(body);
                SharedFormGear.Initialize(body);
                SharedFormHealth.Initialize(body);
                SharedFormAbilities.Initialize(body);
                if (SharedFormIdentity.HumanFor(body) == human)
                    initializedBodies.GetValue(body, Initialized);
            }
            catch
            {
                initializedBodies.Remove(body);
                throw;
            }
        }

        public static void Release(Pawn body)
        {
            if (body == null) return;
            initializedBodies.Remove(body);
            SharedFormAbilities.Release(body);
            SharedFormGear.Release(body);
            SharedFormSystems.Release(body);
            SharedFormHealth.Release(body);
            SharedFormBiology.Release(body);
        }

        public static void Tick(Pawn body)
        {
            object registration;
            if (body == null || !initializedBodies.TryGetValue(body, out registration)) return;
            if (SharedFormIdentity.HumanFor(body) == null) return;
            SharedFormBiology.Tick(body);
            SharedFormGear.Tick(body);
        }

        public static void TickInterval(Pawn body, int delta)
        {
            object registration;
            if (body == null || !initializedBodies.TryGetValue(body, out registration)) return;
            if (SharedFormIdentity.HumanFor(body) == null) return;
            SharedFormBiology.TickInterval(body, delta);
            SharedFormSystems.Tick(body, delta);
            SharedFormGear.TickInterval(body, delta);
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_SharedFormRuntimeTick
    { public static void Postfix(Pawn __instance) { SharedFormRuntime.Tick(__instance); } }

    [HarmonyPatch(typeof(Pawn), "TickInterval")]
    public static class Patch_SharedFormRuntimeInterval
    { public static void Postfix(Pawn __instance, int delta) { SharedFormRuntime.TickInterval(__instance, delta); } }
}
