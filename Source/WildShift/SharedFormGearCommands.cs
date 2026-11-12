using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Utility;
using Verse;

namespace WildShift
{
    // The native apparel gizmo iterator uses its private owning container,
    // unlike the public WornApparel view used by combat/stat calculations.
    [HarmonyPatch(typeof(Pawn_ApparelTracker),"GetGizmos")]
    public static class Patch_SharedWornGearCommands
    {
        public static void Postfix(Pawn ___pawn,ref IEnumerable<Gizmo> __result)
        {
            if(SharedFormIdentity.HumanFor(___pawn)!=null)__result=Append(__result,___pawn);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> native,Pawn body)
        {
            foreach(Gizmo gizmo in native)yield return gizmo;
            Pawn human=SharedFormIdentity.HumanFor(body);
            List<Apparel> sources=SharedFormGear.RawApparel(human);
            List<Apparel> own=SharedFormGear.RawApparel(body);
            if(sources==null)yield break;
            for(int i=0;i<sources.Count;i++)
            {
                Apparel item=sources[i];
                if(item==null||item.Destroyed||(own!=null&&own.Contains(item)))continue;
                foreach(Gizmo gizmo in item.GetWornGizmos())yield return gizmo;
            }
        }
    }

    // Apparel verb owners use their parent tracker directly instead of
    // Apparel.Wearer; the two native wearer paths must agree for permissions,
    // queued reloadable jobs and the command's enabled state.
    [HarmonyPatch(typeof(CompApparelVerbOwner),"get_Wearer")]
    public static class Patch_SharedWornGearVerbOwner
    {
        public static void Postfix(ref Pawn __result){__result=SharedFormGear.ActiveWearer(__result);}
    }

    // Reload jobs compare their acting Pawn with this native gameplay owner.
    // The item's real ThingOwner remains the preserved human's tracker.
    [HarmonyPatch(typeof(ReloadableUtility),"OwnerOf")]
    public static class Patch_SharedReloadableGearActor
    {
        public static void Postfix(ref Pawn __result){__result=SharedFormGear.ActiveWearer(__result);}
    }
}
