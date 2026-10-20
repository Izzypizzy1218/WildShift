namespace WildShift.Tests
{
    // These tests execute the production selector, assignment lifecycle and
    // transformation guard. Scribe below remains a keyed Def-reference stub;
    // native save serialization is covered separately by the runtime suite.
    public static class CustomRacialFormsTests
    {
        private static int assertions;

        private static void Check(bool ok, string name)
        {
            if (!ok) throw new Exception("FAIL custom forms: " + name);
            assertions++;
        }

        private static Pawn Person(string race, string xenotype = null)
        {
            return new Pawn
            {
                def = new ThingDef { defName = race, race = new RaceProperties { Humanlike = true } },
                genes = xenotype == null ? null : new Genes { Xenotype = new Def { defName = xenotype } }
            };
        }

        private static PawnKindDef Animal(string name, bool predator = false, float size = 0.2f)
        {
            PawnKindDef existing = DefDatabase<PawnKindDef>.GetNamedSilentFail(name);
            if (existing != null) return existing;
            var kind = new PawnKindDef
            {
                defName = name,
                race = new ThingDef
                {
                    defName = name,
                    race = new RaceProperties { Animal = true, predator = predator, baseBodySize = size }
                }
            };
            DefDatabase<PawnKindDef>.AllDefsListForReading.Add(kind);
            return kind;
        }

        private static CustomRacialFormRule Rule(string target, float chance, bool xenotype, params string[] animals)
        {
            return new CustomRacialFormRule
            {
                targetDefName = target,
                chance = chance,
                xenotype = xenotype,
                animalKindDefNames = new List<string>(animals)
            };
        }

        private static void Rules(params CustomRacialFormRule[] rules)
        {
            WildShiftMod.Settings.customRacialForms = new List<CustomRacialFormRule>(rules);
        }

        private static void TestRevengeCompanionAssignment()
        {
            var savedKinds = new List<PawnKindDef>(DefDatabase<PawnKindDef>.AllDefsListForReading);
            var oldGenerator = PawnGenerator.Generate;
            try
            {
                // Exercise the complete production companion builder with a
                // small known animal pool, not a reimplementation of its budget.
                DefDatabase<PawnKindDef>.AllDefsListForReading.Clear();
                PawnKindDef leader = Animal("CustomRevengeLeader", true);
                PawnKindDef fallback = Animal("CustomRevengeFallback", true);
                PawnKindDef cat = Animal("Cat");
                PawnKindDef expensive = Animal("Thrumbo", false, 4f);
                leader.combatPower = 100f;
                fallback.combatPower = 100f;
                cat.combatPower = 20f;
                expensive.combatPower = 5000f;
                PawnGenerator.Generate = null;
                Rand.ForceIndex = 0;
                Rand.Force = true;
                var companions = new List<Pawn>();

                Rules(Rule("Human", 1f, false, "Cat"));
                WildShiftMod.Settings.customRacialFormsEnabled = true;
                PawnGenerator.Generated.Clear();
                float used = RevengeCompanions.Prepare(300f, leader, Faction.OfPlayer, companions);
                Check(companions.Count == 3 && used == companions.Count * cat.combatPower,
                    "actual companion builder charges custom cat power and can add more than one ally");
                Check(companions.TrueForAll(p => TransformUtility.TryGetShapeshifterComp(p).assignedKind == cat
                    && TransformUtility.TryGetShapeshifterComp(p).customAssignedKind == cat),
                    "new revenge companions honor guaranteed custom form with provenance");
                Check(companions.TrueForAll(p => TransformUtility.TryGetShapeshifterComp(p).enemyRaidParticipant
                    && !TransformUtility.TryGetShapeshifterComp(p).enemyShiftUsed && !p.Destroyed),
                    "custom companions retain live enemy-raid participation state");

                WildShiftMod.Settings.customRacialFormsEnabled = false;
                companions.Clear();
                PawnGenerator.Generated.Clear();
                int probabilityCalls = Rand.Calls;
                int selectionCalls = Rand.RangeCalls;
                used = RevengeCompanions.Prepare(300f, leader, Faction.OfPlayer, companions);
                Check(companions.Count == 1 && used == fallback.combatPower
                    && TransformUtility.TryGetShapeshifterComp(companions[0]).assignedKind == fallback
                    && TransformUtility.TryGetShapeshifterComp(companions[0]).customAssignedKind == null,
                    "disabled custom rules preserve the legacy revenge companion form and budget");
                Check(Rand.Calls == probabilityCalls && Rand.RangeCalls == selectionCalls + 1,
                    "disabled companion assignment adds no probability or animal-selection rolls");

                WildShiftMod.Settings.customRacialFormsEnabled = true;
                Rules(Rule("Human", 1f, false, "Thrumbo"));
                PawnKindDef secondFallback = Animal("CustomRevengeSecondFallback", true);
                secondFallback.combatPower = 120f;
                companions.Clear();
                PawnGenerator.Generated.Clear();
                used = RevengeCompanions.Prepare(300f, leader, Faction.OfPlayer, companions);
                Check(used == 0f && companions.Count == 0 && PawnGenerator.Generated.Count == 2,
                    "unaffordable custom companions terminate after trying each fallback candidate once");
                Check(PawnGenerator.Generated.TrueForAll(p => p.Destroyed),
                    "rejected custom companion bodies are destroyed instead of leaked");
                DefDatabase<PawnKindDef>.AllDefsListForReading.Remove(secondFallback);

                Rules(Rule("Human", 0.5f, false, "Cat"));
                Rand.Force = false;
                companions.Clear();
                PawnGenerator.Generated.Clear();
                probabilityCalls = Rand.Calls;
                used = RevengeCompanions.Prepare(300f, leader, Faction.OfPlayer, companions);
                Check(used == fallback.combatPower && companions.Count == 1
                    && TransformUtility.TryGetShapeshifterComp(companions[0]).assignedKind == fallback
                    && Rand.Calls == probabilityCalls + 1,
                    "failed companion custom roll safely retains the budgeted ordinary fallback");

                Rules(Rule("Human", 1f, false, "MissingRevengeAnimal"));
                companions.Clear();
                PawnGenerator.Generated.Clear();
                used = RevengeCompanions.Prepare(300f, leader, Faction.OfPlayer, companions);
                Check(used == fallback.combatPower && companions.Count == 1
                    && TransformUtility.TryGetShapeshifterComp(companions[0]).assignedKind == fallback
                    && TransformUtility.TryGetShapeshifterComp(companions[0]).customAssignedKind == null,
                    "missing companion custom animals fall back without invalid provenance");
            }
            finally
            {
                DefDatabase<PawnKindDef>.AllDefsListForReading.Clear();
                DefDatabase<PawnKindDef>.AllDefsListForReading.AddRange(savedKinds);
                PawnGenerator.Generate = oldGenerator;
                PawnGenerator.Generated.Clear();
            }
        }

        public static string Run()
        {
            Settings oldSettings = WildShiftMod.Settings;
            bool? oldForce = Rand.Force;
            int? oldIndex = Rand.ForceIndex;
            int oldPoolIndex = Extensions.PoolIndex;
            try
            {
                WildShiftMod.Settings = new Settings();
                Pawn ratkin = Person("Ratkin");
                Pawn human = Person("Human");
                PawnKindDef warg = Animal("Warg", true);
                PawnKindDef thrumbo = Animal("Thrumbo", false, 4f);
                PawnKindDef rat = Animal("Rat");
                PawnKindDef cat = Animal("Cat");
                Rand.Force = true;
                Rand.ForceIndex = 0;
                Extensions.PoolIndex = 0;

                Rules(Rule("Ratkin", 1f, false, "Thrumbo"));
                int calls = Rand.Calls;
                Check(!WildShiftMod.Settings.customRacialFormsEnabled, "feature is off by default");
                Check(RacialAnimalForms.Choose(ratkin, warg) == rat, "off preserves original Ratkin preference");
                Check(Rand.Calls == calls + 1, "off consumes only the original preference roll");
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(human, warg) == warg, "off preserves ordinary human fallback");
                Check(Rand.Calls == calls, "off adds no random rolls for an ordinary human");

                WildShiftMod.Settings.customRacialFormsEnabled = true;
                bool custom;
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(ratkin, warg, out custom) == thrumbo && custom,
                    "100 percent Ratkin to Thrumbo overrides default preference");
                Check(Rand.Calls == calls, "guaranteed custom rule requires no probability roll");
                PawnKindDef chosen;
                Check(!CustomRacialForms.TryChoose(null, out chosen) && chosen == null, "null pawn is harmless");
                Check(!CustomRacialForms.TryChoose(new Pawn { def = thrumbo.race }, out chosen), "animal cannot match a human race rule");

                Rules(Rule("Ratkin", 0f, false, "Thrumbo"));
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(ratkin, warg, out custom) == rat && !custom,
                    "zero custom chance returns to default racial preference");
                Check(Rand.Calls == calls + 1, "zero custom chance consumes only default probability roll");
                Rules(Rule("Ratkin", 1f, false, "Thrumbo"));
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(Person("NotReallyRatkin"), warg, out custom) == warg && !custom,
                    "race matches exact Def name instead of substring");
                Check(Rand.Calls == calls, "unmatched rules leave random sequence unchanged");

                Rules(Rule("Ratkin", 1f, false, "Thrumbo", "Cat"));
                Rand.ForceIndex = 0;
                Check(RacialAnimalForms.Choose(ratkin, warg, out custom) == thrumbo && custom, "multiple choices first slot");
                Rand.ForceIndex = 1;
                Check(RacialAnimalForms.Choose(ratkin, warg, out custom) == cat && custom, "multiple choices second slot");
                Rand.ForceIndex = 0;

                Pawn hussarRatkin = Person("Ratkin", "Hussar");
                Rules(Rule("Ratkin", 1f, false, "Thrumbo"), Rule("Hussar", 1f, true, "Cat"));
                Check(RacialAnimalForms.Choose(hussarRatkin, warg, out custom) == cat && custom,
                    "exact xenotype rule takes precedence over race rule");
                Check(RacialAnimalForms.Choose(ratkin, warg, out custom) == thrumbo && custom,
                    "missing xenotype uses matching race rule");
                Rules(Rule("Ratkin", 1f, false, "Thrumbo"), Rule("Hussar", 0f, true, "Cat"));
                Check(RacialAnimalForms.Choose(hussarRatkin, warg, out custom) == rat && !custom,
                    "zero xenotype rule falls back to preset rather than race custom rule");
                Rules(Rule("Ratkin", 1f, false, "Thrumbo"), Rule("Hussar", 0.5f, true, "Cat"));
                Rand.Force = false;
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(hussarRatkin, warg, out custom) == warg && !custom,
                    "failed xenotype roll falls back to default pool rather than race custom rule");
                Check(Rand.Calls == calls + 2, "intermediate custom chance and preset use their independent rolls");
                Rand.Force = true;
                Check(RacialAnimalForms.Choose(hussarRatkin, warg, out custom) == cat && custom, "intermediate custom chance can succeed");
                Rules(Rule("Ratkin", 1f, false, "Thrumbo"), Rule("Hussar", 1f, true, "RemovedModAnimal"));
                Check(RacialAnimalForms.Choose(hussarRatkin, warg, out custom) == rat && !custom,
                    "missing xenotype choices return to default rather than race custom rule");

                Rules(Rule("Human", 1f, false, "MissingKind", "Cat", "Cat", null, ""));
                Check(RacialAnimalForms.Choose(human, warg, out custom) == cat && custom,
                    "unknown and duplicate choices do not prevent valid choice");
                Rules(Rule("Human", 1f, false, "MissingKind"));
                Check(RacialAnimalForms.Choose(human, warg, out custom) == warg && !custom, "removed mod kind falls back safely");

                var normalized = new List<CustomRacialFormRule>
                {
                    null,
                    Rule("  ", 1f, false, "Cat"),
                    Rule(" Ratkin ", 4f, false, " Cat ", "Cat", "", null, "MissingOptionalModAnimal"),
                    Rule("Ratkin", 0f, false, "Thrumbo"),
                    Rule("Ratkin", -1f, true, "Thrumbo"),
                    Rule("NaNTarget", float.NaN, false, "Cat"),
                    Rule("InfinityTarget", float.PositiveInfinity, false, "Cat")
                };
                CustomRacialForms.NormalizeRules(normalized);
                Check(normalized.Count == 4, "normalization removes empty rules and duplicate exact targets");
                Check(normalized[0].targetDefName == "Ratkin" && normalized[0].chance == 1f,
                    "normalization trims target and clamps chance above one");
                Check(normalized[0].animalKindDefNames.Count == 2 && normalized[0].animalKindDefNames[0] == "Cat",
                    "normalization trims and deduplicates choices without changing their order");
                Check(normalized[0].animalKindDefNames[1] == "MissingOptionalModAnimal", "absent mod choice names are retained for later reinstall");
                Check(normalized[1].xenotype && normalized[1].targetDefName == "Ratkin" && normalized[1].chance == 0f,
                    "race and xenotype may share a name and negative chance clamps to zero");
                Check(normalized[2].chance == 1f && normalized[3].chance == 1f, "nonfinite saved chances normalize to finite defaults");
                var emptyChoices = Rule("Human", 0.2f, false);
                emptyChoices.animalKindDefNames = null;
                emptyChoices.Normalize();
                Check(emptyChoices.animalKindDefNames != null && emptyChoices.animalKindDefNames.Count == 0,
                    "null saved choice list normalizes to an empty list");
                Rules(Rule("Human", float.NaN, false, "Cat"));
                Check(!CustomRacialForms.TryChoose(human, out chosen), "invalid transient chance never reaches a random call");

                Rules(Rule("Human", 1f, false, "Thrumbo"));
                SapientAnimalsCompatibility.TestPawn = human;
                SapientAnimalsCompatibility.TestOriginalKind = cat;
                calls = Rand.Calls;
                Check(RacialAnimalForms.Choose(human, warg, out custom) == cat && !custom,
                    "sapient animal keeps its existing integration-specific original form");
                Check(Rand.Calls == calls, "sapient original form bypass consumes no random roll");
                SapientAnimalsCompatibility.TestPawn = null;
                SapientAnimalsCompatibility.TestOriginalKind = null;

                Check(CustomRacialForms.IsSelectable(thrumbo), "normal large animal selectable");
                Check(CustomRacialForms.IsSelectable(rat) && CustomRacialForms.IsSelectable(cat), "existing small racial forms selectable");
                Check(CustomRacialForms.IsSelectable(Animal("Fox_Red")), "existing fox racial form selectable");
                Check(!CustomRacialForms.IsSelectable(Animal("CustomTestChicken")), "unlisted small non-pool animal remains ineligible");
                Check(!CustomRacialForms.IsSelectable(null), "null kind is rejected");
                Check(!CustomRacialForms.IsSelectable(new PawnKindDef()), "missing race is rejected");
                Check(!CustomRacialForms.IsSelectable(new PawnKindDef { race = new ThingDef() }), "missing race properties are rejected");
                PawnKindDef unsafeKind = Animal("CustomTestUnsafe", true);
                unsafeKind.race.race.IsMechanoid = true;
                Check(!CustomRacialForms.IsSelectable(unsafeKind), "mechanical animal is rejected");
                unsafeKind.race.race.IsMechanoid = false;
                unsafeKind.race.race.Humanlike = true;
                Check(!CustomRacialForms.IsSelectable(unsafeKind), "humanlike animal is rejected");
                unsafeKind.race.race.Humanlike = false;
                unsafeKind.race.race.IsAnomalyEntity = true;
                Check(!CustomRacialForms.IsSelectable(unsafeKind), "anomaly entity is rejected");
                unsafeKind.race.race.IsAnomalyEntity = false;
                unsafeKind.race.race.IsDrone = true;
                Check(!CustomRacialForms.IsSelectable(unsafeKind), "drone is rejected");
                unsafeKind.race.race.IsDrone = false;
                GeneticsCompatibility.ExcludedNames.Add(unsafeKind.race.defName);
                Check(!CustomRacialForms.IsSelectable(unsafeKind), "unsafe genetic hybrid is rejected");
                GeneticsCompatibility.ExcludedNames.Clear();

                PawnKindDef insect = Animal("CustomTestMegaspider", true);
                insect.race.race.FleshType = FleshTypeDefOf.Insectoid;
                Rules(Rule("Human", 1f, false, insect.defName));
                Check(!CustomRacialForms.IsSelectable(insect), "insect toggle disabled rejects insect choice");
                Check(RacialAnimalForms.Choose(human, warg, out custom) == warg && !custom, "disabled insect cannot be assigned through custom rule");
                WildShiftMod.Settings.allowInsectoids = true;
                Check(CustomRacialForms.IsSelectable(insect), "insect toggle enabled permits eligible insect");
                Check(RacialAnimalForms.Choose(human, warg, out custom) == insect && custom, "enabled insect rule assigns insect");
                WildShiftMod.Settings.allowInsectoids = false;

                Rules(Rule("Human", 1f, false, "Cat"));
                Pawn customHuman = Person("Human");
                HediffComp_Shapeshifter comp = TransformUtility.AddOrGetShapeshifter(customHuman, warg, true);
                Check(comp.assignedKind == cat && comp.customAssignedKind == cat, "new custom form records its own permission marker");
                Check(TransformUtility.TransformToAnimal(customHuman, cat), "production guard permits assigned small custom form");
                Check(!TransformUtility.TransformToAnimal(Person("Human"), cat), "current rule does not grant arbitrary unrelated pawns custom forms");
                calls = Rand.Calls;
                for (int i = 0; i < 100; i++)
                {
                    comp.EnsureAssignedKind();
                    TransformUtility.AddOrGetShapeshifter(customHuman, warg, true);
                }
                Check(comp.assignedKind == cat && Rand.Calls == calls, "callback and redraw validation never reroll existing custom form");
                Rules(Rule("Human", 1f, false, "Thrumbo"));
                comp.EnsureAssignedKind();
                Check(comp.assignedKind == cat && TransformUtility.TransformToAnimal(customHuman, cat), "editing rule leaves existing custom form usable");
                Rules();
                comp.EnsureAssignedKind();
                Check(comp.assignedKind == cat && TransformUtility.TransformToAnimal(customHuman, cat), "deleting rule leaves existing custom form usable");
                WildShiftMod.Settings.customRacialFormsEnabled = false;
                comp.EnsureAssignedKind();
                Check(comp.assignedKind == cat && TransformUtility.TransformToAnimal(customHuman, cat), "master off leaves existing custom form usable");
                comp.CompExposeData();
                var restored = new HediffComp_Shapeshifter { parent = new Hediff { pawn = customHuman } };
                Scribe_Defs.Loading = true;
                restored.CompExposeData();
                Scribe_Defs.Loading = false;
                customHuman.health.hediffSet.Value.Comp = restored;
                restored.EnsureAssignedKind();
                Check(restored.assignedKind == cat && restored.customAssignedKind == cat, "saved custom Def marker survives reload with feature off");
                Check(TransformUtility.TransformToAnimal(customHuman, cat), "production guard accepts restored custom form with feature off");
                Check(!restored.AllowsAssignedKind(rat), "permission marker is restricted to exact assigned form");
                restored.customAssignedKind = rat;
                Check(!restored.AllowsAssignedKind(cat), "mismatched saved marker cannot authorize assigned small form");
                restored.customAssignedKind = cat;
                Pawn freshHuman = Person("Human");
                Check(TransformUtility.AddOrGetShapeshifter(freshHuman, warg, true).assignedKind == warg,
                    "new pawn returns to default while feature is off");
                WildShiftMod.Settings.customRacialFormsEnabled = true;
                Rules(Rule("Human", 1f, false, "Cat"));
                Pawn alreadyAssigned = Person("Human");
                HediffComp_Shapeshifter legacy = TransformUtility.AddOrGetShapeshifter(alreadyAssigned, warg);
                TransformUtility.AddOrGetShapeshifter(alreadyAssigned, null, true);
                Check(legacy.assignedKind == warg && legacy.customAssignedKind == null, "legacy form unaffected by enabling matching rule");
                Pawn tamed = Person("Human");
                Check(TransformUtility.AddOrGetShapeshifter(tamed, warg).assignedKind == warg,
                    "explicit tamed or purchased animal kind keeps its original form");
                TestRevengeCompanionAssignment();
                return "PASS: " + assertions + " custom form assertions (production logic with engine stubs).";
            }
            finally
            {
                WildShiftMod.Settings = oldSettings;
                Rand.Force = oldForce;
                Rand.ForceIndex = oldIndex;
                Extensions.PoolIndex = oldPoolIndex;
                GeneticsCompatibility.ExcludedNames.Clear();
                SapientAnimalsCompatibility.TestPawn = null;
                SapientAnimalsCompatibility.TestOriginalKind = null;
                Scribe_Defs.Loading = false;
            }
        }
    }
}
