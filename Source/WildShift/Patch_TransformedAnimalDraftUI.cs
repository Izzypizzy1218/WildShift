using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WildShift
{
    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    public static class Patch_TransformedAnimalDraftGizmos
    {
        public static bool Prefix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance != null ? __instance.pawn : null;
            if (!TransformUtility.IsTransformedAnimal(pawn))
            {
                return true;
            }

            __result = GetTransformedDraftGizmos(pawn);
            return false;
        }

        public static void Postfix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance != null ? __instance.pawn : null;
            if (TransformUtility.IsTransformedAnimal(pawn))
            {
                return;
            }

            HediffComp_Shapeshifter shapeshifter = TransformUtility.TryGetShapeshifterComp(pawn);
            if (shapeshifter == null)
            {
                return;
            }

            Gizmo shiftGizmo = shapeshifter.CreateShiftGizmo();
            if (shiftGizmo != null)
            {
                __result = InsertAfterDraftGizmo(__result, shiftGizmo);
            }
        }

        private static IEnumerable<Gizmo> GetTransformedDraftGizmos(Pawn animal)
        {
            Command_Toggle command = new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                defaultLabel = animal.Drafted
                    ? "WildShift_CommandUndraftLabel".Translate()
                    : "WildShift_CommandDraftLabel".Translate(),
                defaultDesc = "WildShift_CommandDraftDesc".Translate(),
                icon = TexCommand.Draft,
                isActive = delegate
                {
                    return animal.Drafted;
                },
                toggleAction = delegate
                {
                    if (animal.drafter != null)
                    {
                        animal.drafter.Drafted = !animal.drafter.Drafted;
                    }
                },
                turnOnSound = SoundDefOf.DraftOn,
                turnOffSound = SoundDefOf.DraftOff,
                groupKeyIgnoreContent = 81729172,
                tutorTag = animal.Drafted ? "Undraft" : "Draft"
            };

            if (animal.Downed)
            {
                command.Disable("IsIncapped".Translate(animal.LabelShort, animal));
            }

            yield return command;
        }

        private static IEnumerable<Gizmo> InsertAfterDraftGizmo(
            IEnumerable<Gizmo> original,
            Gizmo shiftGizmo)
        {
            bool inserted = false;
            if (original != null)
            {
                foreach (Gizmo gizmo in original)
                {
                    yield return gizmo;
                    if (!inserted)
                    {
                        inserted = true;
                        yield return shiftGizmo;
                    }
                }
            }

            if (!inserted)
            {
                yield return shiftGizmo;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuUtility), "UseRangedAttack")]
    public static class Patch_TransformedUnarmedAttack
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // Native drafted menus assume an equipment tracker exists. Animal
            // forms without one are unarmed and can use native melee orders.
            if (pawn == null || pawn.equipment != null || !TransformUtility.IsTransformedAnimal(pawn)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_TransformedDraftableAnimalsGizmos
    {
        // Producer metadata cannot change for a loaded Type. Cache both
        // matches and misses without retaining pawns, commands or delegates.
        private static readonly ConcurrentDictionary<Type, bool> externalDraftOwners
            = new ConcurrentDictionary<Type, bool>();
        private static readonly Func<Type, bool> classifyExternalDraftOwner = IsExternalDraftOwner;

        private sealed class AbilityCommandSet : HashSet<Command_Ability>
        {
            public AbilityCommandSet() : base(AbilityCommandReferenceComparer.Instance) { }
        }

        private sealed class AbilityCommandReferenceComparer : IEqualityComparer<Command_Ability>
        {
            internal static readonly AbilityCommandReferenceComparer Instance = new AbilityCommandReferenceComparer();
            public bool Equals(Command_Ability first, Command_Ability second) { return ReferenceEquals(first, second); }
            public int GetHashCode(Command_Ability command) { return RuntimeHelpers.GetHashCode(command); }
        }

        // A passthrough postfix must run after the other mod's passthrough
        // postfix. A ref-result (void) postfix runs before that iterator.
        [HarmonyAfter("DraftableAnimals.Mod", "Poupun.IsekaiCreaturesAddon", "OskarPotocki.VEF")]
        [HarmonyPriority(Priority.Last)]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            if (__instance == null || __instance.Faction?.IsPlayer != true) return __result;
            HediffComp_Transformed marker = TransformUtility.TryGetTransformedComp(__instance);
            if (marker?.HasStoredPawn != true) return __result;
            Pawn source = __instance.abilities is SharedPawnAbilityTracker
                && ModsConfig.IsActive("OskarPotocki.VanillaFactionsExpanded.Core")
                    ? SharedFormIdentity.GetStoredHumanFromActiveBody(__instance, marker) : null;
            return WithoutDuplicateCommands(__result, __instance, source);
        }

        private static IEnumerable<Gizmo> WithoutDuplicateCommands(IEnumerable<Gizmo> original, Pawn body, Pawn source)
        {
            if (original == null) yield break;
            AbilityCommandSet seen = null;
            try
            {
                foreach (Gizmo gizmo in original)
                {
                    // Both draft mods add a toggle for the same animal. Native
                    // grouping can execute both callbacks and undo the first.
                    if (gizmo is Command_Toggle command && IsExternalDraftToggle(command))
                        continue;
                    // VEF can enumerate the merged tracker again. Equipment
                    // abilities can also arrive through the native tracker.
                    if (source != null && gizmo is Command_Ability cast && cast.Ability != null)
                    {
                        if (seen == null) seen = SimplePool<AbilityCommandSet>.Get();
                        if (!seen.Add(cast) && (SharedFormAbilities.IsBorrowedBy(cast.Ability, body, source)
                            || source.abilities?.AllAbilitiesForReading.Contains(cast.Ability) == true))
                            continue;
                    }
                    yield return gizmo;
                }
            }
            finally
            {
                if (seen != null)
                {
                    seen.Clear();
                    SimplePool<AbilityCommandSet>.Return(seen);
                }
            }
        }

        private static bool IsExternalDraftToggle(Command_Toggle command)
        {
            if (command.hotKey != KeyBindingDefOf.Command_ColonistDraft) return false;
            Type owner = command.toggleAction?.Method.DeclaringType;
            if (owner == null) return false;
            return externalDraftOwners.GetOrAdd(owner, classifyExternalDraftOwner);
        }

        private static bool IsExternalDraftOwner(Type owner)
        {
            string assembly = owner.Assembly.GetName().Name;
            if (assembly == "DraftableAnimals") return true;
            if (assembly != "IsekaiCreatures") return false;

            // Match the actual producer, including its compiler-generated
            // closure. Do not hide every Isekai command bound to this key.
            for (Type current = owner; current != null; current = current.DeclaringType)
                if (current.FullName == "IsekaiCreatures.Taming.Pawn_GetGizmos_Pet_Patch")
                    return true;
            return false;
        }
    }
}
