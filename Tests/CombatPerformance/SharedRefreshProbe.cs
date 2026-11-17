using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace WildShiftCombatPerformance
{
    // TEST ONLY. Observe private versions without evaluating any source
    // signatures or conditional affecters a second time.
    internal static class SharedRefreshProbe
    {
        private sealed class Row
        {
            public string Pawn;
            public long Calls,ReentrantCalls,SameTickCalls,DistinctTicks;
            public long SourceChangedCalls,ConditionChecks,ConditionChangedCalls;
            public long GenerationChangedCalls,GenerationIncrementTotal,HealthGenerationChangedCalls,HealthGenerationIncrementTotal;
            public long ExternalChangedCalls,SystemsVersionChangedCalls,AnatomyVersionChangedCalls;
            public int LastTick=int.MinValue;
        }
        public struct Snapshot
        {
            internal object Instance;
            public int Generation,HealthGeneration,SystemsVersion,AnatomyVersion;
            public bool Active,Reentrant;
        }
        private static readonly Dictionary<object,Row> rows=new Dictionary<object,Row>();
        private static readonly Dictionary<Pawn,long> dirtyCaches=new Dictionary<Pawn,long>();
        public static bool Installed;
        public static bool ScopeEnabled;
        private static bool Enabled {get{return Diagnostics.Enabled||ScopeEnabled;}}
        public static void Install(Harmony harmony)
        {
            if(Installed)return;
            Type state=AccessTools.TypeByName("WildShift.SharedFormStats+State");
            if(state==null)throw new InvalidOperationException("Shared refresh diagnostic: Stats.State unavailable");
            foreach(string field in new[]{"refreshing","generation","healthGeneration","systemsVersion","anatomyVersion"})
                if(AccessTools.Field(state,field)==null)throw new InvalidOperationException("Shared refresh diagnostic: missing field "+field);
            harmony.Patch(AccessTools.DeclaredMethod(state,"Refresh"),prefix:new HarmonyMethod(typeof(SharedRefreshProbe),nameof(Before)),postfix:new HarmonyMethod(typeof(SharedRefreshProbe),nameof(After)));
            harmony.Patch(AccessTools.DeclaredMethod(state,"CaptureConditions"),prefix:new HarmonyMethod(typeof(SharedRefreshProbe),nameof(SourceChanged)));
            harmony.Patch(AccessTools.DeclaredMethod(state,"RefreshConditions"),postfix:new HarmonyMethod(typeof(SharedRefreshProbe),nameof(ConditionChecked)));
            harmony.Patch(AccessTools.Method(typeof(HediffSet),"DirtyCache"),prefix:new HarmonyMethod(typeof(SharedRefreshProbe),nameof(HealthDirty)));
            Installed=true;
        }
        public static void Reset(){rows.Clear();dirtyCaches.Clear();}
        public static void HealthDirty(Pawn ___pawn){if(!Enabled||___pawn==null)return;long count;dirtyCaches.TryGetValue(___pawn,out count);dirtyCaches[___pawn]=count+1;}
        private static Row For(object instance)
        {
            Row row;if(!rows.TryGetValue(instance,out row)){row=new Row();rows.Add(instance,row);}return row;
        }
        public static void Before(object __instance,Hediff __0,bool ___refreshing,int ___generation,int ___healthGeneration,int ___systemsVersion,int ___anatomyVersion,out Snapshot __state)
        {
            __state=default(Snapshot);if(!Enabled)return;
            Row row=For(__instance);row.Calls++;
            if(row.Pawn==null)row.Pawn=__0.pawn.GetUniqueLoadID();
            int tick=Find.TickManager.TicksGame;
            if(row.LastTick==tick)row.SameTickCalls++;else{row.DistinctTicks++;row.LastTick=tick;}
            if(___refreshing)row.ReentrantCalls++;
            __state=new Snapshot{Active=true,Instance=__instance,Reentrant=___refreshing,Generation=___generation,HealthGeneration=___healthGeneration,SystemsVersion=___systemsVersion,AnatomyVersion=___anatomyVersion};
        }
        public static void After(Snapshot __state,int ___generation,int ___healthGeneration,int ___systemsVersion,int ___anatomyVersion)
        {
            if(!__state.Active||__state.Reentrant)return;
            Row row=For(__state.Instance);
            if(__state.Generation!=___generation){row.GenerationChangedCalls++;row.GenerationIncrementTotal+=unchecked(___generation-__state.Generation);}
            if(__state.HealthGeneration!=___healthGeneration){row.HealthGenerationChangedCalls++;row.HealthGenerationIncrementTotal+=unchecked(___healthGeneration-__state.HealthGeneration);}
            bool systems=__state.SystemsVersion!=___systemsVersion,anatomy=__state.AnatomyVersion!=___anatomyVersion;
            if(systems)row.SystemsVersionChangedCalls++;if(anatomy)row.AnatomyVersionChangedCalls++;if(systems||anatomy)row.ExternalChangedCalls++;
        }
        public static void SourceChanged(object __instance){if(Enabled)For(__instance).SourceChangedCalls++;}
        public static void ConditionChecked(object __instance,bool __result){if(!Enabled)return;Row row=For(__instance);row.ConditionChecks++;if(__result)row.ConditionChangedCalls++;}
        public static object Result(){return new{Installed,DirtyCacheTotal=dirtyCaches.Values.Sum(),DirtyCacheByPawn=dirtyCaches.Select(pair=>new{Pawn=pair.Key.GetUniqueLoadID(),Calls=pair.Value}).ToArray(),Rows=rows.Values.Select(row=>new{row.Pawn,row.Calls,row.ReentrantCalls,row.SameTickCalls,row.DistinctTicks,row.SourceChangedCalls,row.ConditionChecks,row.ConditionChangedCalls,row.GenerationChangedCalls,row.GenerationIncrementTotal,row.HealthGenerationChangedCalls,row.HealthGenerationIncrementTotal,row.ExternalChangedCalls,row.SystemsVersionChangedCalls,row.AnatomyVersionChangedCalls}).ToArray(),Scope="Diagnostic-only counters for the current enabled scope, reset between transform/draft/battle scopes. SourceChangedCalls observes CaptureConditions, which current Stats.State calls only when sourceChanged is true. Condition changes observe the actual RefreshConditions return; external changes compare private systems/anatomy versions across non-reentrant Refresh. No signature or affecter is recomputed by this observer. Inclusive method timing and instrumentation overhead remain separate."};}
    }
}
