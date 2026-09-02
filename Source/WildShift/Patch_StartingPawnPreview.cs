using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace WildShift
{
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "DoWindowContents")]
    public static class Patch_StartingPawnPreview
    {
        public static void Prefix(Rect rect)
        {
            LoneBeastkinUtility.EnsurePreviewStartingShapeshifter();
        }
    }
}
