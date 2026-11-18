using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Timings isolate production entry points in an initialized
    // game. They are microbenchmarks, not a colony TPS or visible UI claim.
    public static class SharedPerformanceEngineChecks
    {
        private static float sink;
        private static object objectSink;
        public static void Run(Map map, Action<bool,string> check)
        {
            var rows = new List<string> { "Case\tIterations\tSample\tMilliseconds\tAllocatedBytes" };
            File.WriteAllLines(Path.Combine(Boot.Output,"micro-runtime-context.txt"),new[]{
                "Runtime="+Environment.Version,
                "TieredCompilation="+Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                "TieredPGO="+Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                "WarmupMs=250; shared MoveSpeed warmupMs=1500"
            });
            Pawn ordinary = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"), Faction.OfPlayer);
            Pawn human = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn:true, canGeneratePawnRelations:false, mustBeCapableOfViolence:true,
                forcedXenotype:XenotypeDefOf.Baseliner));
            Pawn body = null;
            try
            {
                ordinary.health.RemoveAllHediffs();
                // Exercise the hashed-key CWT path too, as another mod may
                // already have assigned this pawn an identity hash.
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ordinary);
                foreach (int count in new[] {0,5,20,100})
                {
                    ordinary.health.hediffSet.hediffs.Clear();
                    for (int i=0;i<count;i++) ordinary.health.hediffSet.hediffs.Add(new Hediff
                        { def=HediffDefOf.Cut, pawn=ordinary });
                    ordinary.health.hediffSet.DirtyCache();
                    Measure(rows,"ordinary-runtime-pair-H"+count,200000,()=>
                        { SharedFormRuntime.Tick(ordinary); SharedFormRuntime.TickInterval(ordinary,1); });
                    check(SharedFormIdentity.HumanFor(ordinary)==null,
                        "performance/ordinary pawn with "+count+" hediffs remains outside shared runtime");
                }
                ordinary.health.hediffSet.hediffs.Clear();
                human.health.RemoveAllHediffs();
                foreach(Trait trait in human.story.traits.allTraits.ToList())human.story.traits.RemoveTrait(trait);
                foreach(Gene gene in human.genes.GenesListForReading.ToList())human.genes.RemoveGene(gene);
                foreach(Apparel apparel in human.apparel.WornApparel.ToList())
                { human.apparel.Remove(apparel);apparel.Destroy(DestroyMode.Vanish); }
                human.equipment.DestroyAllEquipment();
                human.ideo?.SetIdeo(null);
                human.needs.AddOrRemoveNeedsAsAppropriate();
                check(human.Ideo==null && human.story.traits.allTraits.Count==0
                    && human.genes.GenesListForReading.Count==0 && human.health.hediffSet.hediffs.Count==0,
                    "performance/controlled source excludes random ideology, traits, genes and health modifiers");
                GenSpawn.Spawn(human,CellFinder.StandableCellNear(map.Center,map,12f),map);
                ThingWithComps weapon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MeleeWeapon_LongSword"),ThingDefOf.Plasteel);
                human.equipment.AddEquipment(weapon);
                human.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor")),false);
                PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");
                TransformUtility.AddOrGetShapeshifter(human,kind);
                if(!TransformUtility.TransformToAnimal(human,kind)) throw new InvalidOperationException("Performance transform failed");
                body=SharedFormIdentity.ActiveFormFor(human);
                check(body!=null,"performance/real transformed body is registered");
                Pawn active=body;
                Measure(rows,"active-runtime-same-tick-pair",50000,()=>
                    { SharedFormRuntime.Tick(active); SharedFormRuntime.TickInterval(active,1); });
                Measure(rows,"active-gear-primary",100000,()=>objectSink=active.equipment.Primary);
                Measure(rows,"active-move-speed-uncached",20000,()=>sink=active.GetStatValue(StatDefOf.MoveSpeed,cacheStaleAfterTicks:0));
                Action scope=RenderScopePair();
                check(!SharedFormGear.IsNativeOnly,"performance/render scope starts inactive");
                scope();
                check(!SharedFormGear.IsNativeOnly,"performance/render scope restores native visibility");
                using(SharedFormGear.NativeOnly())
                {
                    scope();
                    check(SharedFormGear.IsNativeOnly,"performance/render scope preserves an outer native-only scope");
                }
                check(!SharedFormGear.IsNativeOnly,"performance/outer scope restores native visibility");
                Measure(rows,"render-scope-prefix-finalizer",500000,scope);
                check(active.equipment.Primary==weapon && weapon.holdingOwner==human.equipment.GetDirectlyHeldThings(),
                    "performance/repeated gear reads preserve canonical weapon and ownership");
                check(TransformUtility.RevertToHuman(body,false)==human,"performance/return succeeds after measured shared queries");
                body=null;
            }
            finally
            {
                File.WriteAllLines(Path.Combine(Boot.Output,"bottom-up-microbench.tsv"),rows);
                if(body!=null && SharedFormIdentity.HumanFor(body)==human) TransformUtility.RevertToHuman(body,false);
                ordinary.health.hediffSet.hediffs.Clear();
                if(!ordinary.Destroyed)ordinary.Destroy(DestroyMode.Vanish);
                if(!human.Destroyed)human.Destroy(DestroyMode.Vanish);
                objectSink=null;
            }
        }
        private static Action RenderScopePair()
        {
            MethodInfo prefix=AccessTools.Method(typeof(Patch_SharedGearPhysicalRendering),"Prefix");
            MethodInfo finalizer=AccessTools.Method(typeof(Patch_SharedGearPhysicalRendering),"Finalizer");
            Type state=prefix.GetParameters()[0].ParameterType.GetElementType();
            var method=new DynamicMethod("NativeRenderScopePair",typeof(void),Type.EmptyTypes,typeof(SharedPerformanceEngineChecks),true);
            ILGenerator il=method.GetILGenerator();
            LocalBuilder local=il.DeclareLocal(state);
            il.Emit(OpCodes.Ldloca,local);il.Emit(OpCodes.Call,prefix);
            il.Emit(OpCodes.Ldloc,local);il.Emit(OpCodes.Call,finalizer);il.Emit(OpCodes.Ret);
            return (Action)method.CreateDelegate(typeof(Action));
        }
        private static void Measure(List<string> rows,string name,int iterations,Action action)
        {
            MethodInfo allocation=AccessTools.Method(typeof(GC),"GetAllocatedBytesForCurrentThread");
            Func<long> bytes=allocation==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocation);
            // Some Mono runtimes expose this API but always return zero.
            // Report allocation as unavailable instead of claiming no GC cost.
            if(bytes!=null)
            {
                long calibration=bytes();
                objectSink=new byte[1024];
                if(bytes()-calibration<1024)bytes=null;
            }
            for(int i=0;i<500;i++)action();
            // Fast negative paths must not deprive later shared queries of
            // JIT warmup that a slower baseline happened to provide.
            var warmup=Stopwatch.StartNew();
            int warmupMs=name=="active-move-speed-uncached"?1500:250;
            while(warmup.ElapsedMilliseconds<warmupMs)
                for(int i=0;i<100;i++)action();
            for(int sample=0;sample<5;sample++)
            {
                long before=bytes==null?0:bytes();
                long start=Stopwatch.GetTimestamp();
                for(int i=0;i<iterations;i++)action();
                double elapsed=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
                long allocated=bytes==null?-1:bytes()-before;
                rows.Add(name+"\t"+iterations+"\t"+sample+"\t"+elapsed.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\t"+allocated);
            }
        }
    }
}
