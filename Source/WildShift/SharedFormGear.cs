using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Getters expose a non-owning view. The actual ThingOwners (and every
    // item's HP, shield energy, jump charges and comps) remain unchanged.
    public static class SharedFormGear
    {
        [ThreadStatic] private static int nativeOnly;
        [ThreadStatic] private static int mutation;
        [ThreadStatic] private static Pawn coverageBody;
        private static readonly ConditionalWeakTable<Pawn, State> states = new ConditionalWeakTable<Pawn, State>();
        private static readonly Action<Thing> tick = AccessTools.MethodDelegate<Action<Thing>>(AccessTools.Method(typeof(Thing), "Tick"));
        private static readonly Action<Thing, int> interval = AccessTools.MethodDelegate<Action<Thing, int>>(AccessTools.Method(typeof(Thing), "TickInterval"));
        private static readonly AccessTools.FieldRef<Pawn_ApparelTracker, ThingOwner<Apparel>> rawApparelOwner
            = AccessTools.FieldRefAccess<Pawn_ApparelTracker, ThingOwner<Apparel>>("wornApparel");
        private static readonly AccessTools.FieldRef<Pawn_EquipmentTracker, ThingOwner<ThingWithComps>> rawEquipmentOwner
            = AccessTools.FieldRefAccess<Pawn_EquipmentTracker, ThingOwner<ThingWithComps>>("equipment");
        private static readonly ConditionalWeakTable<BodyDef, ConditionalWeakTable<BodyDef, Dictionary<BodyPartRecord, BodyPartRecord>>> coverageCache
            = new ConditionalWeakTable<BodyDef, ConditionalWeakTable<BodyDef, Dictionary<BodyPartRecord, BodyPartRecord>>>();

        private sealed class State
        {
            public Pawn ownerBody;
            public Pawn human;
            public ThingWithComps primary;
            public bool suspended, refreshing;
            public int lastTick = int.MinValue, lastInterval = int.MinValue;
            public HediffSet watchedSourceHealth;
            public SharedFormHealth.DirtyStamp sourceHealthStamp;
            public int sourceHealthVersion = int.MinValue, sourceHealthCount = -1;
            public List<Apparel> sourceApparel = new List<Apparel>(), ownApparel = new List<Apparel>();
            public List<ThingWithComps> sourceEquipment = new List<ThingWithComps>(), ownEquipment = new List<ThingWithComps>();
            public List<Hediff> sourceHealth = new List<Hediff>();
            public readonly List<Apparel> apparel = new List<Apparel>();
            public readonly List<ThingWithComps> equipment = new List<ThingWithComps>();
            public readonly List<ThingWithComps> borrowed = new List<ThingWithComps>();

            public void ResetHealthWatch()
            {
                watchedSourceHealth = null;
                sourceHealthStamp = null;
                sourceHealthVersion = int.MinValue;
                sourceHealthCount = -1;
            }

            public void Reset()
            {
                human = null;
                primary = null;
                ResetHealthWatch();
                sourceApparel.Clear();
                ownApparel.Clear();
                sourceEquipment.Clear();
                ownEquipment.Clear();
                sourceHealth.Clear();
                apparel.Clear();
                equipment.Clear();
                borrowed.Clear();
            }

            public bool Refresh(Pawn body, Pawn source = null)
            {
                if (suspended || refreshing) return false;
                // For already validated this exact source immediately before
                // entering here; no gameplay callback runs between the reads.
                // Other callers still resolve and validate their own source.
                source = source ?? SharedFormIdentity.HumanFor(body);
                if (source == null) return false;
                List<Apparel> sa = RawApparel(source), ba = RawApparel(body);
                List<ThingWithComps> se = RawEquipment(source), be = RawEquipment(body);
                HediffSet healthSet = source.health?.hediffSet;
                List<Hediff> health = healthSet?.hediffs;
                int healthCount = health != null ? health.Count : 0;
                bool gearSame = source == human && Same(sourceApparel, sa) && Same(ownApparel, ba)
                    && Same(sourceEquipment, se) && Same(ownEquipment, be);
                if (gearSame)
                {
                    if (ReferenceEquals(watchedSourceHealth, healthSet)
                        && (healthSet == null || (sourceHealthStamp != null && sourceHealthStamp.version == sourceHealthVersion))
                        && sourceHealthCount == healthCount)
                        return true;
                    if (Same(sourceHealth, health))
                    {
                        watchedSourceHealth = healthSet;
                        sourceHealthStamp = SharedFormHealth.WatchHealthSet(healthSet);
                        sourceHealthVersion = sourceHealthStamp != null ? sourceHealthStamp.version : 0;
                        sourceHealthCount = healthCount;
                        return true;
                    }
                }
                refreshing = true;
                try
                {
                    human = source;
                    Capture(sourceApparel,sa); Capture(ownApparel,ba);
                    Capture(sourceEquipment,se); Capture(ownEquipment,be); Capture(sourceHealth,health);
                    watchedSourceHealth = healthSet;
                    sourceHealthStamp = SharedFormHealth.WatchHealthSet(healthSet);
                    sourceHealthVersion = sourceHealthStamp != null ? sourceHealthStamp.version : 0;
                    sourceHealthCount = healthCount;
                    apparel.Clear(); apparel.AddRange(ownApparel);
                    for (int i=0;i<sourceApparel.Count;i++) if (!apparel.Contains(sourceApparel[i])) apparel.Add(sourceApparel[i]);
                    equipment.Clear(); equipment.AddRange(ownEquipment);
                    for (int i=0;i<sourceEquipment.Count;i++) if (!equipment.Contains(sourceEquipment[i])) equipment.Add(sourceEquipment[i]);
                    primary = null;
                    for (int i=0;i<sourceEquipment.Count;i++)
                        if (sourceEquipment[i].def.equipmentType==EquipmentType.Primary) { primary=sourceEquipment[i]; break; }
                    borrowed.Clear();
                    for(int i=0;i<sourceApparel.Count;i++)if(!ownApparel.Contains(sourceApparel[i]))borrowed.Add(sourceApparel[i]);
                    for(int i=0;i<sourceEquipment.Count;i++)if(!ownEquipment.Contains(sourceEquipment[i]))borrowed.Add(sourceEquipment[i]);
                    BindVerbs(body,this);
                }
                finally { refreshing=false; }
                return true;
            }
        }

        private sealed class NativeScope : IDisposable
        {
            private bool disposed;
            public NativeScope() { nativeOnly++; }
            public void Dispose() { if (!disposed) { nativeOnly--; disposed=true; } }
        }
        public static IDisposable NativeOnly() { return new NativeScope(); }
        // The render tree checks this path even when its graph is already
        // resolved. Keep its nested, thread-local scope free of heap objects.
        // Zero means our prefix never ran (another prefix threw or skipped).
        internal static int BeginNativeRender() { return ++nativeOnly; }
        internal static void EndNativeRender(int enteredDepth) { if(enteredDepth>0)nativeOnly=enteredDepth-1; }
        public static bool IsNativeOnly { get { return nativeOnly>0 || Scribe.mode!=LoadSaveMode.Inactive; } }
        private static bool Same<T>(List<T> snapshot,List<T> actual) where T:class
        {
            if(snapshot.Count!=(actual?.Count??0))return false;
            for(int i=0;i<snapshot.Count;i++)if(!ReferenceEquals(snapshot[i],actual[i]))return false;
            return true;
        }
        private static void Capture<T>(List<T> target,List<T> source)
        { target.Clear(); if(source!=null)target.AddRange(source); }
        public static List<Apparel> RawApparel(Pawn pawn)
        {
            Pawn_ApparelTracker tracker = pawn?.apparel;
            return tracker != null ? rawApparelOwner(tracker)?.InnerListForReading : null;
        }
        public static List<ThingWithComps> RawEquipment(Pawn pawn)
        {
            Pawn_EquipmentTracker tracker = pawn?.equipment;
            return tracker != null ? rawEquipmentOwner(tracker)?.InnerListForReading : null;
        }

        public static void Initialize(Pawn body)
        {
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(body);
            Pawn human = marker == null ? null : SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
            if (human==null)return;
            if(body.apparel==null)body.apparel=new Pawn_ApparelTracker(body);
            if(body.equipment==null)body.equipment=new Pawn_EquipmentTracker(body);
            State state=GetState(body, marker); state.suspended=false; state.Reset(); state.Refresh(body, human);
            body.health?.hediffSet?.DirtyCache();
        }
        public static void Release(Pawn body)
        {
            State state;
            if(body==null||!states.TryGetValue(body,out state))return;
            if(state.suspended)return;
            state.Refresh(body);
            state.suspended=true;
            if(state.human!=null)BindVerbs(state.human,state);
            state.Reset();
        }
        private static State GetState(Pawn body, HediffComp_Transformed marker)
        {
            State state = marker?.gearState as State;
            if (state != null && ReferenceEquals(state.ownerBody, body)) return state;
            if (!states.TryGetValue(body, out state)) state = states.GetOrCreateValue(body);
            state.ownerBody = body;
            if (marker != null) marker.gearState = state;
            return state;
        }
        private static State For(Pawn body, Pawn validatedSource = null, HediffComp_Transformed marker = null)
        {
            if(body==null||IsNativeOnly)return null;
            Pawn source=validatedSource;
            if(source==null)
            {
                marker = TransformUtility.TryGetTransformedComp(body);
                if(marker==null)return null;
                source=SharedFormIdentity.GetStoredHumanFromActiveBody(body, marker);
                if(source==null)return null;
            }
            State state=GetState(body, marker);
            return state.Refresh(body,source)?state:null;
        }
        public static List<Apparel> ApparelFor(Pawn body,List<Apparel> native)
        { State state=For(body);return state!=null?state.apparel:native; }
        public static List<ThingWithComps> EquipmentFor(Pawn body,List<ThingWithComps> native)
        { State state=For(body);return state!=null?state.equipment:native; }
        public static Pawn ActiveWearer(Pawn source)
        {
            if(source==null||IsNativeOnly)return source;
            Pawn body=SharedFormIdentity.ActiveFormFor(source);
            if(body==null)return source;
            HediffComp_Transformed marker = source.holdingOwner?.Owner as HediffComp_Transformed;
            State state = marker?.gearState as State;
            if(state==null||!ReferenceEquals(state.ownerBody,body))
                states.TryGetValue(body,out state);
            if(state!=null)
            {
                if(state.suspended)return source;
                if(state.human==source)return body;
            }
            return For(body,source,marker)!=null?body:source;
        }
        public static ThingWithComps PrimaryFor(Pawn body,ThingWithComps native)
        {
            if(native!=null)return native;
            State state=For(body);
            return state!=null?state.primary:null;
        }
        private static ThingWithComps RawPrimary(Pawn pawn)
        {
            List<ThingWithComps> eq = RawEquipment(pawn);
            if (eq == null) return null;
            for (int i = 0; i < eq.Count; i++)
                if (eq[i].def.equipmentType == EquipmentType.Primary) return eq[i];
            return null;
        }
        internal static void GetVisibleGear(Pawn body, Pawn human, HediffComp_Transformed marker,
            out List<Apparel> bodyApparel, out ThingWithComps bodyPrimary)
        {
            if (body == null) { bodyApparel = null; bodyPrimary = null; return; }
            State state = For(body, human, marker);
            if (state != null)
            {
                bodyApparel = body.apparel != null ? state.apparel : null;
                bodyPrimary = body.equipment != null ? (RawPrimary(body) ?? state.primary) : null;
                return;
            }
            bodyApparel = RawApparel(body);
            bodyPrimary = RawPrimary(body);
        }

        private static void BindVerbs(Pawn caster,State state)
        {
            ThingOwner sourceEquipOwner = state.human.equipment != null ? rawEquipmentOwner(state.human.equipment) : null;
            for(int i=0;i<state.sourceEquipment.Count;i++)
            {
                if(state.sourceEquipment[i].holdingOwner!=sourceEquipOwner)continue;
                CompEquippable comp=state.sourceEquipment[i].GetComp<CompEquippable>();
                if(comp!=null)
                {
                    List<Verb> verbs=comp.AllVerbs;
                    for(int v=0;v<verbs.Count;v++)verbs[v].caster=caster;
                }
            }
            ThingOwner sourceApparelOwner = state.human.apparel != null ? rawApparelOwner(state.human.apparel) : null;
            for(int i=0;i<state.sourceApparel.Count;i++)
            {
                if(state.sourceApparel[i].holdingOwner!=sourceApparelOwner)continue;
                CompApparelVerbOwner comp=state.sourceApparel[i].GetComp<CompApparelVerbOwner>();
                if(comp!=null)
                {
                    List<Verb> verbs=comp.AllVerbs;
                    for(int v=0;v<verbs.Count;v++)verbs[v].caster=caster;
                }
            }
            for(int i=0;i<state.sourceHealth.Count;i++)
            {
                HediffWithComps h=state.sourceHealth[i] as HediffWithComps;
                if(h?.comps==null)continue;
                for(int j=0;j<h.comps.Count;j++)
                {
                    HediffComp_VerbGiver giver=h.comps[j] as HediffComp_VerbGiver;
                    if(giver==null)continue;
                    VerbTracker tracker=giver.VerbTracker;
                    if(tracker!=null)
                    {
                        List<Verb> verbs=tracker.AllVerbs;
                        for(int v=0;v<verbs.Count;v++)verbs[v].caster=caster;
                    }
                }
            }
        }

        public static void Tick(Pawn body)
        {
            TickForSource(body, null, null);
        }

        internal static void TickForSource(Pawn body, Pawn source, HediffComp_Transformed marker = null)
        {
            State state=For(body, source, marker);if(state==null||Find.TickManager==null)return;
            int now=Find.TickManager.TicksGame;if(state.lastTick==now)return;state.lastTick=now;
            for(int i=0;i<state.borrowed.Count;i++)
            {
                ThingWithComps item=state.borrowed[i];
                if(!item.Destroyed&&item.def.tickerType==TickerType.Normal)tick(item);
            }
        }
        public static void TickInterval(Pawn body,int delta)
        {
            TickIntervalForSource(body, null, delta, null);
        }

        internal static void TickIntervalForSource(Pawn body, Pawn source, int delta, HediffComp_Transformed marker = null)
        {
            State state=For(body, source, marker);if(state==null||Find.TickManager==null||delta<=0)return;
            int now=Find.TickManager.TicksGame;if(state.lastInterval==now)return;state.lastInterval=now;
            for(int i=0;i<state.borrowed.Count;i++)
            {
                ThingWithComps item=state.borrowed[i];
                if(item.Destroyed)continue;
                if(item.def.tickerType==TickerType.Normal)interval(item,delta);
                else if(item.def.tickerType==TickerType.Rare&&body.IsHashIntervalTick(250,delta))item.TickRare();
                else if(item.def.tickerType==TickerType.Long&&body.IsHashIntervalTick(2000,delta))item.TickLong();
            }
            if(body.IsHashIntervalTick(250,delta))state.human.apparel?.ApparelTrackerTickRare();
        }
        public static Pawn BeginCoverage(Pawn body)
        {
            Pawn previous=coverageBody;
            coverageBody=SharedFormIdentity.HumanFor(body)!=null?body:null;
            return previous;
        }
        public static void EndCoverage(Pawn previous){coverageBody=previous;}
        public static BodyPartRecord MapCoverage(ApparelProperties properties,BodyPartRecord part)
        {
            State state=For(coverageBody);
            if(state==null||part==null)return null;
            bool borrowed=false;
            for(int i=0;i<state.sourceApparel.Count;i++)
                if(state.sourceApparel[i].def.apparel==properties){borrowed=true;break;}
            if(!borrowed)return null;
            BodyDef animalBody=coverageBody.RaceProps.body;
            BodyDef sourceBody=state.human.RaceProps.body;
            Dictionary<BodyPartRecord,BodyPartRecord> map=GetCoverageMap(animalBody,sourceBody);
            BodyPartRecord mapped;
            return map.TryGetValue(part,out mapped)?mapped:null;
        }
        private static Dictionary<BodyPartRecord,BodyPartRecord> GetCoverageMap(BodyDef animalBody,BodyDef sourceBody)
        {
            ConditionalWeakTable<BodyDef,Dictionary<BodyPartRecord,BodyPartRecord>> inner=coverageCache.GetOrCreateValue(animalBody);
            Dictionary<BodyPartRecord,BodyPartRecord> map;
            if(inner.TryGetValue(sourceBody,out map))return map;
            map=new Dictionary<BodyPartRecord,BodyPartRecord>(animalBody.AllParts.Count);
            List<BodyPartRecord> animalParts=animalBody.AllParts;
            for(int i=0;i<animalParts.Count;i++)
                map[animalParts[i]]=ComputeMappedPart(animalBody,sourceBody,animalParts[i]);
            inner.Remove(sourceBody);
            inner.Add(sourceBody,map);
            return map;
        }
        private static BodyPartRecord ComputeMappedPart(BodyDef animalBody,BodyDef sourceBody,BodyPartRecord part)
        {
            if(part==animalBody.corePart)return sourceBody.corePart;
            for(int i=0;i<sourceBody.AllParts.Count;i++)
                if(sourceBody.AllParts[i].def==part.def)return sourceBody.AllParts[i];
            for(BodyPartRecord ancestor=part;ancestor!=null;ancestor=ancestor.parent)
            {
                string function=null;
                if(HasTag(ancestor,"ManipulationLimbCore")||HasTag(ancestor,"ManipulationLimbSegment")||HasTag(ancestor,"ManipulationLimbDigit"))function="ManipulationLimbCore";
                else if(HasTag(ancestor,"MovingLimbCore")||HasTag(ancestor,"MovingLimbSegment")||HasTag(ancestor,"MovingLimbDigit"))function="MovingLimbCore";
                if(function!=null)
                    for(int i=0;i<sourceBody.AllParts.Count;i++)if(HasTag(sourceBody.AllParts[i],function))return sourceBody.AllParts[i];
                if(ancestor.def.defName.IndexOf("head",StringComparison.OrdinalIgnoreCase)>=0||HasTag(ancestor,"ConsciousnessSource"))
                    for(int i=0;i<sourceBody.AllParts.Count;i++)
                        if(sourceBody.AllParts[i].def.defName=="Head")return sourceBody.AllParts[i];
            }
            // A tail or other species-only appendage has no humanoid slot.
            // Treat its borrowed garment coverage as torso coverage instead
            // of silently losing the original armor's coverage.
            return sourceBody.corePart;
        }
        private static bool HasTag(BodyPartRecord part,string tag)
        {
            if(part.def.tags==null)return false;
            for(int i=0;i<part.def.tags.Count;i++)if(part.def.tags[i].defName==tag)return true;
            return false;
        }

        internal static bool RedirectMutation(object tracker,Thing item,MethodBase method,object[] args,ref bool result)
        {
            if(mutation>0||IsNativeOnly)return true;
            Pawn body=tracker is Pawn_ApparelTracker?(tracker as Pawn_ApparelTracker).pawn:(tracker as Pawn_EquipmentTracker)?.pawn;
            State state=For(body);if(state==null||item==null)return true;
            Pawn human=state.human;
            object source=item is Apparel?(object)human.apparel:human.equipment;
            ThingOwner owner=source is Pawn_ApparelTracker?(source as Pawn_ApparelTracker).GetDirectlyHeldThings():(source as Pawn_EquipmentTracker)?.GetDirectlyHeldThings();
            if(owner==null||item.holdingOwner!=owner)return true;
            mutation++;
            try
            {
                if(source is Pawn_ApparelTracker)((Pawn_ApparelTracker)source).pawn=body;
                else ((Pawn_EquipmentTracker)source).pawn=body;
                object value=((MethodInfo)method).Invoke(source,args);
                if(value is bool)result=(bool)value;
            }
            finally
            {
                if(source is Pawn_ApparelTracker)((Pawn_ApparelTracker)source).pawn=human;
                else ((Pawn_EquipmentTracker)source).pawn=human;
                mutation--;
                if(item.holdingOwner!=owner)
                {
                    // Native unequip callbacks see the physical body. Refresh
                    // the human's real ability list and its projected view too.
                    human.abilities?.Notify_TemporaryAbilitiesChanged();
                    SharedFormAbilities.MarkDirty(human);
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker),"get_WornApparel")]
    public static class Patch_SharedGearApparelView
    { public static void Postfix(Pawn ___pawn,ref List<Apparel> __result){__result=SharedFormGear.ApparelFor(___pawn,__result);} }
    [HarmonyPatch(typeof(Pawn_EquipmentTracker),"get_AllEquipmentListForReading")]
    public static class Patch_SharedGearEquipmentView
    { public static void Postfix(Pawn ___pawn,ref List<ThingWithComps> __result){__result=SharedFormGear.EquipmentFor(___pawn,__result);} }
    [HarmonyPatch(typeof(Pawn_EquipmentTracker),"get_Primary")]
    public static class Patch_SharedGearPrimary
    { public static void Postfix(Pawn ___pawn,ref ThingWithComps __result){__result=SharedFormGear.PrimaryFor(___pawn,__result);} }
    [HarmonyPatch(typeof(Apparel),"get_Wearer")]
    public static class Patch_SharedGearWearer
    { public static void Postfix(ref Pawn __result){__result=SharedFormGear.ActiveWearer(__result);} }

    [HarmonyPatch(typeof(CompBiocodable),"IsBiocodedFor",new[]{typeof(Thing),typeof(Pawn)})]
    public static class Patch_SharedGearBiocodedIdentity
    {
        public static void Prefix(ref Pawn pawn)
        { pawn=SharedFormIdentity.HumanFor(pawn)??pawn; }
    }

    [HarmonyPatch(typeof(ArmorUtility),"GetPostArmorDamage")]
    public static class Patch_SharedGearArmorContext
    {
        public static void Prefix(Pawn pawn,out Pawn __state){__state=SharedFormGear.BeginCoverage(pawn);}
        public static Exception Finalizer(Pawn __state,Exception __exception){SharedFormGear.EndCoverage(__state);return __exception;}
    }
    [HarmonyPatch(typeof(ApparelProperties),"CoversBodyPart",new[]{typeof(BodyPartRecord)})]
    public static class Patch_SharedGearArmorCoverage
    {
        [ThreadStatic] private static bool queryingSource;
        public static bool Prefix(ApparelProperties __instance,BodyPartRecord __0,ref bool __result)
        {
            if(queryingSource)return true;
            BodyPartRecord source=SharedFormGear.MapCoverage(__instance,__0);
            if(source==null||ReferenceEquals(source,__0))return true;
            queryingSource=true;
            try { __result=__instance.CoversBodyPart(source); }
            finally { queryingSource=false; }
            return false;
        }
    }

    [HarmonyPatch]
    public static class Patch_SharedGearDropApparel
    {
        public static MethodBase TargetMethod(){return AccessTools.Method(typeof(Pawn_ApparelTracker),"TryDrop",new[]{typeof(Apparel),typeof(Apparel).MakeByRefType(),typeof(IntVec3),typeof(bool)});}
        public static bool Prefix(Pawn_ApparelTracker __instance,object[] __args,MethodBase __originalMethod,ref bool __result)
        {return SharedFormGear.RedirectMutation(__instance,__args[0] as Thing,__originalMethod,__args,ref __result);}
    }
    [HarmonyPatch]
    public static class Patch_SharedGearDropEquipment
    {
        public static MethodBase TargetMethod(){return AccessTools.Method(typeof(Pawn_EquipmentTracker),"TryDropEquipment",new[]{typeof(ThingWithComps),typeof(ThingWithComps).MakeByRefType(),typeof(IntVec3),typeof(bool)});}
        public static bool Prefix(Pawn_EquipmentTracker __instance,object[] __args,MethodBase __originalMethod,ref bool __result)
        {return SharedFormGear.RedirectMutation(__instance,__args[0] as Thing,__originalMethod,__args,ref __result);}
    }

    // Human garments need no invented animal texture. Rendering still uses
    // actual animal garments; the borrowed objects supply native gameplay.
    [HarmonyPatch]
    public static class Patch_SharedGearPhysicalRendering
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {return AccessTools.GetDeclaredMethods(typeof(PawnRenderTree)).Where(m=>m.Name=="TrySetupGraphIfNeeded").Cast<MethodBase>();}
        public static void Prefix(out int __state){__state=SharedFormGear.BeginNativeRender();}
        public static void Finalizer(int __state){SharedFormGear.EndNativeRender(__state);}
    }
}
