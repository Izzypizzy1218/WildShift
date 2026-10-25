using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
}
namespace Verse
{
    public static class Lists { public static bool NullOrEmpty<T>(this List<T> list) { return list == null || list.Count == 0; } }
    public class RaceProperties { public bool Animal = true; public List<TrainableDef> specialTrainables = new List<TrainableDef>(); }
    public class Gizmo { }
    public class Pawn { public bool Destroyed, transformed; public Faction Faction; public RaceProperties RaceProps = new RaceProperties(); public Pawn_TrainingTracker training = new Pawn_TrainingTracker(); }
    public struct AcceptanceReport { public bool Accepted; }
}
namespace RimWorld
{
    public class Faction { public static readonly Faction OfPlayer = new Faction(); }
    public class TrainableDef { public bool assignable = true; public int steps = 2; public List<TrainableDef> prerequisites; }
    public class Pawn_TrainingTracker
    {
        public Pawn pawn;
        public int calls;
        public Dictionary<TrainableDef, int> progress = new Dictionary<TrainableDef, int>();
        public AcceptanceReport CanAssignToTrain(TrainableDef def) { return new AcceptanceReport { Accepted = def.assignable }; }
        public bool HasLearned(TrainableDef def) { int value; return progress.TryGetValue(def, out value) && value >= def.steps; }
        public bool CanBeTrained(TrainableDef def) { return !HasLearned(def); }
        public void Train(TrainableDef def, Pawn trainer, bool complete) { if (!complete || trainer != null) throw new Exception("Expected native complete training without a master"); progress[def] = def.steps; calls++; }
    }
}
namespace WildShift
{
    public static class TransformUtility { public static bool IsTransformedAnimal(Pawn pawn) { return pawn.transformed; } }
}
namespace WildShift.Tests
{
    public static class SpecialTrainingTests
    {
        static int checks;
        static void Check(bool result, string label) { checks++; if (!result) throw new Exception(label); }
        static Pawn Form(params TrainableDef[] trainables)
        {
            var pawn = new Pawn { transformed = true }; pawn.training.pawn = pawn;
            pawn.RaceProps.specialTrainables.AddRange(trainables); return pawn;
        }
        public static void Run()
        {
            var prerequisite = new TrainableDef();
            var first = new TrainableDef { prerequisites = new List<TrainableDef> { prerequisite } };
            var second = new TrainableDef { prerequisites = new List<TrainableDef> { prerequisite } };
            var form = Form(first, second);
            AnimalFormTraining.CompleteSpecialTraining(form);
            Check(form.training.HasLearned(first) && form.training.HasLearned(second), "species skills completed");
            Check(form.training.HasLearned(prerequisite), "prerequisite completed");
            Check(form.training.calls == 3, "shared prerequisite trained once");
            Check(!form.training.HasLearned(new TrainableDef()), "unrelated skill not granted");
            AnimalFormTraining.CompleteSpecialTraining(form);
            Check(form.training.calls == 3, "idempotent initialization");
            var ordinary = Form(first); ordinary.transformed = false;
            AnimalFormTraining.CompleteSpecialTraining(ordinary);
            Check(ordinary.training.calls == 0, "ordinary animal untouched");
            var rejected = Form(new TrainableDef { assignable = false });
            AnimalFormTraining.CompleteSpecialTraining(rejected);
            Check(rejected.training.calls == 0, "unsupported skill not granted");
            var brokenRoot = new TrainableDef { prerequisites = new List<TrainableDef> { new TrainableDef { assignable = false } } };
            var broken = Form(brokenRoot); AnimalFormTraining.CompleteSpecialTraining(broken);
            Check(!broken.training.HasLearned(brokenRoot), "unsupported prerequisite prevents root completion");
            var a = new TrainableDef(); var b = new TrainableDef();
            a.prerequisites = new List<TrainableDef> { b }; b.prerequisites = new List<TrainableDef> { a };
            var cyclic = Form(a); AnimalFormTraining.CompleteSpecialTraining(cyclic);
            Check(cyclic.training.calls == 0, "cyclic requirements terminate without training");
            var nullDef = Form((TrainableDef)null); AnimalFormTraining.CompleteSpecialTraining(nullDef);
            Check(nullDef.training.calls == 0, "null definition ignored");
            AnimalFormTraining.CompleteSpecialTraining(null);
            var noTracker = Form(first); noTracker.training = null; AnimalFormTraining.CompleteSpecialTraining(noTracker);
            var destroyed = Form(first); destroyed.Destroyed = true; AnimalFormTraining.CompleteSpecialTraining(destroyed);
            Check(destroyed.training.calls == 0, "destroyed pawn ignored");
            Check(!Patch_AnimalFormTrainingDecay.Prefix(form.training), "special-capable transformed training retained");
            Check(Patch_AnimalFormTrainingDecay.Prefix(ordinary.training), "ordinary decay remains native");
            var noSkills = Form();
            Check(!Patch_AnimalFormTrainingDecay.Prefix(noSkills.training), "non-special transformed form retains tameness and training");
            noSkills.RaceProps.specialTrainables = null;
            Check(!Patch_AnimalFormTrainingDecay.Prefix(noSkills.training), "missing special-trainable list does not permit transformed decay");
            AnimalFormTraining.CompleteSpecialTraining(noSkills);
            Check(noSkills.training.calls == 0, "decay protection does not grant unrelated training");
            var plainNoSkills = Form(); plainNoSkills.transformed = false;
            Check(Patch_AnimalFormTrainingDecay.Prefix(plainNoSkills.training), "ordinary non-special animal keeps native decay");
            Check(Patch_AnimalFormTrainingDecay.Prefix(new Pawn_TrainingTracker()), "tracker without a pawn keeps native handling");
            var human = Form(first); human.RaceProps.Animal = false;
            AnimalFormTraining.CompleteSpecialTraining(human);
            Check(human.training.calls == 0 && Patch_AnimalFormTrainingDecay.Prefix(human.training), "human untouched");
            IEnumerable<Gizmo> gizmos = null;
            Check(!Patch_EnemyAnimalTrainingGizmos.Prefix(form.training, ref gizmos) && gizmos != null,
                "non-player transformed training commands suppressed");
            form.Faction = Faction.OfPlayer; gizmos = null;
            Check(Patch_EnemyAnimalTrainingGizmos.Prefix(form.training, ref gizmos) && gizmos == null,
                "player form commands remain native");
            Check(Patch_EnemyAnimalTrainingGizmos.Prefix(ordinary.training, ref gizmos), "ordinary animal commands remain native");
            Console.WriteLine("PASS: " + checks + " special-training assertions (production helper with engine stubs).");
        }
    }
}
