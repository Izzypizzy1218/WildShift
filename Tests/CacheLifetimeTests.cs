using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }
}

namespace RimWorld
{
    public sealed class GameEnder { }
    public sealed class Faction
    {
        public static readonly Faction OfPlayer = new Faction();
    }
    public static class PawnsFinder
    {
        public static int Scans;
        public static List<Pawn> AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction
        {
            get { Scans++; return Current.Game == null ? new List<Pawn>() : Current.Game.Pawns; }
        }
    }
}

namespace Verse
{
    public sealed class Game
    {
        public readonly List<Pawn> Pawns = new List<Pawn>();
        public readonly TickManager Ticks = new TickManager();
        public bool ActiveScenario;
        public PawnKindDef StartingKind;
    }
    public static class Current { public static Game Game; }
    public sealed class TickManager { public int TicksGame; }
    public static class Find
    {
        public static TickManager TickManager { get { return Current.Game == null ? null : Current.Game.Ticks; } }
    }
    public sealed class RaceProperties { public bool Animal, Humanlike; }
    public sealed class PawnKindDef { }
    public sealed class Pawn
    {
        public bool Dead, Spawned = true, IsFreeColonist = true, Shapeshifter;
        public Faction Faction = Faction.OfPlayer;
        public RaceProperties RaceProps = new RaceProperties { Humanlike = true };
        public WildShift.HediffComp_Transformed Comp;
    }
    public static class Log { public static void Message(string text) { } }
}

namespace WildShift
{
    public sealed class HediffComp_Transformed { public Pawn StoredPawn; }
    public static class TransformUtility
    {
        public static int Assignments;
        public static HediffComp_Transformed TryGetTransformedComp(Pawn pawn) { return pawn.Comp; }
        public static bool IsShapeshifter(Pawn pawn) { return pawn.Shapeshifter; }
        public static void AddOrGetShapeshifter(Pawn pawn, PawnKindDef kind, bool racial)
        {
            Assignments++;
            pawn.Shapeshifter = true;
        }
    }
    public static class LoneBeastkinUtility
    {
        public static bool IsActiveScenario() { return Current.Game != null && Current.Game.ActiveScenario; }
        public static PawnKindDef GetStartingAnimalKind() { return Current.Game.StartingKind; }
        public static bool HasAnyPlayerShapeshifter()
        {
            foreach (Pawn pawn in Current.Game.Pawns)
                if (pawn.Shapeshifter || pawn.Comp != null) return true;
            return false;
        }
    }
}

namespace WildShift.Tests
{
    public static class CacheLifetimeTests
    {
        private static int passed, failed;

        private static void Check(bool result, string message)
        {
            if (result) passed++;
            else { failed++; Console.WriteLine("FAIL: " + message); }
        }

        private static Pawn Form()
        {
            return new Pawn
            {
                RaceProps = new RaceProperties { Animal = true },
                Comp = new HediffComp_Transformed { StoredPawn = new Pawn() }
            };
        }

        private static bool CheckGameOver()
        {
            bool ending = true;
            int ticks = 500;
            bool runNative = Patch_GameEnder.Prefix(ref ending, ref ticks);
            if (!runNative) Check(!ending && ticks == 0, "living form clears pending game over");
            return runNative;
        }

        // Return through a non-inlined frame so the JIT cannot keep the local
        // game alive while the caller checks its weak reference.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<Game> PrimeGameOverCache()
        {
            Game game = new Game();
            game.Pawns.Add(Form());
            Current.Game = game;
            Check(!CheckGameOver(), "game-over callback sees living preserved human");
            Current.Game = null;
            return new WeakReference<Game>(game);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<Game> PrimeOrdinaryGizmoCache()
        {
            Game game = new Game();
            Current.Game = game;
            Patch_PawnGizmos.Prefix(new Pawn());
            Current.Game = null;
            return new WeakReference<Game>(game);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsAlive(WeakReference<Game> reference)
        {
            Game game;
            return reference.TryGetTarget(out game);
        }

        private static void CheckCollectible(WeakReference<Game> reference, string message)
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(!IsAlive(reference), message);
        }

        private static void TestGameOverCache()
        {
            Patch_GameEnder.InvalidateCache();
            var first = new Game();
            first.Pawns.Add(Form());
            Current.Game = first;
            int scans = PawnsFinder.Scans;
            Check(!CheckGameOver() && PawnsFinder.Scans == scans + 1, "first game is scanned once");
            first.Pawns.Clear();
            first.Ticks.TicksGame = 120;
            Check(!CheckGameOver() && PawnsFinder.Scans == scans + 1, "cache remains valid through 120 ticks");
            first.Ticks.TicksGame = 121;
            Check(CheckGameOver() && PawnsFinder.Scans == scans + 2, "expired cache rescans actual colony");
            first.Pawns.Add(Form());
            Check(CheckGameOver() && PawnsFinder.Scans == scans + 2, "cached negative avoids repeated scans");
            Patch_GameEnder.InvalidateCache();
            Check(!CheckGameOver() && PawnsFinder.Scans == scans + 3, "explicit invalidation refreshes same-tick state");
            var second = new Game();
            second.Ticks.TicksGame = 121;
            Current.Game = second;
            Check(CheckGameOver() && PawnsFinder.Scans == scans + 4, "different game never inherits prior positive cache");
            second.Pawns.Add(Form());
            Patch_GameEnder.InvalidateCache();
            Check(!CheckGameOver(), "second game may prime a positive cache");
            scans = PawnsFinder.Scans;
            Current.Game = null;
            Check(CheckGameOver() && PawnsFinder.Scans == scans, "no current game skips stale cache and pawn scan");
        }

        private static void TestScenarioRecovery()
        {
            int assigned = TransformUtility.Assignments;
            var first = new Game { ActiveScenario = true, StartingKind = new PawnKindDef() };
            Current.Game = first;
            var pawn = new Pawn();
            first.Pawns.Add(pawn);
            Patch_PawnGizmos.Prefix(pawn);
            Check(pawn.Shapeshifter && TransformUtility.Assignments == assigned + 1, "scenario recovery assigns missing starting form");
            pawn.Shapeshifter = false;
            Patch_PawnGizmos.Prefix(pawn);
            Check(TransformUtility.Assignments == assigned + 1, "completed recovery runs only once per game");

            var second = new Game { ActiveScenario = true };
            Current.Game = second;
            var next = new Pawn();
            second.Pawns.Add(next);
            Patch_PawnGizmos.Prefix(next);
            Check(!next.Shapeshifter && TransformUtility.Assignments == assigned + 1, "missing starting kind remains retryable");
            second.StartingKind = new PawnKindDef();
            Patch_PawnGizmos.Prefix(next);
            Check(next.Shapeshifter && TransformUtility.Assignments == assigned + 2, "different game resets recovery completion");

            var third = new Game { ActiveScenario = true, StartingKind = new PawnKindDef() };
            Current.Game = third;
            third.Pawns.Add(new Pawn { Shapeshifter = true });
            var unmarked = new Pawn();
            Patch_PawnGizmos.Prefix(unmarked);
            third.Pawns.Clear();
            Patch_PawnGizmos.Prefix(unmarked);
            Check(TransformUtility.Assignments == assigned + 2, "existing shapeshifter completes recovery without adding a second");

            Current.Game = new Game();
            Patch_PawnGizmos.Prefix(new Pawn());
            Check(TransformUtility.Assignments == assigned + 2, "ordinary scenarios never assign recovery forms");
            Current.Game = null;
            Patch_PawnGizmos.Prefix(null);
            Patch_PawnGizmos.Prefix(new Pawn());
            Check(TransformUtility.Assignments == assigned + 2, "no current game never assigns a recovery form");
        }

        public static void Run()
        {
            CheckCollectible(PrimeGameOverCache(), "retired game is collectible without another game-over callback");
            CheckCollectible(PrimeOrdinaryGizmoCache(), "ordinary retired game is collectible without another pawn selection");
            TestGameOverCache();
            TestScenarioRecovery();
            Current.Game = null;
            Console.WriteLine("Cache lifetime tests: " + passed + " passed, " + failed + " failed.");
            if (failed != 0) throw new Exception("Cache lifetime regression checks failed: " + failed);
        }
    }
}
