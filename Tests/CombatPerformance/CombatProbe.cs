using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WildShiftCombatPerformance
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output=Path.Combine(GenFilePaths.SaveDataFolderPath,"CombatResults");
        public static readonly Harmony Harmony=new Harmony("wildshift.tests.combatperformance");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground=true;
            FrameProbe.Install(Harmony);
            Harmony.Patch(AccessTools.Method(typeof(Root_Play),"SetupForQuickTestPlay"),postfix:new HarmonyMethod(typeof(Boot),nameof(SmallMap)));
            Harmony.Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(Timing),nameof(Timing.Before)),postfix:new HarmonyMethod(typeof(Timing),nameof(Timing.After)));
            foreach(MethodInfo method in typeof(AttackTargetFinder).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Where(m=>m.Name=="BestAttackTarget"))
                Harmony.Patch(method,prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.TargetSearch)));
            foreach(MethodInfo method in typeof(AttackTargetFinder).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Where(m=>m.Name=="BestShootTargetFromCurrentPosition"))
                Harmony.Patch(method,prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.ShootSearch)));
            Harmony.Patch(AccessTools.Method(typeof(Thing),"TakeDamage",new[]{typeof(DamageInfo)}),prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.Damage)));
            Harmony.Patch(AccessTools.Method(typeof(Pawn_JobTracker),"StartJob"),prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.JobStart)));
            Type orders=AccessTools.TypeByName("RimWorld.JobGiver_Orders");
            MethodInfo orderJob=orders==null?null:AccessTools.DeclaredMethod(orders,"TryGiveJob");
            if(orderJob!=null){Harmony.Patch(orderJob,prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.OrdersThink)));Activity.NativeOrdersThinkObserverInstalled=true;}
            Type react=AccessTools.TypeByName("RimWorld.JobGiver_ReactToCloseMeleeThreat")??AccessTools.TypeByName("Verse.AI.JobGiver_ReactToCloseMeleeThreat");
            MethodInfo reactJob=react==null?null:AccessTools.DeclaredMethod(react,"TryGiveJob");
            if(reactJob!=null){Harmony.Patch(reactJob,prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.CloseMeleeThink)));Activity.NativeCloseMeleeThinkObserverInstalled=true;}
            Type launch=AccessTools.TypeByName("Verse.Verb_LaunchProjectile")??AccessTools.TypeByName("RimWorld.Verb_LaunchProjectile");
            MethodInfo shot=launch==null?null:AccessTools.Method(launch,"TryCastShot");
            if(shot!=null)Harmony.Patch(shot,postfix:new HarmonyMethod(typeof(Activity),nameof(Activity.Shot)));
            Type jumping=AccessTools.TypeByName("RimWorld.Utility.JumpUtility")??AccessTools.TypeByName("RimWorld.JumpUtility")??AccessTools.TypeByName("Verse.JumpUtility");
            if(jumping!=null)foreach(MethodInfo method in jumping.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Where(m=>m.Name=="DoJump"))
                Harmony.Patch(method,prefix:new HarmonyMethod(typeof(Activity),nameof(Activity.Jump)));
        }
        public static void SmallMap()
        {
            XDocument input=XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"combat-inputs.xml"));
            Find.GameInitData.mapSize=int.Parse(input.Root.Element("ArenaSize")?.Value??"75");
        }
        public static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        public static void Json(string name,object value){File.WriteAllText(Path.Combine(Output,name),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));}
    }

    internal static class Timing
    {
        private static readonly long[] histogram=new long[200001];
        private static readonly long[] firstTicks=new long[64],firstAllocated=new long[64];
        public static int FirstTickCount;
        public static bool CaptureFirst;
        public static bool Enabled;
        public static long Count,Sum,Max,Overflow,Allocated;
        public struct State{public long Time,Bytes;}
        public static void Reset(){Array.Clear(histogram,0,histogram.Length);Count=Sum=Max=Overflow=Allocated=0;}
        public static void ResetFirstTicks(){Array.Clear(firstTicks,0,firstTicks.Length);Array.Clear(firstAllocated,0,firstAllocated.Length);FirstTickCount=0;CaptureFirst=false;}
        public static void Before(out State __state){__state=Enabled||CaptureFirst?new State{Time=Stopwatch.GetTimestamp(),Bytes=GC.GetAllocatedBytesForCurrentThread()}:default(State);}
        public static void After(State __state)
        {
            if(__state.Time==0)return;
            long ticks=Stopwatch.GetTimestamp()-__state.Time,us=ticks*1000000L/Stopwatch.Frequency;
            if(CaptureFirst&&FirstTickCount<firstTicks.Length){firstTicks[FirstTickCount]=ticks;firstAllocated[FirstTickCount]=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes;FirstTickCount++;if(FirstTickCount==firstTicks.Length)CaptureFirst=false;}
            if(!Enabled)return;
            Count++;Sum+=ticks;Allocated+=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes;if(ticks>Max)Max=ticks;
            if(us>=histogram.Length){us=histogram.Length-1;Overflow++;}histogram[(int)us]++;
        }
        public static double Percentile(double p){long n=0,target=(long)Math.Ceiling(Count*p);for(int i=0;i<histogram.Length;i++){n+=histogram[i];if(n>=target)return i;}return histogram.Length-1;}
        public static double[] FirstTickMs(){return firstTicks.Take(FirstTickCount).Select(t=>t*1000.0/Stopwatch.Frequency).ToArray();}
        public static long[] FirstTickAllocated(){return firstAllocated.Take(FirstTickCount).ToArray();}
    }

    internal static class Activity
    {
        public static readonly HashSet<Pawn> Actors=new HashSet<Pawn>(),Targets=new HashSet<Pawn>();
        public static bool Enabled;
        public static long Searches,ShootSearches,Shots,EnemyShots,DamageEvents,ActorDamageEvents,ActorBulletHits,NativeJumps;
        public static double IncomingDamage,ActorRawDamage;
        public static long ActorJobStarts,OrdersThinkQueries,CloseMeleeThreatThinkQueries;
        public static bool NativeOrdersThinkObserverInstalled,NativeCloseMeleeThinkObserverInstalled;
        private static readonly Dictionary<JobDef,long> actorJobStarts=new Dictionary<JobDef,long>();
        public static void Reset(){Searches=ShootSearches=Shots=EnemyShots=DamageEvents=ActorDamageEvents=ActorBulletHits=NativeJumps=ActorJobStarts=OrdersThinkQueries=CloseMeleeThreatThinkQueries=0;IncomingDamage=ActorRawDamage=0;actorJobStarts.Clear();}
        public static void JobStart(Pawn ___pawn,Job __0){if(!Enabled||!Actors.Contains(___pawn))return;ActorJobStarts++;if(__0?.def==null)return;long count;actorJobStarts.TryGetValue(__0.def,out count);actorJobStarts[__0.def]=count+1;}
        public static void OrdersThink(Pawn __0){if(Enabled&&Actors.Contains(__0))OrdersThinkQueries++;}
        public static void CloseMeleeThink(Pawn __0){if(Enabled&&Actors.Contains(__0))CloseMeleeThreatThinkQueries++;}
        public static object JobActivity(){return new{ActorJobStarts,OrdersThinkQueries,CloseMeleeThreatThinkQueries,NativeOrdersThinkObserverInstalled,NativeCloseMeleeThinkObserverInstalled,JobStarts=actorJobStarts.Select(pair=>new{Job=pair.Key.defName,Calls=pair.Value}).ToArray(),Scope="Constant native job/think observer counters while Activity.Enabled during battle. No jobs or think-tree results are replaced. Unavailable optional observer methods are indicated by installed flags."};}
        public static void TargetSearch(object __0){if(Enabled&&__0 is Pawn&&Actors.Contains((Pawn)__0))Searches++;}
        public static void ShootSearch(object __0){if(Enabled&&__0 is Pawn&&Actors.Contains((Pawn)__0))ShootSearches++;}
        public static void Shot(Verb __instance,bool __result){if(!Enabled||!__result)return;Pawn caster=__instance.CasterPawn;if(Actors.Contains(caster))Shots++;else if(Targets.Contains(caster))EnemyShots++;}
        public static void Damage(Thing __instance,DamageInfo __0)
        {
            if(!Enabled||!(__instance is Pawn))return;Pawn pawn=(Pawn)__instance;
            if(Targets.Contains(pawn)){DamageEvents++;IncomingDamage+=__0.Amount;}
            if(Actors.Contains(pawn)){ActorDamageEvents++;ActorRawDamage+=__0.Amount;if(__0.Def==DamageDefOf.Bullet)ActorBulletHits++;}
        }
        public static void Jump(object __0){if(Enabled&&__0 is Pawn&&Actors.Contains((Pawn)__0))NativeJumps++;}
    }

    internal static class Diagnostics
    {
        private sealed class Row{public string Method;public long Calls,Ticks,Allocated;}
        public struct MethodState{public long Time,Bytes;}
        private sealed class Transition{public string Actor,Phase;public long Time,Bytes;public string[] WorkersBefore;}
        private static readonly Dictionary<MethodBase,Row> rows=new Dictionary<MethodBase,Row>();
        private static readonly List<object> transitionProfiles=new List<object>();
        private static readonly FieldInfo[] workerCacheFields=typeof(StatDef).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(field=>typeof(StatWorker).IsAssignableFrom(field.FieldType)).ToArray();
        private static Transition transition;
        public static bool Enabled,Installed;
        public static void Install()
        {
            if(Installed)return;
            var methods=new HashSet<MethodInfo>();
            foreach(string typeName in new[]{"WildShift.SharedFormIdentity","WildShift.SharedFormStats","WildShift.SharedFormHealth","WildShift.SharedFormGear","WildShift.SharedFormSystems","WildShift.SharedFormAbilities"})
            {
                Type type=AccessTools.TypeByName(typeName);if(type==null)continue;
                foreach(MethodInfo method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.DeclaredOnly))
                    if(!method.ContainsGenericParameters&&!method.IsGenericMethodDefinition&&!method.ReturnType.IsByRef&&!method.Name.StartsWith("<"))methods.Add(method);
                foreach(Type nested in type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))
                    foreach(MethodInfo method in nested.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                        if(method.Name=="Refresh"||method.Name=="GetStage")methods.Add(method);
            }
            foreach(Type type in new[]{typeof(AttackTargetFinder),typeof(Pawn),typeof(Verb),typeof(StatWorker)})
                foreach(MethodInfo method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                    if(new[]{"BestAttackTarget","BestShootTargetFromCurrentPosition","TryGetAttackVerb","get_CurrentEffectiveVerb","get_IsColonistPlayerControlled","CanHitTarget","TryFindShootLineFromTo","GetValue"}.Contains(method.Name)&&!method.IsAbstract)methods.Add(method);
            methods.Add(AccessTools.Method(typeof(HediffSet),"DirtyCache"));
            // Narrow additions, rather than observing every method in these
            // additional types or every nested helper.
            foreach(var target in new[]{
                new[]{"WildShift.SharedFormBiology","Initialize","Bind","SourceMutationCompleted","InvalidateSource","Tick","TickInterval"},
                new[]{"WildShift.SharedFormAnatomy","ModifierVersion","Signature","PartSignature","InstalledParts","Ratio","ApplyRawCapacity"},
                new[]{"WildShift.Patch_SharedHealthCommandRecipients","Prefix","Postfix"},
                new[]{"WildShift.SharedFormRuntime","Initialize","Tick","TickInterval"}})
            {
                Type type=AccessTools.TypeByName(target[0]);if(type==null)continue;
                foreach(MethodInfo method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.DeclaredOnly))
                    if(target.Skip(1).Contains(method.Name))methods.Add(method);
            }
            Type anatomy=AccessTools.TypeByName("WildShift.SharedFormAnatomy+State");
            if(anatomy!=null)methods.Add(AccessTools.DeclaredMethod(anatomy,"Refresh"));
            Type stats=AccessTools.TypeByName("WildShift.SharedFormStats+State");
            if(stats!=null)foreach(string name in new[]{"Build","CaptureConditions","RefreshConditions","PrepareHealth","PrepareWorker"})methods.Add(AccessTools.DeclaredMethod(stats,name));
            methods.Add(AccessTools.PropertyGetter(typeof(StatDef),"Worker"));
            foreach(MethodInfo method in typeof(StatWorker).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name=="ClearCacheForThing"||method.Name=="Init")methods.Add(method);
            methods.Remove(null);
            foreach(MethodInfo method in methods)
            {
                rows[method]=new Row{Method=method.DeclaringType.FullName+"."+method.Name+"("+string.Join(",",method.GetParameters().Select(p=>p.ParameterType.Name))+")"};
                Boot.Harmony.Patch(method,prefix:new HarmonyMethod(typeof(Diagnostics),nameof(Before)),postfix:new HarmonyMethod(typeof(Diagnostics),nameof(After)));
            }
            SharedRefreshProbe.Install(Boot.Harmony);
            Installed=true;
        }
        public static void Before(out MethodState __state){__state=Enabled?new MethodState{Time=Stopwatch.GetTimestamp(),Bytes=GC.GetAllocatedBytesForCurrentThread()}:default(MethodState);}
        public static void After(MethodBase __originalMethod,MethodState __state){if(__state.Time==0||!Enabled)return;Row row=rows[__originalMethod];row.Calls++;row.Ticks+=Stopwatch.GetTimestamp()-__state.Time;row.Allocated+=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes;}
        public static object[] Results(){return rows.Values.OrderByDescending(r=>r.Ticks).Where(r=>r.Calls>0).Select(r=>(object)new{r.Method,r.Calls,InclusiveMilliseconds=r.Ticks*1000.0/Stopwatch.Frequency,InclusiveAllocatedBytes=r.Allocated}).ToArray();}
        public static void Reset(){foreach(Row row in rows.Values)row.Calls=row.Ticks=row.Allocated=0;SharedRefreshProbe.Reset();}
        private static string[] InitializedWorkers(){return DefDatabase<StatDef>.AllDefsListForReading.Where(def=>workerCacheFields.Any(field=>field.GetValue(def)!=null)).Select(def=>def.defName).ToArray();}
        public static void BeginTransition(string actor,string phase)
        {
            if(!Installed||transition!=null)throw new InvalidOperationException("Transition diagnostic requires installed, non-nested scopes");
            Enabled=false;Reset();
            transition=new Transition{Actor=actor,Phase=phase,WorkersBefore=InitializedWorkers()};
            transition.Time=Stopwatch.GetTimestamp();transition.Bytes=GC.GetAllocatedBytesForCurrentThread();Enabled=true;
        }
        public static void EndTransition()
        {
            if(transition==null)return;
            long elapsed=Stopwatch.GetTimestamp()-transition.Time,allocated=GC.GetAllocatedBytesForCurrentThread()-transition.Bytes;Enabled=false;
            string[] after=InitializedWorkers();
            transitionProfiles.Add(new{Actor=transition.Actor,Phase=transition.Phase,Milliseconds=elapsed*1000.0/Stopwatch.Frequency,AllocatedBytes=allocated,InitializedWorkerCacheFields=workerCacheFields.Select(field=>field.Name).ToArray(),TotalStatDefs=DefDatabase<StatDef>.AllDefsListForReading.Count,InitializedWorkersBefore=transition.WorkersBefore.Length,InitializedWorkersAfter=after.Length,NewlyInitializedWorkerDefs=after.Except(transition.WorkersBefore,StringComparer.Ordinal).ToArray(),HotMethods=Results(),Generations=SharedRefreshProbe.Result()});
            transition=null;Reset();
        }
        public static object TransitionResult(){return new{Installed,Profiles=transitionProfiles.ToArray(),Scope="Diagnostic-only per-actor transform and draft scopes, separated from battle timing. Inclusive method times/allocations overlap and include instrumentation overhead. Diagnostic installation and result snapshots are outside phase timing; cached native StatWorker fields are read directly without calling StatDef.Worker or initializing workers. Empty cache-field metadata means the worker snapshot is unavailable."};}
    }

    public sealed partial class CombatBenchmark:GameComponent
    {
        private bool ready,started,measuring,done,diagnostic,blocked,richGenes,enemyAssault,selectActors;
        private int frames,enemyCount,actorCount,startTick,keepalives;
        private double warmupSeconds,measureSeconds,startTime,lastCare;
        private string actorKind,loadout,gearPreset,enemyLoadout,graphicsMode;
        private string scenarioScope=null;
        private bool liveScenario,liveDrafted=true;
        private string[] liveBaselineNativeErrors=new string[0],measurementBaselineNativeErrors=new string[0];
        private IntVec3 liveCameraCell=IntVec3.Invalid;
        private readonly Stopwatch wall=Stopwatch.StartNew();
        private readonly List<Pawn> actors=new List<Pawn>(),originals=new List<Pawn>(),enemies=new List<Pawn>();
        private readonly List<object> samples=new List<object>(),verbPreflight=new List<object>(),transitions=new List<object>();
        private Pawn anchor;
        private Lord assaultLord;
        private float fixtureEnemyShiftChance;
        private int enemiesAtStart,actorsAtStart;
        private long allocatedAtStart;
        private static readonly FieldInfo undrafter=AccessTools.Field(typeof(Pawn_DraftController),"autoUndrafter");
        public CombatBenchmark(Game game){}
        public override void FinalizeInit(){Find.TickManager.Pause();ConfigureLiveFinalize();ready=true;}
        public override void GameComponentUpdate()
        {
            if(!ready||done||LongEventHandler.AnyEventNowOrWaiting||++frames<30)return;
            try
            {
                if(!started){bool waiting=false;AwaitLiveReady(ref waiting);if(waiting)return;Setup();return;}
                bool awaitingLiveEnemies=false;AwaitLiveEnemies(ref awaitingLiveEnemies);if(awaitingLiveEnemies)return;
                bool selectionHandled=false;StepLiveSelectionSequence(ref selectionHandled);if(selectionHandled)return;
                double now=wall.Elapsed.TotalSeconds;
                if(now-lastCare>=1){Care();lastCare=now;}
                if(Find.TickManager.CurTimeSpeed!=TimeSpeed.Ultrafast)Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;
                if(!measuring)
                {
                    if(now-startTime<warmupSeconds)return;
                    enemiesAtStart=Alive(enemies);actorsAtStart=Alive(actors);
                    measurementBaselineNativeErrors=NativeErrorTexts();
                    Timing.Reset();Activity.Reset();if(diagnostic)Diagnostics.Reset();startTick=Find.TickManager.TicksGame;allocatedAtStart=GC.GetTotalAllocatedBytes(false);
                    startTime=lastCare=now;measuring=Timing.Enabled=Activity.Enabled=Diagnostics.Enabled=true;
                    FrameProbe.Stage="measure";FrameProbe.Measuring=true;
                    if(!diagnostic)Diagnostics.Enabled=false;
                    if(diagnostic)ForceSpeedObserver.Phase="measure";
                    Sample("start");return;
                }
                if(now-lastSample>=1){Sample("sample");lastSample=now;}
                if(now-startTime>=measureSeconds)Finish(null);
            }
            catch(Exception error){Finish(error);}
        }
        private double lastSample;
        partial void ConfigureLiveFinalize();
        partial void AwaitLiveReady(ref bool waiting);
        partial void AwaitLiveEnemies(ref bool waiting);
        partial void StartLiveSelectionSequence();
        partial void StepLiveSelectionSequence(ref bool handled);
        partial void OverrideSelectionPassed(Exception error,string[] nativeErrors,ref bool passed);
        partial void FinalizeSelectionFailure(Exception error);
        partial void SetupLive(XElement input,ref bool handled);
        private void Setup()
        {
            started=true;Find.TickManager.Pause();Require(FrameProbe.Installed,"native root-update frame observer installed");
            FrameProbe.Gameplay=true;FrameProbe.Stage="setup/arena";
            XDocument input=XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"combat-inputs.xml"));
            Func<string,string> value=name=>input.Root.Element(name).Value;
            actorKind=value("ActorKind");loadout=value("Loadout");enemyCount=int.Parse(value("Enemies"));actorCount=int.Parse(value("Actors"));
            diagnostic=bool.Parse(value("Diagnostic"));blocked=bool.Parse(value("BlockedLOS"));
            richGenes=input.Root.Element("RichGenes")!=null&&bool.Parse(value("RichGenes"));enemyAssault=input.Root.Element("EnemyAssault")!=null&&bool.Parse(value("EnemyAssault"));
            gearPreset=input.Root.Element("GearPreset")?.Value??"basic";enemyLoadout=input.Root.Element("EnemyLoadout")?.Value??"unarmed";graphicsMode=input.Root.Element("GraphicsMode")?.Value??"Headless";selectActors=bool.Parse(input.Root.Element("SelectActors")?.Value??"false");
            warmupSeconds=double.Parse(value("WarmupSeconds"),System.Globalization.CultureInfo.InvariantCulture);measureSeconds=double.Parse(value("MeasureSeconds"),System.Globalization.CultureInfo.InvariantCulture);
            Require(Boot.Hash(typeof(Pawn).Assembly.Location)==value("GameAssemblySHA256"),"actual game assembly hash");
            Require(Boot.Hash(typeof(WildShift.TransformUtility).Assembly.Location)==value("WildShiftSHA256"),"actual production DLL hash");
            Require(Boot.Hash(typeof(Boot).Assembly.Location)==value("FixtureSHA256"),"actual helper DLL hash");
            Require(LoadedModManager.RunningModsListForReading.Select(m=>m.PackageId).SequenceEqual(input.Root.Element("Mods").Elements("li").Select(x=>x.Value)),"actual isolated mod IDs/order");
            Require(enemyCount>=0&&enemyCount<=400&&(actorCount==1||actorCount==5),"bounded actor/target population");
            DebugSettings.enableStoryteller=false;DebugSettings.enableRandomMentalStates=false;DebugSettings.enableRandomDiseases=false;DebugSettings.noAnimals=false;
            fixtureEnemyShiftChance=WildShift.WildShiftMod.Settings.enemyShapeshifterChance;
            if(enemyAssault)WildShift.WildShiftMod.Settings.enemyShapeshifterChance=0f;
            if(diagnostic)Diagnostics.Install();
            if(input.Root.Element("LiveActorId")!=null)
            {
                liveScenario=true;liveDrafted=bool.Parse(input.Root.Element("Drafted")?.Value??"true");
                bool handled=false;SetupLive(input.Root,ref handled);Require(handled,"live scenario extension is included in this helper");return;
            }
            Map map=Find.CurrentMap;Require(map!=null,"native quicktest map");
            map.TileInfo.PrimaryBiome.animalDensity=0f;
            foreach(Thing thing in map.listerThings.AllThings.ToList())
            {
                if(thing.Destroyed)continue;
                // Anomaly's protected monolith cannot be destroyed. Remove
                // protected map objects through native despawn instead, so
                // arena cleanup neither logs an error nor leaves LOS cover.
                if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);
                else if(thing.Spawned)thing.DeSpawn();
            }
            foreach(IntVec3 cell in map.AllCells){map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.roofGrid.SetRoof(cell,null);map.fogGrid.Unfog(cell);}
            if(blocked)for(int z=1;z<map.Size.z-1;z++)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,DefDatabase<ThingDef>.GetNamed("BlocksGranite")),new IntVec3(28,0,z),map);
            if(diagnostic){ForceSpeedObserver.Install(Boot.Harmony);ForceSpeedObserver.Arm("setup-before-actors");}
            Rand.PushState(764839);
            try
            {
                Faction enemyFaction=Find.FactionManager.AllFactionsListForReading.First(f=>!f.IsPlayer&&f.def.humanlikeFaction&&f.HostileTo(Faction.OfPlayer));
                FrameProbe.Stage="setup/enemies";
                for(int i=0;i<enemyCount;i++)
                {
                    Pawn enemy=Human(enemyFaction,"CombatTarget"+i);if(richGenes)ApplyGenes(enemy,"Hussar");
                    if(enemyLoadout=="rifle")enemy.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle")));
                    int columns=enemyCount>120?16:6;IntVec3 enemyCell=new IntVec3(35+(i%columns)*2,0,(enemyCount>120?10:18)+(i/columns)*2);
                    Require(enemyCell.InBounds(map),"target grid remains inside native arena");GenSpawn.Spawn(enemy,enemyCell,map);
                    if(!enemyAssault)enemy.jobs.StartJob(Wait(JobDefOf.Wait),JobCondition.InterruptForced,null,false,true,null,null,false);
                    enemies.Add(enemy);Activity.Targets.Add(enemy);
                }
                for(int i=0;i<actorCount;i++)
                {
                    IntVec3 position=new IntVec3(15,0,34+i*2);
                    Pawn original=Human(Faction.OfPlayer,"CombatActor"+i);GenSpawn.Spawn(original,position,map);originals.Add(original);
                    if(richGenes)ApplyGenes(original,"Sanguophage");
                    if(loadout=="rifle")original.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle")));
                    if(loadout=="jump")original.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PackJump")),false);
                    FrameProbe.Stage="setup/actor-gear";
                    if(gearPreset=="FullNative")StressLoadout.Apply(original,loadout);
                    Pawn actor=original;
                    FrameProbe.Stage="setup/transform";
                    double transformMs;long transformAllocated;
                    if(diagnostic)Diagnostics.BeginTransition(original.GetUniqueLoadID(),"transform");
                    try
                    {
                        long transformAt=Stopwatch.GetTimestamp(),transformBytes=GC.GetAllocatedBytesForCurrentThread();
                        if(actorKind=="Transformed")
                        {
                            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamed("Warg");WildShift.TransformUtility.AddOrGetShapeshifter(original,kind);
                            Require(WildShift.TransformUtility.TransformToAnimal(original,kind),"production transformation succeeds");actor=WildShift.SharedFormIdentity.ActiveFormFor(original);Require(actor!=null,"original retained by active form");
                        }
                        else if(actorKind=="Animal")
                        {
                            original.Destroy(DestroyMode.Vanish);
                            actor=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Warg"),Faction.OfPlayer);GenSpawn.Spawn(actor,position,map);
                        }
                        Require(actorKind=="Human"||actorKind=="Transformed"||actorKind=="Animal","explicit actor kind");
                        transformMs=(Stopwatch.GetTimestamp()-transformAt)*1000.0/Stopwatch.Frequency;
                        transformAllocated=GC.GetAllocatedBytesForCurrentThread()-transformBytes;
                    }
                    finally{if(diagnostic)Diagnostics.EndTransition();}
                    actors.Add(actor);Activity.Actors.Add(actor);Activity.Actors.Add(original);
                    FrameProbe.Stage="setup/draft";
                    double draftMs;long draftAllocated;
                    if(diagnostic)Diagnostics.BeginTransition(original.GetUniqueLoadID(),"draft");
                    try
                    {
                        long draftAt=Stopwatch.GetTimestamp(),draftBytes=GC.GetAllocatedBytesForCurrentThread();
                        if(actorKind!="Animal")
                        {
                            Require(actor.drafter!=null,"draft controller exists after native spawn");actor.drafter.Drafted=true;actor.drafter.FireAtWill=true;
                            actor.jobs.StartJob(Wait(JobDefOf.Wait_Combat),JobCondition.InterruptForced,null,false,true,null,null,false);
                        }
                        else OrderAnimal(actor);
                        draftMs=(Stopwatch.GetTimestamp()-draftAt)*1000.0/Stopwatch.Frequency;
                        draftAllocated=GC.GetAllocatedBytesForCurrentThread()-draftBytes;
                    }
                    finally{if(diagnostic)Diagnostics.EndTransition();}
                    transitions.Add(new{Actor=i,EnemiesAlreadySpawned=Alive(enemies),TransformOrVariantSetupMilliseconds=transformMs,TransformOrVariantSetupAllocatedBytes=transformAllocated,DraftAndNativeWaitJobMilliseconds=draftMs,DraftAndNativeWaitJobAllocatedBytes=draftAllocated});
                    verbPreflight.Add(new{Pawn=actor.GetUniqueLoadID(),Drafted=actor.Drafted,IsColonist=actor.IsColonist,Effective=VerbInfo(CurrentVerb(actor)),Automatic=VerbInfo(actor.TryGetAttackVerb(null,false)),ManualAllowed=VerbInfo(actor.TryGetAttackVerb(null,true)),Job=actor.CurJobDef?.defName,OriginalGenes=GeneInfo(original),ActiveBodyGenes=GeneInfo(actor),Apparel=PackInfo(original)});
                }
                if(enemyAssault)
                {
                    // The same unarmed normal-human anchor is present in both
                    // variants, so a real raid has a native human objective
                    // even when every measured actor is an animal form.
                    anchor=Human(Faction.OfPlayer,"UnarmedRaidObjectiveAnchor");GenSpawn.Spawn(anchor,new IntVec3(10,0,38),map);
                    anchor.drafter.Drafted=true;anchor.drafter.FireAtWill=false;anchor.jobs.StartJob(Wait(JobDefOf.Wait),JobCondition.InterruptForced);
                    if(enemies.Count>0)assaultLord=LordMaker.MakeNewLord(enemyFaction,new LordJob_AssaultColony(enemyFaction,canKidnap:false,canSteal:false),map,enemies);
                }
            }
            finally{Rand.PopState();}
            BeginTelemetry(map);
            if(bool.Parse(input.Root.Element("ArenaSelectionSequence")?.Value??"false"))StartLiveSelectionSequence();
        }
        private void BeginTelemetry(Map map)
        {
            if(diagnostic)Diagnostics.Install();
            if(diagnostic)ForceSpeedObserver.Phase="warmup";
            FrameProbe.Stage="setup/selection";
            if(selectActors)
            {
                Find.Selector.ClearSelection();foreach(Pawn actor in actors)Find.Selector.Select(actor,false,false);
            }
            if(graphicsMode=="Native")Find.CameraDriver.SetRootPosAndSize(liveScenario?(liveCameraCell.IsValid?liveCameraCell.ToVector3Shifted():actors[0].Position.ToVector3Shifted()):new Vector3(37f,0f,35f),28f);
            Require(Alive(actors)==actorCount&&Alive(enemies)==enemyCount,"all requested actors and targets exist before warmup");
            startTime=lastCare=wall.Elapsed.TotalSeconds;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;Timing.CaptureFirst=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Ultrafast;FrameProbe.Stage="warmup";
            Boot.Json("preflight.json",new{ActorKind=actorKind,Loadout=loadout,Enemies=enemyCount,Actors=actorCount,BlockedLOS=blocked,Diagnostic=diagnostic,RichGenes=richGenes,EnemyAssault=enemyAssault,EnemyShapeshifterChance=WildShift.WildShiftMod.Settings.enemyShapeshifterChance,OriginalEnemyShapeshifterChance=fixtureEnemyShiftChance,Anchor=anchor?.GetUniqueLoadID(),Lord=assaultLord?.GetType().FullName,EnemyGenes=enemies.Select(GeneInfo).ToArray(),Verbs=verbPreflight,Transitions=transitions});
        }
        private static Pawn Human(Faction faction,string name)
        {
            Pawn p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,faction,forceGenerateNewPawn:true,canGeneratePawnRelations:false,mustBeCapableOfViolence:true,fixedGender:Gender.Male,fixedBiologicalAge:25f,forcedXenotype:XenotypeDefOf.Baseliner));
            p.Name=new NameSingle(name);p.health.RemoveAllHediffs();foreach(Trait t in p.story.traits.allTraits.ToList())p.story.traits.RemoveTrait(t);foreach(Gene g in p.genes.GenesListForReading.ToList())p.genes.RemoveGene(g);
            p.equipment.DestroyAllEquipment();foreach(Apparel a in p.apparel.WornApparel.ToList())p.apparel.Remove(a);p.inventory.innerContainer.ClearAndDestroyContents();p.skills.GetSkill(SkillDefOf.Shooting).Level=12;p.skills.GetSkill(SkillDefOf.Melee).Level=12;
            return p;
        }
        private static Job Wait(JobDef def){Job job=JobMaker.MakeJob(def);job.expiryInterval=600000;job.checkOverrideOnExpire=false;job.playerForced=true;return job;}
        private static void ApplyGenes(Pawn pawn,string preset)
        {
            Require(pawn.genes!=null,"rich native genotype requires a gene tracker");
            pawn.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed(preset));
            Require(pawn.genes.GenesListForReading.Any(g=>g.Active),"official native genotype has active genes: "+preset);
        }
        private static object GeneInfo(Pawn pawn)
        {
            return new{Pawn=pawn.GetUniqueLoadID(),Xenotype=pawn.genes?.Xenotype?.defName,Total=pawn.genes?.GenesListForReading.Count??0,Active=pawn.genes?.GenesListForReading.Count(g=>g.Active)??0};
        }
        private static object[] PackInfo(Pawn original)
        {
            return original.apparel==null?new object[0]:original.apparel.WornApparel.Where(a=>a.GetComp<CompApparelReloadable>()!=null).Select(a=>(object)new{Id=a.GetUniqueLoadID(),Def=a.def.defName,RemainingCharges=a.GetComp<CompApparelReloadable>().RemainingCharges}).ToArray();
        }
        private void OrderAnimal(Pawn pawn)
        {
            Pawn target=enemies.Where(p=>p.Spawned&&!p.Dead&&!p.Downed).OrderBy(p=>p.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            if(target==null){pawn.jobs.StartJob(Wait(JobDefOf.Wait),JobCondition.InterruptForced);return;}
            Job job=JobMaker.MakeJob(JobDefOf.AttackMelee,target);job.expiryInterval=600000;job.playerForced=true;pawn.jobs.StartJob(job,JobCondition.InterruptForced);
        }
        private void Care()
        {
            if(liveScenario)return;
            foreach(Pawn actor in actors.Where(p=>p.Spawned&&!p.Dead&&!p.Downed))
            {
                if(actorKind=="Animal"){if(actor.CurJobDef!=JobDefOf.AttackMelee&&enemyCount>0&&!blocked){OrderAnimal(actor);keepalives++;}continue;}
                object auto=undrafter?.GetValue(actor.drafter);if(auto!=null)AccessTools.Field(auto.GetType(),"lastNonWaitTick")?.SetValue(auto,Find.TickManager.TicksGame);
                if(!liveDrafted)continue;
                if(!actor.Drafted){actor.drafter.Drafted=true;keepalives++;}
            }
        }
        private static int Alive(List<Pawn> pawns){return pawns.Count(p=>p.Spawned&&!p.Dead&&!p.Downed);}
        private void Sample(string phase)
        {
            samples.Add(new{Phase=phase,ElapsedSeconds=wall.Elapsed.TotalSeconds-startTime,Tick=Find.TickManager.TicksGame,EffectiveTickRateMultiplier=Find.TickManager.TickRateMultiplier,EffectiveNativeTargetTPS=Find.TickManager.TickRateMultiplier*60,AliveEnemies=Alive(enemies),AliveActors=Alive(actors),AnchorAlive=anchor!=null&&anchor.Spawned&&!anchor.Dead&&!anchor.Downed,Searches=Activity.Searches,ShootSearches=Activity.ShootSearches,Shots=Activity.Shots,EnemyShots=Activity.EnemyShots,ActorDamageEvents=Activity.ActorDamageEvents,ActorBulletHits=Activity.ActorBulletHits,ActorRawDamage=Activity.ActorRawDamage,DamageEvents=Activity.DamageEvents,IncomingDamage=Activity.IncomingDamage,NativeJumps=Activity.NativeJumps,ActorPositions=actors.Select(p=>p.Spawned?p.Position.ToString():"off-map").ToArray(),OriginalApparel=originals.Select(PackInfo).ToArray(),ActorJobs=actors.Select(p=>p.CurJobDef?.defName).ToArray(),ActorTargets=actors.Select(p=>p.CurJob?.targetA.Thing?.GetUniqueLoadID()).ToArray(),EnemyJobCounts=enemies.Where(p=>p.Spawned&&!p.Dead).GroupBy(p=>p.CurJobDef?.defName??"none").ToDictionary(g=>g.Key,g=>g.Count()),EnemyDutyCounts=enemies.Where(p=>p.Spawned&&!p.Dead).GroupBy(p=>p.mindState.duty?.def?.defName??"none").ToDictionary(g=>g.Key,g=>g.Count())});
        }
        private void Finish(Exception error)
        {
            if(done)return;done=true;FinalizeSelectionFailure(error);Timing.Enabled=Timing.CaptureFirst=Activity.Enabled=Diagnostics.Enabled=ForceSpeedObserver.Enabled=false;FrameProbe.Measuring=false;FrameProbe.Stage="finished";double effectiveRate=Find.TickManager.TickRateMultiplier;Find.TickManager.Pause();
            double elapsed=wall.Elapsed.TotalSeconds-startTime;Sample("end");
            var nativeErrors=NativeErrorTexts();
            var newAfterSetupStart=ErrorDifference(nativeErrors,liveBaselineNativeErrors);
            var setupAndWarmupErrors=ErrorDifference(measurementBaselineNativeErrors,liveBaselineNativeErrors);
            var measurementErrors=ErrorDifference(nativeErrors,measurementBaselineNativeErrors);
            Boot.Json("native-error-window.json",new{LiveScenario=liveScenario,BaselineCaptured=liveScenario,StartupNativeErrors=liveBaselineNativeErrors,AtMeasurementStart=measurementBaselineNativeErrors,SetupAndWarmupNativeErrors=setupAndWarmupErrors,NewNativeErrorsAfterSetupStart=newAfterSetupStart,NewNativeErrorsDuringMeasurement=measurementErrors,NoNewNativeErrors=newAfterSetupStart.Length==0,NoNewMeasurementNativeErrors=measuring&&measurementErrors.Length==0,AllNativeErrorsAtFinish=nativeErrors,Scope="Snapshots of native recorded Error log entries, differenced as text multisets. Live baseline is captured before enemy setup, transformation and drafting; measurement baseline follows setup/warmup. Native error recording is not suppressed. A clean delta is not whole-modpack validation and native suppressed/repeated log entries are not independent event counts."});
            bool activity=enemyCount==0||blocked||loadout!="rifle"||actorKind=="Animal"||!liveDrafted||Activity.Shots>0;
            bool passed=error==null&&measuring&&Timing.Count>0&&actorsAtStart==actorCount&&enemiesAtStart>=(enemyCount==0?0:1)&&Alive(actors)==actorCount&&activity&&nativeErrors.Length==0;
            OverrideSelectionPassed(error,nativeErrors,ref passed);
            if(diagnostic)Boot.Json("force-speed-observer.json",ForceSpeedObserver.Result());
            if(diagnostic)Boot.Json("shared-refresh-generations.json",SharedRefreshProbe.Result());
            if(diagnostic)Boot.Json("transition-hot-methods.json",Diagnostics.TransitionResult());
            Boot.Json("frame-result.json",FrameProbe.Result());
            Boot.Json("native-job-activity.json",Activity.JobActivity());
            Boot.Json("stress-state.json",new{GearPreset=gearPreset,EnemyLoadout=enemyLoadout,GraphicsMode=graphicsMode,SelectActors=selectActors,OriginalState=originals.Select(StressLoadout.Snapshot).ToArray()});
            Boot.Json("result.json",new{Passed=passed,Exception=error?.ToString(),GameVersion=VersionControl.CurrentVersionStringWithRev,GameAssembly=typeof(Pawn).Assembly.Location,GameSHA256=Boot.Hash(typeof(Pawn).Assembly.Location),WildShiftSHA256=Boot.Hash(typeof(WildShift.TransformUtility).Assembly.Location),FixtureSHA256=Boot.Hash(typeof(Boot).Assembly.Location),ActorKind=actorKind,Loadout=loadout,GearPreset=gearPreset,EnemyLoadout=enemyLoadout,GraphicsMode=graphicsMode,SelectedActors=selectActors,LiveScenario=liveScenario,LiveDrafted=liveDrafted,RequestedEnemies=enemyCount,ActorCount=actorCount,BlockedLOS=blocked,Diagnostic=diagnostic,RichGenes=richGenes,EnemyAssault=enemyAssault,EnemyShapeshifterChance=enemyAssault?0f:fixtureEnemyShiftChance,OriginalEnemyShapeshifterChance=fixtureEnemyShiftChance,Anchor=anchor==null?null:new{Pawn=anchor.GetUniqueLoadID(),Alive=anchor.Spawned&&!anchor.Dead&&!anchor.Downed,Position=anchor.Spawned?anchor.Position.ToString():"off-map"},EnemyGenes=enemies.Select(GeneInfo).ToArray(),WarmupSeconds=warmupSeconds,RequestedMeasureSeconds=measureSeconds,ActualMeasureSeconds=elapsed,NativeTicks=Timing.Count,GameTickDelta=Find.TickManager.TicksGame-startTick,TPS=Timing.Count/elapsed,EffectiveTickRateMultiplier=effectiveRate,EffectiveNativeTargetTPS=effectiveRate*60,MeanTickMs=Timing.Count==0?0:Timing.Sum*1000.0/Stopwatch.Frequency/Timing.Count,P50TickUs=Timing.Percentile(.50),P95TickUs=Timing.Percentile(.95),P99TickUs=Timing.Percentile(.99),MaxTickMs=Timing.Max*1000.0/Stopwatch.Frequency,Timing.Overflow,AllocatedBytesDuringTicks=Timing.Allocated,AllocatedBytesPerTick=Timing.Count==0?0:Timing.Allocated/(double)Timing.Count,ProcessAllocatedDelta=GC.GetTotalAllocatedBytes(false)-allocatedAtStart,AliveEnemiesAtMeasurementStart=enemiesAtStart,AliveEnemiesAtEnd=Alive(enemies),AliveActorsAtStart=actorsAtStart,AliveActorsAtEnd=Alive(actors),Activity.Searches,Activity.ShootSearches,Activity.Shots,Activity.EnemyShots,Activity.ActorDamageEvents,Activity.ActorBulletHits,Activity.ActorRawDamage,Activity.DamageEvents,Activity.IncomingDamage,Activity.NativeJumps,KeepaliveInterventions=keepalives,Preflight=verbPreflight,Transitions=transitions,FirstNativeTickMilliseconds=Timing.FirstTickMs(),FirstNativeTickAllocatedBytes=Timing.FirstTickAllocated(),Samples=samples,NativeErrors=nativeErrors,FrameMetrics=FrameProbe.Result(),HotMethods=Diagnostics.Results(),ForcedNormalSpeedSignals=diagnostic?ForceSpeedObserver.Result():null,Scope=ScopeText()});
            if(WildShift.WildShiftMod.Settings!=null)WildShift.WildShiftMod.Settings.enemyShapeshifterChance=fixtureEnemyShiftChance;
            Application.Quit();
        }
        private string ScopeText(){return scenarioScope??(blocked?"Native drafted target-search workload with opaque LOS barrier; no full-combat or rendering claim.":enemyAssault?"Native LordJob_AssaultColony against drafted player actors plus one unarmed human objective anchor; official Sanguophage/Hussar gene preset optional. Real damage and casualties, no invulnerability. Enemy shapeshifter generation disabled only in this private scenario; sample population and jobs before comparing. Diagnostic inclusive timings include instrumentation.":"Native drafted Wait_Combat and real projectile attacks; targets use native waiting jobs, not an adversarial raid AI benchmark. Animal comparator uses explicit native melee jobs. Diagnostic inclusive timings are instrumented and not comparable to uninstrumented tick times.");}
        private static string[] NativeErrorTexts(){return Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();}
        private static string[] ErrorDifference(string[] after,string[] before)
        {
            var remaining=before.GroupBy(text=>text).ToDictionary(group=>group.Key,group=>group.Count(),StringComparer.Ordinal);
            var added=new List<string>();
            foreach(string text in after){int count;if(remaining.TryGetValue(text,out count)&&count>0)remaining[text]=count-1;else added.Add(text);}
            return added.ToArray();
        }
        private static Verb CurrentVerb(Pawn p){PropertyInfo property=AccessTools.Property(typeof(Pawn),"CurrentEffectiveVerb");return property?.GetValue(p) as Verb;}
        private static object VerbInfo(Verb verb)
        {
            if(verb==null)return null;
            var flags=verb.verbProps.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Where(f=>f.Name.IndexOf("manual",StringComparison.OrdinalIgnoreCase)>=0&&f.FieldType==typeof(bool)).ToDictionary(f=>f.Name,f=>f.GetValue(verb.verbProps));
            return new{Class=verb.GetType().FullName,Label=verb.verbProps.label,Range=verb.verbProps.range,Caster=verb.CasterPawn?.GetUniqueLoadID(),ManualFlags=flags};
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Combat fixture prerequisite: "+message);}
    }
}
