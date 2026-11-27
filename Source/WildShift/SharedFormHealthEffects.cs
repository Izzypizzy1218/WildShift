using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch]
    public static class Patch_SharedHealthQueryCache
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PawnCapacitiesHandler), "GetLevel");
            yield return AccessTools.PropertyGetter(typeof(HediffSet), "PainTotal");
            yield return AccessTools.PropertyGetter(typeof(HediffSet), "BleedRateTotal");
            yield return AccessTools.Method(typeof(HediffSet), "FactorForDamage", new[] { typeof(DamageInfo) });
            yield return AccessTools.Method(typeof(HediffSet), "GetHungerRateFactor", new[] { typeof(HediffDef) });
            yield return AccessTools.Method(typeof(HediffSet), "TryGetNeedEnablingHediff");
            yield return AccessTools.Method(typeof(HediffSet), "TryGetNeedDisablingHediff");
            string[] properties = { "RestFallFactor", "OverrideMoodBase", "PreventVacuumBurns", "HasRegeneration", "RemoveRoamMtb" };
            for (int i=0;i<properties.Length;i++)
            {
                MethodInfo getter=AccessTools.PropertyGetter(typeof(HediffSet),properties[i]);
                if(getter!=null)yield return getter;
            }
        }
        public static void Prefix(Pawn ___pawn) { SharedFormHealth.PrepareHealthFor(___pawn); }
    }

    // Component objects are canonical source objects. Native component getters
    // must see the currently occupied body without changing their saved owner.
    [HarmonyPatch(typeof(HediffComp), "get_Pawn")]
    public static class Patch_SharedHealthComponentPawn
    {
        public static void Postfix(HediffComp __instance, ref Pawn __result)
        {
            Pawn body = SharedFormIdentity.ActiveFormFor(__result);
            if (body != null && SharedFormHealth.IsSupported(__instance.parent)) __result = body;
        }
    }

    [HarmonyPatch(typeof(HediffComp_GiveHediffsInRange), "CompPostTick")]
    public static class Patch_SharedHealthCommandRecipients
    {
        private sealed class Owner { public Owner() { } public Pawn pawn; }
        private static readonly ConditionalWeakTable<HediffComp_GiveHediffsInRange, Owner> owners
            = new ConditionalWeakTable<HediffComp_GiveHediffsInRange, Owner>();
        private static readonly AccessTools.FieldRef<HediffComp_GiveHediffsInRange, Mote> mote
            = AccessTools.FieldRefAccess<HediffComp_GiveHediffsInRange, Mote>("mote");
        public static void Prefix(HediffComp_GiveHediffsInRange __instance)
        {
            Pawn body = __instance.parent?.pawn;
            if (body == null) return;
            Owner previous;
            if (!owners.TryGetValue(__instance, out previous))
            {
                if (SharedFormIdentity.HumanFor(body) == null) return;
                previous = owners.GetOrCreateValue(__instance);
            }
            if (previous.pawn == body) return;
            Mote old = mote(__instance);
            if (old != null && !old.Destroyed) old.Destroy();
            mote(__instance) = null;
            previous.pawn = body;
        }
        public static void Postfix(HediffComp_GiveHediffsInRange __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            Pawn source = __instance.parent?.pawn;
            if (source?.health == null || !source.Spawned || !source.Awake() || source.health.InPainShock) return;
            Map map = source.Map;
            if (map == null) return;
            Pawn identity = SharedFormIdentity.HumanFor(source) ?? source;
            bool hasSpawnedForms = SharedFormSystems.HasSpawnedBodyOn(map);
            if (identity == source && !hasSpawnedForms) return;

            HediffCompProperties_GiveHediffsInRange props = __instance.Props;
            Faction faction = props.onlyPawnsInSameFaction ? source.Faction : null;
            if (hasSpawnedForms)
            {
                // Borrow a fresh pooled buffer per invocation so reentrant aura
                // callbacks from third-party AddHediff hooks cannot clobber the
                // outer recipient list while iterating.
                List<Pawn> candidates = SimplePool<List<Pawn>>.Get();
                try
                {
                    TransformUtility.FillSpawnedTransformedOn(map, faction, candidates);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Pawn body = candidates[i];
                        if (body == null || body == source || body.health == null || body.Dead
                            || body.Position.DistanceTo(source.Position) > props.range) continue;
                        Pawn human = SharedFormIdentity.HumanFor(body);
                        if (human == null
                            || (props.targetingParameters != null && !props.targetingParameters.CanTarget(human))
                            || !body.Spawned || body.Map != map) continue;
                        if (!source.Spawned || source.Map != map) return;
                        Hediff buff = body.health.hediffSet.GetFirstHediffOfDef(props.hediff);
                        if (buff == null)
                        {
                            buff = body.health.AddHediff(props.hediff, body.health.hediffSet.GetBrain());
                            if (!source.Spawned || source.Map != map) return;
                            if (buff == null || !body.Spawned || body.Map != map) continue;
                            buff.Severity = props.initialSeverity;
                        }
                        HediffComp_Link link = buff.TryGetComp<HediffComp_Link>();
                        if (link != null) { link.other = identity; link.drawConnection = true; }
                        HediffComp_Disappears expiration = buff.TryGetComp<HediffComp_Disappears>();
                        if (expiration != null) expiration.ticksToDisappear = 5;
                    }
                }
                finally
                {
                    candidates.Clear();
                    SimplePool<List<Pawn>>.Return(candidates);
                }
            }
            // Vanilla recipients also retain the source identity, so a link
            // follows a later return instead of pointing at a cached animal.
            if (identity != source)
            {
                IReadOnlyList<Pawn> pawns = faction != null
                    ? map.mapPawns.SpawnedPawnsInFaction(faction) : map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p == source || p.Dead || p.health == null
                        || p.Position.DistanceTo(source.Position) > props.range) continue;
                    List<Hediff> hediffs = p.health.hediffSet.hediffs;
                    for (int j = 0; j < hediffs.Count; j++)
                    {
                        Hediff buff = hediffs[j];
                        if (buff.def != props.hediff) continue;
                        HediffComp_Link link = buff.TryGetComp<HediffComp_Link>();
                        if (link != null && link.other == source) link.other = identity;
                        break;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(HediffComp_Link), "get_CompShouldRemove")]
    public static class Patch_SharedHealthLink
    {
        public static bool Prefix(HediffComp_Link __instance, ref bool __result)
        {
            Pawn owner = __instance.parent?.pawn;
            Pawn other = __instance.other as Pawn;
            if (other == null) return true;
            Pawn body = SharedFormIdentity.ActiveFormFor(owner) ?? owner;
            Pawn otherBody = SharedFormIdentity.ActiveFormFor(other) ?? other;
            Pawn identity = SharedFormIdentity.HumanFor(body) ?? owner;
            Pawn otherIdentity = SharedFormIdentity.HumanFor(otherBody) ?? other;
            if (body == owner && otherBody == other && identity == owner && otherIdentity == other) return true;
            if (body == null || otherBody == null || !body.SpawnedOrAnyParentSpawned || !otherBody.SpawnedOrAnyParentSpawned
                || (__instance.Props.maxDistance > 0f && !body.PositionHeld.InHorDistOf(otherBody.PositionHeld, __instance.Props.maxDistance)))
            { __result = true; return false; }
            if (!__instance.Props.requireLinkOnOtherPawn) { __result = false; return false; }
            List<Hediff> effects = otherIdentity.health.hediffSet.hediffs;
            for (int i = 0; i < effects.Count; i++)
            {
                HediffComp_Link reciprocal = effects[i].TryGetComp<HediffComp_Link>();
                Pawn reciprocalPawn = reciprocal?.other as Pawn;
                Pawn reciprocalIdentity = SharedFormIdentity.HumanFor(reciprocalPawn) ?? reciprocalPawn;
                if (reciprocal != null && reciprocalIdentity == identity && reciprocal.parent.def == __instance.parent.def)
                { __result = false; return false; }
            }
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffComp_Link), "CompPostTick")]
    public static class Patch_SharedHealthLinkVisual
    {
        private sealed class Targets
        {
            public Targets() { }
            public Pawn owner;
            public Thing other;
        }
        public struct LinkRestore
        {
            internal bool Applied;
            internal Thing Original;
            internal Thing Temporary;
        }
        private static readonly ConditionalWeakTable<HediffComp_Link, Targets> targets = new ConditionalWeakTable<HediffComp_Link, Targets>();
        private static readonly AccessTools.FieldRef<HediffComp_Link, MoteDualAttached> mote
            = AccessTools.FieldRefAccess<HediffComp_Link, MoteDualAttached>("mote");
        public static void Prefix(HediffComp_Link __instance, out LinkRestore __state)
        {
            __state = default;
            Pawn owner = __instance.parent?.pawn;
            if (owner == null) return;
            Thing original = __instance.other;
            Thing visibleOther = SharedFormIdentity.ActiveFormFor(original as Pawn) ?? original;
            Targets previous;
            bool existing = targets.TryGetValue(__instance, out previous);
            if (!existing && visibleOther == original && SharedFormIdentity.HumanFor(owner) == null) return;
            if (!existing) previous = targets.GetOrCreateValue(__instance);
            if (previous.owner != owner || previous.other != visibleOther)
            {
                MoteDualAttached old = mote(__instance);
                if (old != null && !old.Destroyed) old.Destroy();
                mote(__instance) = null;
                previous.owner = owner; previous.other = visibleOther;
            }
            if (visibleOther == original) return;
            __state = new LinkRestore { Applied = true, Original = original, Temporary = visibleOther };
            __instance.other = visibleOther;
        }
        public static void Finalizer(HediffComp_Link __instance, LinkRestore __state)
        {
            if (__state.Applied && ReferenceEquals(__instance.other, __state.Temporary))
                __instance.other = __state.Original;
        }
    }

    internal static class SharedHealthDefinitionFlags
    {
        public static bool Any(Pawn body, Predicate<HediffDef> predicate)
        {
            Pawn human = SharedFormIdentity.HumanFor(body);
            if (human?.health == null) return false;
            List<Hediff> sources = human.health.hediffSet.hediffs;
            for (int i = 0; i < sources.Count; i++)
            {
                Hediff h = sources[i];
                if (h?.def != null && predicate(h.def) && SharedFormHealth.IsSupported(h)) return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(HediffSet), "get_HasPreventsDeath")]
    public static class Patch_SharedHealthPreventsDeath
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        { if (!__result) __result = SharedHealthDefinitionFlags.Any(___pawn, def => def.preventsDeath); }
    }

    [HarmonyPatch(typeof(HediffSet), "HasHediffBlocksSleeping")]
    public static class Patch_SharedHealthBlocksSleeping
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        { if (!__result) __result = SharedHealthDefinitionFlags.Any(___pawn, def => def.blocksSleeping); }
    }

    [HarmonyPatch(typeof(HediffSet), "get_AnyHediffPreventsCrawling")]
    public static class Patch_SharedHealthPreventsCrawling
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        { if (!__result) __result = SharedHealthDefinitionFlags.Any(___pawn, def => def.preventsCrawling); }
    }

    [HarmonyPatch(typeof(HediffSet), "HasHediffPreventsPregnancy")]
    public static class Patch_SharedHealthPreventsPregnancy
    {
        public static void Postfix(Pawn ___pawn, ref bool __result)
        { if (!__result) __result = SharedHealthDefinitionFlags.Any(___pawn, def => def.preventsPregnancy); }
    }
}
