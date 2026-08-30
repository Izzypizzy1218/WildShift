using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    public class WildShiftSettings : ModSettings
    {
        public float shiftCooldownDays = 1f;
        public float fieldShapeshifterChance = 0.01f;
        public float joinMtbDaysWithoutShifter = 40f;
        public float deathChance = 0.2f;
        public float spilloverDamageFactor = 0f;
        public bool allowInsectoids;
        public bool allowAdditionalJoiners;

        public int ShiftCooldownTicks
        {
            get
            {
                return Mathf.Max(1, Mathf.RoundToInt(shiftCooldownDays * GenDate.TicksPerDay));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref shiftCooldownDays, "shiftCooldownDays", 1f);
            Scribe_Values.Look(ref fieldShapeshifterChance, "fieldShapeshifterChance", 0.01f);
            Scribe_Values.Look(ref joinMtbDaysWithoutShifter, "joinMtbDaysWithoutShifter", 40f);
            Scribe_Values.Look(ref deathChance, "deathChance", 0.2f);
            Scribe_Values.Look(ref spilloverDamageFactor, "spilloverDamageFactor", 0f);
            Scribe_Values.Look(ref allowInsectoids, "allowInsectoids", false);
            Scribe_Values.Look(ref allowAdditionalJoiners, "allowAdditionalJoiners", false);
            Normalize();
        }

        public void Reset()
        {
            shiftCooldownDays = 1f;
            fieldShapeshifterChance = 0.01f;
            joinMtbDaysWithoutShifter = 40f;
            deathChance = 0.2f;
            spilloverDamageFactor = 0f;
            allowInsectoids = false;
            allowAdditionalJoiners = false;
        }

        public void Normalize()
        {
            shiftCooldownDays = Mathf.Clamp(shiftCooldownDays, 0.1f, 10f);
            fieldShapeshifterChance = Mathf.Clamp01(fieldShapeshifterChance);
            joinMtbDaysWithoutShifter = Mathf.Clamp(joinMtbDaysWithoutShifter, 2f, 200f);
            deathChance = Mathf.Clamp01(deathChance);
            spilloverDamageFactor = Mathf.Clamp01(spilloverDamageFactor);
        }
    }

    public class WildShiftMod : Mod
    {
        public static WildShiftSettings Settings;

        private static Harmony harmony;
        private Vector2 scrollPosition;

        public WildShiftMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WildShiftSettings>();
            Settings.Normalize();
            harmony = new Harmony("wildshift.mod");
            harmony.PatchAll();
        }

        public override string SettingsCategory()
        {
            return "WildShift";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 360f);

            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.Label("WildShift_SettingsShiftCooldown".Translate(Settings.shiftCooldownDays.ToString("0.##")));
            Settings.shiftCooldownDays = Widgets.HorizontalSlider(
                listing.GetRect(24f),
                Settings.shiftCooldownDays,
                0.1f,
                10f,
                false,
                null,
                "0.1",
                "10",
                0.1f);

            listing.Gap();
            listing.Label("WildShift_SettingsFieldChance".Translate((Settings.fieldShapeshifterChance * 100f).ToString("0.##")));
            Settings.fieldShapeshifterChance = Widgets.HorizontalSlider(
                listing.GetRect(24f),
                Settings.fieldShapeshifterChance,
                0f,
                0.2f,
                false,
                null,
                "0",
                "20",
                0.001f);

            listing.Gap();
            listing.Label("WildShift_SettingsJoinMtb".Translate(Settings.joinMtbDaysWithoutShifter.ToString("0")));
            Settings.joinMtbDaysWithoutShifter = Widgets.HorizontalSlider(
                listing.GetRect(24f),
                Settings.joinMtbDaysWithoutShifter,
                2f,
                200f,
                false,
                null,
                "2",
                "200",
                1f);

            listing.Gap();
            listing.Label("WildShift_SettingsDeathChance".Translate((Settings.deathChance * 100f).ToString("0")));
            Settings.deathChance = Widgets.HorizontalSlider(
                listing.GetRect(24f),
                Settings.deathChance,
                0f,
                1f,
                false,
                null,
                "0",
                "100",
                0.01f);

            listing.Gap();
            listing.Label("WildShift_SettingsSpillover".Translate((Settings.spilloverDamageFactor * 100f).ToString("0")));
            Settings.spilloverDamageFactor = Widgets.HorizontalSlider(
                listing.GetRect(24f),
                Settings.spilloverDamageFactor,
                0f,
                1f,
                false,
                null,
                "0",
                "100",
                0.01f);

            listing.GapLine();
            listing.CheckboxLabeled(
                "WildShift_SettingsAllowInsects".Translate(),
                ref Settings.allowInsectoids,
                "WildShift_SettingsAllowInsectsDesc".Translate());
            listing.CheckboxLabeled(
                "WildShift_SettingsAllowExtraJoiners".Translate(),
                ref Settings.allowAdditionalJoiners,
                "WildShift_SettingsAllowExtraJoinersDesc".Translate());

            listing.Gap();
            if (listing.ButtonText("WildShift_SettingsReset".Translate()))
            {
                Settings.Reset();
            }

            listing.End();
            Widgets.EndScrollView();

            Settings.Normalize();
        }
    }
}
