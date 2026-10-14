using System;

namespace WildShift
{
    public static class SoldRevengeBalance
    {
        public const int MinDelayDays = 120;
        public const int MaxDelayDays = 150;
        public const int ExpiryGraceDays = 90;
        public const int LegacyDelayIncreaseDays = 105;
        public const float PointsMultiplier = 2f;
        public const float ShapeshifterShare = 0.30f;

        // The leader costs one animal's combat power too. Round the total
        // force to whole animals, retaining at least one companion. There is
        // no fixed pack-size cap that would silently discard the raid budget.
        public static int CompanionCount(float basePoints, float animalCombatPower, float otherShifterPower = 0f)
        {
            if (float.IsNaN(basePoints) || float.IsInfinity(basePoints) || basePoints <= 0f
                || float.IsNaN(animalCombatPower) || float.IsInfinity(animalCombatPower) || animalCombatPower <= 0f
                || float.IsNaN(otherShifterPower) || float.IsInfinity(otherShifterPower) || otherShifterPower < 0f)
                return 0;
            double total = Math.Round(((double)basePoints * PointsMultiplier - otherShifterPower) / animalCombatPower, MidpointRounding.AwayFromZero);
            if (total > int.MaxValue) return 0;
            return Math.Max(2, (int)total) - 1;
        }
    }
}
