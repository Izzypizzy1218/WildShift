using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    public class WildShiftSettings : ModSettings
    {
        public float shiftCooldownDays = 1f;
        public float fieldShapeshifterChance = 0.03f;
        public bool forceTraderShapeshifters;
        public float enemyShapeshifterChance = 0.05f;
        public float soldRevengeChance = 0.5f;
        public float deathChance = 0.2f;
        public float spilloverDamageFactor = 0f;
        public bool allowInsectoids;
        public bool customRacialFormsEnabled;
        public List<CustomRacialFormRule> customRacialForms = new List<CustomRacialFormRule>();

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
            Scribe_Values.Look(ref fieldShapeshifterChance, "fieldShapeshifterChance", 0.03f);
            Scribe_Values.Look(ref forceTraderShapeshifters, "forceTraderShapeshifters", false);
            Scribe_Values.Look(ref enemyShapeshifterChance, "enemyShapeshifterChance", 0.05f);
            Scribe_Values.Look(ref soldRevengeChance, "soldRevengeChance", 0.5f);
            Scribe_Values.Look(ref deathChance, "deathChance", 0.2f);
            Scribe_Values.Look(ref spilloverDamageFactor, "spilloverDamageFactor", 0f);
            Scribe_Values.Look(ref allowInsectoids, "allowInsectoids", false);
            Scribe_Values.Look(ref customRacialFormsEnabled, "customRacialFormsEnabled", false);
            Scribe_Collections.Look(ref customRacialForms, "customRacialForms", LookMode.Deep);
            Normalize();
        }

        public void Reset()
        {
            shiftCooldownDays = 1f;
            fieldShapeshifterChance = 0.03f;
            forceTraderShapeshifters = false;
            enemyShapeshifterChance = 0.05f;
            soldRevengeChance = 0.5f;
            deathChance = 0.2f;
            spilloverDamageFactor = 0f;
            allowInsectoids = false;
            customRacialFormsEnabled = false;
            customRacialForms = new List<CustomRacialFormRule>();
        }

        public void Normalize()
        {
            shiftCooldownDays = Mathf.Clamp(shiftCooldownDays, 0.1f, 10f);
            fieldShapeshifterChance = Mathf.Clamp01(fieldShapeshifterChance);
            enemyShapeshifterChance = float.IsNaN(enemyShapeshifterChance) ? 0.05f : Mathf.Clamp01(enemyShapeshifterChance);
            soldRevengeChance = float.IsNaN(soldRevengeChance) ? 0.5f : Mathf.Clamp01(soldRevengeChance);
            deathChance = Mathf.Clamp01(deathChance);
            spilloverDamageFactor = Mathf.Clamp01(spilloverDamageFactor);
            if (customRacialForms == null) customRacialForms = new List<CustomRacialFormRule>();
            CustomRacialForms.NormalizeRules(customRacialForms);
        }
    }

    public class WildShiftMod : Mod
    {
        public static WildShiftSettings Settings;

        private static Harmony harmony;
        private Vector2 scrollPosition;
        private float settingsViewHeight = 700f;

        public WildShiftMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<WildShiftSettings>();
            Settings.Normalize();
            harmony = new Harmony("wildshift.mod");
            harmony.PatchAll();
            AnimalModCompatibility.Install(harmony);
            SapientAnimalsCompatibility.Install(harmony);
        }

        public override string SettingsCategory()
        {
            return "WildShift";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, Mathf.Max(inRect.height - 16f, settingsViewHeight));

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
            listing.Label("WildShift_SettingsEnemyChance".Translate((Settings.enemyShapeshifterChance * 100f).ToString("0")));
            Settings.enemyShapeshifterChance = Widgets.HorizontalSlider(
                listing.GetRect(24f), Settings.enemyShapeshifterChance,
                0f, 1f, false, null, "0", "100", 0.01f);

            listing.Gap();
            listing.Label("WildShift_SettingsSoldRevenge".Translate((Settings.soldRevengeChance * 100f).ToString("0")));
            Settings.soldRevengeChance = Widgets.HorizontalSlider(
                listing.GetRect(24f), Settings.soldRevengeChance, 0f, 1f, false, null, "0", "100", 0.01f);

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
                "WildShift_SettingsForceTrader".Translate(),
                ref Settings.forceTraderShapeshifters,
                "WildShift_SettingsForceTraderDesc".Translate());
            listing.Gap();
            listing.CheckboxLabeled(
                "WildShift_SettingsAllowInsects".Translate(),
                ref Settings.allowInsectoids,
                "WildShift_SettingsAllowInsectsDesc".Translate());

            listing.GapLine();
            CustomRacialFormsSettingsUI.Draw(listing, Settings);

            listing.Gap();
            if (listing.ButtonText("WildShift_SettingsReset".Translate()))
            {
                Settings.Reset();
            }

            settingsViewHeight = listing.CurHeight + 12f;
            listing.End();
            Widgets.EndScrollView();

            Settings.Normalize();
        }
    }
}
