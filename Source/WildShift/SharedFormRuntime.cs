using HarmonyLib;
using Verse;

namespace WildShift
{
    public static class SharedFormRuntime
    {
        public static void Initialize(Pawn body)
        {
            if (SharedFormIdentity.HumanFor(body) == null) return;
            SharedFormIdentity.Initialize(body);
            SharedFormBiology.Initialize(body);
            SharedFormThoughts.Initialize(body);
            SharedFormSystems.Initialize(body);
            SharedFormGear.Initialize(body);
            SharedFormHealth.Initialize(body);
            SharedFormAbilities.Initialize(body);
        }

        public static void Release(Pawn body)
        {
            if (body == null) return;
            SharedFormAbilities.Release(body);
            SharedFormGear.Release(body);
            SharedFormSystems.Release(body);
            SharedFormHealth.Release(body);
            SharedFormBiology.Release(body);
        }

        public static void Tick(Pawn body)
        {
            if (SharedFormIdentity.HumanFor(body) == null) return;
            SharedFormBiology.Tick(body);
            SharedFormGear.Tick(body);
        }

        public static void TickInterval(Pawn body, int delta)
        {
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
