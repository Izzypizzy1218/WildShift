using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

// Isolated logic/lifecycle tests. These stubs do not simulate RimWorld rendering,
// HAR transformations, real save loading, or another mod's runtime patches.
namespace Verse
{
    public class Def { public string defName; public string LabelCap { get { return defName; } } }
    public class ThingDef : Def { public RaceProperties race; }
    public class PawnKindDef : Def { public ThingDef race; }
    public class RaceProperties { public bool Animal, Humanlike, IsMechanoid, predator; public bool hasGenders = true; public float baseBodySize; public object FleshType; }
    public enum Gender { None, Male, Female }
    public struct PawnGenerationRequest
    {
        public PawnKindDef KindDef; public object Faction; public bool ForceGenerateNewPawn; public Gender? FixedGender;
        public PawnGenerationRequest(PawnKindDef kind, object faction, bool forceGenerateNewPawn, Gender? fixedGender)
        { KindDef = kind; Faction = faction; ForceGenerateNewPawn = forceGenerateNewPawn; FixedGender = fixedGender; }
    }
    public class Pawn
    {
        public ThingDef def; public PawnKindDef kindDef; public Genes genes; public Gender gender;
        public RaceProperties RaceProps { get { return def.race; } }
        public Health health = new Health(); public Abilities abilities; public object drafter;
        public object Faction; public bool Dead, Spawned = true; public object Map = new object();
    }
    public class Genes { public Def Xenotype; }
    public class Health
    {
        public HediffSet hediffSet = new HediffSet();
        public void AddHediff(Hediff h) { hediffSet.Value = h; h.Comp.CompPostPostAdd(null); }
        public void RemoveHediff(Hediff h) { if (hediffSet.Value == h) hediffSet.Value = null; }
    }
    public class HediffSet { public Hediff Value; public Hediff GetFirstHediffOfDef(object d) { return Value; } }
    public class Hediff
    {
        public Pawn pawn; public WildShift.HediffComp_Shapeshifter Comp;
        public T TryGetComp<T>() where T : class { return Comp as T; }
    }
    public static class HediffMaker
    {
        public static Hediff MakeHediff(object d, Pawn p)
        {
            Hediff h = new Hediff { pawn = p }; h.Comp = new WildShift.HediffComp_Shapeshifter { parent = h }; return h;
        }
    }
    public class HediffCompProperties { public Type compClass; }
    public class HediffComp
    {
        public Hediff parent;
        public virtual string CompLabelInBracketsExtra { get { return null; } }
        public virtual void CompExposeData() { }
        public virtual void CompPostPostAdd(DamageInfo? d) { }
        public virtual IEnumerable<Gizmo> CompGetGizmos() { yield break; }
    }
    public struct DamageInfo { }
    public class Gizmo { }
    public class Command_Action : Gizmo
    {
        public string defaultLabel, defaultDesc; public object icon; public Action action;
        public void Disable(string reason) { }
    }
    public class Abilities { public object GetAbility(object d) { return null; } public void RemoveAbility(object d) { } }
    public static class DefDatabase<T> where T : Def
    {
        public static List<T> AllDefsListForReading = new List<T>();
        public static T GetNamedSilentFail(string name) { return AllDefsListForReading.Find(x => x.defName == name); }
    }
    public static class Rand
    {
        public static bool? Force; public static int? ForceIndex = 0; public static int Calls; private static Random random = new Random(947);
        public static bool Chance(float chance) { Calls++; return Force ?? random.NextDouble() < chance; }
        public static int Range(int min, int max) { return ForceIndex ?? random.Next(min, max); }
    }
    public static class Extensions
    {
        public static int PoolCalls, PoolIndex;
        public static T RandomElement<T>(this List<T> list) { return list[0]; }
        public static T RandomElementWithFallback<T>(this List<T> list) { PoolCalls++; return list.Count == 0 ? default(T) : list[PoolIndex]; }
        public static string Translate(this string s, params object[] args) { return s; }
        public static bool NullOrEmpty(this string s) { return string.IsNullOrEmpty(s); }
    }
    public static class Scribe_Defs
    {
        public static bool Loading; public static string SavedName;
        public static void Look(ref PawnKindDef value, string key)
        {
            if (Loading) value = DefDatabase<PawnKindDef>.GetNamedSilentFail(SavedName);
            else SavedName = value == null ? null : value.defName;
        }
    }
    public static class Scribe_Values { public static void Look<T>(ref T v, string k, T d) { } }
    public static class Log { public static void Warning(string s) { } public static void Error(string s) { throw new Exception(s); } }
    public static class Messages { public static void Message(string s, Pawn p, object t, bool h) { } }
    public static class LongEventHandler { public static void ExecuteWhenFinished(Action a) { a(); } }
    public static class Find { public static TickManager TickManager; }
    public class TickManager { public int TicksGame; }
}
namespace RimWorld
{
    public static class PawnKindDefOf { public static PawnKindDef Colonist = new PawnKindDef { defName = "Colonist" }; }
    public static class FleshTypeDefOf { public static object Insectoid = new object(); }
    public static class Faction { public static object OfPlayer = new object(); }
    public static class MessageTypeDefOf { public static object RejectInput; }
}
namespace WildShift
{
    public static class WildShiftMod { public static Settings Settings = new Settings(); }
    public class Settings { public bool allowInsectoids; public int ShiftCooldownTicks; }
    public static class WildShiftDefOf { public static object WildShift_Shift, WildShift_Shapeshifter; }
    public static class WildShiftTextures { public static object Transform; }
    public static partial class TransformUtility
    {
        public static bool IsTransformedAnimal(Pawn p) { return false; }
        // Exercise the actual production guard without spawning an engine pawn.
        public static bool TransformToAnimal(Pawn p, PawnKindDef k) { string reason; return CanTransformToAnimal(p, k, out reason); }
    }
}
namespace WildShift.Tests
{
    public static class RacialAnimalFormsTests
    {
        private static int assertions;
        private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); assertions++; }
        private static Pawn Person(string race, string xeno = null)
        {
            return new Pawn { def = new ThingDef { defName = race, race = new RaceProperties { Humanlike = true } },
                genes = xeno == null ? null : new Genes { Xenotype = new Def { defName = xeno } } };
        }
        private static PawnKindDef Kind(string name, bool normal)
        {
            PawnKindDef k = new PawnKindDef { defName = name, race = new ThingDef { defName = name,
                race = new RaceProperties { Animal = true, predator = normal } } };
            DefDatabase<PawnKindDef>.AllDefsListForReading.Add(k); return k;
        }
        public static string Run()
        {
            PawnKindDef normal = Kind("Warg", true);
            foreach (string n in new[] { "Rat", "Cat", "Fox_Red", "Fox_Arctic", "Fox_Fennec", "Hare", "Snowhare", "Husky", "LabradorRetriever", "Sheep" }) Kind(n, false);
            Rand.Force = true;
            string[,] races = { { "Ratkin", "Rat" }, { "Kiiro_Race", "Cat" }, { "Alien_Nyaron", "Cat" },
                { "Kurin_Race", "Fox_Red" }, { "ReviaRaceAlien", "Fox_Red" }, { "Alien_Miho", "Fox_Red" },
                { "Rabbie", "Hare" }, { "Yuran_Race", "Hare" }, { "Alien_Bori", "Husky" }, { "Alien_SP", "Sheep" } };
            for (int i = 0; i < races.GetLength(0); i++)
            {
                Pawn p = Person(races[i, 0]); PawnKindDef k = RacialAnimalForms.Choose(p);
                Check(k.defName == races[i, 1], "mapping " + races[i, 0]);
                Check(RacialAnimalForms.IsAllowed(p, k), "exception allowed");
                Check(!AnimalPool.IsEligible(k), "normal pool unchanged");
                Check(!RacialAnimalForms.IsAllowed(Person("Human"), k), "exception scoped to race");
            }
            string[,] xenos = { { "RK_XenoType_Ratkin", "Rat" }, { "YuranXenotype", "Hare" },
                { "Xeno_CelestialMiho", "Fox_Red" }, { "Xeno_CelestialMiho_Arctic", "Fox_Arctic" },
                { "Xeno_CelestialMiho_Desert", "Fox_Fennec" }, { "Xeno_CelestialMiho_Highland", "Fox_Red" },
                { "Xeno_CelestialMiho_Highmate", "Fox_Red" }, { "Xeno_CelestialMiho_Voidborn", "Fox_Red" } };
            for (int i = 0; i < xenos.GetLength(0); i++)
                Check(RacialAnimalForms.Choose(Person("Human", xenos[i, 0])).defName == xenos[i, 1], "xenotype " + xenos[i, 0]);
            Check(RacialAnimalForms.Choose(Person("Alien_Miho", "Xeno_CelestialMiho_Arctic")).defName == "Fox_Arctic", "variant precedence");
            Check(RacialAnimalForms.Choose(Person("NotReallyRatkin")) == normal, "no substring detection");
            Check(RacialAnimalForms.Choose(null) == normal, "null pawn fallback");
            Pawn ratkin = Person("Ratkin"); PawnKindDef rat = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rat");
            DefDatabase<PawnKindDef>.AllDefsListForReading.Remove(rat);
            Check(RacialAnimalForms.Choose(ratkin) == normal, "missing animal silent fallback");
            DefDatabase<PawnKindDef>.AllDefsListForReading.Add(rat);
            rat.race.race.IsMechanoid = true;
            Check(RacialAnimalForms.Choose(ratkin) == normal && !RacialAnimalForms.IsAllowed(ratkin, rat), "reject mechanoid replacement");
            rat.race.race.IsMechanoid = false;
            Rand.Force = false;
            Check(RacialAnimalForms.Choose(ratkin) == normal, "other eighty percent normal pool");
            Rand.Force = true; Rand.Calls = 0;
            HediffComp_Shapeshifter comp = TransformUtility.AddOrGetShapeshifter(ratkin, normal, true);
            Check(comp.assignedKind == rat && Rand.Calls == 1, "single initial roll including AddHediff callbacks");
            for (int i = 0; i < 100; i++) { comp.EnsureAssignedKind(); TransformUtility.AddOrGetShapeshifter(ratkin, normal, true); }
            Check(comp.assignedKind == rat && Rand.Calls == 1, "redraw and reapplication never reroll");
            Check(TransformUtility.TransformToAnimal(ratkin, rat), "production transformation guard accepts racial form");
            Check(!TransformUtility.TransformToAnimal(Person("Human"), rat), "production guard rejects unrelated racial exception");
            Check(TransformUtility.TransformToAnimal(Person("Human"), normal), "normal transformation guard unchanged");
            comp.CompExposeData();
            HediffComp_Shapeshifter restored = new HediffComp_Shapeshifter { parent = new Hediff { pawn = ratkin } };
            Scribe_Defs.Loading = true; restored.CompExposeData(); restored.EnsureAssignedKind(); Scribe_Defs.Loading = false;
            Check(restored.assignedKind == rat && Rand.Calls == 1, "saved Def reference survives validation (stub Scribe)");
            Pawn legacy = Person("Ratkin");
            TransformUtility.AddOrGetShapeshifter(legacy, normal);
            TransformUtility.AddOrGetShapeshifter(legacy, null, true);
            Check(legacy.health.hediffSet.Value.Comp.assignedKind == normal, "existing normal form preserved");
            Pawn tamed = Person("Human");
            Check(TransformUtility.AddOrGetShapeshifter(tamed, normal).assignedKind == normal && Rand.Calls == 1, "explicit taming form preserved without affinity roll");
            Rand.ForceIndex = 1;
            Check(RacialAnimalForms.Choose(ratkin) == normal, "missing hamster slot falls back instead of becoming rat");
            PawnKindDef hamster = Kind("Ratkin_KingHamster", false);
            Check(RacialAnimalForms.Choose(ratkin) == hamster, "hamster mapping");
            Check(RacialAnimalForms.Choose(Person("Human", "RK_XenoType_Ratkin")) == hamster, "hamster xenotype mapping");
            Check(RacialAnimalForms.IsAllowed(ratkin, hamster), "hamster racial exception allowed");
            Check(!RacialAnimalForms.IsAllowed(Person("Human"), hamster), "hamster exception scoped");
            comp.assignedKind = hamster; comp.EnsureAssignedKind();
            Check(comp.assignedKind == hamster, "assigned hamster survives validation");
            foreach (Gender gender in new[] { Gender.Male, Gender.Female, Gender.None })
            {
                ratkin.gender = gender; ratkin.Faction = Faction.OfPlayer;
                PawnGenerationRequest request = AnimalFormGender.CreateRequest(ratkin, rat);
                Check(request.FixedGender == gender, "gender passed at generation: " + gender);
                Check(request.KindDef == rat && request.Faction == ratkin.Faction && request.ForceGenerateNewPawn, "fresh form with correct kind and faction");
            }
            ratkin.gender = Gender.Female; rat.race.race.hasGenders = false;
            Check(AnimalFormGender.CreateRequest(ratkin, rat).FixedGender == Gender.None, "genderless species remain genderless");
            rat.race.race.hasGenders = true;
            foreach (Gender gender in new[] { Gender.Male, Gender.Female })
            {
                ratkin.gender = gender;
                Check(AnimalFormGender.CreateHumanRequest(ratkin).FixedGender == gender, "taming reveal preserves sex: " + gender);
            }
            ratkin.gender = Gender.None;
            PawnGenerationRequest humanRequest = AnimalFormGender.CreateHumanRequest(ratkin);
            Check(humanRequest.FixedGender == null, "genderless taming origin allows ordinary human gender generation");
            Check(humanRequest.KindDef == PawnKindDefOf.Colonist && humanRequest.Faction == Faction.OfPlayer && humanRequest.ForceGenerateNewPawn, "fresh tamed human generation");
            Rand.Force = true; Rand.ForceIndex = 0;
            StartingFormPreviewCache cache = new StartingFormPreviewCache();
            Pawn candidate = Person("Ratkin");
            cache.Activate(candidate);
            Hediff preview = candidate.health.hediffSet.Value;
            int callsAfterPreview = Rand.Calls;
            cache.Deactivate(candidate);
            Check(candidate.health.hediffSet.Value == null, "scenario marker removed from inactive candidate");
            Rand.ForceIndex = 1;
            cache.Activate(candidate);
            Check(candidate.health.hediffSet.Value != preview && candidate.health.hediffSet.Value.Comp.assignedKind == hamster, "same candidate rerolls racial form after reordering");
            Check(Rand.Calls == callsAfterPreview + 1, "reactivation performs one preference roll");
            int callsAfterReactivation = Rand.Calls;
            for (int i = 0; i < 100; i++) cache.Activate(candidate);
            Check(Rand.Calls == callsAfterReactivation, "redraw never rerolls");
            cache.Deactivate(legacy);
            Check(legacy.health.hediffSet.Value != null, "unrelated preexisting shapeshifter is not stripped");
            Pawn ordinaryCandidate = Person("Human");
            Kind("Cougar", true);
            Extensions.PoolIndex = 0;
            cache.Activate(ordinaryCandidate);
            PawnKindDef oldForm = ordinaryCandidate.health.hediffSet.Value.Comp.assignedKind;
            int oldPoolCalls = Extensions.PoolCalls;
            cache.Deactivate(ordinaryCandidate);
            Extensions.PoolIndex = 1;
            cache.Activate(ordinaryCandidate);
            Check(Extensions.PoolCalls == oldPoolCalls + 1 && ordinaryCandidate.health.hediffSet.Value.Comp.assignedKind != oldForm, "ordinary human rerolls pool rather than fixed scenario fallback");
            oldPoolCalls = Extensions.PoolCalls;
            for (int i = 0; i < 100; i++) cache.Activate(ordinaryCandidate);
            Check(Extensions.PoolCalls == oldPoolCalls, "ordinary preview redraw does not access random pool");
            cache.Deactivate(ordinaryCandidate);
            cache.Activate(ordinaryCandidate);
            Check(Extensions.PoolCalls == oldPoolCalls + 1, "repeated slot changes continue rerolling without stale cache entries");
            Extensions.PoolIndex = 0;
            Rand.Force = null; Rand.ForceIndex = null; int rats = 0, hamsters = 0, cats = 0;
            for (int i = 0; i < 10000; i++)
            {
                PawnKindDef chosen = RacialAnimalForms.Choose(ratkin);
                if (chosen == rat) rats++; else if (chosen == hamster) hamsters++;
                if (RacialAnimalForms.Choose(Person("Kiiro_Race")).defName == "Cat") cats++;
            }
            Check(rats > 850 && rats < 1150 && hamsters > 850 && hamsters < 1150, "rough ten percent each Ratkin branch");
            Check(cats > 1800 && cats < 2200, "rough twenty percent other races");
            return "PASS: " + assertions + " assertions; per 10000: rat=" + rats + ", hamster=" + hamsters + ", Kiiro cat=" + cats + ". Engine stubs only; in-game testing still required.";
        }
    }
}
