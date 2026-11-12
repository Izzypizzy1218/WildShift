using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WildShift
{
    // Abilities remain deep-owned by the human. The active body's tracker only
    // borrows those exact objects; no second cooldown, charge or comp state exists.
    public static class SharedFormAbilities
    {
        private sealed class Binding
        {
            public Pawn human;
            public Pawn body;
            public int lastTick = int.MinValue;
        }

        private sealed class ResourceClock { public int lastTick; public Gene_Hemogen source; }
        private static readonly ConditionalWeakTable<Ability, Binding> bindings = new ConditionalWeakTable<Ability, Binding>();
        private static readonly ConditionalWeakTable<Pawn, ResourceClock> resourceClocks = new ConditionalWeakTable<Pawn, ResourceClock>();
        private static readonly FieldInfo cachedGizmo = AccessTools.Field(typeof(Ability), "gizmo");

        internal static int Now { get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; } }

        public static void Initialize(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null || human.abilities == null) return;
            SharedPawnAbilityTracker proxy = animal.abilities as SharedPawnAbilityTracker;
            if (proxy == null || !proxy.IsForBody(animal))
            {
                proxy = new SharedPawnAbilityTracker(animal, animal.abilities);
                animal.abilities = proxy;
            }
            Gene_Hemogen resource = HemogenFor(human);
            ResourceClock clock = resourceClocks.GetValue(human, delegate(Pawn ignored) { return new ResourceClock { lastTick = Now }; });
            if (clock.source != resource) { clock.source = resource; clock.lastTick = Now; }
            proxy.Bind();
        }

        // Call before the human is released from its owner, after stopping the
        // body's jobs. Also works with a detached human during a failed transfer.
        public static void Release(Pawn animal)
        {
            SharedPawnAbilityTracker proxy = animal != null ? animal.abilities as SharedPawnAbilityTracker : null;
            if (proxy != null && proxy.IsForBody(animal)) proxy.Release();
        }

        public static bool IsSupported(Ability ability)
        {
            if (ability == null || ability.def == null || ability.GetType().Assembly != typeof(Ability).Assembly) return false;
            AbilityDef def = ability.def;
            if (def.gizmoClass != null && def.gizmoClass.Assembly != typeof(Command_Ability).Assembly) return false;
            Type verb = def.verbProperties != null ? def.verbProperties.verbClass : null;
            if (verb == null || !typeof(Verb).IsAssignableFrom(verb) || verb.Assembly != typeof(Ability).Assembly) return false;
            // A failed or newly injected custom comp must not bypass the gate.
            int actual = ability.comps != null ? ability.comps.Count : 0;
            if (actual != (def.comps != null ? def.comps.Count : 0)) return false;
            for (int i = 0; i < actual; i++)
                if (ability.comps[i] == null || ability.comps[i].GetType().Assembly != typeof(Ability).Assembly) return false;
            return true;
        }

        public static Pawn HumanFor(Ability ability)
        {
            Binding binding;
            return ability != null && bindings.TryGetValue(ability, out binding)
                && binding.body == ability.pawn && SharedFormIdentity.HumanFor(binding.body) == binding.human
                ? binding.human : null;
        }

        internal static void BindAbility(Ability ability, Pawn human, Pawn body)
        {
            Binding binding = bindings.GetValue(ability, delegate(Ability ignored) { return new Binding(); });
            binding.human = human;
            binding.body = body;
            SetCaster(ability, body);
        }

        internal static void SetCaster(Ability ability, Pawn body)
        {
            bool changed = ability.pawn != body;
            ability.pawn = body;
            List<Verb> verbs = ability.VerbTracker.AllVerbs;
            for (int i = 0; i < verbs.Count; i++) verbs[i].caster = body;
            // Native Command_Ability captures its Pawn in the constructor.
            if (changed && cachedGizmo != null) cachedGizmo.SetValue(ability, null);
        }

        internal static void UnbindAbility(Ability ability, Pawn body)
        {
            Binding binding;
            if (!bindings.TryGetValue(ability, out binding) || binding.body != body) return;
            if (body.jobs != null)
            {
                body.jobs.jobQueue.RemoveAll(body, delegate(Job job) { return job.ability == ability; });
                if (body.CurJob != null && body.CurJob.ability == ability)
                    body.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
            SetCaster(ability, binding.human);
            bindings.Remove(ability);
        }

        internal static bool CanTick(Ability ability)
        {
            Binding binding;
            if (!bindings.TryGetValue(ability, out binding) || HumanFor(ability) == null) return true;
            if (binding.lastTick == Now) return false;
            binding.lastTick = Now;
            return true;
        }

        public static Gene_Hemogen HemogenFor(Pawn human)
        {
            Gene_Hemogen gene = human != null && human.genes != null ? human.genes.GetFirstGeneOfType<Gene_Hemogen>() : null;
            Gene_HemogenDrain drain = human != null && human.genes != null ? human.genes.GetFirstGeneOfType<Gene_HemogenDrain>() : null;
            return gene != null && gene.GetType() == typeof(Gene_Hemogen) && gene.Active
                && (drain == null || drain.GetType() == typeof(Gene_HemogenDrain)) ? gene : null;
        }

        public static void MarkDirty(Pawn human)
        {
            Pawn body = SharedFormIdentity.ActiveFormFor(human);
            SharedPawnAbilityTracker proxy = body != null ? body.abilities as SharedPawnAbilityTracker : null;
            if (proxy != null) proxy.Invalidate();
        }

        internal static bool HasActiveSource(Ability ability, Pawn human)
        {
            if (human?.abilities == null || !human.abilities.AllAbilitiesForReading.Contains(ability)) return false;
            // Temporary role, implant and apparel abilities remain owned by
            // their original native provider. Only gene-owned permanent entries
            // need the additional suppression check used by the old bridge.
            if (!human.abilities.abilities.Contains(ability) || human.genes == null) return true;
            List<Gene> genes = human.genes.GenesListForReading;
            bool geneGranted = false;
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i].def.abilities == null || !genes[i].def.abilities.Contains(ability.def)) continue;
                geneGranted = true;
                if (genes[i].Active) return true;
            }
            return !geneGranted;
        }

        internal static bool MeetsHumanViolenceRequirement(Ability ability, Pawn human)
        {
            return !ability.def.casterMustBeCapableOfViolence || !human.WorkTagIsDisabled(WorkTags.Violent);
        }

        internal static void TickHemogen(Pawn animal)
        {
            Pawn human = SharedFormIdentity.HumanFor(animal);
            if (human == null) return;
            Gene_Hemogen resource = HemogenFor(human);
            // Keep this factory noncapturing: the tracker calls this path every
            // tick, even when an existing clock has no hemogen source.
            ResourceClock clock = resourceClocks.GetValue(human, delegate(Pawn ignored) { return new ResourceClock { lastTick = Now }; });
            if (clock.source != resource) { clock.source = resource; clock.lastTick = Now; }
            if (resource == null) { clock.lastTick = Now; return; }
            int elapsed = Now - clock.lastTick;
            if (elapsed <= 0) return;
            clock.lastTick = Now;
            List<Gene> genes = human.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
                if (IsNativeDrain(genes[i]) && genes[i].Active)
                    GeneResourceDrainUtility.TickResourceDrainInterval((IGeneResourceDrain)genes[i], elapsed);
        }

        internal static bool IsNativeDrain(Gene gene)
        {
            return gene != null && (gene.GetType() == typeof(Gene_Hemogen) || gene.GetType() == typeof(Gene_HemogenDrain));
        }

        internal static void ResetResourceClock(Pawn human)
        {
            if (human != null) resourceClocks.Remove(human);
        }

        internal static float QueuedHemogen(Pawn body)
        {
            float cost = 0f;
            if (body.jobs == null) return cost;
            Verb_CastAbility current = body.CurJob != null ? body.CurJob.verbToUse as Verb_CastAbility : null;
            if (current != null && current.ability != null) cost += current.ability.HemogenCost();
            for (int i = 0; i < body.jobs.jobQueue.Count; i++)
            {
                Verb_CastAbility verb = body.jobs.jobQueue[i].job.verbToUse as Verb_CastAbility;
                if (verb != null && verb.ability != null) cost += verb.ability.HemogenCost();
            }
            return cost;
        }
    }

    public sealed class SharedPawnAbilityTracker : Pawn_AbilityTracker, IExposable
    {
        private readonly Pawn activeBody;
        private Pawn_AbilityTracker bodyAbilities;
        private Pawn sourceHuman;
        private readonly List<Ability> borrowed = new List<Ability>();
        private readonly List<Ability> next = new List<Ability>();
        private readonly List<Ability> merged = new List<Ability>();
        private readonly List<Ability> nativeSnapshot = new List<Ability>();
        private readonly HashSet<AbilityDef> sourceDefs = new HashSet<AbilityDef>();
        private int refreshedTick = int.MinValue;
        private bool refreshing;
        private bool mergeDirty = true;

        public SharedPawnAbilityTracker(Pawn pawn) : this(pawn, null) { }

        public SharedPawnAbilityTracker(Pawn pawn, Pawn_AbilityTracker previous) : base(pawn)
        {
            activeBody = pawn;
            SharedPawnAbilityTracker old = previous as SharedPawnAbilityTracker;
            bodyAbilities = old != null ? old.bodyAbilities : previous;
            if (bodyAbilities == null) bodyAbilities = new Pawn_AbilityTracker(pawn);
            if (bodyAbilities.pawn != pawn)
            {
                Pawn oldBody = bodyAbilities.pawn;
                bodyAbilities.pawn = pawn;
                for (int i = 0; i < bodyAbilities.abilities.Count; i++)
                    if (bodyAbilities.abilities[i].pawn == oldBody) SharedFormAbilities.SetCaster(bodyAbilities.abilities[i], pawn);
            }
            // The serialized native list never contains borrowed abilities.
            abilities = bodyAbilities.abilities;
        }

        public bool IsForBody(Pawn pawn) { return activeBody == pawn; }

        public void Bind() { refreshedTick = int.MinValue; Refresh(); }
        internal void Invalidate() { refreshedTick = int.MinValue; }

        private void Refresh()
        {
            if (refreshing || refreshedTick == SharedFormAbilities.Now) return;
            refreshing = true;
            try
            {
                Pawn human = SharedFormIdentity.HumanFor(activeBody);
                next.Clear();
                sourceDefs.Clear();
                if (human != null && human.abilities != null)
                {
                    List<Ability> sources = human.abilities.AllAbilitiesForReading;
                    for (int i = 0; i < sources.Count; i++)
                    {
                        Ability ability = sources[i];
                        if (ability?.def == null || sourceDefs.Contains(ability.def)
                            || !SharedFormAbilities.IsSupported(ability) || !SharedFormAbilities.HasActiveSource(ability,human)) continue;
                        bool needsHemogen = ability.CompOfType<CompAbilityEffect_HemogenCost>() != null
                            || ability.CompOfType<CompAbilityEffect_BloodfeederBite>() != null;
                        if (needsHemogen && SharedFormAbilities.HemogenFor(human) == null) continue;
                        sourceDefs.Add(ability.def);
                        next.Add(ability);
                    }
                }
                for (int i = 0; i < borrowed.Count; i++)
                    if (!next.Contains(borrowed[i])) SharedFormAbilities.UnbindAbility(borrowed[i], activeBody);
                bool changed = borrowed.Count != next.Count;
                for (int i = 0; i < borrowed.Count && !changed; i++) changed = borrowed[i] != next[i];
                if (changed) { borrowed.Clear(); borrowed.AddRange(next); mergeDirty = true; }
                sourceHuman = human;
                for (int i = 0; i < borrowed.Count; i++) SharedFormAbilities.BindAbility(borrowed[i], human, activeBody);
                refreshedTick = SharedFormAbilities.Now;
            }
            finally { refreshing = false; }
        }

        internal List<Ability> Merge(List<Ability> native)
        {
            Refresh();
            bool changed = nativeSnapshot.Count != native.Count;
            for (int i = 0; i < native.Count && !changed; i++) changed = nativeSnapshot[i] != native[i];
            if (!changed && !mergeDirty) return merged;
            // Native callers can enumerate a list while another ability queries
            // the same tracker. Do not mutate an unchanged cached list on reads.
            nativeSnapshot.Clear();
            nativeSnapshot.AddRange(native);
            merged.Clear();
            for (int i = 0; i < native.Count; i++)
                if (!sourceDefs.Contains(native[i].def)) merged.Add(native[i]);
            for (int i = 0; i < borrowed.Count; i++) if (!merged.Contains(borrowed[i])) merged.Add(borrowed[i]);
            mergeDirty = false;
            return merged;
        }

        internal Ability BorrowedAbility(AbilityDef def)
        {
            Refresh();
            return FirstWithDef(borrowed, def);
        }

        private static Ability FirstWithDef(List<Ability> list, AbilityDef def)
        {
            // Preserve List.Find's first-match behavior without allocating a
            // captured predicate on every native tracker refresh or lookup.
            for (int i = 0; i < list.Count; i++)
                if (list[i].def == def) return list[i];
            return null;
        }

        public void Release()
        {
            for (int i = 0; i < borrowed.Count; i++) SharedFormAbilities.UnbindAbility(borrowed[i], activeBody);
            SharedFormAbilities.ResetResourceClock(sourceHuman);
            borrowed.Clear(); next.Clear(); merged.Clear(); nativeSnapshot.Clear(); sourceDefs.Clear();
            sourceHuman = null;
            refreshedTick = int.MinValue;
            mergeDirty = true;
            Notify_TemporaryAbilitiesChanged();
        }

        public new void ExposeData()
        {
            // Deep-save only the real animal's own tracker, including custom
            // tracker state. The human already deep-saves all borrowed abilities.
            Scribe_Deep.Look(ref bodyAbilities, "bodyAbilities", activeBody);
            if (bodyAbilities == null || bodyAbilities is SharedPawnAbilityTracker)
                bodyAbilities = new Pawn_AbilityTracker(activeBody);
            abilities = bodyAbilities.abilities;
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { refreshedTick = int.MinValue; mergeDirty = true; }
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), "get_AllAbilitiesForReading")]
    public static class Patch_SharedAbilityList
    {
        public static void Postfix(Pawn_AbilityTracker __instance, ref List<Ability> __result)
        {
            SharedPawnAbilityTracker proxy = __instance as SharedPawnAbilityTracker;
            if (proxy != null) __result = proxy.Merge(__result);
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), "GetAbility")]
    public static class Patch_SharedAbilityLookup
    {
        public static void Postfix(Pawn_AbilityTracker __instance, AbilityDef def, ref Ability __result)
        {
            SharedPawnAbilityTracker proxy = __instance as SharedPawnAbilityTracker;
            Ability source = proxy != null ? proxy.BorrowedAbility(def) : null;
            if (source != null) __result = source;
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), "AbilitiesTick")]
    public static class Patch_SharedAbilityResourceClock
    {
        public static void Postfix(Pawn_AbilityTracker __instance)
        {
            if (__instance is SharedPawnAbilityTracker) SharedFormAbilities.TickHemogen(__instance.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_AbilityTracker), "GetGizmos")]
    public static class Patch_SharedHemogenGizmo
    {
        public static void Postfix(Pawn_AbilityTracker __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance is SharedPawnAbilityTracker) __result = WithResource(__result, __instance.pawn);
        }

        private static IEnumerable<Gizmo> WithResource(IEnumerable<Gizmo> native, Pawn body)
        {
            foreach (Gizmo gizmo in native) yield return gizmo;
            Gene_Hemogen gene = SharedFormAbilities.HemogenFor(SharedFormIdentity.HumanFor(body));
            if (gene != null) foreach (Gizmo gizmo in gene.GetGizmos()) yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(Ability), "AbilityTick")]
    public static class Patch_SharedAbilitySingleTick
    {
        public static bool Prefix(Ability __instance) { return SharedFormAbilities.CanTick(__instance); }
    }

    [HarmonyPatch(typeof(Gene), "OverrideBy")]
    public static class Patch_SharedAbilityGeneSuppression
    {
        public static void Postfix(Gene __instance) { SharedFormAbilities.MarkDirty(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Pawn_GeneTracker), "Notify_GenesChanged")]
    public static class Patch_SharedAbilityGenesChanged
    {
        public static void Postfix(Pawn_GeneTracker __instance) { SharedFormAbilities.MarkDirty(__instance.pawn); }
    }

    [HarmonyPatch(typeof(Ability), "get_CanCast")]
    public static class Patch_SharedAbilityActiveSource
    {
        public static void Postfix(Ability __instance, ref AcceptanceReport __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance);
            if (human != null && (!SharedFormAbilities.HasActiveSource(__instance, human)
                || !SharedFormAbilities.MeetsHumanViolenceRequirement(__instance, human))) __result = false;
        }
    }

    [HarmonyPatch(typeof(Ability), "GizmoDisabled")]
    public static class Patch_SharedAbilityHumanViolence
    {
        public static bool Prefix(Ability __instance, ref string reason, ref bool __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance);
            if (human == null || SharedFormAbilities.MeetsHumanViolenceRequirement(__instance, human)) return true;
            reason = "IsIncapableOfViolence".Translate(human.LabelShort, human);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Ability), "Activate", new Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
    public static class Patch_SharedAbilityCastSource
    {
        public static bool Prefix(Ability __instance, ref bool __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance);
            if (human == null) return true;
            Gene_Hemogen gene = SharedFormAbilities.HemogenFor(human);
            if (SharedFormAbilities.HasActiveSource(__instance, human)
                && SharedFormAbilities.MeetsHumanViolenceRequirement(__instance, human)
                && (__instance.HemogenCost() <= 0f || (gene != null && gene.Value >= __instance.HemogenCost()))) return true;
            __result = false;
            return false;
        }
    }

    // Psycast overrides spend focus before invoking Ability.Activate. Validate
    // the canonical provider before any derived native implementation can spend
    // resources, including world-targeted casts such as farskip.
    [HarmonyPatch]
    public static class Patch_SharedAbilityDerivedCastSource
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(Ability).Assembly.GetTypes())
            {
                if (type == typeof(Ability) || !typeof(Ability).IsAssignableFrom(type)) continue;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly))
                    if (method.Name == "Activate" && method.ReturnType == typeof(bool)) yield return method;
            }
            MethodInfo world = AccessTools.Method(typeof(Ability),"Activate",new[]{typeof(GlobalTargetInfo)});
            if (world != null) yield return world;
        }

        public static bool Prefix(Ability __instance,ref bool __result)
        {
            return Patch_SharedAbilityCastSource.Prefix(__instance,ref __result);
        }
    }

    [HarmonyPatch(typeof(Ability), "PreActivate")]
    public static class Patch_SharedAbilityGroupCooldown
    {
        public static void Postfix(Ability __instance)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance);
            if (human == null || __instance.def.groupDef == null) return;
            // Native activation iterates the active body's list. Include hidden
            // source abilities in that same group, without exposing them to cast.
            List<Ability> originals = human.abilities.AllAbilitiesForReading;
            for (int i = 0; i < originals.Count; i++)
                if (originals[i] != __instance)
                    originals[i].Notify_GroupStartedCooldown(__instance.def.groupDef, __instance.CooldownTicksRemaining);
        }
    }

    [HarmonyPatch(typeof(GeneUtility), "OffsetHemogen")]
    public static class Patch_SharedHemogenOffset
    {
        public static void Prefix(ref Pawn pawn)
        {
            Pawn human = SharedFormIdentity.HumanFor(pawn);
            if (SharedFormAbilities.HemogenFor(human) != null) pawn = human;
        }
    }

    [HarmonyPatch(typeof(Gene_Hemogen), "TickInterval")]
    public static class Patch_SharedHemogenSingleTick
    {
        public static bool Prefix(Gene_Hemogen __instance)
        {
            return !SharedFormAbilities.IsNativeDrain(__instance) || SharedFormIdentity.ActiveFormFor(__instance.pawn) == null;
        }
    }

    [HarmonyPatch(typeof(Gene_HemogenDrain), "TickInterval")]
    public static class Patch_SharedHemogenDrainSingleTick
    {
        public static bool Prefix(Gene_HemogenDrain __instance)
        {
            return !SharedFormAbilities.IsNativeDrain(__instance) || SharedFormIdentity.ActiveFormFor(__instance.pawn) == null;
        }
    }

    [HarmonyPatch(typeof(CompAbilityEffect_HemogenCost), "GizmoDisabled")]
    public static class Patch_SharedHemogenPermission
    {
        public static bool Prefix(CompAbilityEffect_HemogenCost __instance, ref string reason, ref bool __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance.parent);
            if (human == null) return true;
            Gene_Hemogen gene = SharedFormAbilities.HemogenFor(human);
            float cost = __instance.Props.hemogenCost;
            __result = gene == null || gene.Value < cost
                || (cost > float.Epsilon && cost + SharedFormAbilities.QueuedHemogen(__instance.parent.pawn) > gene.Value);
            reason = __result ? (string)"AbilityDisabledNoHemogen".Translate(__instance.parent.pawn) : null;
            return false;
        }
    }

    [HarmonyPatch(typeof(CompAbilityEffect_HemogenCost), "AICanTargetNow")]
    public static class Patch_SharedHemogenAIPermission
    {
        public static bool Prefix(CompAbilityEffect_HemogenCost __instance, ref bool __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance.parent);
            if (human == null) return true;
            Gene_Hemogen gene = SharedFormAbilities.HemogenFor(human);
            __result = gene != null && gene.Value >= __instance.Props.hemogenCost;
            return false;
        }
    }

    [HarmonyPatch(typeof(CompAbility_RequiresCapacity), "GizmoDisabled")]
    public static class Patch_SharedAbilityManipulation
    {
        public static bool Prefix(CompAbility_RequiresCapacity __instance, ref string reason, ref bool __result)
        {
            Pawn human = SharedFormAbilities.HumanFor(__instance.parent);
            if (human == null || __instance.Props.capacity != PawnCapacityDefOf.Manipulation) return true;
            __result = !human.health.capacities.CapableOf(__instance.Props.capacity);
            reason = __result ? (string)"AbilityDisabledNoCapacity".Translate(__instance.parent.pawn, __instance.Props.capacity.label) : null;
            return false;
        }
    }
}
