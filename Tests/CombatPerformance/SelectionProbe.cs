using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShiftCombatPerformance
{
    // TEST ONLY. One frozen battle state, native selection and normal GUI
    // callbacks. Optional running phases retain real damage and casualties.
    internal static class SelectionUiProbe
    {
        internal sealed class MethodRow { public string Method;public long Calls,Ticks,Bytes; }
        internal sealed class Phase
        {
            public string Name;public bool Selected,Paused;public double Seconds,Started,Ended;
            public int StartTick,EndTick,ExpectedSelectedCount;
            public string StartState,EndState,StartCamera,EndCamera,StartedUtc,EndedUtc;
            public object NativeActivity;
            public object TimingSettingsStart,TimingSettingsEnd;
            public object ActorStateStart,ActorStateEnd,Generations;
            public object Details;
            public string HoverCase="HoverNotIntentionallyTested";
            public long EnemyShots,ActorBulletHits;
            public long Roots,RootTicks,RootBytes,MaxRoot,GuiCalls,GuiTicks,GuiBytes,MaxGui,Repaints,Layouts,IntervalCount,IntervalTicks,MaxInterval,PreviousStart;
            public bool SawGui,SawRepaint;public Vector2 FirstMouse,LastMouse,FirstRepaintMouse,LastRepaintMouse;public Vector3 FirstScreenMouse,LastScreenMouse;
            public readonly List<long> RootSamples=new List<long>(4096),GuiSamples=new List<long>(4096),Intervals=new List<long>(4096);
            public readonly Dictionary<MethodBase,MethodRow> Methods=new Dictionary<MethodBase,MethodRow>();
        }
        public struct State { internal Phase Phase;public long Start,Bytes; }
        public struct PathState { internal Phase Phase;internal MethodRow Row;public long Start,Bytes; }
        private static Phase current;
        private static bool installed,pathsInstalled,fullPathsInstalled,coarsePathsInstalled,modifierPathsInstalled;
        private static string guiEntryPoint;
        private static readonly List<MethodInfo> paths=new List<MethodInfo>();
        public static void Install(bool uiPaths,bool coarseUi=false,bool modifiers=false)
        {
            if(!installed)
            {
                Boot.Harmony.Patch(AccessTools.DeclaredMethod(typeof(Root_Play),"Update",Type.EmptyTypes),prefix:new HarmonyMethod(typeof(SelectionUiProbe),nameof(RootBefore)),finalizer:new HarmonyMethod(typeof(SelectionUiProbe),nameof(RootAfter)));
                MethodInfo gui;
                if(VersionControl.CurrentMinor==6)
                {
                    Type ui=AccessTools.TypeByName("RimWorld.UIRoot_Play");
                    gui=ui==null?null:AccessTools.DeclaredMethod(ui,"UIRootOnGUI",Type.EmptyTypes);
                }
                else gui=AccessTools.DeclaredMethod(typeof(Root),"OnGUI",Type.EmptyTypes);
                if(gui==null||gui.GetMethodBody()==null)throw new InvalidOperationException("Native managed GUI entry is unavailable");
                guiEntryPoint=gui.DeclaringType.FullName+"."+gui.Name;
                Boot.Harmony.Patch(gui,prefix:new HarmonyMethod(typeof(SelectionUiProbe),nameof(GuiBefore)),finalizer:new HarmonyMethod(typeof(SelectionUiProbe),nameof(GuiAfter)));
                installed=true;
            }
            if((!uiPaths&&!coarseUi&&!modifiers)||pathsInstalled)return;
            var methods=new HashSet<MethodInfo>();
            if(uiPaths)
            {
            Add(methods,"Verse.GizmoGridDrawer","DrawGizmoGridFor","DrawGizmoGrid");
            Add(methods,"RimWorld.MainTabWindow_Inspect","ExtraOnGUI","DoWindowContents","DoPaneContents");
            Add(methods,"RimWorld.InspectPaneUtility","ExtraOnGUI","InspectPaneOnGUI","UpdateTabs","DoTabs");
            Add(methods,"Verse.TooltipHandler","DoTooltipGUI","TipRegion");
            Add(methods,"RimHUD.Interface.Hud.Layers.LayoutLayer","Draw");
            Add(methods,"RimHUD.Interface.Screen.InspectPanePlus","DrawPane");
            Add(methods,"RimHUD.Interface.Screen.InspectPaneTabs","Draw");
            Add(methods,"RimHUD.Interface.Hud.Models.Values.HealthConditionValue","GetValue");
            Add(methods,"RimHUD.Interface.Hud.Models.Values.CompInfoValue","GetValue");
            Add(methods,"RimHUD.Interface.Hud.Tooltips.TooltipsPlus","Draw");
            Type meditation=AccessTools.TypeByName("RimWorld.MeditationUtility");
            if(meditation!=null)foreach(MethodInfo method in meditation.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name.Contains("CanOrder")||method.Name.Contains("SafeEnvironmentalConditions"))methods.Add(method);
            foreach(string name in new[]{"Verse.Pawn","RimWorld.Pawn_DraftController","Verse.Pawn_EquipmentTracker","RimWorld.Pawn_ApparelTracker","Verse.Pawn_HealthTracker","Verse.HediffWithComps","RimWorld.Pawn_AbilityTracker","RimWorld.Ability"})
            {
                Type type=AccessTools.TypeByName(name);if(type==null)continue;
                AddGizmos(methods,type);
            }
            foreach(Type type in typeof(WildShift.TransformUtility).Assembly.GetTypes())
            {
                if(type.FullName.Contains("Gizmo")||type.Name=="HediffComp_Transformed"||type.Name=="HediffComp_Shapeshifter")AddGizmos(methods,type);
                if(type.IsNested&&type.Name.IndexOf("Gizmo",StringComparison.OrdinalIgnoreCase)>=0)AddMoveNext(methods,type);
            }
            }
            if(coarseUi)
            {
                Add(methods,"RimHUD.Interface.Hud.Layers.LayoutLayer","Draw");
                Add(methods,"Verse.GizmoGridDrawer","DrawGizmoGridFor","DrawGizmoGrid");
                Add(methods,"RimWorld.ITab_Pawn_Health","FillTab");
                Add(methods,"Verse.PawnRenderer","DynamicDrawPhaseAt","RenderPawnAt");
                Add(methods,"RimWorld.PortraitsCache","Get");
                Add(methods,"RimWorld.ColonistBarColonistDrawer","DrawColonist");
                Add(methods,"RimWorld.SelectionDrawer","DrawSelectionOverlays");
            }
            if(modifiers)
            {
                Add(methods,"WildShift.SharedFormStats+State","Refresh","Build");
                Add(methods,"WildShift.SharedFormAnatomy+State","Refresh");
                Add(methods,"WildShift.SharedFormGear+State","Refresh");
                Type systems=AccessTools.TypeByName("WildShift.SharedFormSystems");
                MethodInfo version=systems?.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(method=>method.Name=="ModifierVersion").OrderByDescending(method=>method.GetParameters().Length).FirstOrDefault();
                if(version!=null)methods.Add(version);
            }
            foreach(MethodInfo method in methods.Where(method=>method!=null&&!method.IsAbstract&&!method.ContainsGenericParameters&&method.GetMethodBody()!=null))
            {
                Boot.Harmony.Patch(method,prefix:new HarmonyMethod(typeof(SelectionUiProbe),nameof(PathBefore)),finalizer:new HarmonyMethod(typeof(SelectionUiProbe),nameof(PathAfter)));
                paths.Add(method);
            }
            pathsInstalled=true;fullPathsInstalled=uiPaths;coarsePathsInstalled=coarseUi;modifierPathsInstalled=modifiers;
        }
        private static void Add(HashSet<MethodInfo> methods,string name,params string[] names)
        {
            Type type=AccessTools.TypeByName(name);if(type==null)return;
            foreach(MethodInfo method in type.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))if(names.Contains(method.Name))methods.Add(method);
        }
        private static void AddGizmos(HashSet<MethodInfo> methods,Type type)
        {
            foreach(MethodInfo method in type.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name.IndexOf("Gizmo",StringComparison.OrdinalIgnoreCase)>=0&&!method.Name.StartsWith("<"))methods.Add(method);
            foreach(Type nested in type.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))if(nested.Name.IndexOf("Gizmo",StringComparison.OrdinalIgnoreCase)>=0)AddMoveNext(methods,nested);
        }
        private static void AddMoveNext(HashSet<MethodInfo> methods,Type type)
        {
            foreach(MethodInfo method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))if(method.Name=="MoveNext"||method.Name.EndsWith(".MoveNext",StringComparison.Ordinal))methods.Add(method);
        }
        public static void Begin(Phase phase)
        {
            foreach(MethodInfo method in paths)phase.Methods[method]=new MethodRow{Method=method.DeclaringType.FullName+"."+method.Name+"("+string.Join(",",method.GetParameters().Select(parameter=>parameter.ParameterType.Name))+")"};
            current=phase;
        }
        public static void Stop(){current=null;}
        public static void RootBefore(out State __state)
        {
            Phase phase=current;__state=phase==null?default(State):new State{Phase=phase,Start=Stopwatch.GetTimestamp(),Bytes=GC.GetAllocatedBytesForCurrentThread()};
            if(phase==null)return;
            if(phase.PreviousStart!=0){long interval=__state.Start-phase.PreviousStart;phase.IntervalCount++;phase.IntervalTicks+=interval;phase.MaxInterval=Math.Max(phase.MaxInterval,interval);if(phase.Intervals.Count<4096)phase.Intervals.Add(interval);}phase.PreviousStart=__state.Start;
        }
        public static void RootAfter(State __state)
        {
            if(__state.Phase==null||!ReferenceEquals(current,__state.Phase))return;
            long ticks=Stopwatch.GetTimestamp()-__state.Start;Phase phase=__state.Phase;
            phase.Roots++;phase.RootTicks+=ticks;phase.RootBytes+=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes;phase.MaxRoot=Math.Max(phase.MaxRoot,ticks);if(phase.RootSamples.Count<4096)phase.RootSamples.Add(ticks);
        }
        public static void GuiBefore(out State __state)
        {
            Phase phase=current;__state=phase==null?default(State):new State{Phase=phase,Start=Stopwatch.GetTimestamp(),Bytes=GC.GetAllocatedBytesForCurrentThread()};
            if(phase==null)return;
            Event nativeEvent=Event.current;Vector2 mouse=nativeEvent?.mousePosition??Vector2.zero;Vector3 screenMouse=Input.mousePosition;
            if(!phase.SawGui){phase.FirstMouse=mouse;phase.FirstScreenMouse=screenMouse;phase.SawGui=true;}phase.LastMouse=mouse;phase.LastScreenMouse=screenMouse;
            if(nativeEvent?.type==EventType.Repaint){phase.Repaints++;if(!phase.SawRepaint){phase.FirstRepaintMouse=mouse;phase.SawRepaint=true;}phase.LastRepaintMouse=mouse;}
            if(nativeEvent?.type==EventType.Layout)phase.Layouts++;
        }
        public static void GuiAfter(State __state)
        {
            if(__state.Phase==null||!ReferenceEquals(current,__state.Phase))return;
            long ticks=Stopwatch.GetTimestamp()-__state.Start;Phase phase=__state.Phase;
            phase.GuiCalls++;phase.GuiTicks+=ticks;phase.GuiBytes+=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes;phase.MaxGui=Math.Max(phase.MaxGui,ticks);if(phase.GuiSamples.Count<4096)phase.GuiSamples.Add(ticks);
        }
        public static void PathBefore(MethodBase __originalMethod,out PathState __state)
        {
            Phase phase=current;__state=default(PathState);if(phase==null)return;MethodRow row;
            if(phase.Methods.TryGetValue(__originalMethod,out row))__state=new PathState{Phase=phase,Row=row,Start=Stopwatch.GetTimestamp(),Bytes=GC.GetAllocatedBytesForCurrentThread()};
        }
        public static void PathAfter(PathState __state)
        {
            if(__state.Row==null||!ReferenceEquals(current,__state.Phase))return;
            Interlocked.Increment(ref __state.Row.Calls);Interlocked.Add(ref __state.Row.Ticks,Stopwatch.GetTimestamp()-__state.Start);Interlocked.Add(ref __state.Row.Bytes,GC.GetAllocatedBytesForCurrentThread()-__state.Bytes);
        }
        private static double Ms(long ticks){return ticks*1000.0/Stopwatch.Frequency;}
        private static double P95(List<long> values){if(values.Count==0)return 0;long[] sorted=values.OrderBy(value=>value).ToArray();return Ms(sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*.95)-1)]);}
        public static object View(Phase phase){return new{phase.Name,phase.Selected,phase.Paused,RequestedSeconds=phase.Seconds,ActualSeconds=phase.Ended-phase.Started,phase.StartedUtc,phase.EndedUtc,phase.StartTick,phase.EndTick,phase.NativeActivity,phase.ActorStateStart,phase.ActorStateEnd,phase.Generations,phase.Details,phase.EnemyShots,phase.ActorBulletHits,phase.TimingSettingsStart,phase.TimingSettingsEnd,SamplingValid=phase.Roots>=(phase.Paused?20:1)&&phase.Repaints>=(phase.Paused?20:1),MinimumRootAndRepaintObservations=phase.Paused?20:1,NativeTickDelta=phase.EndTick-phase.StartTick,StateStable=phase.StartState==phase.EndState,CameraStable=phase.StartCamera==phase.EndCamera,phase.StartState,phase.EndState,phase.StartCamera,phase.EndCamera,phase.Roots,MeanRootCpuMs=phase.Roots==0?0:Ms(phase.RootTicks)/phase.Roots,MaxRootCpuMs=Ms(phase.MaxRoot),P95RootCpuMs=P95(phase.RootSamples),phase.RootBytes,phase.GuiCalls,MeanGuiCpuMs=phase.GuiCalls==0?0:Ms(phase.GuiTicks)/phase.GuiCalls,TotalGuiCpuMs=Ms(phase.GuiTicks),MaxGuiCpuMs=Ms(phase.MaxGui),P95GuiCpuMs=P95(phase.GuiSamples),phase.GuiBytes,phase.Repaints,phase.Layouts,ObservedFrameRate=phase.IntervalTicks==0?0:phase.IntervalCount*Stopwatch.Frequency/(double)phase.IntervalTicks,MeanFrameIntervalMs=phase.IntervalCount==0?0:Ms(phase.IntervalTicks)/phase.IntervalCount,MaxFrameIntervalMs=Ms(phase.MaxInterval),phase.HoverCase,FirstGuiMouse=phase.FirstMouse.ToString(),LastGuiMouse=phase.LastMouse.ToString(),FirstRepaintMouse=phase.FirstRepaintMouse.ToString(),LastRepaintMouse=phase.LastRepaintMouse.ToString(),FirstScreenMouse=phase.FirstScreenMouse.ToString(),LastScreenMouse=phase.LastScreenMouse.ToString(),GuiEntryPoint=guiEntryPoint,UiPathDiagnostics=pathsInstalled,FullUiPathDiagnostics=fullPathsInstalled,CoarseUiBuckets=coarsePathsInstalled,ModifierDiagnostics=modifierPathsInstalled,UiPaths=phase.Methods.Values.Where(row=>row.Calls>0||coarsePathsInstalled||modifierPathsInstalled).OrderByDescending(row=>row.Ticks).Select(row=>new{row.Method,row.Calls,InclusiveMs=Ms(row.Ticks),InclusiveAllocatedBytes=row.Bytes}).ToArray()};}
    }

    public partial class CombatBenchmark
    {
        private readonly List<SelectionUiProbe.Phase> selectionPhases=new List<SelectionUiProbe.Phase>();
        private int selectionIndex=-1;private bool selectionActive,selectionComplete,selectionValid;
        private bool selectionArena;
        private bool selectionRunningOnly,selectionGenerationDiagnostics;
        private double selectionPostHitSeconds,selectionPrerollSeconds=1,postHitInitialInjuries;
        private long postHitInitialArmor;
        private bool postHitDamageObserved;
        private int selectionFrozenTick;
        private object postHitSummary;
        private string selectionException;
        private bool selectionPreroll;private double selectionPrerollSince;private string selectionPrerollStartedUtc;
        private string[] selectionBaselineErrors;private string selectionInitialState,selectionInitialCamera;
        partial void StartLiveSelectionSequence()
        {
            XElement input=XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"combat-inputs.xml")).Root;
            selectionArena=bool.Parse(input.Element("ArenaSelectionSequence")?.Value??"false");
            if(!selectionArena&&!bool.Parse(input.Element("LiveSelectionSequence")?.Value??"false"))return;
            Require(graphicsMode=="Native"&&(liveScenario||selectionArena)&&actors.Count==1,"selection UI probe requires one native actor and an explicit live/arena mode");
            if(selectionArena)Prefs.DevMode=false;
            ConfigureSelectionDetails(input);
            bool paths=bool.Parse(input.Element("SelectionUiDiagnostics")?.Value??"false"),running=bool.Parse(input.Element("SelectionRunning")?.Value??"false");
            bool coarseUi=bool.Parse(input.Element("SelectionCoarseUiBuckets")?.Value??"false"),modifiers=bool.Parse(input.Element("SelectionModifierDiagnostics")?.Value??"false");
            selectionRunningOnly=bool.Parse(input.Element("SelectionRunningOnly")?.Value??"false");
            selectionGenerationDiagnostics=bool.Parse(input.Element("SelectionGenerationDiagnostics")?.Value??"false");
            if(selectionRunningOnly)running=true;
            double seconds=double.Parse(input.Element("SelectionSeconds")?.Value??"2",System.Globalization.CultureInfo.InvariantCulture),runSeconds=double.Parse(input.Element("SelectionRunningSeconds")?.Value??"0.5",System.Globalization.CultureInfo.InvariantCulture);
            selectionPostHitSeconds=double.Parse(input.Element("SelectionPostHitFreezeSeconds")?.Value??"0",System.Globalization.CultureInfo.InvariantCulture);
            Require(selectionPostHitSeconds>=0&&selectionPostHitSeconds<=10,"bounded native post-hit combat duration");
            if(selectionPostHitSeconds>0)Require(liveScenario&&!selectionArena&&!running&&!selectionRunningOnly&&enemyCount>0,"post-hit mode requires a live battle followed only by paused selection phases");
            selectionPrerollSeconds=selectionPostHitSeconds>0?selectionPostHitSeconds:1;
            Require(seconds>=.1&&seconds<=10&&runSeconds>=.1&&runSeconds<=2,"bounded UI phase durations");
            if(selectionRunningOnly)Require(!selectionArena&&runSeconds>=1,"live running-only phases must each last at least one second");
            Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            Find.TickManager.Pause();Timing.Enabled=Timing.CaptureFirst=Activity.Enabled=Diagnostics.Enabled=ForceSpeedObserver.Enabled=false;Timing.Reset();Timing.ResetFirstTicks();FrameProbe.Measuring=false;
            SelectionUiProbe.Install(paths,coarseUi,modifiers);
            if(selectionGenerationDiagnostics)SharedRefreshProbe.Install(Boot.Harmony);
            if(!selectionRunningOnly)foreach(bool selected in new[]{true,false,true})selectionPhases.Add(new SelectionUiProbe.Phase{Name="paused-"+(selected?"selected":"deselected")+"-"+selectionPhases.Count,Selected=selected,Paused=true,Seconds=seconds});
            if(running)foreach(bool selected in new[]{true,false,true})selectionPhases.Add(new SelectionUiProbe.Phase{Name="running-"+(selected?"selected":"deselected")+"-"+selectionPhases.Count,Selected=selected,Paused=false,Seconds=runSeconds});
            selectionBaselineErrors=NativeErrorTexts();selectionInitialState=SelectionState();selectionInitialCamera=SelectionCamera();
            postHitInitialInjuries=SelectionInjurySeverity();postHitInitialArmor=SelectionArmorHitPoints();
            startTick=Find.TickManager.TicksGame;startTime=wall.Elapsed.TotalSeconds;allocatedAtStart=GC.GetTotalAllocatedBytes(false);actorsAtStart=Alive(actors);enemiesAtStart=Alive(enemies);measurementBaselineNativeErrors=selectionBaselineErrors;
            scenarioScope="Within one native battle state: paused selected/deselected/selected UI isolation; optional short running phases retain real damage and may drift. Paused UI results are not combat TPS measurements. Root and OnGUI CPU are separate; frame intervals include rendering/pacing. Hover was not intentionally tested. Live Care and speed overrides are bypassed.";
            if(selectionArena)scenarioScope="Synthetic cleared arena with one native generated actor and fixture NPCs; no actual user save/profile. Paused selected/deselected/reselected UI isolation with identical raw pawn state and camera. Arena drafted actors use fixture forced Wait_Combat jobs; this is a GUI workload, not native combat AI/TPS validation. Root and GUI CPU are separate and intervals include rendering/pacing. Hover was not intentionally tested.";
            if(selectionRunningOnly)scenarioScope="Within one live native battle: running selected/deselected/selected phases after a one-second native Normal combat preroll. Real hits, health and armor damage and state drift are retained. Root, OnGUI and native tick CPU are separate; intervals include rendering/pacing. No invulnerability, forced ticks or native damage overrides. Hover was not intentionally tested.";
            if(selectionPostHitSeconds>0)scenarioScope="Post-hit frozen-state UI comparison: native Normal combat after transformation must produce actor bullet hits and real injury or armor wear while the actor survives. The same wounded state is then paused for selected/deselected/reselected phases. Raw actor/original/enemy health, genes, gear, jobs and camera must remain identical, with zero native ticks in every measured phase. UI CPU results are not combat TPS. Optional coarse/outer-modifier timing has observer overhead and no getter/hash instrumentation.";
            selectionActive=true;selectionPreroll=true;selectionPrerollSince=wall.Elapsed.TotalSeconds;selectionPrerollStartedUtc=DateTime.UtcNow.ToString("O");
            if(selectionRunningOnly||selectionPostHitSeconds>0){Timing.Reset();Activity.Reset();Timing.Enabled=Activity.Enabled=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;}
            Find.Selector.ClearSelection();Find.Selector.Select(actors[0],false,false);SelectionUiProbe.Stop();FrameProbe.Stage="selection/unmeasured-selected-preroll";
            ApplySelectionDetails(true);
            Boot.Json("selection-preroll.json",new{Complete=false,StartedUtc=selectionPrerollStartedUtc,RequestedSeconds=selectionPrerollSeconds,Tick=Find.TickManager.TicksGame,RunningNativeCombat=selectionRunningOnly||selectionPostHitSeconds>0,PostHitFreeze=selectionPostHitSeconds>0,Settings=SelectionSettings(),Scope=selectionPostHitSeconds>0?"Real native combat after transformation, before freezing one wounded state for all three paused UI phases.":selectionRunningOnly?"Unmeasured one-second native Normal combat/selected-UI preroll. Real shots and damage remain enabled.":"Unmeasured first selected GUI preroll to warm hook/JIT and first-draw caches. Native simulation remains paused."});
        }
        partial void StepLiveSelectionSequence(ref bool handled)
        {
            if(!selectionActive)return;handled=true;
            if(selectionComplete){selectionActive=false;Finish(null);return;}
            if(selectionPreroll)
            {
                if(selectionRunningOnly||selectionPostHitSeconds>0)Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;else Find.TickManager.Pause();
                if(selectionPostHitSeconds>0&&Alive(actors)!=actorCount){Find.TickManager.Pause();RecordPostHitWarmup(false);}
                Require(Alive(actors)==actorCount,"selection actor survived real precombat preroll");
                if(wall.Elapsed.TotalSeconds-selectionPrerollSince<selectionPrerollSeconds)return;
                if(selectionPostHitSeconds>0)
                {
                    Find.TickManager.Pause();RecordPostHitWarmup(true);
                    Require(postHitDamageObserved,"post-hit UI comparison requires real actor bullet hits and observed injury increase or armor HP loss; inspect selection-posthit-warmup.json");
                }
                string warmedState=SelectionState(),warmedCamera=SelectionCamera();
                Boot.Json("selection-preroll.json",new{Complete=true,StartedUtc=selectionPrerollStartedUtc,EndedUtc=DateTime.UtcNow.ToString("O"),ActualSeconds=wall.Elapsed.TotalSeconds-selectionPrerollSince,StartTick=startTick,EndTick=Find.TickManager.TicksGame,RunningNativeCombat=selectionRunningOnly||selectionPostHitSeconds>0,PostHitFreeze=selectionPostHitSeconds>0,NativeActivity=SelectionNativeActivity(),ActorState=SelectionActorHealth(),RawStateChanged=selectionInitialState!=warmedState,CameraChanged=selectionInitialCamera!=warmedCamera,Settings=SelectionSettings(),Scope=selectionPostHitSeconds>0?"Real native combat completed; simulation is now paused. All measured phases use exactly this wounded raw-state/camera baseline.":selectionRunningOnly?"One-second native Normal combat and selected-UI preroll, excluded from the three measured running phases. Actual shots/damage recorded; no forced ticks or damage overrides.":"Unmeasured first selected GUI preroll; hook/JIT and first-draw cache work is excluded from measured phases. Native simulation remains paused. Measured phases use the warmed snapshot as their common baseline."});
                if(!selectionRunningOnly&&selectionPostHitSeconds==0)Require(Find.TickManager.TicksGame==startTick,"selected preroll did not advance native simulation");
                selectionInitialState=warmedState;selectionInitialCamera=warmedCamera;selectionFrozenTick=Find.TickManager.TicksGame;selectionPreroll=false;BeginSelectionPhase();return;
            }
            SelectionUiProbe.Phase phase=selectionPhases[selectionIndex];
            if(phase.Paused)Find.TickManager.Pause();else Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            Require(Alive(actors)==actorCount,"selection actor survived real native running phase");
            if(wall.Elapsed.TotalSeconds-phase.Started<phase.Seconds)return;
            SelectionUiProbe.Stop();SharedRefreshProbe.ScopeEnabled=false;phase.Ended=wall.Elapsed.TotalSeconds;phase.EndedUtc=DateTime.UtcNow.ToString("O");phase.EndTick=Find.TickManager.TicksGame;phase.EndState=SelectionState();phase.EndCamera=SelectionCamera();phase.NativeActivity=SelectionNativeActivity();phase.TimingSettingsEnd=SelectionSettings();phase.ActorStateEnd=SelectionActorHealth();phase.Generations=selectionGenerationDiagnostics?SharedRefreshProbe.Result():null;phase.EnemyShots=Activity.EnemyShots;phase.ActorBulletHits=Activity.ActorBulletHits;
            CaptureSelectionDetails(ref phase.Details,ref phase.HoverCase);
            if(selectionIndex+1<selectionPhases.Count){BeginSelectionPhase();return;}
            Find.TickManager.Pause();selectionComplete=true;
            selectionValid=selectionPhases.All(row=>row.Roots>=(row.Paused?20:1)&&row.GuiCalls>0&&row.Repaints>=(row.Paused?20:1)&&row.StartCamera==row.EndCamera&&(!row.Paused||(row.StartTick==row.EndTick&&row.StartState==row.EndState)))&&selectionPhases.Where(row=>row.Paused).All(row=>row.StartState==selectionInitialState&&row.StartCamera==selectionInitialCamera)&&ErrorDifference(NativeErrorTexts(),selectionBaselineErrors).Length==0;
            if(selectionRunningOnly&&enemyCount>0)selectionValid=selectionValid&&selectionPhases.Sum(row=>row.EnemyShots)>0&&selectionPhases.Sum(row=>row.ActorBulletHits)>0;
            if(selectionPostHitSeconds>0)selectionValid=selectionValid&&postHitDamageObserved&&selectionPhases.All(row=>row.Paused&&row.StartTick==selectionFrozenTick&&row.EndTick==selectionFrozenTick);
            ValidateSelectionDetails(ref selectionValid);
            WriteSelectionResult();
        }
        private void BeginSelectionPhase()
        {
            SelectionUiProbe.Phase phase=selectionPhases[++selectionIndex];
            Find.Selector.ClearSelection();if(phase.Selected)Find.Selector.Select(actors[0],false,false);
            ApplySelectionDetails(phase.Selected);
            if(phase.Paused)Find.TickManager.Pause();else Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            phase.Started=wall.Elapsed.TotalSeconds;phase.StartedUtc=DateTime.UtcNow.ToString("O");phase.StartTick=Find.TickManager.TicksGame;phase.StartState=SelectionState();phase.StartCamera=SelectionCamera();
            phase.TimingSettingsStart=SelectionSettings();
            phase.ActorStateStart=SelectionActorHealth();
            phase.ExpectedSelectedCount=phase.Selected?1:0;Require(Find.Selector.NumSelected==phase.ExpectedSelectedCount,"native selected/deselected phase applied");
            Timing.Enabled=Activity.Enabled=false;Timing.Reset();Activity.Reset();if(!phase.Paused)Timing.Enabled=Activity.Enabled=true;
            if(selectionGenerationDiagnostics){SharedRefreshProbe.Reset();SharedRefreshProbe.ScopeEnabled=true;}
            FrameProbe.Stage="selection/"+phase.Name;SelectionUiProbe.Begin(phase);
            Boot.Json("selection-phase.json",new{Phase=phase.Name,phase.Selected,phase.Paused,phase.StartedUtc,phase.StartTick,RequestedSeconds=phase.Seconds,HoverCase="HoverNotIntentionallyTested"});
        }
        private static object SelectionNativeActivity(){return new{NativeTicks=Timing.Count,MeanNativeTickMs=Timing.Count==0?0:Timing.Sum*1000.0/Stopwatch.Frequency/Timing.Count,MaxNativeTickMs=Timing.Max*1000.0/Stopwatch.Frequency,TickAllocatedBytes=Timing.Allocated,Activity.Shots,Activity.EnemyShots,Activity.ActorDamageEvents,Activity.ActorRawDamage,Activity.ActorJobStarts,Activity.OrdersThinkQueries,Activity.CloseMeleeThreatThinkQueries};}
        private static object SelectionSettings(){return new{Application.runInBackground,Application.targetFrameRate,QualitySettings.vSyncCount,HasFocus=Application.isFocused,ScreenWidth=Screen.width,ScreenHeight=Screen.height,ScreenMouse=Input.mousePosition.ToString()};}
        private double SelectionInjurySeverity(){return actors.Sum(pawn=>pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(injury=>(double)injury.Severity));}
        private long SelectionArmorHitPoints(){return actors.Concat(originals).Distinct().SelectMany(pawn=>pawn.apparel==null?Enumerable.Empty<Apparel>():pawn.apparel.WornApparel).Distinct().Sum(item=>(long)item.HitPoints);}
        private void RecordPostHitWarmup(bool complete)
        {
            double injuries=SelectionInjurySeverity();long armor=SelectionArmorHitPoints();
            postHitDamageObserved=complete&&Alive(actors)==actorCount&&Activity.EnemyShots>0&&Activity.ActorBulletHits>0&&(injuries>postHitInitialInjuries+.00001||armor<postHitInitialArmor);
            postHitSummary=new{Complete=complete,DamageObserved=postHitDamageObserved,RequestedSeconds=selectionPostHitSeconds,ActualSeconds=wall.Elapsed.TotalSeconds-selectionPrerollSince,StartedUtc=selectionPrerollStartedUtc,EndedUtc=DateTime.UtcNow.ToString("O"),StartTick=startTick,EndTick=Find.TickManager.TicksGame,ActualNativeTicks=Find.TickManager.TicksGame-startTick,AliveActors=Alive(actors),AliveEnemies=Alive(enemies),InjurySeverityBefore=postHitInitialInjuries,InjurySeverityAfter=injuries,ArmorHitPointsBefore=postHitInitialArmor,ArmorHitPointsAfter=armor,NativeActivity=SelectionNativeActivity(),ActorState=SelectionActorHealth(),Paused=Find.TickManager.Paused,Scope="Actual native Normal combat after transformation, no forced ticks/damage/invulnerability. Success requires survival, enemy shots, actor bullet hits, and injury increase or real armor HP loss. This exact resulting raw state is frozen for all measured selection phases."};
            Boot.Json("selection-posthit-warmup.json",postHitSummary);
        }
        private object SelectionActorHealth(){return actors.Concat(originals).Distinct().Select(pawn=>new{Pawn=pawn.GetUniqueLoadID(),pawn.Dead,pawn.Downed,pawn.Spawned,pawn.Drafted,SummaryHealthPercent=pawn.health.summaryHealth.SummaryHealthPercent,PainTotal=pawn.health.hediffSet.PainTotal,TotalInjurySeverity=pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(injury=>injury.Severity),Armor=pawn.apparel?.WornApparel.Select(item=>new{Id=item.GetUniqueLoadID(),Def=item.def.defName,item.HitPoints,item.MaxHitPoints}).ToArray()}).ToArray();}
        partial void FinalizeSelectionFailure(Exception error)
        {
            if(error==null||!selectionActive)return;
            SelectionUiProbe.Stop();SharedRefreshProbe.ScopeEnabled=false;selectionException=error.ToString();selectionValid=false;selectionComplete=true;
            if(selectionIndex>=0)
            {
                SelectionUiProbe.Phase phase=selectionPhases[selectionIndex];
                if(phase.EndedUtc==null){phase.Ended=wall.Elapsed.TotalSeconds;phase.EndedUtc=DateTime.UtcNow.ToString("O");phase.EndTick=Find.TickManager.TicksGame;phase.EndState=SelectionState();phase.EndCamera=SelectionCamera();phase.NativeActivity=SelectionNativeActivity();phase.TimingSettingsEnd=SelectionSettings();phase.ActorStateEnd=SelectionActorHealth();phase.Generations=selectionGenerationDiagnostics?SharedRefreshProbe.Result():null;}
            }
            WriteSelectionResult();
        }
        private string SelectionState(){return JsonSerializer.Serialize(actors.Concat(originals).Distinct().Concat(enemies).Select(pawn=>new{Id=pawn.GetUniqueLoadID(),pawn.Dead,pawn.Downed,pawn.Spawned,Position=pawn.Spawned?pawn.Position.ToString():null,Job=pawn.CurJobDef?.defName,Health=pawn.health.hediffSet.hediffs.Select(hediff=>new{Def=hediff.def.defName,Part=hediff.Part?.def.defName,hediff.Severity}).ToArray(),Genes=pawn.genes?.GenesListForReading.Select(gene=>new{Def=gene.def.defName,gene.Active}).ToArray(),Traits=pawn.story?.traits?.allTraits.Select(trait=>new{Def=trait.def.defName,trait.Degree}).ToArray(),Equipment=pawn.equipment?.AllEquipmentListForReading.Select(item=>new{Id=item.GetUniqueLoadID(),item.HitPoints}).ToArray(),Apparel=pawn.apparel?.WornApparel.Select(item=>new{Id=item.GetUniqueLoadID(),item.HitPoints}).ToArray()}).ToArray());}
        private static string SelectionCamera(){return string.Join("|",typeof(CameraDriver).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(field=>field.Name=="rootPos"||field.Name=="desiredSize"||field.Name=="rootSize").Select(field=>field.Name+"="+SelectionCameraValue(field.GetValue(Find.CameraDriver))));}
        private static string SelectionCameraValue(object value)
        {
            if(value is Vector3){Vector3 vector=(Vector3)value;return vector.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+vector.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+vector.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
            if(value is float)return ((float)value).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(value is double)return ((double)value).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
        }
        private void WriteSelectionResult(){Boot.Json("selection-result.json",new{Passed=selectionValid,SequenceCompleted=selectionComplete&&selectionException==null,Exception=selectionException,ExpectedPhases=selectionPhases.Count,Mode=selectionPostHitSeconds>0?"PostHitFrozenSelectionUI":selectionArena?"SyntheticArenaSelectionUI":selectionRunningOnly?"RunningBattleSelectionUI":"SameBattleSelectionUI",GameSHA256=Boot.Hash(typeof(Pawn).Assembly.Location),WildShiftSHA256=Boot.Hash(typeof(WildShift.TransformUtility).Assembly.Location),FixtureSHA256=Boot.Hash(typeof(Boot).Assembly.Location),RequestedEnemies=enemyCount,Actor=actors[0].GetUniqueLoadID(),AliveActors=Alive(actors),AliveEnemies=Alive(enemies),NativeTickDelta=Find.TickManager.TicksGame-startTick,FrozenTick=selectionFrozenTick,FrozenNativeTickDelta=selectionPostHitSeconds>0?Find.TickManager.TicksGame-selectionFrozenTick:(int?)null,PostHitWarmup=postHitSummary,StartupNativeErrors=selectionBaselineErrors,NewNativeErrors=ErrorDifference(NativeErrorTexts(),selectionBaselineErrors),Phases=selectionPhases.Where(phase=>phase.StartedUtc!=null).Select(SelectionUiProbe.View).ToArray(),Scope=scenarioScope});}
        partial void OverrideSelectionPassed(Exception error,string[] nativeErrors,ref bool passed){if(selectionComplete)passed=error==null&&selectionValid&&nativeErrors.Length==0;}
        partial void ConfigureSelectionDetails(XElement input);
        partial void ApplySelectionDetails(bool selected);
        partial void CaptureSelectionDetails(ref object details,ref string hoverCase);
        partial void ValidateSelectionDetails(ref bool passed);
    }
}
