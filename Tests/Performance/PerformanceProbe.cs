using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WildShift;
#if RIMWORLD_1_7
using System.Text.Json;
#else
using WildShiftSharedStateValidation.Json;
#endif

namespace WildShiftPerformanceValidation
{
    [StaticConstructorOnStartup]
    public static class Boot
    {
        public static readonly string Output = Path.Combine(GenFilePaths.SaveDataFolderPath, "PerformanceResults");
        static Boot()
        {
            Directory.CreateDirectory(Output);
            Application.runInBackground = true;
            var harmony = new Harmony("wildshift.tests.performance");
            harmony.Patch(AccessTools.Method(typeof(Root_Play), "SetupForQuickTestPlay"), postfix: new HarmonyMethod(typeof(Boot), nameof(SmallMap)));
            harmony.Patch(AccessTools.Method(typeof(TickManager), "DoSingleTick"),
                prefix: new HarmonyMethod(typeof(TickTiming), nameof(TickTiming.Before)),
                postfix: new HarmonyMethod(typeof(TickTiming), nameof(TickTiming.After)));
        }
        public static void SmallMap() { Find.GameInitData.mapSize = 75; }
        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        public static void Json(string name, object value)
        {
            File.WriteAllText(Path.Combine(Output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    // One fixed 1-us histogram (~1.6 MB) allocated during helper startup, no
    // per-tick collection/delegate/allocation. The hook observes, never runs or
    // replaces, native ticks. Its constant cost applies to every condition.
    internal static class TickTiming
    {
        private const int MaximumMicroseconds = 200000;
        private static readonly long[] histogram = new long[MaximumMicroseconds + 1];
        public static bool Enabled;
        public static long Count, SumStopwatchTicks, MaxStopwatchTicks, Overflow;
        public static void Reset()
        {
            Array.Clear(histogram, 0, histogram.Length);
            Count = SumStopwatchTicks = MaxStopwatchTicks = Overflow = 0;
        }
        public static void Before(out long __state) { __state = Enabled ? Stopwatch.GetTimestamp() : 0; }
        public static void After(long __state)
        {
            if (__state == 0 || !Enabled) return;
            long elapsed = Stopwatch.GetTimestamp() - __state;
            long microseconds = elapsed * 1000000L / Stopwatch.Frequency;
            Count++; SumStopwatchTicks += elapsed;
            if (elapsed > MaxStopwatchTicks) MaxStopwatchTicks = elapsed;
            if (microseconds >= MaximumMicroseconds) { microseconds = MaximumMicroseconds; Overflow++; }
            histogram[(int)microseconds]++;
        }
        public static double Percentile(double fraction)
        {
            if (Count == 0) return 0;
            long target = (long)Math.Ceiling(Count * fraction), seen = 0;
            for (int i = 0; i < histogram.Length; i++) { seen += histogram[i]; if (seen >= target) return i; }
            return MaximumMicroseconds;
        }
    }

    public sealed class Benchmark : GameComponent
    {
        private List<Pawn> originals = new List<Pawn>();
        private bool seeded, ready, started, measuring, done;
        private int frames, activeForms, totalPawns, warmupSeconds, measureSeconds, sampleSeconds, startTick, lastTick;
        private int interventions, speedCorrections, phaseStartTick, autoUndraftKeepaliveCalls, targetFps, startFrame, endFrame;
        private double startTime, lastSampleTime, lastCareTime, careSeconds;
        private readonly Stopwatch wall = Stopwatch.StartNew();
        private MachineSample first, last, end;
        private string preset, phase, seededPreset, rngBeforeVariant, rngAfterVariant, graphicsMode;
        private bool smoke;
        private StreamWriter csv;
        private long peakPrivate, peakWorkingSet, peakManaged;
        private ulong minimumAvailable;
        private readonly List<string> checks = new List<string>();
        private sealed class ExpectedBody
        {
            public readonly System.WeakReference<Pawn> Reference;
            public readonly string Id;
            public readonly int WrapperHash;
            public ExpectedBody(Pawn body)
            {
                Reference = new System.WeakReference<Pawn>(body);
                Id = body.GetUniqueLoadID();
                WrapperHash = RuntimeHelpers.GetHashCode(body);
            }
        }
        // Diagnostics must not root animal wrappers and hide a production weak
        // identity/cache lifetime defect. Original humans are the existing
        // canonical seed references; expected animal bodies stay weak here.
        private readonly Dictionary<Pawn, ExpectedBody> expectedBodies = new Dictionary<Pawn, ExpectedBody>();
        private static readonly AccessTools.FieldRef<Pawn_DraftController, AutoUndrafter> autoUndrafter
            = AccessTools.FieldRefAccess<Pawn_DraftController, AutoUndrafter>("autoUndrafter");
        public Benchmark(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref seeded, "performanceSeeded", false);
            Scribe_Values.Look(ref seededPreset, "performanceFeaturePreset");
            Scribe_Collections.Look(ref originals, "performanceOriginals", LookMode.Reference);
        }
        public override void FinalizeInit() { Find.TickManager.Pause(); ready = true; }

        public override void GameComponentUpdate()
        {
            if (!ready || done || LongEventHandler.AnyEventNowOrWaiting || ++frames < 30) return;
            try
            {
                if (!started) { Start(); return; }
                if (phase != "measure") return;
                double now = wall.Elapsed.TotalSeconds;
                if (now - lastCareTime >= 1.0) { Care(); lastCareTime = now; }
                if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
                {
                    speedCorrections++;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                }
                if (!measuring)
                {
                    if (now - startTime < warmupSeconds) return;
                    ValidatePopulation();
                    TickTiming.Reset();
                    first = last = WindowsMetrics.Capture();
                    peakPrivate = first.PrivateBytes; peakWorkingSet = first.WorkingSet; peakManaged = first.ManagedHeap;
                    minimumAvailable = first.AvailablePhysical;
                    startTick = lastTick = Find.TickManager.TicksGame;
                    startFrame = UnityEngine.Time.frameCount;
                    startTime = lastSampleTime = wall.Elapsed.TotalSeconds;
                    interventions = autoUndraftKeepaliveCalls = speedCorrections = 0;
                    careSeconds = 0;
                    measuring = TickTiming.Enabled = true;
                    WriteProgress("measuring", 0);
                    return;
                }
                double seconds = now - startTime;
                if (now - lastSampleTime >= sampleSeconds || seconds >= measureSeconds) Sample(now);
                if (seconds >= measureSeconds) Finish();
            }
            catch (Exception exception) { Fail(exception); }
        }

        private void Start()
        {
            started = true;
            Find.TickManager.Pause();
            XDocument input = XDocument.Load(Path.Combine(GenFilePaths.SaveDataFolderPath, "performance-inputs.xml"));
            phase = Text(input, "Phase"); preset = Text(input, "FeaturePreset");
            graphicsMode = Text(input, "GraphicsMode"); targetFps = Integer(input, "TargetFPS");
            activeForms = Integer(input, "ActiveForms"); totalPawns = Integer(input, "TotalPawns");
            warmupSeconds = Integer(input, "WarmupSeconds"); measureSeconds = Integer(input, "MeasureSeconds"); sampleSeconds = Integer(input, "SampleSeconds");
            smoke = bool.Parse(Text(input, "Smoke"));
            Require(totalPawns == 50 && (activeForms == 0 || activeForms == 20 || activeForms == 50), "fixed 50 canonical originals and supported active-form condition");
            Require(preset == "basic" || preset == "abilities", "explicit basic or abilities feature preset");
            Require((graphicsMode == "Native" || graphicsMode == "Headless") && targetFps == 60, "explicit native/headless graphics mode and common 60 FPS target");
            Application.targetFrameRate = targetFps;
            QualitySettings.vSyncCount = 0;
            Require(Application.targetFrameRate == targetFps, "native frame target reads back correctly; actual FPS cap remains independently measured");
            Require(Prefs.AutosaveIntervalDays >= 1000f && !Current.Game.Info.permadeathMode,
                "isolated native autosave interval lies beyond the benchmark duration");
            Require(smoke || (warmupSeconds >= 0 && measureSeconds >= 60), "comparison has nonnegative warmup and at least 60s measured time; actual duration is reported");
            Require(sampleSeconds >= 1 && sampleSeconds <= 30, "bounded metric sample interval");
            Require(Text(input, "GameVersion") == VersionControl.CurrentVersionString, "actual native game version");
            Require(SamePath(Text(input, "GameAssemblyPath"), typeof(Pawn).Assembly.Location)
                && Text(input, "GameAssemblySHA256") == Boot.Hash(typeof(Pawn).Assembly.Location), "actual native game assembly path and hash");
            Require(SamePath(Text(input, "WildShiftPath"), typeof(TransformUtility).Assembly.Location)
                && Text(input, "WildShiftSHA256") == Boot.Hash(typeof(TransformUtility).Assembly.Location), "actual production DLL path and hash");
            Require(Text(input, "FixtureSHA256") == Boot.Hash(typeof(Boot).Assembly.Location), "actual performance helper DLL hash");
            string[] expected = input.Root.Element("Mods").Elements("li").Select(n => n.Value.ToLowerInvariant()).ToArray();
            Require(expected.SequenceEqual(LoadedModManager.RunningModsListForReading.Select(m => m.PackageId.ToLowerInvariant())), "actual isolated mod IDs and order");
            Require(ModsConfig.RoyaltyActive && ModsConfig.IdeologyActive && ModsConfig.BiotechActive && ModsConfig.AnomalyActive && ModsConfig.OdysseyActive, "all five DLCs active");
            DebugSettings.enableStoryteller = false;
            DebugSettings.enableRandomMentalStates = false;
            DebugSettings.enableRandomDiseases = false;
            // noAnimals destroys existing spawned animals in native Pawn.Tick.
            // Zero the isolated world's biome density instead: the ordinary
            // wild spawner then considers its zero target full, without touching
            // any existing player-owned transformed body or replacing its tick.
            DebugSettings.noAnimals = false;
            Map controlledMap = Find.CurrentMap;
            Require(controlledMap != null && controlledMap.TileInfo?.PrimaryBiome != null, "controlled native map and tile biome exist");
            controlledMap.TileInfo.PrimaryBiome.animalDensity = 0f;
            Require(controlledMap.TileInfo.AnimalDensity == 0f && controlledMap.wildAnimalSpawner.AnimalEcosystemFull,
                "native new-wild-animal density is zero without the destructive noAnimals flag");
            DebugSettings.fastEcology = false;
            DebugSettings.pauseOnError = false;
            if (phase == "seed") { Seed(); return; }
            Require(phase == "measure" && seeded && originals != null && originals.Count == totalPawns, "fresh native load resolves the common baseline's exact 50 originals");
            Require(seededPreset == preset, "measurement feature preset matches the serialized common baseline");
            Require(originals.Select(p => p.GetUniqueLoadID()).Distinct().Count() == totalPawns, "canonical original IDs remain unique");
            rngBeforeVariant = RandomState();
            Require(rngBeforeVariant != "unavailable", "native random-state snapshot is available for the generation guard");
            Rand.PushState(73519);
            try
            {
                for (int i = 0; i < activeForms; i++)
                    Require(TransformUtility.TransformToAnimal(originals[i], Kind()), "native production transformation " + i);
            }
            finally { Rand.PopState(); }
            rngAfterVariant = RandomState();
            Require(rngBeforeVariant == rngAfterVariant, "variant generation restores the common baseline's world random state");
            foreach (Pawn original in originals)
            {
                Pawn body = SharedFormIdentity.ActiveFormFor(original) ?? original;
                expectedBodies.Add(original, new ExpectedBody(body));
                Require(body.drafter != null, "active body has a native draft controller");
                body.drafter.Drafted = true;
            }
            ValidatePopulation(); Care();
            phaseStartTick = Find.TickManager.TicksGame;
            csv = new StreamWriter(Path.Combine(Boot.Output, "samples.csv"), false);
            csv.WriteLine("seconds,ticks,window_tps,overall_tps,tick_mean_us,tick_p50_us,tick_p95_us,working_set_bytes,private_bytes,managed_heap_bytes,allocated_delta_bytes,process_cpu_percent_one_core,system_busy_percent,available_physical_bytes,page_fault_delta,gen0_delta,gen1_delta,gen2_delta,tick_rate_multiplier,native_frames,overall_fps,native_target_fps");
            csv.Flush();
            startTime = lastSampleTime = lastCareTime = wall.Elapsed.TotalSeconds;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            WriteProgress("warming-up", 0);
        }

        private void Seed()
        {
            Require(!seeded, "seed creates a new baseline rather than overwriting an existing fixture world");
            Map map = Find.CurrentMap;
            Require(map != null && map.Size.x == 75, "native quicktest has the controlled 75x75 map");
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                pawn.DeSpawn();
                if (pawn.Faction?.IsPlayer == true) pawn.SetFaction(null);
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }
            foreach (Thing thing in map.listerThings.AllThings.ToList()) if (thing.def.destroyable) thing.Destroy();
            foreach (IntVec3 cell in map.AllCells) { map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); map.fogGrid.Unfog(cell); }
            originals = new List<Pawn>(totalPawns);
            Rand.PushState(847391);
            try
            {
                for (int i = 0; i < totalPawns; i++)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                        forceGenerateNewPawn: true, canGeneratePawnRelations: false, mustBeCapableOfViolence: true,
                        fixedBiologicalAge: 25, fixedChronologicalAge: 25, fixedGender: i % 2 == 0 ? Gender.Male : Gender.Female,
                        forcedXenotype: XenotypeDefOf.Baseliner));
                    pawn.Name = new NameSingle("Performance" + i.ToString("D2", CultureInfo.InvariantCulture));
                    pawn.health.RemoveAllHediffs();
                    TransformUtility.AddOrGetShapeshifter(pawn, Kind());
                    if (preset == "abilities") SeedAbilities(pawn);
                    IntVec3 position = map.Center + new IntVec3((i % 10) * 3 - 14, 0, (i / 10) * 3 - 6);
                    GenSpawn.Spawn(pawn, position, map);
                    pawn.drafter.Drafted = true;
                    originals.Add(pawn);
                }
            }
            finally { Rand.PopState(); }
            seeded = true;
            seededPreset = preset;
            Care();
            Require(originals.Count == 50 && map.mapPawns.AllPawnsSpawned.Count == 50, "baseline contains exactly 50 spawned canonical humans");
            GameDataSaveLoader.SaveGame("autostart");
            Require(File.Exists(GenFilePaths.FilePathForSavedGame("autostart")), "native common baseline save exists");
            Boot.Json("seed-result.json", new {
                Passed = true, Phase = "seed", TotalPawns = totalPawns, FeaturePreset = preset,
                Tick = Find.TickManager.TicksGame, OriginalIds = originals.Select(p => p.GetUniqueLoadID()).ToArray(),
                BaselineSave = GenFilePaths.FilePathForSavedGame("autostart"), BaselineSHA256 = Boot.Hash(GenFilePaths.FilePathForSavedGame("autostart")),
                GameVersion = VersionControl.CurrentVersionString, WildShiftSHA256 = Boot.Hash(typeof(TransformUtility).Assembly.Location),
                FixtureSHA256 = Boot.Hash(typeof(Boot).Assembly.Location), Checks = checks.ToArray(), RandomState = RandomState(),
                NoAnimalsDestructionFlag = DebugSettings.noAnimals, NewWildAnimalDensity = map.TileInfo.AnimalDensity,
                NativeErrors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray()
            });
            done = true;
            Application.Quit();
        }

        private static void SeedAbilities(Pawn pawn)
        {
            foreach (string name in new[] { "FireSpew", "AnimalWarcall", "Hemogenic", "HemogenDrain", "MoveSpeed_Quick" }) pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed(name), false);
            var buff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("WorkDrive"), pawn, pawn.health.hediffSet.GetBrain());
            buff.TryGetComp<HediffComp_Disappears>().SetDuration(60000000);
            pawn.health.AddHediff(buff, buff.Part);
        }
        private void Care()
        {
            long startedAt = Stopwatch.GetTimestamp();
            foreach (Pawn original in originals)
            {
                Refill(original);
                Pawn active = SharedFormIdentity.ActiveFormFor(original);
                if (active != null) Refill(active);
                Pawn body = active ?? original;
                if (!body.Drafted || body.drafter == null)
                    throw new InvalidOperationException("Drafted workload changed before native keepalive: " + Describe(body) + "; " + IdentityState(original));
                // Native AutoUndrafter releases a stationary Wait_Combat pawn
                // after 10,000 game ticks. Keep this controlled drafted case
                // active using its ordinary notification; never toggle Drafted,
                // replace its job or suppress the game's tick/undraft method.
                autoUndrafter(body.drafter).Notify_Drafted();
                autoUndraftKeepaliveCalls++;
                if (preset == "abilities")
                {
                    Gene_Hemogen hemogen = SharedFormAbilities.HemogenFor(original);
                    if (hemogen != null) hemogen.Value = .8f;
                }
            }
            interventions++;
            careSeconds += (Stopwatch.GetTimestamp() - startedAt) / (double)Stopwatch.Frequency;
        }
        private static void Refill(Pawn pawn)
        {
            if (pawn.needs == null) return;
            foreach (Need need in pawn.needs.AllNeeds) need.CurLevel = .95f;
        }
        private void ValidatePopulation()
        {
            int active = 0;
            Map map = Find.CurrentMap;
            foreach (Pawn original in originals)
            {
                if (original == null || original.Destroyed || original.Dead || original.Downed || original.InMentalState)
                    throw new InvalidOperationException("Canonical population drifted: " + Describe(original));
                Pawn form = SharedFormIdentity.ActiveFormFor(original);
                Pawn body = form ?? original;
                if (form != null) { active++; if (SharedFormIdentity.HumanFor(form) != original) throw new InvalidOperationException("Active form lost its exact original: " + Describe(form)); }
                if (!body.Spawned || body.Map != map || body.Dead || body.Downed || body.InMentalState || !body.Drafted)
                    throw new InvalidOperationException("Active population/map/drafted workload drifted: " + Describe(body) + "; " + IdentityState(original));
                if (original.health.hediffSet.hediffs.Any(h => h.def.isBad || h is Hediff_Injury)
                    || body.health.hediffSet.hediffs.Any(h => h.def.isBad || h is Hediff_Injury))
                    throw new InvalidOperationException("Controlled healthy population gained harmful health state: original=" + Describe(original) + "; body=" + Describe(body));
            }
            if (active != activeForms || map.mapPawns.AllPawnsSpawned.Count != totalPawns)
                throw new InvalidOperationException("Expected 50 active bodies and exact transformed condition; observed " + active);
        }
        private void Sample(double now)
        {
            ValidatePopulation();
            end = WindowsMetrics.Capture();
            int tick = Find.TickManager.TicksGame;
            double interval = now - lastSampleTime, elapsed = now - startTime;
            peakWorkingSet = Math.Max(peakWorkingSet, end.WorkingSet); peakPrivate = Math.Max(peakPrivate, end.PrivateBytes); peakManaged = Math.Max(peakManaged, end.ManagedHeap);
            minimumAvailable = Math.Min(minimumAvailable, end.AvailablePhysical);
            double systemBusy = end.SystemTotal100ns > last.SystemTotal100ns
                ? 100 * (1 - (end.SystemIdle100ns - last.SystemIdle100ns) / (double)(end.SystemTotal100ns - last.SystemTotal100ns)) : 0;
            string line = string.Join(",", new[] {
                F(elapsed), (tick-startTick).ToString(), F((tick-lastTick)/interval), F((tick-startTick)/elapsed),
                F(TickTiming.Count == 0 ? 0 : TickTiming.SumStopwatchTicks*1000000.0/Stopwatch.Frequency/TickTiming.Count),
                F(TickTiming.Percentile(.5)),F(TickTiming.Percentile(.95)),end.WorkingSet.ToString(),end.PrivateBytes.ToString(),end.ManagedHeap.ToString(),
                (end.AllocatedBytes >= 0 ? end.AllocatedBytes-first.AllocatedBytes : -1).ToString(),
                F((end.ProcessCpu100ns-last.ProcessCpu100ns)/100000.0/interval),F(systemBusy),end.AvailablePhysical.ToString(),unchecked(end.PageFaults-last.PageFaults).ToString(),
                (end.Gen0-first.Gen0).ToString(),(end.Gen1-first.Gen1).ToString(),(end.Gen2-first.Gen2).ToString(),F(Find.TickManager.TickRateMultiplier),
                (UnityEngine.Time.frameCount-startFrame).ToString(),F((UnityEngine.Time.frameCount-startFrame)/elapsed),Application.targetFrameRate.ToString()
            });
            csv.WriteLine(line); csv.Flush();
            last = end; lastTick = tick; lastSampleTime = now;
            WriteProgress("measuring", elapsed);
        }
        private void WriteProgress(string state, double seconds)
        {
            Boot.Json("progress.json", new { State = state, ActiveForms = activeForms, TotalPawns = totalPawns, FeaturePreset = preset,
                MeasuredSeconds = seconds, RequiredSeconds = measureSeconds, CurrentTick = Find.TickManager.TicksGame, TickRateMultiplier = Find.TickManager.TickRateMultiplier,
                WorkingSetBytes = end.WorkingSet, PrivateBytes = end.PrivateBytes, ManagedHeapBytes = end.ManagedHeap,
                GraphicsMode = graphicsMode, TargetFPS = targetFps, RequestedVSyncCount = 0, VSyncCount = QualitySettings.vSyncCount,
                MeasuredFrameCount = measuring ? UnityEngine.Time.frameCount - startFrame : 0,
                ObservedFPS = measuring && seconds > 0 ? (UnityEngine.Time.frameCount - startFrame) / seconds : 0 });
        }
        private void Finish()
        {
            TickTiming.Enabled = false; measuring = false; done = true;
            double effectiveMultiplier = Find.TickManager.TickRateMultiplier;
            endFrame = UnityEngine.Time.frameCount;
            Find.TickManager.Pause(); csv.Dispose(); csv = null;
            double seconds = lastSampleTime - startTime;
            int ticks = lastTick - startTick;
            Require(seconds >= measureSeconds && TickTiming.Count == ticks, "full measured wall-clock duration and native tick-hook count agree");
            Require(ticks > 0, "real native TickManager advances during the measured window");
            double observedFps = (endFrame - startFrame) / seconds;
            Require(endFrame > startFrame, "native engine frame count advances during measurement");
            if (graphicsMode == "Native") Require(observedFps <= targetFps * 1.15,
                "observed native frame rate remains within the configured 60 FPS cap (lower rates remain valid bottleneck evidence)");
            ValidatePopulation();
            long beforeGc = GC.GetTotalMemory(false);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long retained = GC.GetTotalMemory(false);
            Boot.Json("result.json", new {
                Passed = true, Smoke = smoke, Phase = "complete", ActiveForms = activeForms, TotalCanonicalPawns = totalPawns, SpawnedBodies = totalPawns,
                FeaturePreset = preset, WarmupSeconds = warmupSeconds, MeasuredSeconds = seconds, NativeTicks = ticks,
                StartTick = startTick, EndTick = lastTick, VariantStartTick = phaseStartTick,
                Tps = ticks / seconds, TickMeanMicroseconds = TickTiming.SumStopwatchTicks * 1000000.0 / Stopwatch.Frequency / TickTiming.Count,
                TickP50Microseconds = TickTiming.Percentile(.5), TickP95Microseconds = TickTiming.Percentile(.95), TickP99Microseconds = TickTiming.Percentile(.99),
                TickMaxMicroseconds = TickTiming.MaxStopwatchTicks * 1000000.0 / Stopwatch.Frequency, TickHistogramOverflow = TickTiming.Overflow, HistogramResolutionMicroseconds = 1,
                RequestedSpeed = "Ultrafast", NormalNativeMapTargetTps = 900, SchedulerPatched = false, ManualTickLoopUsed = false,
                EffectiveTickRateMultiplierAtEnd = effectiveMultiplier, EffectiveNativeTargetTpsAtEnd = effectiveMultiplier * 60,
                GraphicsMode = graphicsMode, ApplicationIsBatchMode = Application.isBatchMode, GraphicsDeviceType = Convert.ToString(SystemInfo.graphicsDeviceType),
                NoAnimalsDestructionFlag = DebugSettings.noAnimals, NewWildAnimalDensity = Find.CurrentMap.TileInfo.AnimalDensity,
                RequestedTargetFPS = targetFps, NativeTargetFPSAtEnd = Application.targetFrameRate, RequestedVSyncCount = 0, VSyncCountAtEnd = QualitySettings.vSyncCount,
                NativeAutosaveIntervalDays = Prefs.AutosaveIntervalDays, Permadeath = Current.Game.Info.permadeathMode,
                NativeFrames = endFrame - startFrame, FramesPerSecond = observedFps, NativeFrameCapVerified = graphicsMode == "Native" && observedFps <= targetFps * 1.15,
                FirstWorkingSetBytes = first.WorkingSet, LastWorkingSetBytes = end.WorkingSet, PeakWorkingSetBytes = peakWorkingSet,
                FirstPrivateBytes = first.PrivateBytes, LastPrivateBytes = end.PrivateBytes, PeakPrivateBytes = peakPrivate,
                FirstManagedHeapBytes = first.ManagedHeap, LastManagedHeapBytes = end.ManagedHeap, PeakManagedHeapBytes = peakManaged,
                AllocatedBytes = end.AllocatedBytes >= 0 ? end.AllocatedBytes - first.AllocatedBytes : -1, AllocationScope = WindowsMetrics.AllocationScope,
                Gen0Collections = end.Gen0 - first.Gen0, Gen1Collections = end.Gen1 - first.Gen1, Gen2Collections = end.Gen2 - first.Gen2,
                ProcessCpuSeconds = (end.ProcessCpu100ns - first.ProcessCpu100ns) / 10000000.0,
                ProcessCpuPercentOneCore = (end.ProcessCpu100ns - first.ProcessCpu100ns) / 100000.0 / seconds,
                ProcessCpuPercentAllLogicalCores = (end.ProcessCpu100ns - first.ProcessCpu100ns) / 100000.0 / seconds / Environment.ProcessorCount,
                PageFaults = unchecked(end.PageFaults - first.PageFaults), MinimumAvailablePhysicalBytes = minimumAvailable, TotalPhysicalBytes = first.TotalPhysical,
                PostMeasurementGcOnly = new { ManagedBeforeBytes = beforeGc, RetainedManagedBytes = retained },
                MaintenanceCalls = interventions, MaintenanceWallSeconds = careSeconds, AutoUndraftKeepaliveCalls = autoUndraftKeepaliveCalls, SpeedCorrections = speedCorrections,
                OriginalIds = originals.Select(p => p.GetUniqueLoadID()).ToArray(), RandomStateBeforeVariant = rngBeforeVariant, RandomStateAfterVariant = rngAfterVariant,
                GameVersion = VersionControl.CurrentVersionString, GameAssemblyPath = typeof(Pawn).Assembly.Location, GameAssemblySHA256 = Boot.Hash(typeof(Pawn).Assembly.Location),
                WildShiftPath = typeof(TransformUtility).Assembly.Location, WildShiftSHA256 = Boot.Hash(typeof(TransformUtility).Assembly.Location), FixtureSHA256 = Boot.Hash(typeof(Boot).Assembly.Location),
                Mods = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray(), NativeErrors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray(), Checks = checks.ToArray(),
                Scope = "Controlled 50-canonical-pawn stationary drafted native-tick benchmark, minimal all-DLC profile. Storyteller/random disease/mental breaks and new wild animals disabled; needs refilled and native auto-undraft notification refreshed once per wall second. Unexpected undrafting still fails the invariant. No AI hauling/combat, graphical mod-pack or actual-colony performance claim. No forced GC during warmup/measurement; final retained-GC diagnostic is separate."
            });
            Application.Quit();
        }
        private void Fail(Exception error)
        {
            TickTiming.Enabled = false; done = true; Find.TickManager.Pause(); if (csv != null) csv.Dispose();
            string[] identityDiagnostics = originals == null ? null : originals.Select(IdentityState).ToArray();
            string failureSave = null, failureSaveError = null;
            if (Find.CurrentMap != null)
            {
                try { GameDataSaveLoader.SaveGame("failure-snapshot"); failureSave = GenFilePaths.FilePathForSavedGame("failure-snapshot"); }
                catch (Exception saveError) { failureSaveError = saveError.ToString(); }
            }
            Boot.Json(phase == "seed" ? "seed-result.json" : "result.json", new {
                Passed = false, Phase = phase, ActiveForms = activeForms, FeaturePreset = preset, Smoke = smoke,
                Exception = error.ToString(), Checks = checks.ToArray(), Tick = Find.TickManager.TicksGame,
                ExpectedBodyDiagnostics = identityDiagnostics,
                IsolatedFailureSave = failureSave, FailureSaveException = failureSaveError,
                GraphicsMode = graphicsMode, RequestedTargetFPS = targetFps, NativeTargetFPS = Application.targetFrameRate,
                NativeErrors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray()
            });
            Application.Quit();
        }
        private void Require(bool condition, string name) { checks.Add((condition ? "PASS: " : "FAIL: ") + name); if (!condition) throw new InvalidOperationException(name); }
        private static PawnKindDef Kind() { return DefDatabase<PawnKindDef>.GetNamed("Warg"); }
        private static string Text(XDocument input, string key) { return input.Root.Element(key)?.Value ?? throw new InvalidOperationException("Missing input " + key); }
        private static int Integer(XDocument input, string key) { return int.Parse(Text(input, key), CultureInfo.InvariantCulture); }
        private static bool SamePath(string firstPath, string secondPath) { return string.Equals(Path.GetFullPath(firstPath), Path.GetFullPath(secondPath), StringComparison.OrdinalIgnoreCase); }
        private static string F(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Describe(Pawn pawn)
        {
            if (pawn == null) return "null";
            return pawn.GetUniqueLoadID() + " def=" + pawn.def?.defName + " spawned=" + pawn.Spawned
                + " destroyed=" + pawn.Destroyed + " dead=" + pawn.Dead + " downed=" + pawn.Downed
                + " mental=" + (pawn.MentalStateDef?.defName ?? "none") + " drafted=" + pawn.Drafted
                + " map=" + (pawn.Map?.uniqueID.ToString(CultureInfo.InvariantCulture) ?? "none")
                + " job=" + (pawn.CurJobDef?.defName ?? "none")
                + " health=" + string.Join(";", pawn.health.hediffSet.hediffs.Select(h => h.def.defName + ":" + F(h.Severity)));
        }
        private string IdentityState(Pawn original)
        {
            ExpectedBody record;
            if (original == null || !expectedBodies.TryGetValue(original, out record)) return "no recorded expected body for " + Describe(original);
            Pawn expected;
            bool weakTargetAlive = record.Reference.TryGetTarget(out expected);
            Pawn actualMapBody = Find.CurrentMap?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.GetUniqueLoadID() == record.Id);
            Pawn diagnosticBody = expected ?? actualMapBody;
            Pawn cached = SharedFormIdentity.ActiveFormFor(original);
            Pawn direct = SharedFormIdentity.HumanFor(diagnosticBody);
            HediffComp_Transformed comp = TransformUtility.TryGetTransformedComp(diagnosticBody);
            return "original={" + Describe(original) + "}; originalHolder=" + original.ParentHolder?.GetType().FullName
                + "; originalWrapperHash=" + RuntimeHelpers.GetHashCode(original)
                + "; expectedBodyId=" + record.Id + "; initialBodyWrapperHash=" + record.WrapperHash
                + "; expectedWeakTargetAlive=" + weakTargetAlive
                + "; expectedBody={" + Describe(expected) + "}; mapBody={" + Describe(actualMapBody) + "}"
                + "; currentMapWrapperHash=" + (actualMapBody != null ? RuntimeHelpers.GetHashCode(actualMapBody).ToString(CultureInfo.InvariantCulture) : "null")
                + "; cachedActive=" + (cached?.GetUniqueLoadID() ?? "null")
                + "; directHuman=" + (direct?.GetUniqueLoadID() ?? "null")
                + "; directHumanReferenceEqualsOriginal=" + ReferenceEquals(direct, original)
                + "; compStoredHuman=" + (comp?.StoredPawn?.GetUniqueLoadID() ?? "null")
                + "; storedHumanReferenceEqualsOriginal=" + ReferenceEquals(comp?.StoredPawn, original)
                + "; exactHumanHoldingOwner=" + (comp != null && ReferenceEquals(original.holdingOwner, comp.GetDirectlyHeldThings()));
        }
        private static string RandomState()
        {
            var property = AccessTools.Property(typeof(Rand), "StateCompressed");
            return property != null ? Convert.ToString(property.GetValue(null, null), CultureInfo.InvariantCulture) : "unavailable";
        }
    }
}
