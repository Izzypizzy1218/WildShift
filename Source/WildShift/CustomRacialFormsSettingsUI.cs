using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShift
{
    public static class CustomRacialFormsSettingsUI
    {
        // The editor is a separate window so long mod lists do not enlarge the main settings page.
        public static float Height { get { return 144f; } }

        public static void Draw(Listing_Standard listing, WildShiftSettings settings)
        {
            listing.GapLine();
            listing.CheckboxLabeled("WildShift_CustomFormsEnabled".Translate(),
                ref settings.customRacialFormsEnabled, "WildShift_CustomFormsEnabledDesc".Translate());
            listing.Label("WildShift_CustomFormsNewOnly".Translate());
            if (settings.customRacialFormsEnabled
                && listing.ButtonText("WildShift_CustomFormsOpen".Translate(settings.customRacialForms.Count)))
            {
                Find.WindowStack.Add(new CustomRacialFormsEditor(settings));
            }
        }

        internal static string TargetLabel(CustomRacialFormRule rule)
        {
            Def target = rule.xenotype
                ? (Def)DefDatabase<XenotypeDef>.GetNamedSilentFail(rule.targetDefName)
                : DefDatabase<ThingDef>.GetNamedSilentFail(rule.targetDefName);
            return target != null ? target.LabelCap.ToString()
                : "WildShift_CustomFormsMissing".Translate(rule.targetDefName).ToString();
        }

        internal static string AnimalLabel(string defName)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
            return kind != null ? kind.LabelCap.ToString()
                : "WildShift_CustomFormsMissing".Translate(defName).ToString();
        }
    }

    internal sealed class CustomFormOption
    {
        public string defName;
        public string label;
        public string source;

        public CustomFormOption(Def def)
        {
            defName = def.defName;
            label = def.LabelCap.ToString();
            source = def.modContentPack != null ? def.modContentPack.Name : string.Empty;
        }

        public bool Matches(string search)
        {
            return string.IsNullOrEmpty(search)
                || label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || source.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || defName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Sort(List<CustomFormOption> options)
        {
            options.Sort(delegate(CustomFormOption a, CustomFormOption b)
            {
                int labelOrder = StringComparer.CurrentCultureIgnoreCase.Compare(a.label, b.label);
                return labelOrder != 0 ? labelOrder : StringComparer.Ordinal.Compare(a.defName, b.defName);
            });
        }
    }

    internal sealed class CustomRacialFormsEditor : Window
    {
        private readonly WildShiftSettings settings;
        private readonly List<CustomFormOption> raceOptions = new List<CustomFormOption>();
        private readonly List<CustomFormOption> xenotypeOptions = new List<CustomFormOption>();
        private readonly List<CustomFormOption> animalOptions = new List<CustomFormOption>();
        private Vector2 scroll;
        private const float RuleHeight = 124f;

        public override Vector2 InitialSize
        {
            get { return new Vector2(Mathf.Min(860f, UI.screenWidth - 40f), Mathf.Min(720f, UI.screenHeight - 40f)); }
        }

        public CustomRacialFormsEditor(WildShiftSettings settings)
        {
            this.settings = settings;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;

            // Definitions are collected and sorted once per editor opening, never on a game tick.
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                if (def.race != null && def.race.Humanlike) raceOptions.Add(new CustomFormOption(def));
            if (ModsConfig.BiotechActive)
                foreach (XenotypeDef def in DefDatabase<XenotypeDef>.AllDefsListForReading)
                    xenotypeOptions.Add(new CustomFormOption(def));
            foreach (PawnKindDef def in DefDatabase<PawnKindDef>.AllDefsListForReading)
                if (CustomRacialForms.IsSelectable(def)) animalOptions.Add(new CustomFormOption(def));
            CustomFormOption.Sort(raceOptions);
            CustomFormOption.Sort(xenotypeOptions);
            CustomFormOption.Sort(animalOptions);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 36f, 34f), "WildShift_CustomFormsTitle".Translate());
            Text.Font = GameFont.Small;
            string explanation = "WildShift_CustomFormsRulesDesc".Translate();
            float explanationHeight = Text.CalcHeight(explanation, inRect.width);
            Widgets.Label(new Rect(0f, 40f, inRect.width, explanationHeight), explanation);

            float buttonsY = 50f + explanationHeight;
            float buttonWidth = (inRect.width - 24f) / 3f;
            if (Widgets.ButtonText(new Rect(0f, buttonsY, buttonWidth, 30f), "WildShift_CustomFormsAddRace".Translate()))
                OpenTargetSelector(false);
            if (ModsConfig.BiotechActive && Widgets.ButtonText(new Rect(buttonWidth + 12f, buttonsY, buttonWidth, 30f),
                "WildShift_CustomFormsAddXenotype".Translate())) OpenTargetSelector(true);
            if (Widgets.ButtonText(new Rect(inRect.width - buttonWidth, buttonsY, buttonWidth, 30f),
                "WildShift_CustomFormsReset".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("WildShift_CustomFormsResetConfirm".Translate(), delegate
                {
                    settings.customRacialForms.Clear();
                    settings.customRacialFormsEnabled = false;
                    Close();
                }));
            }

            float listY = buttonsY + 42f;
            Rect listRect = new Rect(0f, listY, inRect.width, Mathf.Max(40f, inRect.height - listY - 48f));
            Rect view = new Rect(0f, 0f, listRect.width - 18f,
                Mathf.Max(listRect.height, settings.customRacialForms.Count * RuleHeight));
            Widgets.BeginScrollView(listRect, ref scroll, view);
            if (settings.customRacialForms.Count == 0)
                Widgets.Label(new Rect(8f, 12f, view.width - 16f, 70f), "WildShift_CustomFormsEmpty".Translate());
            for (int i = 0; i < settings.customRacialForms.Count; i++)
            {
                CustomRacialFormRule rule = settings.customRacialForms[i];
                Rect row = new Rect(0f, i * RuleHeight, view.width, RuleHeight - 8f);
                if ((i & 1) == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.035f));
                string target = "WildShift_CustomFormsTarget".Translate(
                    (rule.xenotype ? "WildShift_CustomFormsXenotype" : "WildShift_CustomFormsRace").Translate(),
                    CustomRacialFormsSettingsUI.TargetLabel(rule));
                Rect targetRect = new Rect(8f, row.y + 5f, row.width - 120f, 26f);
                Widgets.Label(targetRect, target.Truncate(targetRect.width));
                TooltipHandler.TipRegion(targetRect, target);
                if (Widgets.ButtonText(new Rect(row.width - 100f, row.y + 4f, 92f, 26f), "WildShift_CustomFormsDelete".Translate()))
                {
                    settings.customRacialForms.RemoveAt(i);
                    break;
                }

                List<string> labels = new List<string>();
                for (int j = 0; j < rule.animalKindDefNames.Count; j++)
                    labels.Add(CustomRacialFormsSettingsUI.AnimalLabel(rule.animalKindDefNames[j]));
                string animals = labels.Count == 0 ? "WildShift_CustomFormsNoAnimals".Translate().ToString() : string.Join(", ", labels.ToArray());
                Rect animalsRect = new Rect(8f, row.y + 36f, row.width - 130f, 25f);
                bool oldWrap = Text.WordWrap;
                Text.WordWrap = false;
                Widgets.Label(animalsRect, animals.Truncate(animalsRect.width));
                Text.WordWrap = oldWrap;
                TooltipHandler.TipRegion(animalsRect, animals);
                if (Widgets.ButtonText(new Rect(row.width - 112f, row.y + 34f, 104f, 28f), "WildShift_CustomFormsEditAnimals".Translate()))
                    Find.WindowStack.Add(new CustomAnimalFormsSelector(rule, animalOptions));

                Widgets.Label(new Rect(8f, row.y + 76f, 180f, 28f),
                    "WildShift_CustomFormsChance".Translate((rule.chance * 100f).ToString("0")));
                rule.chance = Widgets.HorizontalSlider(new Rect(192f, row.y + 76f, row.width - 204f, 24f),
                    rule.chance, 0f, 1f, false, null, "0", "100", 0.01f);
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 34f, 120f, 30f), "CloseButton".Translate())) Close();
        }

        private void OpenTargetSelector(bool xenotype)
        {
            List<CustomFormOption> available = new List<CustomFormOption>();
            foreach (CustomFormOption option in xenotype ? xenotypeOptions : raceOptions)
            {
                bool exists = false;
                foreach (CustomRacialFormRule rule in settings.customRacialForms)
                    if (rule.xenotype == xenotype && rule.targetDefName == option.defName) { exists = true; break; }
                if (!exists) available.Add(option);
            }
            Find.WindowStack.Add(new CustomFormTargetSelector(available, xenotype, delegate(CustomFormOption option)
            {
                CustomRacialFormRule rule = new CustomRacialFormRule();
                rule.xenotype = xenotype;
                rule.targetDefName = option.defName;
                // Cancelling a new animal selection must not leave an empty
                // xenotype override behind an existing race rule.
                Find.WindowStack.Add(new CustomAnimalFormsSelector(rule, animalOptions, delegate
                {
                    settings.customRacialForms.Add(rule);
                }));
            }));
        }
    }

    internal abstract class CustomFormSearchWindow : Window
    {
        protected readonly List<CustomFormOption> options;
        protected readonly List<CustomFormOption> filtered;
        protected Vector2 scroll;
        private string search = string.Empty;

        public override Vector2 InitialSize
        {
            get { return new Vector2(Mathf.Min(650f, UI.screenWidth - 40f), Mathf.Min(680f, UI.screenHeight - 40f)); }
        }

        protected CustomFormSearchWindow(List<CustomFormOption> options)
        {
            this.options = options;
            filtered = new List<CustomFormOption>(options);
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
        }

        protected void DrawSearch(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, 70f, rect.height), "WildShift_CustomFormsSearch".Translate());
            string next = Widgets.TextField(new Rect(rect.x + 76f, rect.y, rect.width - 76f, rect.height), search);
            if (next != search)
            {
                search = next;
                filtered.Clear();
                foreach (CustomFormOption option in options) if (option.Matches(search)) filtered.Add(option);
                scroll = Vector2.zero;
            }
        }

        protected void DrawOptionLabel(Rect rect, CustomFormOption option)
        {
            Widgets.Label(rect, option.label.Truncate(rect.width));
            if (!string.IsNullOrEmpty(option.source)) TooltipHandler.TipRegion(rect, option.label + "\n" + option.source);
        }
    }

    internal sealed class CustomFormTargetSelector : CustomFormSearchWindow
    {
        private readonly bool xenotype;
        private readonly Action<CustomFormOption> selected;

        public CustomFormTargetSelector(List<CustomFormOption> options, bool xenotype, Action<CustomFormOption> selected) : base(options)
        {
            this.xenotype = xenotype;
            this.selected = selected;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 32f, 34f),
                (xenotype ? "WildShift_CustomFormsAddXenotype" : "WildShift_CustomFormsAddRace").Translate());
            Text.Font = GameFont.Small;
            DrawSearch(new Rect(0f, 44f, inRect.width, 30f));
            Rect listRect = new Rect(0f, 86f, inRect.width, inRect.height - 86f);
            Rect view = new Rect(0f, 0f, listRect.width - 18f, Mathf.Max(listRect.height, filtered.Count * 34f));
            Widgets.BeginScrollView(listRect, ref scroll, view);
            if (filtered.Count == 0) Widgets.Label(new Rect(4f, 4f, view.width - 8f, 70f), "WildShift_CustomFormsNoMatches".Translate());
            for (int i = 0; i < filtered.Count; i++)
            {
                Rect row = new Rect(0f, i * 34f, view.width, 32f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                DrawOptionLabel(new Rect(8f, row.y + 3f, row.width - 16f, 27f), filtered[i]);
                if (Widgets.ButtonInvisible(row))
                {
                    CustomFormOption option = filtered[i];
                    Close();
                    selected(option);
                    break;
                }
            }
            Widgets.EndScrollView();
        }
    }

    internal sealed class CustomAnimalFormsSelector : CustomFormSearchWindow
    {
        private readonly CustomRacialFormRule rule;
        private readonly HashSet<string> selected;
        private readonly HashSet<string> available;
        private readonly Action applied;

        public CustomAnimalFormsSelector(CustomRacialFormRule rule, List<CustomFormOption> options, Action applied = null) : base(options)
        {
            this.rule = rule;
            this.applied = applied;
            selected = new HashSet<string>(rule.animalKindDefNames);
            available = new HashSet<string>();
            foreach (CustomFormOption option in options) available.Add(option.defName);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 32f, 34f), "WildShift_CustomFormsAnimalTitle".Translate());
            Text.Font = GameFont.Small;
            string hint = "WildShift_CustomFormsAnimalDesc".Translate();
            float hintHeight = Text.CalcHeight(hint, inRect.width);
            Widgets.Label(new Rect(0f, 40f, inRect.width, hintHeight), hint);
            float searchY = 50f + hintHeight;
            DrawSearch(new Rect(0f, searchY, inRect.width, 30f));
            int unavailableCount = 0;
            foreach (string name in selected) if (!available.Contains(name)) unavailableCount++;
            Widgets.Label(new Rect(0f, searchY + 36f, inRect.width, 28f),
                "WildShift_CustomFormsSelected".Translate(selected.Count, unavailableCount));

            float listY = searchY + 70f;
            Rect listRect = new Rect(0f, listY, inRect.width, Mathf.Max(40f, inRect.height - listY - 48f));
            Rect view = new Rect(0f, 0f, listRect.width - 18f, Mathf.Max(listRect.height, filtered.Count * 34f));
            Widgets.BeginScrollView(listRect, ref scroll, view);
            if (filtered.Count == 0) Widgets.Label(new Rect(4f, 4f, view.width - 8f, 70f), "WildShift_CustomFormsNoMatches".Translate());
            for (int i = 0; i < filtered.Count; i++)
            {
                CustomFormOption option = filtered[i];
                Rect row = new Rect(4f, i * 34f, view.width - 8f, 32f);
                bool isSelected = selected.Contains(option.defName);
                bool wasSelected = isSelected;
                Widgets.CheckboxLabeled(row, option.label.Truncate(row.width - 40f), ref isSelected);
                if (isSelected != wasSelected)
                {
                    if (isSelected) selected.Add(option.defName);
                    else selected.Remove(option.defName);
                }
                if (!string.IsNullOrEmpty(option.source)) TooltipHandler.TipRegion(row, option.label + "\n" + option.source);
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(0f, inRect.height - 34f, 120f, 30f), "WildShift_CustomFormsClear".Translate())) selected.Clear();
            if (Widgets.ButtonText(new Rect(inRect.width - 248f, inRect.height - 34f, 120f, 30f), "CancelButton".Translate())) Close();
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 34f, 120f, 30f), "WildShift_CustomFormsApply".Translate()))
            {
                rule.animalKindDefNames = new List<string>(selected);
                rule.animalKindDefNames.Sort(StringComparer.Ordinal);
                if (applied != null) applied();
                Close();
            }
        }
    }
}
