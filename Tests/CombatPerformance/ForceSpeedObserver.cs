// TEST ONLY. Optional diagnostic observer. Not compiled into WildShift.
// No setter, return value, signal argument, scheduler or game time is changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WildShiftCombatPerformance
{
    public static class ForceSpeedObserver
    {
        public sealed class SignalRecord
        {
            public string Phase { get; set; }
            public string SignalMethod { get; set; }
            public int TickBefore { get; set; }
            public int TickAfter { get; set; }
            public int ForceUntilBefore { get; set; }
            public int ForceUntilAfter { get; set; }
            public bool ForcedBefore { get; set; }
            public bool ForcedAfter { get; set; }
            public bool NeverForceDebugFlag { get; set; }
            public string RequestedTimeSpeed { get; set; }
            public string Stack { get; set; }
        }

        public sealed class InitialRecord
        {
            public string Phase { get; set; }
            public int Tick { get; set; }
            public int ForceUntil { get; set; }
            public bool Forced { get; set; }
            public bool NeverForceDebugFlag { get; set; }
            public string RequestedTimeSpeed { get; set; }
        }

        private static readonly FieldInfo forceUntil = AccessTools.Field(typeof(TimeSlower), "forceNormalSpeedUntil");
        private static readonly List<SignalRecord> records = new List<SignalRecord>();
        public static bool Enabled;
        public static string Phase = "not-armed";
        public static long StandardSignals, ShortSignals;
        public static InitialRecord Initial;

        public static void Install(Harmony harmony)
        {
            if (forceUntil == null) throw new MissingFieldException(typeof(TimeSlower).FullName, "forceNormalSpeedUntil");
            foreach (string name in new[] { "SignalForceNormalSpeed", "SignalForceNormalSpeedShort" })
            {
                MethodInfo method = AccessTools.Method(typeof(TimeSlower), name, Type.EmptyTypes);
                if (method == null) throw new MissingMethodException(typeof(TimeSlower).FullName, name);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(ForceSpeedObserver), "Before"),
                    postfix: new HarmonyMethod(typeof(ForceSpeedObserver), "After"));
            }
        }

        // Arm before actor spawn/drafting/native Wait_Combat initialization,
        // not only after Setup: the first signal can happen in that initAction.
        public static void Arm(string phase)
        {
            records.Clear(); StandardSignals = ShortSignals = 0; Phase = phase;
            TickManager manager = Find.TickManager;
            Initial = new InitialRecord { Phase = phase, Tick = manager.TicksGame,
                ForceUntil = (int)forceUntil.GetValue(manager.slower), Forced = manager.slower.ForcedNormalSpeed,
                NeverForceDebugFlag = DebugViewSettings.neverForceNormalSpeed,
                RequestedTimeSpeed = manager.CurTimeSpeed.ToString() };
            Enabled = true;
        }

        public static void Before(TimeSlower __instance, MethodBase __originalMethod, out SignalRecord __state)
        {
            __state = null;
            if (!Enabled) return;
            if (__originalMethod.Name == "SignalForceNormalSpeedShort") ShortSignals++; else StandardSignals++;
            if (records.Count >= 10) return;
            TickManager manager = Find.TickManager;
            __state = new SignalRecord { Phase = Phase, SignalMethod = __originalMethod.Name,
                TickBefore = manager.TicksGame, ForceUntilBefore = (int)forceUntil.GetValue(__instance),
                ForcedBefore = __instance.ForcedNormalSpeed,
                NeverForceDebugFlag = DebugViewSettings.neverForceNormalSpeed,
                RequestedTimeSpeed = manager.CurTimeSpeed.ToString(), Stack = new StackTrace(1, true).ToString() };
            records.Add(__state);
        }

        public static void After(TimeSlower __instance, SignalRecord __state)
        {
            if (__state == null) return;
            __state.TickAfter = Find.TickManager.TicksGame;
            __state.ForceUntilAfter = (int)forceUntil.GetValue(__instance);
            __state.ForcedAfter = __instance.ForcedNormalSpeed;
        }

        public static object Result()
        {
            return new { Initial, StandardSignals, ShortSignals, FirstTenSignals = records.ToArray(),
                Scope = "Diagnostic observer only; first ten stack captures allocate and must not be used for uninstrumented CPU/TPS comparison." };
        }
    }
}
