using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace WildShiftCombatPerformance
{
    // TEST ONLY. Root update CPU and frame intervals are distinct metrics.
    // The interval includes native rendering/pacing; it is not CPU attribution.
    internal static class FrameProbe
    {
        public struct State { public long Start, Bytes, Interval; public string Stage; public bool Measure; }
        private struct Sample { public long Start, Cpu, Bytes, Interval; public string Enter, Exit; }
        private static readonly Sample[] firstGameplay = new Sample[256], firstMeasured = new Sample[256], slow = new Sample[64];
        private static readonly long[] cpuHistogram = new long[100001], intervalHistogram = new long[100001];
        private static long previousStart, firstStart, cpuSum, intervalSum, maxCpu, maxInterval, measuredFrames, guiCalls, guiTicks,cpuOverflow,intervalOverflow;
        private static int gameplayCount, measuredCount, slowCount;
        public static string Stage = "loading";
        public static bool Gameplay, Measuring;
        public static bool Installed, GuiHookInstalled;
        private static readonly List<string> hookErrors=new List<string>();
        public static void Install(Harmony harmony)
        {
            if (Installed) return;
            try
            {
                var update=AccessTools.DeclaredMethod(typeof(Root_Play),"Update",Type.EmptyTypes);
                if(update==null||update.GetMethodBody()==null)throw new InvalidOperationException("Declared Root_Play.Update implementation unavailable");
                harmony.Patch(update,prefix:new HarmonyMethod(typeof(FrameProbe),nameof(Before)),finalizer:new HarmonyMethod(typeof(FrameProbe),nameof(After)));
                Installed=true;
            }
            catch(Exception error){hookErrors.Add("Root update hook: "+error);}
            try
            {
                // Harmony rejects a base MethodInfo reflected through its
                // child type. Resolve the actual declared implementation.
                var gui=AccessTools.DeclaredMethod(typeof(Root),"OnGUI",Type.EmptyTypes);
                if(gui==null||gui.GetMethodBody()==null)throw new InvalidOperationException("Declared Root.OnGUI implementation unavailable");
                harmony.Patch(gui, prefix:new HarmonyMethod(typeof(FrameProbe), nameof(GuiBefore)),
                    finalizer:new HarmonyMethod(typeof(FrameProbe), nameof(GuiAfter)));
                GuiHookInstalled = true;
            }
            catch(Exception error){hookErrors.Add("Root GUI hook: "+error);}
        }
        public static void Before(out State __state)
        {
            long now=Stopwatch.GetTimestamp();
            if(firstStart==0)firstStart=now;
            __state=new State{Start=now,Bytes=GC.GetAllocatedBytesForCurrentThread(),Interval=previousStart==0?0:now-previousStart,Stage=Stage,Measure=Measuring};
            previousStart=now;
        }
        public static void After(State __state)
        {
            long elapsed=Stopwatch.GetTimestamp()-__state.Start;
            var sample=new Sample{Start=__state.Start,Cpu=elapsed,Bytes=GC.GetAllocatedBytesForCurrentThread()-__state.Bytes,Interval=__state.Interval,Enter=__state.Stage,Exit=Stage};
            if(Gameplay&&gameplayCount<firstGameplay.Length)firstGameplay[gameplayCount++]=sample;
            if(Gameplay&&elapsed>=16*Stopwatch.Frequency/1000&&slowCount<slow.Length)slow[slowCount++]=sample;
            if(!__state.Measure)return;
            measuredFrames++;cpuSum+=elapsed;intervalSum+=__state.Interval;maxCpu=Math.Max(maxCpu,elapsed);maxInterval=Math.Max(maxInterval,__state.Interval);
            if(elapsed*1000000L/Stopwatch.Frequency>=cpuHistogram.Length)cpuOverflow++;
            if(__state.Interval*1000000L/Stopwatch.Frequency>=intervalHistogram.Length)intervalOverflow++;
            cpuHistogram[Bin(elapsed)]++;intervalHistogram[Bin(__state.Interval)]++;
            if(measuredCount<firstMeasured.Length)firstMeasured[measuredCount++]=sample;
        }
        public static void GuiBefore(out long __state){__state=Measuring?Stopwatch.GetTimestamp():0;}
        public static void GuiAfter(long __state){if(__state!=0){guiCalls++;guiTicks+=Stopwatch.GetTimestamp()-__state;}}
        private static int Bin(long ticks){return (int)Math.Min(cpuHistogram.Length-1,ticks*1000000L/Stopwatch.Frequency);}
        private static double Millis(long ticks){return ticks*1000.0/Stopwatch.Frequency;}
        private static long Percentile(long[] histogram,double fraction)
        {
            long target=(long)Math.Ceiling(measuredFrames*fraction),sum=0;
            for(int i=0;i<histogram.Length;i++){sum+=histogram[i];if(sum>=target)return i;}
            return histogram.Length-1;
        }
        private static object View(Sample sample){return new{SinceFirstRootMilliseconds=Millis(sample.Start-firstStart),RootUpdateCpuMs=Millis(sample.Cpu),RootUpdateAllocatedBytes=sample.Bytes,FrameStartIntervalMs=Millis(sample.Interval),EnteredStage=sample.Enter,ExitedStage=sample.Exit};}
        public static object Result(){return new{Installed,GuiHookInstalled,HookErrors=hookErrors.ToArray(),MeasuredFrames=measuredFrames,MeanRootUpdateCpuMs=measuredFrames==0?0:Millis(cpuSum)/measuredFrames,MaxRootUpdateCpuMs=Millis(maxCpu),P95RootUpdateCpuUs=Percentile(cpuHistogram,.95),MeanFrameStartIntervalMs=measuredFrames==0?0:Millis(intervalSum)/measuredFrames,MaxFrameStartIntervalMs=Millis(maxInterval),P95FrameStartIntervalUs=Percentile(intervalHistogram,.95),HistogramCapMicroseconds=100000,RootCpuOverflow=cpuOverflow,FrameIntervalOverflow=intervalOverflow,ApproxObservedFrameRate=intervalSum==0?0:measuredFrames*Stopwatch.Frequency/(double)intervalSum,OnGuiCalls=guiCalls,OnGuiCpuMs=Millis(guiTicks),FirstGameplayRootFrames=firstGameplay.Take(gameplayCount).Select(View).ToArray(),FirstMeasuredRootFrames=firstMeasured.Take(measuredCount).Select(View).ToArray(),FirstSlowRootFrames=slow.Take(slowCount).Select(View).ToArray(),Scope="Root_Play.Update CPU and wall frame-start intervals; OnGUI is separate. Intervals include engine rendering/pacing and are not GPU or pure CPU timings. Headless intervals are not display FPS. Setup frames include arena generation; use per-actor transition timings for transformation/drafting."};}
    }
}
