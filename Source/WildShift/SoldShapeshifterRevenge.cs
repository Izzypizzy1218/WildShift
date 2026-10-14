using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace WildShift
{
    public sealed class SoldShapeshifterRecord : IExposable
    {
        public Pawn animal, human;
        public int dueTick, expiresTick, homeMapId = -1;
        public int scheduleVersion = 1;
        public void ExposeData()
        {
            Scribe_References.Look(ref animal, "animal");
            Scribe_References.Look(ref human, "human");
            Scribe_Values.Look(ref dueTick, "dueTick", 0);
            Scribe_Values.Look(ref expiresTick, "expiresTick", 0);
            Scribe_Values.Look(ref homeMapId, "homeMapId", -1);
            Scribe_Values.Look(ref scheduleVersion, "scheduleVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) UpgradeSchedule();
        }

        public void UpgradeSchedule()
        {
            if (scheduleVersion >= 1) return;
            // v1.2.19 saved expiry relative to the ORIGINAL due date, while
            // failed attempts could change dueTick. Recover the original roll
            // from expiry so retries do not extend the 120-150 day window.
            int originalDue = expiresTick - SoldRevengeBalance.ExpiryGraceDays * GenDate.TicksPerDay;
            dueTick = originalDue + SoldRevengeBalance.LegacyDelayIncreaseDays * GenDate.TicksPerDay;
            expiresTick = dueTick + SoldRevengeBalance.ExpiryGraceDays * GenDate.TicksPerDay;
            scheduleVersion = 1;
        }
    }

    public sealed class GameComponent_SoldShapeshifterRevenge : GameComponent
    {
        private List<SoldShapeshifterRecord> pending = new List<SoldShapeshifterRecord>();
        private readonly HashSet<Pawn> retained = new HashSet<Pawn>();
        private int nextCheckTick;
        private int nextArrivalTick;
        public int PendingCount => pending.Count;
        public static GameComponent_SoldShapeshifterRevenge Current => Verse.Current.Game?.GetComponent<GameComponent_SoldShapeshifterRevenge>();
        public GameComponent_SoldShapeshifterRevenge(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending, "soldShapeshifterRevenge", LookMode.Deep);
            Scribe_Values.Look(ref nextCheckTick, "nextRevengeCheck", 0);
            Scribe_Values.Look(ref nextArrivalTick, "nextRevengeArrival", 0);
            if (pending == null) pending = new List<SoldShapeshifterRecord>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Reindex();
        }

        public void RecordSale(Pawn animal, Pawn human, Map saleMap)
        {
            var comp = TransformUtility.TryGetShapeshifterComp(human);
            if (comp == null || comp.saleRevengeRolled || human.Dead || human.Destroyed) return;
            float chance = WildShiftMod.Settings?.soldRevengeChance ?? 0.5f;
            if (chance <= 0f) return;
            // One roll per original pawn, including failed rolls and buybacks.
            // Selling the same person repeatedly cannot farm revenge events.
            comp.saleRevengeRolled = true;
            if (!Rand.Chance(chance)) return;
            int due = Find.TickManager.TicksGame + Rand.RangeInclusive(SoldRevengeBalance.MinDelayDays, SoldRevengeBalance.MaxDelayDays) * GenDate.TicksPerDay;
            pending.Add(new SoldShapeshifterRecord { animal = animal, human = human, dueTick = due,
                expiresTick = due + SoldRevengeBalance.ExpiryGraceDays * GenDate.TicksPerDay, homeMapId = saleMap?.uniqueID ?? -1 });
            Reindex();
        }

        public void CancelFor(Pawn pawn)
        {
            if (pawn != null && pending.RemoveAll(r => r.animal == pawn || r.human == pawn) > 0) Reindex();
        }

        public bool Retains(Pawn pawn) => pawn != null && retained.Contains(pawn) && !pawn.Dead && !pawn.Destroyed;

        private void Reindex()
        {
            retained.Clear();
            foreach (var record in pending)
            {
                if (record?.animal != null) retained.Add(record.animal);
                if (record?.human != null) retained.Add(record.human);
            }
        }

        public override void GameComponentTick()
        {
            if (pending.Count == 0 || Find.TickManager.TicksGame < nextCheckTick) return;
            nextCheckTick = Find.TickManager.TicksGame + 2500;
            ProcessPending();
        }

        public void ProcessPending()
        {
            int now = Find.TickManager.TicksGame;
            bool disabled = (WildShiftMod.Settings?.soldRevengeChance ?? 0.5f) <= 0f;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var record = pending[i];
                Pawn human = record?.human;
                var holder = TransformUtility.TryGetTransformedComp(record?.animal);
                bool inAnimal = human != null && holder?.StoredPawn == human;
                Pawn active = inAnimal ? record.animal : human;
                if (disabled || human == null || human.Dead || human.Destroyed || active == null || active.Dead || active.Destroyed
                    || active.Faction == Faction.OfPlayer || active.IsPrisonerOfColony || active.IsSlaveOfColony || now > record.expiresTick)
                { pending.RemoveAt(i); continue; }
                if (now < record.dueTick || now < nextArrivalTick || !Find.Storyteller.difficulty.allowBigThreats) continue;
                // The trader must actually have left with the pawn. Never take
                // a pawn from a live map, a caravan or a trader's stock container.
                if (active.SpawnedOrAnyParentSpawned || active.holdingOwner != null || !active.IsWorldPawn()
                    || human.Downed || active.Downed || human.InMentalState || active.InMentalState) continue;
                Map map = Find.Maps.Find(m => m.IsPlayerHome && m.uniqueID == record.homeMapId)
                    ?? Find.Maps.Find(m => m.IsPlayerHome);
                if (map == null || map.dangerWatcher.DangerRating == StoryDanger.High) continue;
                bool handled = false;
                try { handled = SoldShapeshifterRevenge.TryArrive(record, map); }
                catch (Exception ex)
                {
                    Log.Error("[WildShift] Sold shapeshifter revenge arrival failed: " + ex);
                    // Once the real pawn is on-map, never duplicate an arrival.
                    var activeForm = human.ParentHolder as HediffComp_Transformed;
                    handled = human.Spawned || activeForm?.parent?.pawn?.Spawned == true;
                }
                if (handled)
                {
                    pending.RemoveAt(i);
                    nextArrivalTick = now + 3 * GenDate.TicksPerDay;
                }
                else record.dueTick = now + GenDate.TicksPerDay;
            }
            Reindex();
        }
    }

    public static class SoldShapeshifterRevenge
    {
        public static bool IsRevengePawn(Pawn pawn) => pawn?.Faction?.def == WildShiftDefOf.WildShift_RevengeFaction;

        public static bool IsPackAnimal(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.Spawned && pawn.lord != null && pawn.RaceProps.Animal
                && IsRevengePawn(pawn);
        }

        public static bool TryArrive(SoldShapeshifterRecord record, Map map)
        {
            Pawn human = record.human;
            var shifter = TransformUtility.TryGetShapeshifterComp(human);
            if (shifter == null) return true;
            shifter.EnsureAssignedKind();
            PawnKindDef kind = shifter.assignedKind;
            if (kind == null) return false;
            IntVec3 entry;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out entry, map, 0f, true)) return false;
            Faction faction = Find.FactionManager.FirstFactionOfDef(WildShiftDefOf.WildShift_RevengeFaction);
            if (faction == null)
            {
                faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(WildShiftDefOf.WildShift_RevengeFaction));
                Find.FactionManager.Add(faction);
            }

            // Compute at arrival, after all delays. Native home-map baseline
            // includes current wealth, colonists, adaptation and difficulty;
            // no extra random raid multiplier or historical raid average.
            float baseline = StorytellerUtility.DefaultThreatPointsNow(map);
            if (SoldRevengeBalance.CompanionCount(baseline, kind.combatPower) == 0) return false;
            var companions = new List<Pawn>();
            var arrivals = new List<Pawn>();
            bool committed = false;
            try
            {
                // Prepare all generated companions before extracting the
                // irreplaceable original. New allies are separate people.
                float otherShifterPower = RevengeCompanions.Prepare(baseline, kind, faction, companions);
                int packCount = SoldRevengeBalance.CompanionCount(baseline, kind.combatPower, otherShifterPower);
                for (int i = 0; i < packCount; i++)
                {
                    Pawn companion = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, forceGenerateNewPawn: true));
                    if (companion == null || companion.Dead || companion.Destroyed) continue;
                    companions.Add(companion);
                    var latent = companion.health.hediffSet.GetFirstHediffOfDef(WildShiftDefOf.WildShift_FieldShapeshifterMarker);
                    if (latent != null) companion.health.RemoveHediff(latent);
                }
                if (companions.Count == 0) return false;
                if (TransformUtility.TryGetTransformedComp(record.animal)?.StoredPawn == human)
                {
                    if (TransformUtility.RevertToHuman(record.animal, false) != human) return false;
                }
                if (human.Spawned || human.holdingOwner != null || human.Dead || human.Destroyed) return false;
                Faction previousFaction = human.Faction;
                human.SetFaction(faction);
                if (!FormTransferUtility.TrySpawn(human, entry, map))
                { human.SetFaction(previousFaction); return false; }
                committed = true;
                arrivals.Add(human);
                human.guest?.SetGuestStatus(null);
                shifter.enemyRaidParticipant = true;
                shifter.enemyShiftUsed = false;

                foreach (Pawn companion in companions)
                {
                    IntVec3 cell;
                    if (!CellFinder.TryFindRandomCellNear(entry, map, 5, c => c.Standable(map), out cell)) cell = entry;
                    if (FormTransferUtility.TrySpawn(companion, cell, map)) arrivals.Add(companion);
                }
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canSteal: false), map, arrivals);
                // The existing enemy safe-point queue changes the actual leader
                // into its assigned form, then maintains raid duties and retreat.
                MapComponent_EnemyShapeshifters.For(map)?.QueueShift(human);
                int allies = 0, animals = 0;
                foreach (Pawn companion in arrivals)
                {
                    if (companion == human) continue;
                    if (TransformUtility.TryGetShapeshifterComp(companion) != null)
                    {
                        allies++;
                        MapComponent_EnemyShapeshifters.For(map)?.QueueShift(companion);
                    }
                    else animals++;
                }
                Find.LetterStack.ReceiveLetter("WildShift_RevengeLetterLabel".Translate(),
                    "WildShift_RevengeLetterText".Translate(human.LabelShortCap, kind.LabelCap, animals, allies),
                    LetterDefOf.ThreatBig, human);
                Find.TickManager.slower.SignalForceNormalSpeedShort();
                Patch_GameEnder.InvalidateCache();
                return true;
            }
            finally
            {
                foreach (Pawn pawn in companions)
                    if (!pawn.SpawnedOrAnyParentSpawned && pawn.holdingOwner == null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                // No generated clone of the sold human ever exists. If an
                // external spawn/lord callback fails after commitment, retain
                // the actual on-map pawn and prevent a second arrival.
                if (committed) shifter.saleRevengeRolled = true;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "PreTraded")]
    public static class Patch_SoldShapeshifterRevenge
    {
        public static void Prefix(Pawn __instance, TradeAction action, out Pawn __state)
        {
            __state = action == TradeAction.PlayerSells && __instance.Faction == Faction.OfPlayer
                ? TransformUtility.TryGetTransformedComp(__instance)?.StoredPawn : null;
        }
        public static void Postfix(Pawn __instance, TradeAction action, Pawn playerNegotiator, Pawn __state)
        {
            var manager = GameComponent_SoldShapeshifterRevenge.Current;
            if (action == TradeAction.PlayerBuys) manager?.CancelFor(__instance);
            else if (__state != null && action == TradeAction.PlayerSells)
                manager?.RecordSale(__instance, __state, playerNegotiator?.MapHeld);
            Patch_GameEnder.InvalidateCache();
        }
    }

    [HarmonyPatch(typeof(WorldPawnGC), "GetCriticalPawnReason")]
    public static class Patch_RetainSoldShapeshifter
    {
        public static void Postfix(Pawn pawn, ref string __result)
        {
            if (__result == null && (GameComponent_SoldShapeshifterRevenge.Current?.Retains(pawn) ?? false))
                __result = "WildShiftPendingRevenge";
        }
    }
}
