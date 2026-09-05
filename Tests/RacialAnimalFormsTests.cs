using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

// Isolated logic/lifecycle tests. These stubs do not simulate RimWorld rendering,
// HAR transformations, real save loading, or another mod's runtime patches.
namespace Verse
{
    public class Def { public string defName; public string LabelCap { get { return defName; } } }
    public class ThingDef : Def { public RaceProperties race; }
    public class PawnKindDef : Def { public ThingDef race; }
    public class RaceProperties { public bool Animal, Humanlike, IsMechanoid, predator; public float baseBodySize; public object FleshType; }
    public class Pawn
    {
        public ThingDef def; public PawnKindDef kindDef; public Genes genes;
        public RaceProperties RaceProps { get { return def.race; } }
        public Health health = new Health(); public Abilities abilities; public object drafter;
        public object Faction; public bool Dead, Spawned = true; public object Map = new object();
    }
    public class Genes { public Def Xenotype; }
    public class Health
    {
        public HediffSet hediffSet = new HediffSet();
        public void AddHediff(Hediff h) { hediffSet.Value = h; h.Comp.CompPostPostAdd(null); }
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
        public static bool? Force; public static int Calls; private static Random random = new Random(947);
        public static bool Chance(float chance) { Calls++; return Force ?? random.NextDouble() < chance; }
    }
    public static class Extensions
    {
        public static T RandomElement<T>(this List<T> list) { return list[0]; }
        public static T RandomElementWithFallback<T>(this List<T> list) { return list.Count == 0 ? default(T) : list[0]; }
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
            Check(RacialAnimalForms.Choose(ratkin) == normal, "other half normal pool");
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
            Rand.Force = null; int preferred = 0;
            for (int i = 0; i < 10000; i++) if (RacialAnimalForms.Choose(Person("Ratkin")) == rat) preferred++;
            Check(preferred > 4700 && preferred < 5300, "rough fifty percent distribution");
            return "PASS: " + assertions + " assertions; " + preferred + "/10000 racial forms. Engine stubs only; in-game race-mod testing still required.";
        }
    }
}
