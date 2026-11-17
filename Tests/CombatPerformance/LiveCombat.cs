// TEST ONLY. Existing-colony fixture; never included in a runtime release.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace WildShiftCombatPerformance
{
    public partial class CombatBenchmark
    {
        private bool? liveGateRequested;
        private string managedSamplerReadyFile;
        private bool liveNativeReadyRecorded,managedSamplerReleased;
        private double managedSamplerSince=-1;
        private int livePrewarmTicks=120,livePrewarmStartTick;
        private bool livePrewarmStarted,livePrewarmComplete;
        private double livePrewarmSince,lastLivePrewarmSample=-1;
        private readonly List<object> livePrewarmSamples=new List<object>();
        private sealed class LiveSetupPending
        {
            public XElement Input;
            public Pawn Original;
            public Map Map;
            public PawnKindDef Kind;
            public Faction EnemyFaction;
            public CellRect Arena;
            public IntVec3 ActorCell;
            public string Id,Form;
            public int PawnCountBefore,EnemyWarmTicks,EnemyWarmStartTick,ArmedWarmTicks,ArmedWarmStartTick;
            public double EnemyWarmSince,LastWarmSample=-1,ArmedWarmSince,LastArmedWarmSample=-1;
            public bool EnemyWarmComplete,ArmedWarmStarted,ArmedWarmComplete;
            public object SourceState;
        }
        private LiveSetupPending liveSetupPending;
        private readonly List<object> liveTrialWindowClosures=new List<object>();

        partial void ConfigureLiveFinalize()
        {
            XElement input=XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"combat-inputs.xml")).Root;
            if(input.Element("LiveActorId")==null)return;
            bool before=Prefs.DevMode;Prefs.DevMode=false;
            Boot.Json("live-development-mode.json",new{Stage="FinalizeInit-before-prewarm",Utc=DateTime.UtcNow.ToString("O"),Tick=Find.TickManager.TicksGame,AutoloadDevMode=before,BenchmarkDevMode=Prefs.DevMode,Scope="The disposable profile may enable developer mode only to permit native autostart. Live FinalizeInit disables it before native colony/enemy prewarming and all trial measurements. No normal profile is changed."});
        }
        private double liveWaitSince=-1,lastLiveWaitSample=-1;
        private readonly List<object> liveWaitSamples=new List<object>();

        partial void AwaitLiveReady(ref bool waiting)
        {
            if(!liveGateRequested.HasValue)
            {
                XElement input=XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"combat-inputs.xml")).Root;
                liveGateRequested=input.Element("LiveActorId")!=null;
                managedSamplerReadyFile=input.Element("ManagedSamplerReadyFile")?.Value;
                livePrewarmTicks=int.Parse(input.Element("LivePrewarmTicks")?.Value??"120");
                Require(livePrewarmTicks>=0&&livePrewarmTicks<=3600,"bounded live prewarm tick count");
                if(!string.IsNullOrEmpty(managedSamplerReadyFile))Require(Path.IsPathRooted(managedSamplerReadyFile),"managed sampler ready file is an absolute test-output path");
            }
            if(!liveGateRequested.Value)return;
            double now=wall.Elapsed.TotalSeconds;
            bool blocked=Find.TickManager.ForcePaused||!Current.Game.PlayerHasControl;
            if(!blocked)
            {
                if(!liveNativeReadyRecorded)
                {
                    liveWaitSamples.Add(LivePauseState("native-ready"));
                    Boot.Json("live-ready-gate.json",new{Ready=true,WaitSeconds=liveWaitSince<0?0:now-liveWaitSince,Samples=liveWaitSamples});
                    liveNativeReadyRecorded=true;
                }
                if(!AwaitLivePrewarm(now)){waiting=true;return;}
                return;
            }
            if(liveWaitSince<0)liveWaitSince=now;
            FrameProbe.Stage="live/waiting-native-ready";
            if(now-lastLiveWaitSample>=1)
            {
                liveWaitSamples.Add(LivePauseState("waiting"));lastLiveWaitSample=now;
                Boot.Json("live-ready-gate.json",new{Ready=false,WaitSeconds=now-liveWaitSince,Samples=liveWaitSamples});
            }
            // These exact informational windows have known non-gameplay
            // dismissals. Native Close runs their normal close callbacks;
            // HugsLib's read state is saved only in this private profile.
            // Other dialogs retain their callbacks and are never guessed at.
            foreach(Window window in Find.WindowStack.Windows.ToList())
                if(window.forcePause&&(window.GetType().FullName=="LudeonTK.EditWindow_Log"
                    ||window.GetType().FullName=="HugsLib.News.Dialog_UpdateFeatures"))
                {
                    string type=window.GetType().FullName;
                    liveWaitSamples.Add(new{Stage="before-native-close-known-information-window",Type=type,ForcePause=window.forcePause});
                    window.Close(false);
                    liveWaitSamples.Add(new{Stage="after-native-close-known-information-window",Type=type,Callback="Window.Close(false), including native PreClose/PostClose",Removed=!Find.WindowStack.Windows.Contains(window)});
                }
            Require(now-liveWaitSince<=60,"native game remains force-paused or denies player control; inspect live-ready-gate.json. No ticks were forced");
            waiting=true;
        }

        private bool AwaitLivePrewarm(double now)
        {
            if(livePrewarmComplete)return true;
            FrameProbe.Stage="live/prewarm";
            if(!livePrewarmStarted)
            {
                livePrewarmStarted=true;livePrewarmSince=now;livePrewarmStartTick=Find.TickManager.TicksGame;
                DebugSettings.enableStoryteller=false;DebugSettings.enableRandomMentalStates=false;DebugSettings.enableRandomDiseases=false;
                Timing.Enabled=false;Timing.Reset();Timing.ResetFirstTicks();
                if(livePrewarmTicks>0)
                {
                    Timing.Enabled=Timing.CaptureFirst=true;
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
                    RecordLivePrewarm("started",false);return false;
                }
            }
            int ticks=Find.TickManager.TicksGame-livePrewarmStartTick;
            if(ticks>=livePrewarmTicks)
            {
                Timing.Enabled=Timing.CaptureFirst=false;
                Find.TickManager.Pause();RecordLivePrewarm("complete",true);
                Timing.Reset();Timing.ResetFirstTicks();livePrewarmComplete=true;return true;
            }
            if(now-livePrewarmSince>60)
            {
                Timing.Enabled=Timing.CaptureFirst=false;
                Find.TickManager.Pause();RecordLivePrewarm("timeout",false);
                Timing.Reset();Timing.ResetFirstTicks();
                Require(false,"native live prewarm did not reach requested ticks within 60 seconds; inspect live-prewarm.json. No ticks were forced");
            }
            Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            if(now-lastLivePrewarmSample>=1){RecordLivePrewarm("progress",false);lastLivePrewarmSample=now;}
            return false;
        }

        private void RecordLivePrewarm(string phase,bool complete)
        {
            double elapsed=wall.Elapsed.TotalSeconds-livePrewarmSince;
            int tick=Find.TickManager.TicksGame;
            livePrewarmSamples.Add(new{Phase=phase,ElapsedSeconds=elapsed,Tick=tick,ActualTicks=tick-livePrewarmStartTick,ObservedNativeTicks=Timing.Count,MaxNativeTickMilliseconds=Timing.Max*1000.0/Stopwatch.Frequency});
            Boot.Json("live-prewarm.json",new{Complete=complete,Phase=phase,RequestedTicks=livePrewarmTicks,StartTick=livePrewarmStartTick,EndTick=tick,ActualTicks=tick-livePrewarmStartTick,ObservedNativeTicks=Timing.Count,ElapsedSeconds=elapsed,RequestedNativeSpeed="Normal",MeanNativeTickMilliseconds=Timing.Count==0?0:Timing.Sum*1000.0/Stopwatch.Frequency/Timing.Count,MaxNativeTickMilliseconds=Timing.Max*1000.0/Stopwatch.Frequency,P95NativeTickMicroseconds=Timing.Percentile(.95),AllocatedBytesDuringNativeTicks=Timing.Allocated,FirstNativeTickMilliseconds=Timing.FirstTickMs(),FirstNativeTickAllocatedBytes=Timing.FirstTickAllocated(),Samples=livePrewarmSamples.ToArray(),PauseState=LivePauseState("prewarm-"+phase),Scope="Existing colony cold initialization before sampler attachment, Setup and transformation. Only ordinary native updates at TimeSpeed.Normal advance ticks; storyteller/random mental/random disease debug gates are disabled in this private process. No ticks are forced. Timing and first-tick captures are reset before the transformation trial; these cold ticks are not transformation measurements."});
        }

        private void AwaitManagedSampler(double now,ref bool waiting)
        {
            if(string.IsNullOrEmpty(managedSamplerReadyFile)||managedSamplerReleased)return;
            FrameProbe.Stage="live/waiting-managed-sampler";
            if(managedSamplerSince<0)
            {
                managedSamplerSince=now;
                using(Process process=Process.GetCurrentProcess())
                    Boot.Json("managed-sampler-request.json",new{PID=process.Id,RequestedUtc=DateTime.UtcNow.ToString("O"),RequestedStopwatchTimestamp=Stopwatch.GetTimestamp(),StopwatchFrequency=Stopwatch.Frequency,Tick=Find.TickManager.TicksGame,ManagedSamplerReadyFile=managedSamplerReadyFile,TimeoutSeconds=30,Scope="Opt-in pre-Setup handshake; native control is ready. Ordinary game-component updates wait for an external managed sampler to create the ready file. No ticks are forced and no profiler is launched by the fixture."});
                waiting=true;return;
            }
            if(File.Exists(managedSamplerReadyFile))
            {
                managedSamplerReleased=true;
                Boot.Json("managed-sampler-release.json",new{ReleasedUtc=DateTime.UtcNow.ToString("O"),ReleasedStopwatchTimestamp=Stopwatch.GetTimestamp(),StopwatchFrequency=Stopwatch.Frequency,Tick=Find.TickManager.TicksGame,WaitSeconds=now-managedSamplerSince,ManagedSamplerReadyFile=managedSamplerReadyFile});
                return;
            }
            Require(now-managedSamplerSince<=30,"external managed sampler did not create its ready file within 30 seconds; inspect managed-sampler-request.json. No ticks were forced");
            waiting=true;
        }

        private static object LivePauseState(string stage)
        {
            object gravship=Find.GravshipController;
            var cutsceneProperty=gravship?.GetType().GetProperty("CutsceneInProgress",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            return new{Stage=stage,Tick=Find.TickManager.TicksGame,TimeSpeed=Find.TickManager.CurTimeSpeed.ToString(),Paused=Find.TickManager.Paused,ForcePaused=Find.TickManager.ForcePaused,PlayerHasControl=Current.Game.PlayerHasControl,LongEventForcePause=LongEventHandler.ForcePause,LongEventQueued=LongEventHandler.AnyEventNowOrWaiting,WindowStackForcePause=Find.WindowStack.WindowsForcePause,Windows=Find.WindowStack.Windows.Select(w=>new{Type=w.GetType().FullName,ForcePause=w.forcePause}).ToArray(),TilePickerActive=Find.TilePicker.Active,MapGeneratorDebugMode=MapGenerator.debugMode,GravshipLandingConfirmation=Find.GravshipController?.LandingAreaConfirmationInProgress??false,GravshipCutscene=cutsceneProperty?.GetValue(null)};
        }

        partial void SetupLive(XElement input, ref bool handled)
        {
            handled=true;
            liveBaselineNativeErrors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();
            Boot.Json("live-setup-pause-state.json",LivePauseState("setup-start"));
            string id=input.Element("LiveActorId").Value;
            string form=input.Element("FormKind")?.Value??"Warg";
            Pawn original=Find.Maps.SelectMany(m=>m.mapPawns.AllPawnsSpawned).FirstOrDefault(p=>p.ThingID==id||p.GetUniqueLoadID()==id);
            Require(original!=null,"existing source-save actor "+id);
            Require(original.Faction==Faction.OfPlayer&&!original.Dead&&!original.Downed&&original.RaceProps.Humanlike,"existing actor is a living controllable human");
            Require(actorCount==1&&enemyCount<=60,"live fixture is bounded to one existing actor and at most 60 hostiles");
            Require(actorKind=="Human"||actorKind=="Transformed","live actor preserves human identity");
            Map map=original.Map;
            Require(map!=null&&map.Size.x>=100&&map.Size.z>=100,"existing full colony map");
            PawnKindDef kind=DefDatabase<PawnKindDef>.GetNamedSilentFail(form);
            Require(kind!=null&&kind.RaceProps.Animal,"requested installed animal form "+form);
            int pawnCountBefore=map.mapPawns.AllPawnsSpawned.Count;
            object sourceState=LiveSnapshot(original);
            CellRect arena=ChooseArena(map,original);
            HashSet<IntVec3> used=new HashSet<IntVec3>();
            IntVec3 actorCell=OpenCell(map,arena,new IntVec3(arena.minX+12,0,arena.minZ+arena.Height/2),used);
            original.jobs.StopAll();
            if(original.Drafted)original.drafter.Drafted=false;
            original.DeSpawn();GenSpawn.Spawn(original,actorCell,map);
            originals.Add(original);
            Faction enemyFaction=Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f=>!f.IsPlayer&&!f.defeated&&f.def.humanlikeFaction&&f.HostileTo(Faction.OfPlayer));
            Require(enemyFaction!=null,"existing hostile human faction");
            if(diagnostic){ForceSpeedObserver.Install(Boot.Harmony);ForceSpeedObserver.Arm("live-before-enemies");}
            Rand.PushState(764839);
            try
            {
                FrameProbe.Stage="live/enemies";
                for(int i=0;i<enemyCount;i++)
                {
                    Pawn enemy=Human(enemyFaction,"LiveCombatTarget"+i);
                    IntVec3 target=new IntVec3(arena.minX+36+(i%6)*2,0,arena.minZ+16+(i/6)*2);
                    GenSpawn.Spawn(enemy,OpenCell(map,arena,target,used),map);
                    enemy.jobs.StartJob(Wait(JobDefOf.Wait),JobCondition.InterruptForced,null,false,true,null,null,false);
                    enemies.Add(enemy);Activity.Targets.Add(enemy);
                }
            }
            finally{Rand.PopState();}
            original.jobs.StartJob(Wait(JobDefOf.Wait),JobCondition.InterruptForced,null,false,true,null,null,false);
            int enemyWarmTicks=int.Parse(input.Element("LiveEnemyPrewarmTicks")?.Value??"30");
            Require(enemyWarmTicks>=0&&enemyWarmTicks<=3600,"bounded live enemy prewarm tick count");
            int armedWarmTicks=int.Parse(input.Element("LiveArmedPrewarmTicks")?.Value??"15");
            Require(armedWarmTicks>=0&&armedWarmTicks<=3600,"bounded live armed prewarm tick count");
            liveSetupPending=new LiveSetupPending{Input=input,Original=original,Map=map,Kind=kind,EnemyFaction=enemyFaction,Arena=arena,ActorCell=actorCell,Id=id,Form=form,PawnCountBefore=pawnCountBefore,SourceState=sourceState,EnemyWarmTicks=enemyCount==0?0:enemyWarmTicks,EnemyWarmStartTick=Find.TickManager.TicksGame,EnemyWarmSince=wall.Elapsed.TotalSeconds,ArmedWarmTicks=enemyCount==0?0:armedWarmTicks};
            Timing.Enabled=false;Timing.Reset();Timing.ResetFirstTicks();
            if(liveSetupPending.EnemyWarmTicks>0){Timing.Enabled=Timing.CaptureFirst=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;}
            RecordLiveEnemyWarm("started",false);
        }

        partial void AwaitLiveEnemies(ref bool waiting)
        {
            if(liveSetupPending==null)return;
            waiting=true;LiveSetupPending pending=liveSetupPending;
            CloseLiveTrialDebugLog("enemy-warm-or-sampler-wait");
            double now=wall.Elapsed.TotalSeconds;
            if(!pending.EnemyWarmComplete)
            {
                FrameProbe.Stage="live/enemy-prewarm";
                int ticks=Find.TickManager.TicksGame-pending.EnemyWarmStartTick;
                if(ticks<pending.EnemyWarmTicks)
                {
                    if(now-pending.EnemyWarmSince>60)
                    {
                        Timing.Enabled=Timing.CaptureFirst=false;Find.TickManager.Pause();RecordLiveEnemyWarm("timeout",false);Timing.Reset();Timing.ResetFirstTicks();
                        Require(false,"new unarmed enemy native prewarm did not complete within 60 seconds; inspect live-enemy-prewarm.json. No ticks were forced");
                    }
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
                    if(now-pending.LastWarmSample>=1){RecordLiveEnemyWarm("progress",false);pending.LastWarmSample=now;}
                    return;
                }
                Timing.Enabled=Timing.CaptureFirst=false;Find.TickManager.Pause();RecordLiveEnemyWarm("complete",true);
                Timing.Reset();Timing.ResetFirstTicks();pending.EnemyWarmComplete=true;
            }
            if(!AwaitLiveArmedPrewarm(now))return;
            bool samplerWaiting=false;AwaitManagedSampler(now,ref samplerWaiting);if(samplerWaiting)return;
            CloseLiveTrialDebugLog("before-transform-trial");
            Boot.Json("live-trial-ui.json",new{BenchmarkDevMode=Prefs.DevMode,RecordedBaselineErrors=liveBaselineNativeErrors,CurrentErrors=NativeErrorTexts(),Closures=liveTrialWindowClosures.ToArray(),RemainingWindows=Find.WindowStack.Windows.Select(window=>new{Type=window.GetType().FullName,ForcePause=window.forcePause}).ToArray(),Scope="Only the exact native debug-log window is closed with native Close(false); error messages and baseline snapshots remain recorded. Other windows are retained."});
            ContinueLiveSetup();liveSetupPending=null;
        }

        private void CloseLiveTrialDebugLog(string phase)
        {
            foreach(Window window in Find.WindowStack.Windows.ToList())
            {
                if(window.GetType().FullName!="LudeonTK.EditWindow_Log")continue;
                string[] before=NativeErrorTexts();bool forcePause=window.forcePause;
                window.Close(false);
                liveTrialWindowClosures.Add(new{Phase=phase,Utc=DateTime.UtcNow.ToString("O"),StopwatchTimestamp=Stopwatch.GetTimestamp(),Tick=Find.TickManager.TicksGame,Type="LudeonTK.EditWindow_Log",ForcePause=forcePause,Removed=!Find.WindowStack.Windows.Contains(window),ErrorsBefore=before,ErrorsAfter=NativeErrorTexts(),Callback="native Window.Close(false); no Log.Clear or error suppression"});
            }
        }

        private void RecordLiveEnemyWarm(string phase,bool complete)
        {
            LiveSetupPending pending=liveSetupPending;
            Boot.Json("live-enemy-prewarm.json",new{Complete=complete,Phase=phase,Utc=DateTime.UtcNow.ToString("O"),StopwatchTimestamp=Stopwatch.GetTimestamp(),StopwatchFrequency=Stopwatch.Frequency,RequestedTicks=pending.EnemyWarmTicks,StartTick=pending.EnemyWarmStartTick,EndTick=Find.TickManager.TicksGame,ActualTicks=Find.TickManager.TicksGame-pending.EnemyWarmStartTick,ObservedNativeTicks=Timing.Count,ElapsedSeconds=wall.Elapsed.TotalSeconds-pending.EnemyWarmSince,AliveEnemies=Alive(enemies),EnemiesWithEquipment=enemies.Count(enemy=>enemy.equipment.AllEquipmentListForReading.Count>0),EnemyJobs=enemies.GroupBy(enemy=>enemy.CurJobDef?.defName??"none").ToDictionary(group=>group.Key,group=>group.Count()),OriginalDrafted=pending.Original.Drafted,MeanNativeTickMilliseconds=Timing.Count==0?0:Timing.Sum*1000.0/Stopwatch.Frequency/Timing.Count,MaxNativeTickMilliseconds=Timing.Max*1000.0/Stopwatch.Frequency,AllocatedBytesDuringNativeTicks=Timing.Allocated,FirstNativeTickMilliseconds=Timing.FirstTickMs(),FirstNativeTickAllocatedBytes=Timing.FirstTickAllocated(),PauseState=LivePauseState("enemy-prewarm-"+phase),Scope="New fixture enemies are unarmed on native Wait jobs and the existing original is undrafted on a native Wait job. Ordinary native Normal updates warm pawn components before rifles, transformation/drafting, assault Lord or sampler attachment. No direct component ticks, forced game ticks, invulnerability or damage suppression. Timing captures are reset before the trial."});
        }

        private bool AwaitLiveArmedPrewarm(double now)
        {
            LiveSetupPending pending=liveSetupPending;
            if(pending.ArmedWarmComplete)return true;
            FrameProbe.Stage="live/armed-prewarm";
            if(!pending.ArmedWarmStarted)
            {
                Require(pending.Original.Spawned&&!pending.Original.Dead&&!pending.Original.Downed,"existing human survived unarmed enemy prewarm");
                Require(Alive(enemies)==enemyCount,"requested enemies survived unarmed native prewarm");
                Require(enemies.All(enemy=>enemy.equipment.AllEquipmentListForReading.Count==0),"enemies remained unarmed during native component prewarm");
                foreach(Pawn enemy in enemies)enemy.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle")));
                if(enemies.Count>0)
                {
                    assaultLord=LordMaker.MakeNewLord(pending.EnemyFaction,new LordJob_AssaultColony(pending.EnemyFaction,canKidnap:false,canSteal:false),pending.Map,enemies);
                    var beforeRelease=enemies.Select(enemy=>new{Pawn=enemy.GetUniqueLoadID(),Job=enemy.CurJobDef?.defName,PlayerForced=enemy.CurJob?.playerForced??false}).ToArray();
                    int warmWaitsCleared=enemies.Count(enemy=>enemy.CurJobDef==JobDefOf.Wait);
                    foreach(Pawn enemy in enemies)enemy.jobs.StopAll();
                    Boot.Json("live-enemy-job-release.json",new{Utc=DateTime.UtcNow.ToString("O"),StopwatchTimestamp=Stopwatch.GetTimestamp(),Tick=Find.TickManager.TicksGame,EnemiesReleased=enemies.Count,WarmWaitJobsBeforeRelease=warmWaitsCleared,Lord=assaultLord.GetType().FullName,TargetStillHuman=pending.Original.RaceProps.Humanlike,Before=beforeRelease,After=enemies.Select(enemy=>new{Pawn=enemy.GetUniqueLoadID(),Job=enemy.CurJobDef?.defName,PlayerForced=enemy.CurJob?.playerForced??false}).ToArray(),Scope="While the original is still human, rifles and the native assault Lord are set up and StopAll releases temporary warm Wait jobs. Native think trees and Lord duties acquire targets; no replacement combat jobs are assigned."});
                }
                pending.ArmedWarmStarted=true;pending.ArmedWarmStartTick=Find.TickManager.TicksGame;pending.ArmedWarmSince=now;
                Timing.Enabled=false;Timing.Reset();Timing.ResetFirstTicks();
                if(pending.ArmedWarmTicks>0){Timing.Enabled=Timing.CaptureFirst=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;}
                RecordLiveArmedWarm("started",false);
            }
            if(!pending.Original.Spawned||pending.Original.Dead||pending.Original.Downed)
            {
                Timing.Enabled=Timing.CaptureFirst=false;Find.TickManager.Pause();RecordLiveArmedWarm("human-unavailable",false);Timing.Reset();Timing.ResetFirstTicks();
                Require(false,"existing human died or was downed during real armed prewarm; no invulnerability or damage override was used");
            }
            int ticks=Find.TickManager.TicksGame-pending.ArmedWarmStartTick;
            if(ticks>=pending.ArmedWarmTicks)
            {
                Timing.Enabled=Timing.CaptureFirst=false;Find.TickManager.Pause();RecordLiveArmedWarm("complete",true);
                Timing.Reset();Timing.ResetFirstTicks();pending.ArmedWarmComplete=true;return true;
            }
            if(now-pending.ArmedWarmSince>60)
            {
                Timing.Enabled=Timing.CaptureFirst=false;Find.TickManager.Pause();RecordLiveArmedWarm("timeout",false);Timing.Reset();Timing.ResetFirstTicks();
                Require(false,"armed native target-acquisition phase did not finish within 60 seconds; inspect live-armed-prewarm.json. No ticks were forced");
            }
            Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            if(now-pending.LastArmedWarmSample>=1){RecordLiveArmedWarm("progress",false);pending.LastArmedWarmSample=now;}
            return false;
        }

        private void RecordLiveArmedWarm(string phase,bool complete)
        {
            LiveSetupPending pending=liveSetupPending;
            Boot.Json("live-armed-prewarm.json",new{Complete=complete,Phase=phase,Utc=DateTime.UtcNow.ToString("O"),StopwatchTimestamp=Stopwatch.GetTimestamp(),StopwatchFrequency=Stopwatch.Frequency,RequestedTicks=pending.ArmedWarmTicks,StartTick=pending.ArmedWarmStartTick,EndTick=Find.TickManager.TicksGame,ActualTicks=Find.TickManager.TicksGame-pending.ArmedWarmStartTick,ObservedNativeTicks=Timing.Count,ElapsedSeconds=wall.Elapsed.TotalSeconds-pending.ArmedWarmSince,AliveEnemies=Alive(enemies),OriginalState=LiveSnapshot(pending.Original),Enemies=enemies.Select(enemy=>LiveEnemyCombatSnapshot(enemy,pending.Original)).ToArray(),Lord=assaultLord?.GetType().FullName,MeanNativeTickMilliseconds=Timing.Count==0?0:Timing.Sum*1000.0/Stopwatch.Frequency/Timing.Count,MaxNativeTickMilliseconds=Timing.Max*1000.0/Stopwatch.Frequency,AllocatedBytesDuringNativeTicks=Timing.Allocated,FirstNativeTickMilliseconds=Timing.FirstTickMs(),FirstNativeTickAllocatedBytes=Timing.FirstTickAllocated(),Scope="Armed native assault runs against the still-human original for ordinary Normal ticks before sampler attachment and transformation. Actual jobs, weapons, current targets and LOS are recorded; acquisition or firing is not assumed. Human death/downing fails the trial. No explicit target/combat jobs, forced ticks, invulnerability or damage overrides. The same Lord is retained after transformation."});
        }

        private static object LiveEnemyCombatSnapshot(Pawn enemy,Pawn original)
        {
            Verb verb=CurrentVerb(enemy);
            LocalTargetInfo target=verb==null?LocalTargetInfo.Invalid:verb.CurrentTarget;
            return new{Pawn=enemy.GetUniqueLoadID(),Spawned=enemy.Spawned,Dead=enemy.Dead,Downed=enemy.Downed,Job=enemy.CurJobDef?.defName,JobPlayerForced=enemy.CurJob?.playerForced??false,JobTarget=enemy.CurJob?.targetA.Thing?.GetUniqueLoadID(),JobTargetCell=enemy.CurJob?.targetA.Cell.ToString(),Duty=enemy.mindState?.duty?.def?.defName,Primary=enemy.equipment?.Primary?.def.defName,ViolenceDisabled=enemy.WorkTagIsDisabled(WorkTags.Violent),EffectiveVerb=VerbInfo(verb),CurrentTarget=target.Thing?.GetUniqueLoadID(),CurrentTargetCell=target.IsValid?target.Cell.ToString():null,CurrentTargetIsOriginal=target.Thing==original,Stance=enemy.stances?.curStance?.GetType().FullName,DistanceToOriginal=enemy.Spawned&&original.Spawned?enemy.Position.DistanceTo(original.Position):(double?)null,LineOfSightToOriginal=enemy.Spawned&&original.Spawned&&enemy.Map==original.Map&&GenSight.LineOfSight(enemy.Position,original.Position,enemy.Map)};
        }

        private void ContinueLiveSetup()
        {
            LiveSetupPending pending=liveSetupPending;
            XElement input=pending.Input;Pawn original=pending.Original;Map map=pending.Map;PawnKindDef kind=pending.Kind;Faction enemyFaction=pending.EnemyFaction;CellRect arena=pending.Arena;IntVec3 actorCell=pending.ActorCell;string id=pending.Id,form=pending.Form;int pawnCountBefore=pending.PawnCountBefore;object sourceState=pending.SourceState;
            Require(original.Spawned&&!original.Dead&&!original.Downed,"existing source actor survived native enemy prewarm");
            Require(Alive(enemies)==enemyCount,"requested enemies survived native unarmed prewarm");
            original.jobs.StopAll();
            if(bool.Parse(input.Element("SelectionNativeSerums")?.Value??"false"))
            {
                object beforeSerums=LiveSnapshot(original);
                var serums=new List<object>();
                foreach(string name in new[]{"MindNumbSerum","Metalblood"})
                {
                    HediffDef def=DefDatabase<HediffDef>.GetNamed(name);
                    Hediff source=original.health.hediffSet.GetFirstHediffOfDef(def);bool added=source==null;
                    if(added)source=original.health.AddHediff(def);
                    Require(WildShift.SharedFormHealth.IsSupported(source),"native selection serum is a supported shared personal effect: "+name);
                    serums.Add(new{Def=name,Added=added,Id=source.GetUniqueLoadID(),source.Severity,Supported=true});
                }
                Boot.Json("selection-native-serums.json",new{Utc=DateTime.UtcNow.ToString("O"),Tick=Find.TickManager.TicksGame,Pawn=original.GetUniqueLoadID(),BeforeTransform=true,Applied=serums.ToArray(),Before=beforeSerums,After=LiveSnapshot(original),Scope="Real native MindNumbSerum and Metalblood hediffs on the human before transformation. Native health and damage code remain active; no invulnerability, health replacement or damage override."});
            }
            object preTransformState=LiveSnapshot(original);
            Boot.Json("live-pretransform-combat-state.json",new{Utc=DateTime.UtcNow.ToString("O"),StopwatchTimestamp=Stopwatch.GetTimestamp(),StopwatchFrequency=Stopwatch.Frequency,Tick=Find.TickManager.TicksGame,SourceState=preTransformState,Enemies=enemies.Select(enemy=>LiveEnemyCombatSnapshot(enemy,original)).ToArray(),ArmedPrewarmTicks=pending.ArmedWarmTicks,Lord=assaultLord?.GetType().FullName});
            Pawn actor=original;Rand.PushState(764839);
            try
            {
                FrameProbe.Stage="live/transform";
                double transformMs;long transformBytes;
                if(diagnostic)Diagnostics.BeginTransition(id,"transform");
                try
                {
                    long at=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();
                    if(actorKind=="Transformed")
                    {
                        WildShift.TransformUtility.AddOrGetShapeshifter(original,kind);
                        Require(WildShift.TransformUtility.TransformToAnimal(original,kind),"existing actor production transformation");
                        actor=WildShift.SharedFormIdentity.ActiveFormFor(original);
                        Require(actor!=null&&actor!=original&&actor.Spawned,"production active animal owns the existing human");
                    }
                    transformMs=(Stopwatch.GetTimestamp()-at)*1000.0/Stopwatch.Frequency;
                    transformBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                }
                finally{if(diagnostic)Diagnostics.EndTransition();}
                actors.Add(actor);Activity.Actors.Add(actor);Activity.Actors.Add(original);
                FrameProbe.Stage="live/draft";
                double draftMs;long draftBytes;
                if(diagnostic)Diagnostics.BeginTransition(id,"draft");
                try
                {
                    long at=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();
                    Require(actor.drafter!=null,"existing actor has a draft controller");
                    actor.drafter.Drafted=liveDrafted;actor.drafter.FireAtWill=true;
                    draftMs=(Stopwatch.GetTimestamp()-at)*1000.0/Stopwatch.Frequency;
                    draftBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                }
                finally{if(diagnostic)Diagnostics.EndTransition();}
                transitions.Add(new{Actor=id,EnemiesAlreadySpawned=Alive(enemies),TransformOrVariantSetupMilliseconds=transformMs,TransformOrVariantSetupAllocatedBytes=transformBytes,DraftAndNativeWaitJobMilliseconds=draftMs,DraftAndNativeWaitJobAllocatedBytes=draftBytes,Drafted=liveDrafted});
                verbPreflight.Add(new{Pawn=actor.GetUniqueLoadID(),Original=id,Drafted=actor.Drafted,IsColonist=actor.IsColonist,Effective=VerbInfo(CurrentVerb(actor)),Automatic=VerbInfo(actor.TryGetAttackVerb(null,false)),ManualAllowed=VerbInfo(actor.TryGetAttackVerb(null,true)),Job=actor.CurJobDef?.defName,OriginalState=LiveSnapshot(original),ActiveState=LiveSnapshot(actor)});
                FrameProbe.Stage="live/assault";
                Require(enemyCount==0||assaultLord!=null,"existing armed-prewarm assault Lord is retained after transformation");
            }
            finally{Rand.PopState();}
            liveCameraCell=new IntVec3(arena.minX+29,0,arena.minZ+arena.Height/2);
            scenarioScope="Existing source colony and its full active mod profile, with private network-generating settings disabled. One existing actor is relocated to a bounded existing-map arena and optionally transformed/drafted through production code. Up to 60 newly generated rifle hostiles use native assault AI. Original traits, genes, hediffs, equipment and apparel are preserved; no health/damage replacement or invulnerability, no other-pawn cleanup, and no map-wide terrain/thing clearing. Existing colony simulation and actual casualties remain in measurements. Disabled network modules also omit their normal CPU work; conclusions do not establish their irrelevance.";
            Boot.Json("live-preflight.json",new{SourceSaveSHA256=input.Element("SourceSaveSHA256")?.Value,OriginalActorId=id,FormKind=form,RequestedDrafted=liveDrafted,BenchmarkDevMode=Prefs.DevMode,EnemyPrewarmTicks=pending.EnemyWarmTicks,ArmedPrewarmTicks=pending.ArmedWarmTicks,Map=map.uniqueID,MapSize=map.Size.ToString(),Arena=new{arena.minX,arena.minZ,arena.maxX,arena.maxZ},ActorCell=actorCell.ToString(),MapPawnCountBefore=pawnCountBefore,MapPawnCountAfter=map.mapPawns.AllPawnsSpawned.Count,SourceState=sourceState,PreTransformState=preTransformState,OriginalAfter=LiveSnapshot(original),ActiveState=LiveSnapshot(actor),NetworkProfile="RimTalk isEnabled=false; Persona Director experimental/autogen/autoevolve=false; AutoTranslation enabled/newmods=false. Copied profile only.",Scope=scenarioScope});
            BeginTelemetry(map);
            Boot.Json("live-telemetry-pause-state.json",LivePauseState("telemetry-start"));
            Require(!Find.TickManager.ForcePaused&&Find.TickManager.CurTimeSpeed!=TimeSpeed.Paused,"native clock accepted start after existing-save setup; inspect live telemetry pause state");
            StartLiveSelectionSequence();
        }

        private static CellRect ChooseArena(Map map,Pawn actor)
        {
            const int width=65,height=70,margin=8;
            CellRect best=new CellRect(margin,margin,width,height);long bestScore=long.MaxValue;
            foreach(int x in new[]{margin,map.Size.x-width-margin})
            foreach(int z in new[]{margin,map.Size.z-height-margin})
            {
                CellRect candidate=new CellRect(x,z,width,height);long score=0;
                foreach(IntVec3 cell in candidate)
                {
                    if(!cell.Standable(map))score+=20;
                    if(cell.GetEdifice(map)!=null)score+=100;
                    if(cell.GetThingList(map).OfType<Pawn>().Any(p=>p!=actor))score+=10000;
                }
                if(score<bestScore){bestScore=score;best=candidate;}
            }
            return best;
        }

        private static IntVec3 OpenCell(Map map,CellRect arena,IntVec3 near,HashSet<IntVec3> used)
        {
            foreach(IntVec3 cell in GenRadial.RadialCellsAround(near,15f,true))
            {
                if(!arena.Contains(cell)||!cell.InBounds(map)||!cell.Standable(map)||cell.GetEdifice(map)!=null||cell.GetThingList(map).OfType<Pawn>().Any()||used.Contains(cell))continue;
                used.Add(cell);return cell;
            }
            throw new InvalidOperationException("Live combat arena has no unoccupied native standable spawn cell near "+near);
        }

        private static object LiveSnapshot(Pawn p)
        {
            return new{Pawn=p.GetUniqueLoadID(),Def=p.def.defName,Kind=p.kindDef?.defName,Name=p.Name?.ToStringFull,Faction=p.Faction?.GetUniqueLoadID(),Spawned=p.Spawned,Position=p.Spawned?p.Position.ToString():"held",Dead=p.Dead,Downed=p.Downed,Drafted=p.Drafted,Genes=p.genes?.GenesListForReading.Select(g=>new{Def=g.def.defName,Active=g.Active}).ToArray(),Traits=p.story?.traits?.allTraits.Select(t=>new{Def=t.def.defName,Degree=t.Degree}).ToArray(),Hediffs=p.health.hediffSet.hediffs.Select(h=>new{Def=h.def.defName,Part=h.Part?.def?.defName,Severity=h.Severity}).ToArray(),Equipment=p.equipment?.AllEquipmentListForReading.Select(e=>new{Id=e.GetUniqueLoadID(),Def=e.def.defName}).ToArray(),Apparel=p.apparel?.WornApparel.Select(a=>new{Id=a.GetUniqueLoadID(),Def=a.def.defName,HP=a.HitPoints,MaxHP=a.MaxHitPoints}).ToArray(),Inventory=p.inventory?.innerContainer.Select(t=>new{Id=t.GetUniqueLoadID(),Def=t.def.defName,Count=t.stackCount}).ToArray()};
        }
    }
}
