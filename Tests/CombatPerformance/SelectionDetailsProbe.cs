using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShiftCombatPerformance
{
    // TEST ONLY. The synthetic hover is confined to the real RimHUD helper.
    // It exercises the tooltip getter, not physical OS hover or full rendering.
    internal static class SelectionHealthGetterHover
    {
        public struct State { public Event Event;public Vector2 Previous;public bool Applied; }
        private static bool installed,enabled,used;
        private static int depth;
        private static long attempts,over,blocked,getterCalls,layouts,repaints;
        private static string firstRect,firstProvider,firstPhysicalMouse;
        private static readonly PropertyInfo inputBlocked=AccessTools.Property(typeof(Mouse),"IsInputBlockedNow");
        public static void Install()
        {
            if(installed)return;
            Type tooltips=AccessTools.TypeByName("RimHUD.Interface.Hud.Tooltips.TooltipsPlus");
            Type health=AccessTools.TypeByName("RimHUD.Interface.Hud.Tooltips.HealthTooltip");
            MethodInfo draw=tooltips==null?null:AccessTools.DeclaredMethod(tooltips,"Draw");
            MethodInfo get=health==null?null:AccessTools.DeclaredMethod(health,"Get",Type.EmptyTypes);
            if(draw==null||get==null)throw new InvalidOperationException("Synthetic health getter hover requires the verified installed RimHUD tooltip methods");
            Boot.Harmony.Patch(AccessTools.DeclaredMethod(typeof(Root),"OnGUI",Type.EmptyTypes),prefix:new HarmonyMethod(typeof(SelectionHealthGetterHover),nameof(BeginGui)));
            Boot.Harmony.Patch(draw,prefix:new HarmonyMethod(typeof(SelectionHealthGetterHover),nameof(BeforeDraw)),finalizer:new HarmonyMethod(typeof(SelectionHealthGetterHover),nameof(AfterDraw)));
            Boot.Harmony.Patch(get,prefix:new HarmonyMethod(typeof(SelectionHealthGetterHover),nameof(BeforeGet)));
            installed=true;
        }
        public static void BeginGui(){used=false;}
        public static void ConfigurePhase(bool selected){enabled=selected;attempts=over=blocked=getterCalls=layouts=repaints=0;firstRect=firstProvider=firstPhysicalMouse=null;used=false;depth=0;}
        public static void BeforeDraw(Rect __0,Func<string> __1,out State __state)
        {
            __state=default(State);Event guiEvent=Event.current;
            if(!enabled||used||guiEvent==null||(__1?.Method.DeclaringType?.FullName!="RimHUD.Interface.Hud.Tooltips.HealthTooltip")||__1.Method.Name!="Get"||!__1.Method.IsStatic||__1.Target!=null||(guiEvent.type!=EventType.Layout&&guiEvent.type!=EventType.Repaint))return;
            used=true;attempts++;if(guiEvent.type==EventType.Layout)layouts++;else repaints++;
            if(firstRect==null){firstRect=__0.ToString();firstProvider=__1.Method.DeclaringType.FullName+"."+__1.Method.Name;firstPhysicalMouse=Input.mousePosition.ToString();}
            __state=new State{Event=guiEvent,Previous=guiEvent.mousePosition,Applied=true};
            guiEvent.mousePosition=__0.center;depth++;
            if(Mouse.IsOver(__0))over++;
            object inputBlock=inputBlocked?.GetValue(null,null);if(inputBlock is bool&&(bool)inputBlock)blocked++;
        }
        public static void AfterDraw(State __state)
        {
            if(!__state.Applied)return;
            __state.Event.mousePosition=__state.Previous;if(depth>0)depth--;
        }
        public static void BeforeGet(){if(enabled&&depth>0)getterCalls++;}
        public static bool Exercised {get{return attempts>0&&over>0&&getterCalls>0;}}
        public static object Snapshot(){return new{Mode="SyntheticHealthTooltipGetterHover",Installed=installed,Requested=enabled,Attempts=attempts,MouseOverTrue=over,InputBlockedTrue=blocked,GetterCalls=getterCalls,Layouts=layouts,Repaints=repaints,FirstRect=firstRect,Provider=firstProvider,PhysicalInputMouse=firstPhysicalMouse,Exercised,Scope="Only the first matching real HealthTooltip.Get delegate in each native Layout/Repaint is hovered inside TooltipsPlus.Draw. Event mouse position is restored in a finalizer; physical Input.mousePosition and clicks are not changed. This isolates actual getter calculation and does not prove OS hover or a visible tooltip window."};}
    }

    public partial class CombatBenchmark
    {
        private bool selectionHealthTab,selectionSyntheticHover,selectionDetailsSelected,selectionDetailsValid=true;
        partial void ConfigureSelectionDetails(XElement input)
        {
            selectionHealthTab=input.Element("SelectionInspectTab")?.Value=="Health";
            selectionSyntheticHover=bool.Parse(input.Element("SelectionSyntheticHealthHover")?.Value??"false");
            if(selectionSyntheticHover)SelectionHealthGetterHover.Install();
        }
        partial void ApplySelectionDetails(bool selected)
        {
            selectionDetailsSelected=selected;
            if(selectionSyntheticHover)SelectionHealthGetterHover.ConfigurePhase(selected);
            if(!selectionHealthTab)return;
            MainTabWindow_Inspect pane=MainButtonDefOf.Inspect.TabWindow as MainTabWindow_Inspect;
            Require(pane!=null,"native inspect pane exists");
            if(!selected){pane.CloseOpenTab();return;}
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect,true);
            var tabs=pane.CurTabs.ToArray();
            Require(tabs.Any(tab=>tab.GetType()==typeof(ITab_Pawn_Health)&&tab.IsVisible),"native Health tab is available for the selected actor");
            InspectTabBase opened=InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Health));
            Require(opened!=null&&opened.IsVisible&&pane.OpenTabType==typeof(ITab_Pawn_Health),"native Health tab opened for selection probe");
        }
        partial void CaptureSelectionDetails(ref object details,ref string hoverCase)
        {
            if(!selectionHealthTab&&!selectionSyntheticHover)return;
            hoverCase=selectionSyntheticHover&&selectionDetailsSelected?"SyntheticHealthTooltipGetterHover":"HoverNotIntentionallyTested";
            MainTabWindow_Inspect pane=MainButtonDefOf.Inspect.TabWindow as MainTabWindow_Inspect;
            bool tabApplied=!selectionHealthTab||!selectionDetailsSelected||(pane!=null&&pane.OpenTabType==typeof(ITab_Pawn_Health));
            bool hoverApplied=!selectionSyntheticHover||!selectionDetailsSelected||SelectionHealthGetterHover.Exercised;
            selectionDetailsValid=selectionDetailsValid&&tabApplied&&hoverApplied;
            details=new{RequestedTab=selectionHealthTab?"Health":"None",Selected=selectionDetailsSelected,OpenTab=pane?.OpenTabType?.FullName,AvailableTabs=pane?.CurTabs?.Select(tab=>new{Type=tab.GetType().FullName,tab.IsVisible}).ToArray(),TabApplied=tabApplied,HoverApplied=hoverApplied,Hover=selectionSyntheticHover?SelectionHealthGetterHover.Snapshot():null};
        }
        partial void ValidateSelectionDetails(ref bool passed){passed=passed&&selectionDetailsValid;}
    }
}
