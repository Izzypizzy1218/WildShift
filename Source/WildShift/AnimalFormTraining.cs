using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    public static class AnimalFormTraining
    {
        public static void CompleteSpecialTraining(Pawn animal)
        {
            if (animal == null || animal.Destroyed || animal.training == null
                || animal.RaceProps == null || !animal.RaceProps.Animal
                || animal.RaceProps.specialTrainables.NullOrEmpty()
                || !TransformUtility.IsTransformedAnimal(animal)) return;

            var visiting = new HashSet<TrainableDef>();
            var completed = new HashSet<TrainableDef>();
            foreach (TrainableDef trainable in animal.RaceProps.specialTrainables)
                CompleteWithPrerequisites(animal.training, trainable, visiting, completed);
        }

        private static bool CompleteWithPrerequisites(Pawn_TrainingTracker training,
            TrainableDef trainable, HashSet<TrainableDef> visiting, HashSet<TrainableDef> completed)
        {
            if (trainable == null) return false;
            if (completed.Contains(trainable)) return true;
            // Reject cycles and unsupported prerequisites in third-party defs.
            if (!visiting.Add(trainable)) return false;
            try
            {
                if (!training.CanAssignToTrain(trainable).Accepted) return false;
                if (trainable.prerequisites != null)
                    foreach (TrainableDef prerequisite in trainable.prerequisites)
                        if (!CompleteWithPrerequisites(training, prerequisite, visiting, completed)) return false;

                if (!training.HasLearned(trainable) || training.CanBeTrained(trainable))
                    training.Train(trainable, null, true);
                completed.Add(trainable);
                return true;
            }
            finally { visiting.Remove(trainable); }
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), "GetGizmos")]
    public static class Patch_EnemyAnimalTrainingGizmos
    {
        public static bool Prefix(Pawn_TrainingTracker __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn animal = __instance.pawn;
            if (animal == null || animal.Faction == Faction.OfPlayer
                || !TransformUtility.IsTransformedAnimal(animal)) return true;

            // Odyssey assumes a trained attack animal has player settings.
            // Enemy forms can retain special training without a player master.
            __result = Array.Empty<Gizmo>();
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_TrainingTracker), "TrainingTrackerTickRare")]
    public static class Patch_AnimalFormTrainingDecay
    {
        public static bool Prefix(Pawn_TrainingTracker __instance)
        {
            Pawn animal = __instance.pawn;
            // This native method only maintains training decay. Every human
            // mind retains its tameness/training, including ordinary forms and
            // games without Odyssey special trainables. Otherwise native decay
            // can eventually SetFaction(null) and strand the preserved human.
            // No repeated completion, definition scan or allocation per tick.
            return animal == null || animal.RaceProps == null
                || !animal.RaceProps.Animal || !TransformUtility.IsTransformedAnimal(animal);
        }
    }
}
