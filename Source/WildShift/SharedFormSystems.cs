using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WildShift
{
    // Trackers keep one native state object and one native owner in saves.
    // An active form borrows their runtime interface; Pawn.ExposeData below
    // temporarily exposes only the body's own trackers while saving that body.
    public static class SharedFormSystems
    {
        internal sealed class Binding
        {
            public Pawn body, human;
            public Pawn_PsychicEntropyTracker bodyPsychic, sourcePsychic;
            public Pawn_MechanitorTracker bodyMechanitor, sourceMechanitor;
            public InspirationHandler bodyInspiration, sourceInspiration;
            public MentalBreaker bodyBreaker, sourceBreaker;
            public MentalStateHandler bodyMentalState, sourceMentalState;
            public int lastInterval = int.MinValue;
            public int lastPsychicInterval = int.MinValue, lastInspirationInterval = int.MinValue;
            public int lastMentalBreakInterval = int.MinValue, lastMentalStateInterval = int.MinValue;
            public int bodySaveDepth;
        }

        public sealed class SaveState
        {
            internal Binding binding;
            internal bool body;
        }

        private static readonly ConditionalWeakTable<Pawn, Binding> bindings = new ConditionalWeakTable<Pawn, Binding>();
        private static readonly ConditionalWeakTable<Pawn, System.WeakReference<Binding>> sources = new ConditionalWeakTable<Pawn, System.WeakReference<Binding>>();
        private static readonly List<System.WeakReference<Pawn>> activeBodies=new List<System.WeakReference<Pawn>>();
        private static readonly AccessTools.FieldRef<Pawn_PsychicEntropyTracker, Pawn> psychicPawn
            = AccessTools.FieldRefAccess<Pawn_PsychicEntropyTracker, Pawn>("pawn");
        private static readonly FieldInfo psychicGizmo = AccessTools.Field(typeof(Pawn_PsychicEntropyTracker), "gizmo");
        private static readonly AccessTools.FieldRef<MentalBreaker,Pawn> breakerPawn=AccessTools.FieldRefAccess<MentalBreaker,Pawn>("pawn");
        private static readonly AccessTools.FieldRef<MentalStateHandler,Pawn> mentalStatePawn=AccessTools.FieldRefAccess<MentalStateHandler,Pawn>("pawn");
        [ThreadStatic] private static HashSet<Pawn> storing;

        public static void Initialize(Pawn body)
        {
            Pawn human = SharedFormIdentity.HumanFor(body);
            if (human == null) return;
            Binding binding;
            if (!bindings.TryGetValue(body, out binding))
            {
                Binding previous=ForSource(human);
                binding = new Binding { body = body, human = human, bodyPsychic = body.psychicEntropy,
                    bodyMechanitor = body.mechanitor, bodyInspiration = body.mindState?.inspirationHandler,
                    bodyBreaker=body.mindState?.mentalBreaker,bodyMentalState=body.mindState?.mentalStateHandler };
                if(binding.bodyPsychic==human.psychicEntropy)binding.bodyPsychic=previous?.bodyPsychic;
                if(binding.bodyMechanitor==human.mechanitor)binding.bodyMechanitor=previous?.bodyMechanitor;
                if(binding.bodyInspiration==human.mindState?.inspirationHandler)binding.bodyInspiration=previous?.bodyInspiration;
                if(binding.bodyBreaker==human.mindState?.mentalBreaker)binding.bodyBreaker=previous?.bodyBreaker;
                if(binding.bodyMentalState==human.mindState?.mentalStateHandler)binding.bodyMentalState=previous?.bodyMentalState;
                bindings.Add(body, binding);
                activeBodies.Add(new System.WeakReference<Pawn>(body));
            }
            else if (binding.human != human)
            {
                Release(body);
                Initialize(body);
                return;
            }
            if(binding.sourcePsychic!=human.psychicEntropy&&binding.sourcePsychic!=null)
                psychicPawn(binding.sourcePsychic)=human;
            if(binding.sourceInspiration!=human.mindState?.inspirationHandler&&binding.sourceInspiration!=null)
            {
                binding.sourceInspiration.pawn=human;
                if(binding.sourceInspiration.CurState!=null)binding.sourceInspiration.CurState.pawn=human;
            }
            if(binding.sourceBreaker!=human.mindState?.mentalBreaker&&binding.sourceBreaker!=null)breakerPawn(binding.sourceBreaker)=human;
            if(binding.sourceMentalState!=human.mindState?.mentalStateHandler&&binding.sourceMentalState!=null)
            {
                mentalStatePawn(binding.sourceMentalState)=human;
                if(binding.sourceMentalState.CurState!=null)binding.sourceMentalState.CurState.pawn=human;
            }
            binding.sourcePsychic = human.psychicEntropy;
            binding.sourceMechanitor = human.mechanitor;
            binding.sourceInspiration = human.mindState?.inspirationHandler;
            binding.sourceBreaker=human.mindState?.mentalBreaker;
            binding.sourceMentalState=human.mindState?.mentalStateHandler;
            System.WeakReference<Binding> source=sources.GetValue(human,delegate(Pawn ignored){return new System.WeakReference<Binding>(null);});
            source.SetTarget(binding);
            Bind(binding);
        }

        private static void Bind(Binding binding)
        {
            Pawn body = binding.body;
            if (binding.sourcePsychic != null)
            {
                bool changed = body.psychicEntropy != binding.sourcePsychic || psychicPawn(binding.sourcePsychic) != body;
                body.psychicEntropy = binding.sourcePsychic;
                psychicPawn(binding.sourcePsychic) = body;
                if (changed) psychicGizmo?.SetValue(binding.sourcePsychic, null);
            }
            else body.psychicEntropy=binding.bodyPsychic;
            body.mechanitor = binding.sourceMechanitor ?? binding.bodyMechanitor;
            if (binding.sourceInspiration != null && body.mindState != null)
            {
                body.mindState.inspirationHandler = binding.sourceInspiration;
                binding.sourceInspiration.pawn = body;
                if (binding.sourceInspiration.CurState != null) binding.sourceInspiration.CurState.pawn = body;
            }
            else if(body.mindState!=null)body.mindState.inspirationHandler=binding.bodyInspiration;
            if(body.mindState!=null)
            {
                body.mindState.mentalBreaker=binding.sourceBreaker??binding.bodyBreaker;
                body.mindState.mentalStateHandler=binding.sourceMentalState??binding.bodyMentalState;
                if(binding.sourceBreaker!=null)breakerPawn(binding.sourceBreaker)=body;
                if(binding.sourceMentalState!=null)
                {
                    mentalStatePawn(binding.sourceMentalState)=body;
                    if(binding.sourceMentalState.CurState!=null)binding.sourceMentalState.CurState.pawn=body;
                }
            }
        }

        public static void Release(Pawn body)
        {
            Binding binding;
            if (body == null || !bindings.TryGetValue(body,out binding)) return;
            if(ForSource(binding.human)==binding)
            {
                RestoreSourceOwner(binding);
                sources.Remove(binding.human);
            }
            body.psychicEntropy = binding.bodyPsychic;
            body.mechanitor = binding.bodyMechanitor;
            if (body.mindState != null) body.mindState.inspirationHandler = binding.bodyInspiration;
            if(body.mindState!=null)
            {body.mindState.mentalBreaker=binding.bodyBreaker;body.mindState.mentalStateHandler=binding.bodyMentalState;}
            bindings.Remove(body);
            for(int i=activeBodies.Count-1;i>=0;i--)
            {Pawn item;if(!activeBodies[i].TryGetTarget(out item)||item==body)activeBodies.RemoveAt(i);}
        }

        private static void RestoreSourceOwner(Binding binding)
        {
            if (binding.sourcePsychic != null)
            {
                psychicPawn(binding.sourcePsychic) = binding.human;
                psychicGizmo?.SetValue(binding.sourcePsychic, null);
            }
            if (binding.sourceInspiration != null)
            {
                binding.sourceInspiration.pawn = binding.human;
                if (binding.sourceInspiration.CurState != null) binding.sourceInspiration.CurState.pawn = binding.human;
            }
            if(binding.sourceBreaker!=null)breakerPawn(binding.sourceBreaker)=binding.human;
            if(binding.sourceMentalState!=null)
            {
                mentalStatePawn(binding.sourceMentalState)=binding.human;
                if(binding.sourceMentalState.CurState!=null)binding.sourceMentalState.CurState.pawn=binding.human;
            }
        }

        public static void Tick(Pawn body, int delta)
        {
            if (body == null || SharedFormIdentity.HumanFor(body) == null) return;
            Initialize(body);
            Binding binding;
            if (!bindings.TryGetValue(body,out binding)) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (binding.lastInterval == now || delta <= 0) return;
            binding.lastInterval = now;
            // The normal active Pawn tick advances its borrowed psychic
            // tracker. Its mind-state tracker likewise advances inspiration;
            // the source human is suspended and is never fully ticked here.
            if (binding.sourceInspiration?.CurState != null)
                binding.sourceInspiration.CurState.pawn = body;
        }

        public static SaveState BeginSave(Pawn pawn)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return null;
            Binding binding;
            if (bindings.TryGetValue(pawn,out binding))
            {
                binding.bodySaveDepth++;
                pawn.psychicEntropy = binding.bodyPsychic;
                pawn.mechanitor = binding.bodyMechanitor;
                if (pawn.mindState != null) pawn.mindState.inspirationHandler = binding.bodyInspiration;
                if(pawn.mindState!=null)
                {pawn.mindState.mentalBreaker=binding.bodyBreaker;pawn.mindState.mentalStateHandler=binding.bodyMentalState;}
                return new SaveState { binding = binding, body = true };
            }
            binding=ForSource(pawn);
            if (binding != null)
            {
                RestoreSourceOwner(binding);
                return new SaveState { binding = binding };
            }
            return null;
        }

        public static void EndSave(SaveState state)
        {
            if (state?.binding == null) return;
            Binding binding = state.binding;
            if(state.body)binding.bodySaveDepth=Math.Max(0,binding.bodySaveDepth-1);
            // A stored human can be deep-saved inside the animal's health
            // tracker, before Pawn.ExposeData reaches its tracker fields.
            // Rebinding there would serialize the same source a second time.
            if (binding.bodySaveDepth==0&&SharedFormIdentity.HumanFor(binding.body) == binding.human) Bind(binding);
        }

        private static Binding ForSource(Pawn human)
        {
            System.WeakReference<Binding> reference;Binding binding;
            return human!=null&&sources.TryGetValue(human,out reference)&&reference.TryGetTarget(out binding)?binding:null;
        }

        public static Pawn ActiveBody(Pawn pawn)
        {
            return SharedFormIdentity.ActiveFormFor(pawn) ?? pawn;
        }

        public static Pawn Canonical(Pawn pawn)
        {
            return SharedFormIdentity.HumanFor(pawn) ?? pawn;
        }

        public static bool CanTickPsychic(Pawn_PsychicEntropyTracker tracker)
        {
            Binding binding;
            if(tracker?.Pawn==null||!bindings.TryGetValue(tracker.Pawn,out binding)||binding.sourcePsychic!=tracker)return true;
            int tick=Find.TickManager!=null?Find.TickManager.TicksGame:0;
            if(binding.lastPsychicInterval==tick)return false;
            binding.lastPsychicInterval=tick;return true;
        }

        public static bool CanTickInspiration(InspirationHandler handler)
        {
            Binding binding;
            if(handler?.pawn==null||!bindings.TryGetValue(handler.pawn,out binding)||binding.sourceInspiration!=handler)return true;
            int tick=Find.TickManager!=null?Find.TickManager.TicksGame:0;
            if(binding.lastInspirationInterval==tick)return false;
            binding.lastInspirationInterval=tick;return true;
        }

        public static bool CanTickMentalBreaker(MentalBreaker controller)
        {
            Pawn body=controller!=null?breakerPawn(controller):null;Binding binding;
            if(body==null||!bindings.TryGetValue(body,out binding)||binding.sourceBreaker!=controller)return true;
            int tick=Find.TickManager!=null?Find.TickManager.TicksGame:0;
            if(binding.lastMentalBreakInterval==tick)return false;
            binding.lastMentalBreakInterval=tick;return true;
        }

        public static bool CanTickMentalState(MentalStateHandler handler)
        {
            Pawn body=handler!=null?mentalStatePawn(handler):null;Binding binding;
            if(body==null||!bindings.TryGetValue(body,out binding)||binding.sourceMentalState!=handler)return true;
            int tick=Find.TickManager!=null?Find.TickManager.TicksGame:0;
            if(binding.lastMentalStateInterval==tick)return false;
            binding.lastMentalStateInterval=tick;return true;
        }

        public static void BeginStore(Pawn pawn)
        {
            if (pawn == null) return;
            if (storing == null) storing = new HashSet<Pawn>();
            storing.Add(pawn);
        }

        public static void EndStore(Pawn pawn) { if (pawn != null) storing?.Remove(pawn); }
        public static bool IsStoring(Pawn pawn) { return pawn != null && storing != null && storing.Contains(pawn); }

        // Only the ideology's belief census uses this list. Do not inject
        // hidden humans into global map/world pawn enumeration or ticking.
        public static List<Pawn> WithActivePeople(IEnumerable<Pawn> native)
        {
            var result=new List<Pawn>();var seen=new HashSet<Pawn>();
            foreach(Pawn pawn in native)
            {
                Pawn person=Canonical(pawn);
                if(person!=null&&seen.Add(person))result.Add(person);
            }
            for(int i=0;i<activeBodies.Count;i++)
            {
                Pawn body;
                if(!activeBodies[i].TryGetTarget(out body)||body==null||body.Suspended
                    ||(!body.Spawned&&!body.IsCaravanMember()&&!PawnUtility.IsTravelingInTransportPodWorldObject(body)))continue;
                Pawn person=SharedFormIdentity.HumanFor(body);
                if(person!=null&&person.IsColonist&&seen.Add(person))result.Add(person);
            }
            if(storing!=null)foreach(Pawn person in storing)
                if(person!=null&&person.IsColonist&&seen.Add(person))result.Add(person);
            return result;
        }

        // Pure declarative bonuses retain the animal's base. Native inspiration
        // is normally supplied by the borrowed handler at its original order in
        // StatWorker; the fallback only covers reads during initialization.
        public static bool AppendModifiers(Pawn body,List<StatModifier> offsets,List<StatModifier> factors)
        {
            Pawn human = SharedFormIdentity.HumanFor(body);
            if (human == null) return false;
            bool changed = false;
            InspirationDef inspiration=human.mindState?.inspirationHandler?.CurStateDef;
            InspirationDef bodyInspiration=body.mindState?.inspirationHandler?.CurStateDef;
            if (inspiration!=null && inspiration != bodyInspiration)
            {
                changed |= Merge(offsets,inspiration.statOffsets,false);
                changed |= Merge(factors,inspiration.statFactors,true);
            }
            Ideo sourceIdeo = human.Ideo;
            if (sourceIdeo != null)
            {
                if (body.Ideo != sourceIdeo)
                {
                    List<Precept> precepts = sourceIdeo.PreceptsListForReading;
                    StatRequest request = StatRequest.For(body);
                    for (int i=0;i<precepts.Count;i++)
                    {
                        PreceptDef def = precepts[i].def;
                        changed |= Merge(offsets,def.statOffsets,false);
                        changed |= Merge(factors,def.statFactors,true);
                        if (def.conditionalStatAffecters == null) continue;
                        for (int j=0;j<def.conditionalStatAffecters.Count;j++)
                        {
                            ConditionalStatAffecter condition=def.conditionalStatAffecters[j];
                            if (condition == null || !condition.Applies(request)) continue;
                            changed |= Merge(offsets,condition.statOffsets,false);
                            changed |= Merge(factors,condition.statFactors,true);
                        }
                    }
                }
                Precept_Role role = sourceIdeo.GetRole(human);
                Precept_Role bodyRole = body.Ideo?.GetRole(body);
                if (role?.def.roleEffects != null && role != bodyRole)
                    for (int i=0;i<role.def.roleEffects.Count;i++)
                    {
                        RoleEffect effect=role.def.roleEffects[i];
                        RoleEffect_PawnStatOffset offset=effect as RoleEffect_PawnStatOffset;
                        RoleEffect_PawnStatFactor factor=effect as RoleEffect_PawnStatFactor;
                        if (offset?.statDef != null) changed |= MergeOne(offsets,offset.statDef,offset.modifier,false);
                        if (factor?.statDef != null) changed |= MergeOne(factors,factor.statDef,factor.modifier,true);
                    }
            }
            if (human.apparel != null)
                for (int i=0;i<human.apparel.WornApparel.Count;i++)
                {
                    Apparel gear=human.apparel.WornApparel[i];
                    if (body.apparel != null && body.apparel.WornApparel.Contains(gear)) continue;
                    changed |= AddGear(offsets,gear);
                }
            ThingWithComps weapon=human.equipment?.Primary;
            if (weapon != null && body.equipment?.Primary != weapon) changed |= AddGear(offsets,weapon);
            return changed;
        }

        private static bool AddGear(List<StatModifier> offsets,Thing gear)
        {
            bool changed=false;
            HashSet<StatDef> stats=new HashSet<StatDef>();
            if (gear.def.equippedStatOffsets != null)
                for (int i=0;i<gear.def.equippedStatOffsets.Count;i++)
                    if (gear.def.equippedStatOffsets[i]?.stat != null) stats.Add(gear.def.equippedStatOffsets[i].stat);
            CompBladelinkWeapon link=gear.TryGetComp<CompBladelinkWeapon>();
            if (link != null)
                for(int i=0;i<link.TraitsListForReading.Count;i++)
                {
                    List<StatModifier> list=link.TraitsListForReading[i].equippedStatOffsets;
                    if(list!=null)for(int j=0;j<list.Count;j++)if(list[j]?.stat!=null)stats.Add(list[j].stat);
                }
            foreach(StatDef stat in stats) changed |= MergeOne(offsets,stat,StatWorker.StatOffsetFromGear(gear,stat),false);
            return changed;
        }

        private static bool Merge(List<StatModifier> target,List<StatModifier> source,bool factor)
        {
            if(source==null)return false;
            bool changed=false;
            var seen=new HashSet<StatDef>();
            for(int i=0;i<source.Count;i++)
            {
                StatModifier item=source[i];
                if(item?.stat==null||!seen.Add(item.stat))continue;
                changed|=MergeOne(target,item.stat,item.value,factor);
            }
            return changed;
        }

        private static bool MergeOne(List<StatModifier> target,StatDef stat,float value,bool factor)
        {
            if(value==(factor?1f:0f))return false;
            for(int i=0;i<target.Count;i++)if(target[i].stat==stat)
            {target[i].value=factor?target[i].value*value:target[i].value+value;return true;}
            target.Add(new StatModifier{stat=stat,value=value});return true;
        }

        public static int ModifierVersion(Pawn body)
        {
            Pawn human=SharedFormIdentity.HumanFor(body);
            if(human==null)return 0;
            unchecked
            {
                int hash=17;
                hash=Hash(hash,human.mindState?.inspirationHandler?.CurStateDef);hash=Hash(hash,body.mindState?.inspirationHandler?.CurStateDef);
                Ideo ideo=human.Ideo;hash=Hash(hash,ideo);hash=Hash(hash,body.Ideo);
                if(ideo!=null)
                {
                    hash=Hash(hash,ideo.GetRole(human));hash=Hash(hash,body.Ideo?.GetRole(body));
                    List<Precept> precepts=ideo.PreceptsListForReading;
                    hash=hash*31+precepts.Count;
                    StatRequest request=StatRequest.For(body);
                    for(int i=0;i<precepts.Count;i++)
                    {
                        PreceptDef def=precepts[i].def;hash=Hash(hash,def);
                        if(def.conditionalStatAffecters==null)continue;
                        for(int j=0;j<def.conditionalStatAffecters.Count;j++)
                        {
                            ConditionalStatAffecter condition=def.conditionalStatAffecters[j];
                            hash=hash*31+(condition!=null&&condition.Applies(request)?1:0);
                        }
                    }
                }
                if(human.apparel!=null)for(int i=0;i<human.apparel.WornApparel.Count;i++)hash=HashGear(hash,human.apparel.WornApparel[i]);
                hash=HashGear(hash,human.equipment?.Primary);
                return hash;
            }
        }

        private static int Hash(int seed,object value) { unchecked{return seed*31+(value==null?0:RuntimeHelpers.GetHashCode(value));} }
        private static int HashGear(int seed,Thing gear)
        {
            unchecked
            {
                seed=Hash(seed,gear);
                if(gear==null)return seed;
                seed=Hash(seed,gear.Stuff);seed=seed*31+gear.HitPoints;
                QualityCategory quality;if(gear.TryGetQuality(out quality))seed=seed*31+(int)quality;
                CompBladelinkWeapon link=gear.TryGetComp<CompBladelinkWeapon>();
                if(link!=null)for(int i=0;i<link.TraitsListForReading.Count;i++)seed=Hash(seed,link.TraitsListForReading[i]);
                return seed;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn),"ExposeData")]
    public static class Patch_SharedSystemSaveOwnership
    {
        public static void Prefix(Pawn __instance,out SharedFormSystems.SaveState __state) { __state=SharedFormSystems.BeginSave(__instance); }
        public static Exception Finalizer(Exception __exception,SharedFormSystems.SaveState __state)
        {SharedFormSystems.EndSave(__state);return __exception;}
    }

    [HarmonyPatch(typeof(FormTransferUtility),"TryStore")]
    public static class Patch_SharedSystemStoreContinuity
    {
        public static void Prefix(Pawn human) { SharedFormSystems.BeginStore(human); }
        public static Exception Finalizer(Exception __exception,Pawn human) {SharedFormSystems.EndStore(human);return __exception;}
    }

    [HarmonyPatch(typeof(Ideo),"Notify_MemberLost")]
    public static class Patch_SharedSystemIdeologyStore
    {
        public static bool Prefix(Pawn member){return !SharedFormSystems.IsStoring(member);}
    }

    [HarmonyPatch(typeof(Ideo),"RecacheColonistBelieverCount")]
    public static class Patch_SharedSystemIdeologyBelievers
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter=AccessTools.PropertyGetter(typeof(PawnsFinder),"AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep");
            MethodInfo shared=AccessTools.Method(typeof(SharedFormSystems),nameof(SharedFormSystems.WithActivePeople));
            foreach(CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if(instruction.Calls(getter))yield return new CodeInstruction(OpCodes.Call,shared);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker),"Notify_DeSpawned")]
    public static class Patch_SharedMechanitorStore
    {
        public static bool Prefix(Pawn_MechanitorTracker __instance) {return !SharedFormSystems.IsStoring(__instance.Pawn);}
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker),"get_CanControlMechs")]
    public static class Patch_SharedMechanitorControl
    {
        public static bool Prefix(Pawn_MechanitorTracker __instance,ref AcceptanceReport __result)
        {
            Pawn human=__instance.Pawn,body=SharedFormIdentity.ActiveFormFor(human);
            if(body==null)return true;
            if(body.Downed)__result="MechControllerDowned".Translate(body.Named("PAWN"));
            else if(human.IsPrisoner||body.IsPrisoner)__result="MechControllerImprisoned".Translate(body.Named("PAWN"));
            else if(!body.Spawned)__result=false;
            else if(body.InMentalState)__result="MechControllerMentalState".Translate(body.Named("PAWN"),body.MentalStateDef.Named("MENTALSTATE"));
            else __result=true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker),"CanCommandTo")]
    public static class Patch_SharedMechanitorRange
    {
        public static bool Prefix(Pawn_MechanitorTracker __instance,LocalTargetInfo target,ref bool __result)
        {
            Pawn body=SharedFormIdentity.ActiveFormFor(__instance.Pawn);
            if(body==null)return true;
            __result=body.MapHeld!=null&&target.Cell.InBounds(body.MapHeld)&&body.Position.DistanceToSquared(target.Cell)<620.01f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker),"DrawCommandRadius")]
    public static class Patch_SharedMechanitorRadius
    {
        public static bool Prefix(Pawn_MechanitorTracker __instance)
        {
            Pawn body=SharedFormIdentity.ActiveFormFor(__instance.Pawn);
            if(body==null)return true;
            if(body.Spawned&&__instance.AnySelectedDraftedMechs)
                GenDraw.DrawRadiusRing(body.Position,24.9f,Color.white,delegate(IntVec3 cell){return __instance.CanCommandTo(cell);});
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_SharedMechanitorRecognition
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type type=AccessTools.TypeByName("MechanitorUtility");
            yield return AccessTools.Method(type,"IsMechanitor");
            yield return AccessTools.Method(type,"ShouldBeMechanitor");
        }
        public static void Prefix(ref Pawn pawn) {pawn=SharedFormSystems.Canonical(pawn);}
    }

    [HarmonyPatch(typeof(PawnComponentsUtility),"AddAndRemoveDynamicComponents")]
    public static class Patch_SharedSystemDynamicComponents
    {
        public static void Postfix(Pawn pawn)
        {
            // During load later Pawn tracker fields have not necessarily been
            // deserialized; during save they are deliberately native-only.
            // The post-load Runtime callback binds after all fields resolve.
            if(Scribe.mode==LoadSaveMode.Inactive&&SharedFormIdentity.HumanFor(pawn)!=null)SharedFormSystems.Initialize(pawn);
        }
    }

    [HarmonyPatch]
    public static class Patch_SharedPsylinkSource
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PawnUtility),"GetMainPsylinkSource");
            // This native helper independently scans for typed psylink Hediffs;
            // it does not call GetMainPsylinkSource or the entropy tracker.
            yield return AccessTools.Method(typeof(PawnUtility),"GetPsylinkLevel");
            yield return AccessTools.Method(typeof(PawnUtility),"GetMaxPsylinkLevelTitle");
        }
        public static void Prefix(ref Pawn pawn) {pawn=SharedFormSystems.Canonical(pawn);}
    }

    [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker),"NeedToShowGizmo")]
    public static class Patch_SharedPsychicGizmo
    {
        public static void Postfix(Pawn_PsychicEntropyTracker __instance,ref bool __result)
        {
            Pawn human=SharedFormIdentity.HumanFor(__instance.Pawn);
            if(human!=null&&human.GetMainPsylinkSource()!=null&&__instance.PsychicSensitivity>float.Epsilon)__result=true;
        }
    }

    [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker),"PsychicEntropyTrackerTickInterval")]
    public static class Patch_SharedPsychicSingleInterval
    {
        public static bool Prefix(Pawn_PsychicEntropyTracker __instance){return SharedFormSystems.CanTickPsychic(__instance);}
    }

    [HarmonyPatch(typeof(InspirationHandler),"InspirationHandlerTickInterval")]
    public static class Patch_SharedInspirationSingleInterval
    {
        public static bool Prefix(InspirationHandler __instance){return SharedFormSystems.CanTickInspiration(__instance);}
    }

    [HarmonyPatch(typeof(MentalBreaker),"MentalBreakerTickInterval")]
    public static class Patch_SharedMentalBreakerSingleInterval
    {public static bool Prefix(MentalBreaker __instance){return SharedFormSystems.CanTickMentalBreaker(__instance);}}

    [HarmonyPatch(typeof(MentalStateHandler),"MentalStateHandlerTickInterval")]
    public static class Patch_SharedMentalStateSingleInterval
    {public static bool Prefix(MentalStateHandler __instance){return SharedFormSystems.CanTickMentalState(__instance);}}

    [HarmonyPatch(typeof(Pawn_AbilityTracker),"Notify_TemporaryAbilitiesChanged")]
    public static class Patch_SharedAbilitySourcesChanged
    {
        public static void Postfix(Pawn_AbilityTracker __instance) {SharedFormAbilities.MarkDirty(__instance.pawn);}
    }
}
