using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // The original human remains the sole serialized owner of personal state.
    // These weak lookups describe only currently active bodies, never own them.
    public static class SharedFormIdentity
    {
        private static readonly ConditionalWeakTable<Pawn, System.WeakReference<Pawn>> activeForms
            = new ConditionalWeakTable<Pawn, System.WeakReference<Pawn>>();
        private sealed class MemoryClock { public int lastTick = int.MinValue; }
        private static readonly ConditionalWeakTable<Pawn, MemoryClock> memoryClocks
            = new ConditionalWeakTable<Pawn, MemoryClock>();

        public static Pawn HumanFor(Pawn animal)
        {
            // Sapient Animals can turn the active beast into a humanlike race
            // while migrating this same transformed comp and its real owner.
            // Native Scribe constructs story/trait views before loading the
            // Pawn's health tracker. Such a partial Pawn has no active binding.
            if (animal == null || animal.Destroyed || animal.health == null || animal.Dead || animal.RaceProps == null) return null;
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(animal);
            Pawn human = comp != null ? comp.StoredPawn : null;
            return human != null && human != animal && !human.Destroyed && human.health != null && !human.Dead
                && human.RaceProps != null && human.RaceProps.Humanlike
                && human.holdingOwner == comp.GetDirectlyHeldThings() ? human : null;
        }

        public static Pawn ActiveFormFor(Pawn human)
        {
            if (human == null) return null;
            System.WeakReference<Pawn> reference;
            Pawn form;
            if (activeForms.TryGetValue(human, out reference) && reference.TryGetTarget(out form)
                && HumanFor(form) == human) return form;
            return null;
        }

        public static void Initialize(Pawn animal)
        {
            Pawn human = HumanFor(animal);
            if (human == null) return;
            System.WeakReference<Pawn> reference = activeForms.GetValue(human,
                delegate(Pawn ignored) { return new System.WeakReference<Pawn>(animal); });
            reference.SetTarget(animal);
            if (human.skills == null) return;
            SharedPawnSkillTracker tracker = animal.skills as SharedPawnSkillTracker;
            if (tracker == null || !tracker.IsForBody(animal))
            {
                tracker = new SharedPawnSkillTracker(animal, animal.skills);
                animal.skills = tracker;
            }
            tracker.Bind();
        }

        public static void TickMemories(Pawn animal, int delta)
        {
            if (SharedFormBiology.SharesPersonalClock(animal)) return;
            Pawn human = HumanFor(animal);
            if (human == null || Find.TickManager == null || human.needs == null
                || human.needs.mood == null || !human.IsHashIntervalTick(150, delta)) return;
            MemoryClock clock = memoryClocks.GetValue(human, delegate(Pawn ignored) { return new MemoryClock(); });
            int now = Find.TickManager.TicksGame;
            if (clock.lastTick == now) return;
            clock.lastTick = now;
            // Keep the person's ordinary memory clock running. Do not run a
            // second pawn/needs/AI tick or evaluate the hidden body's weather.
            human.needs.mood.thoughts.memories.MemoryThoughtInterval();
        }
    }

    // Native skill consumers can still use pawn.skills and its public list.
    // Records belong to the human and are serialized ONLY through that human;
    // this proxy never deep-saves another copy of those records.
    public sealed class SharedPawnSkillTracker : Pawn_SkillTracker, IExposable
    {
        private readonly Pawn activeBody;
        private Pawn_SkillTracker bodySkills;

        public SharedPawnSkillTracker(Pawn pawn) : this(pawn, null) { }

        public SharedPawnSkillTracker(Pawn pawn, Pawn_SkillTracker previous) : base(pawn)
        {
            activeBody = pawn;
            bodySkills = previous is SharedPawnSkillTracker ? null : previous;
            if (bodySkills == null) bodySkills = new Pawn_SkillTracker(pawn);
            Bind();
        }

        public Pawn_SkillTracker OriginalSkills
        {
            get
            {
                Pawn human = SharedFormIdentity.HumanFor(activeBody);
                Pawn_SkillTracker source = human != null ? human.skills : null;
                return source is SharedPawnSkillTracker ? null : source;
            }
        }

        public bool IsForBody(Pawn pawn) { return activeBody == pawn; }

        public void Bind()
        {
            Pawn_SkillTracker source = OriginalSkills;
            skills = source != null ? source.skills : bodySkills.skills;
        }

        public new void ExposeData()
        {
            // Preserve any animal-specific tracker supplied by another mod,
            // while the person's actual skills remain on the original pawn.
            Scribe_Deep.Look(ref bodySkills, "bodySkills", activeBody);
            if (bodySkills == null || bodySkills is SharedPawnSkillTracker)
                bodySkills = new Pawn_SkillTracker(activeBody);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Bind();
        }
    }

    [HarmonyPatch(typeof(Pawn_SkillTracker), "CanGainXP")]
    public static class Patch_SharedSkillPermission
    {
        private static readonly MethodInfo nativePermission = AccessTools.Method(typeof(Pawn_SkillTracker), "CanGainXP");

        public static bool Prefix(Pawn_SkillTracker __instance, ref bool __result)
        {
            SharedPawnSkillTracker proxy = __instance as SharedPawnSkillTracker;
            Pawn_SkillTracker source = proxy != null ? proxy.OriginalSkills : null;
            if (source == null) return true;
            __result = (bool)nativePermission.Invoke(source, null);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_SkillTracker), "SkillsTickInterval")]
    public static class Patch_SharedPersonalMemoryClock
    {
        private static readonly FieldInfo midnightStamp = AccessTools.Field(typeof(Pawn_SkillTracker), "lastXpSinceMidnightResetTimestamp");

        public static void Prefix(Pawn_SkillTracker __instance, ref int ___lastXpSinceMidnightResetTimestamp)
        {
            SharedPawnSkillTracker proxy = __instance as SharedPawnSkillTracker;
            if (proxy == null) return;
            proxy.Bind();
            Pawn_SkillTracker source = proxy.OriginalSkills;
            if (source != null)
                ___lastXpSinceMidnightResetTimestamp = (int)midnightStamp.GetValue(source);
        }

        public static void Postfix(Pawn_SkillTracker __instance, Pawn ___pawn, int delta,
            int ___lastXpSinceMidnightResetTimestamp)
        {
            SharedPawnSkillTracker proxy = __instance as SharedPawnSkillTracker;
            if (proxy == null) return;
            Pawn_SkillTracker source = proxy.OriginalSkills;
            if (source != null) midnightStamp.SetValue(source, ___lastXpSinceMidnightResetTimestamp);
            SharedFormIdentity.TickMemories(___pawn, delta);
        }
    }
}
