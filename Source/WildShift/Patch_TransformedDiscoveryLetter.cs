using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    [HarmonyPatch(typeof(Thing), "Notify_Unfogged")]
    public static class Patch_TransformedDiscoveryLetter
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo lookup = ((Func<Thing, CompLetterOnRevealed>)ThingCompUtility.TryGetComp<CompLetterOnRevealed>).Method;
            int index = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(lookup)) continue;
                if (index >= 0)
                {
                    Log.Error("[WildShift] Ambiguous discovery-letter lookup; leaving native discovery unchanged.");
                    return code;
                }
                index = i;
            }
            if (index < 0)
            {
                Log.Error("[WildShift] Discovery-letter lookup not found; leaving native discovery unchanged.");
                return code;
            }

            // Replace only the optional letter lookup. Preserve the revealed
            // flag, codex discovery, quest signals and other native callbacks.
            code[index].opcode = OpCodes.Call;
            code[index].operand = AccessTools.Method(typeof(Patch_TransformedDiscoveryLetter), nameof(GetDiscoveryLetter));
            return code;
        }

        public static CompLetterOnRevealed GetDiscoveryLetter(Thing thing)
        {
            CompLetterOnRevealed comp = thing.TryGetComp<CompLetterOnRevealed>();
            Pawn pawn = thing as Pawn;
            return comp != null && pawn != null && pawn.RaceProps.Animal
                && TransformUtility.IsTransformedAnimal(pawn) ? null : comp;
        }
    }
}
