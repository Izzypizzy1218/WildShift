using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

// These stubs model ownership and injected failures, not RimWorld rendering/AI.
namespace HarmonyLib { public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string s) {} } }
namespace UnityEngine
{
    public struct Vector2 {}
    public enum EventType { MouseDown }
    public class Event { public static Event current; public EventType type; public int button; public bool used; public void Use() { used = true; } }
    public static class Mathf { public static float Clamp(float n,float a,float b) { return Math.Max(a,Math.Min(n,b)); } }
}
namespace Verse
{
    public enum DestroyMode { Vanish }
    public struct IntVec3 {}
    public struct Rot4 {}
    public interface IThingHolder { ThingOwner GetDirectlyHeldThings(); }
    public class Thing { public bool Destroyed, Spawned; public Map Map; public IntVec3 Position; }
    public class RaceProperties { public bool Animal = true; }
    public class Jobs { public void StopAll(bool b) {} }
    public class Pawn : Thing
    {
        public ThingOwner holdingOwner;
        public IThingHolder ParentHolder { get { return holdingOwner; } }
        public Map MapHeld { get { return Map; } }
        public IntVec3 PositionHeld { get { return Position; } }
        public Rot4 Rotation;
        public bool Dead;
        public Pawn_DraftController drafter = new Pawn_DraftController();
        public bool Drafted { get { return drafter != null && drafter.Drafted; } }
        public string LabelShortCap = "test pawn";
        public Pawn_PlayerSettings playerSettings = new Pawn_PlayerSettings();
        public Faction Faction = Faction.OfPlayer;
        public RaceProperties RaceProps = new RaceProperties();
        public int thingIDNumber;
        public Jobs jobs = new Jobs();
        public WildShift.HediffComp_Transformed comp;
        public void SetFaction(Faction faction) { Faction = faction; }
        public void DeSpawn(DestroyMode mode) { Spawned = false; Map = null; drafter = null; }
        public void Destroy(DestroyMode mode) { Destroyed = true; Spawned = false; if (holdingOwner != null) holdingOwner.Remove(this); Find.WorldPawns.RemovePawn(this); }
        public void Kill(DamageInfo? info) { Dead = true; }
        public void TakeDamage(DamageInfo info) {}
    }
    public class ThingOwner : IThingHolder
    {
        public List<Pawn> pawns = new List<Pawn>();
        public Predicate<Pawn> Reject, ThrowBefore, ThrowAfter;
        public bool PreflightReject;
        public int Count { get { return pawns.Count; } }
        public bool Contains(Pawn p) { return p != null && p.holdingOwner == this; }
        public bool CanAcceptAnyOf(Pawn p, bool merge) { return !PreflightReject; }
        public bool Remove(Pawn p) { if (!Contains(p)) return false; pawns.Remove(p); p.holdingOwner = null; return true; }
        public bool TryAddOrTransfer(Pawn p, bool merge)
        {
            if (PreflightReject || (Reject != null && Reject(p))) return false;
            // Inject after detaching from the source, like a failing transfer callback.
            if (p.holdingOwner != null) p.holdingOwner.Remove(p);
            if (ThrowBefore != null && ThrowBefore(p)) throw new Exception("insert before commit");
            pawns.Add(p); p.holdingOwner = this;
            if (ThrowAfter != null && ThrowAfter(p)) throw new Exception("insert after commit");
            return true;
        }
        public ThingOwner GetDirectlyHeldThings() { return this; }
    }
    public class MapPawns
    {
        public int reads; public List<Pawn> pawns = new List<Pawn>();
        public IReadOnlyList<Pawn> AllPawnsSpawned { get { reads++; return pawns; } }
    }
    public class Map { public MapPawns mapPawns = new MapPawns(); public bool IsPlayerHome; public int uniqueID; }
    public static class GenSpawn
    {
        public static Predicate<Pawn> FailBefore, FailAfter;
        public static void Spawn(Pawn p, IntVec3 cell, Map map)
        {
            if (FailBefore != null && FailBefore(p)) throw new Exception("spawn before commit");
            p.Spawned = true; p.Map = map; p.Position = cell; p.drafter = new Pawn_DraftController();
            if (FailAfter != null && FailAfter(p)) throw new Exception("spawn after commit");
        }
    }
    public static class Log { public static void Error(string s) {} public static void Warning(string s) {} }
    public class PlaySettings { public bool showColonistBar = true; }
    public static class Find
    {
        public static WorldPawns WorldPawns = new WorldPawns();
        public static List<Map> Maps = new List<Map>();
        public static WorldObjects WorldObjects = new WorldObjects();
        public static PlaySettings PlaySettings = new PlaySettings();
        public static Selector Selector = new Selector();
        public static ColonistBar ColonistBar = new ColonistBar();
    }
    public static class Extensions
    {
        public static void SortBy<T,K>(this List<T> list, Func<T,K> key) { var sorted=list.OrderBy(key).ToList(); list.Clear(); list.AddRange(sorted); }
        public static void SortBy<T,K,L>(this List<T> list, Func<T,K> a, Func<T,L> b) { var sorted=list.OrderBy(a).ThenBy(b).ToList(); list.Clear(); list.AddRange(sorted); }
        public static string Translate(this string s, params object[] args) { return s; }
    }
    public static class Messages { public static void Message(string s, Pawn p, object type, bool historical) {} }
    public struct DamageInfo { public object Def; public float Amount; public DamageInfo(object def, float n) { Def=def; Amount=n; } }
    public static class Rand { public static bool Chance(float n) { return n >= 1; } }
    public struct LocalTargetInfo { public Thing Thing; }
    public class TargetingParameters { public static TargetingParameters ForAttackAny() { return new TargetingParameters(); } }
    public static class GenUI { public static List<LocalTargetInfo> targets = new List<LocalTargetInfo>(); public static IEnumerable<LocalTargetInfo> TargetsAtMouse(TargetingParameters p, bool b, object o) { return targets; } }
}
namespace RimWorld
{
    public class Faction { public static Faction OfPlayer = new Faction(); }
    public class Pawn_DraftController { public bool Drafted; }
    public class Pawn_PlayerSettings { public const int UnsetDisplayOrder = -999; public int displayOrder; }
    public class Selector
    {
        public List<Pawn> SelectedPawns = new List<Pawn>();
        public bool IsSelected(Pawn p) { return SelectedPawns.Contains(p); }
        public void Deselect(Pawn p) { SelectedPawns.Remove(p); }
        public void Select(Pawn p) { SelectedPawns.Add(p); }
    }
    public static class MessageTypeDefOf { public static object PositiveEvent = new object(); }
    public static class DamageDefOf { public static object ExecutionCut = new object(), Blunt = new object(); }
    public static class FleckDefOf { public static object FeedbackMelee = new object(); }
    public static class FleckMaker { public static void Static(IntVec3 c, Map m, object d, float scale) {} }
    public class Drawer { public int calls; public void Notify_RecachedEntries() { calls++; } }
    public class ColonistBar
    {
        public Drawer drawer = new Drawer(); public void MarkColonistsDirty() {}
        public struct Entry { public Pawn pawn; public Map map; public int group; public Entry(Pawn p, Map m, int g) { pawn=p; map=m; group=g; } }
    }
    public class ColonistBarDrawLocsFinder { public void CalculateDrawLocs(List<Vector2> v, out float scale, int count) { scale=1; } }
}
namespace RimWorld.Planet
{
    public enum PawnDiscardDecideMode { KeepForever }
    public class WorldPawns
    {
        public HashSet<Pawn> pawns = new HashSet<Pawn>(); public bool Fail;
        public void PassToWorld(Pawn p, PawnDiscardDecideMode m) { if(Fail) throw new Exception("world registration"); pawns.Add(p); }
        public void RemovePawn(Pawn p) { pawns.Remove(p); }
    }
    public static class WorldPawnsUtility { public static bool IsWorldPawn(Pawn p) { return Find.WorldPawns.pawns.Contains(p); } }
    public class Caravan { public int ID; public bool IsPlayerControlled = true; public List<Pawn> PawnsListForReading = new List<Pawn>(); }
    public class WorldObjects { public List<Caravan> Caravans = new List<Caravan>(); }
}
namespace WildShift
{
    public class HediffComp_Transformed
    {
        public ThingOwner storage = new ThingOwner();
        public bool HasStoredPawn { get { return storage.Count > 0; } }
        public Pawn StoredPawn { get { return HasStoredPawn ? storage.pawns[0] : null; } }
        public bool TryStore(Pawn p) { return FormTransferUtility.TryStore(p,storage); }
        public Pawn ReleaseStoredPawn() { Pawn p=StoredPawn; if(p!=null) storage.Remove(p); return p; }
    }
    public static partial class TransformUtility
    {
        public static int orders; public static bool orderSuccess = true;
        public static HediffComp_Transformed TryGetTransformedComp(Pawn p) { return p != null ? p.comp : null; }
        public static bool IsTransformedAnimal(Pawn p) { return p != null && p.comp != null && p.comp.HasStoredPawn; }
        public static void EnsureTransformedAnimalControl(Pawn p, bool b) {}
        public static bool TryOrderMeleeAttack(Pawn p, Thing target) { orders++; return orderSuccess; }
    }
    public static class Patch_GameEnder { public static void InvalidateCache() {} }
    public class Settings { public float deathChance, spilloverDamageFactor; }
    public static class WildShiftMod { public static Settings Settings = new Settings(); }
}
namespace WildShift.Tests
{
    public static class StabilityTests
    {
        private static int assertions;
        private static void Check(bool b,string message) { assertions++; if(!b) throw new Exception(message); }
        private static Pawn Form(out Pawn human, Map map = null)
        {
            human=new Pawn(); Pawn animal=new Pawn { comp=new HediffComp_Transformed() };
            animal.comp.TryStore(human);
            if(map!=null) GenSpawn.Spawn(animal,new IntVec3(),map);
            return animal;
        }
        private static void Reset()
        {
            GenSpawn.FailBefore=null; GenSpawn.FailAfter=null; Find.WorldPawns=new WorldPawns();
            Find.Selector=new Selector(); TransformUtility.orderSuccess=true;
        }
        public static string Run()
        {
            Map map=new Map(); Pawn human,animal; ThingOwner owner;
            Reset(); human=new Pawn(); GenSpawn.Spawn(human,new IntVec3(),map); human.drafter.Drafted=true;
            owner=new ThingOwner { PreflightReject=true };
            Check(!FormTransferUtility.TryStore(human,owner) && human.Spawned && human.Drafted,"preflight preserves source");
            owner=new ThingOwner { ThrowBefore=p=>true };
            Check(!FormTransferUtility.TryStore(human,owner),"storage fault reported");
            Check(human.Spawned && human.Map==map && human.Drafted,"storage fault restores source and draft");
            owner=new ThingOwner { ThrowAfter=p=>true };
            Check(FormTransferUtility.TryStore(human,owner) && owner.Contains(human) && !human.Spawned,"post-commit fault must not duplicate");

            Reset(); animal=Form(out human,map);
            Check(TransformUtility.RevertToHuman(animal)==human,"map return succeeds");
            Check(human.Spawned && animal.Destroyed && !animal.comp.HasStoredPawn,"map transfer commits before destruction");
            Check(!FormTransferUtility.IsReturning(animal),"return guard released");
            Reset(); animal=Form(out human,map); GenSpawn.FailBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null,"failed map return reported");
            Check(animal.Spawned && !animal.Destroyed && animal.comp.StoredPawn==human && !human.Spawned,"failed spawn restores stored human");
            Check(!FormTransferUtility.IsReturning(animal),"failed return guard released");
            GenSpawn.FailBefore=null;
            Check(TransformUtility.RevertToHuman(animal)==human,"retry after rollback succeeds");
            Reset(); animal=Form(out human,map); GenSpawn.FailAfter=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==human && human.Spawned && human.holdingOwner==null,"post-spawn fault keeps committed human");

            Reset(); animal=Form(out human); owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false);
            Check(TransformUtility.RevertToHuman(animal)==human && owner.Contains(human) && animal.Destroyed,"container return succeeds");
            Reset(); animal=Form(out human); owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false); owner.ThrowBefore=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==null,"container exception reported");
            Check(owner.Contains(animal) && animal.comp.StoredPawn==human && !animal.Destroyed,"container rollback restores both bodies");
            Reset(); animal=Form(out human); owner=new ThingOwner(); owner.TryAddOrTransfer(animal,false); owner.ThrowAfter=p=>p==human;
            Check(TransformUtility.RevertToHuman(animal)==human && owner.Contains(human) && animal.Destroyed,"post-container fault accepts committed transfer");
            Reset(); animal=Form(out human); Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever);
            Check(TransformUtility.RevertToHuman(animal)==human && WorldPawnsUtility.IsWorldPawn(human) && animal.Destroyed,"world return succeeds");
            Reset(); animal=Form(out human); Find.WorldPawns.PassToWorld(animal,PawnDiscardDecideMode.KeepForever); Find.WorldPawns.Fail=true;
            Check(TransformUtility.RevertToHuman(animal)==null && animal.comp.StoredPawn==human && !animal.Destroyed,"world failure restores human");
            Reset(); human=new Pawn(); FormTransferUtility.PreserveDetachedPawn(human); owner=new ThingOwner();
            Check(FormTransferUtility.TryStore(human,owner) && !WorldPawnsUtility.IsWorldPawn(human),"recovered world pawn not double-owned on retry");
            Reset(); animal=Form(out human); FormTransferUtility.DestroyEmptyForm(animal);
            Check(!animal.Destroyed && animal.comp.StoredPawn==human,"refuse to delete occupied form");
            Check(FormTransferUtility.TryBeginReturn(animal) && !FormTransferUtility.TryBeginReturn(animal),"reentry rejected");
            Check(!Patch_TransformedDeath.Prefix(animal,null),"death does not interrupt return"); FormTransferUtility.EndReturn(animal);

            Reset(); animal=Form(out human,map); GenSpawn.FailBefore=p=>p==human;
            Check(!Patch_TransformedDeath.Prefix(animal,new DamageInfo(DamageDefOf.ExecutionCut,100)),"execution intercepted");
            Check(human.Dead && animal.Destroyed && WorldPawnsUtility.IsWorldPawn(human),"failed execution placement still kills human in emergency storage");
            Reset(); animal=Form(out human,map); GenSpawn.FailBefore=p=>p==human; Find.WorldPawns.Fail=true;
            Check(!Patch_TransformedDeath.Prefix(animal,null) && animal.comp.StoredPawn==human && !animal.Destroyed,"all recovery failures preserve occupied form");

            Reset(); animal=Form(out human,map); animal.drafter.Drafted=true;
            Find.Selector.SelectedPawns.Add(animal); Find.Selector.SelectedPawns.Add(human);
            Event.current=new Event { type=EventType.MouseDown,button=1 };
            Check(Patch_TransformedAnimalDirectRightClickAttack.Prefix() && Find.Selector.SelectedPawns.Count==2 && !Event.current.used,"mixed selection reaches vanilla unchanged");
            Find.Selector.SelectedPawns.Remove(human);
            GenUI.targets=new List<LocalTargetInfo> { new LocalTargetInfo { Thing=new Thing { Spawned=true,Map=map } } };
            Check(!Patch_TransformedAnimalDirectRightClickAttack.Prefix() && Event.current.used,"animal direct attack consumes click without menu");
            TransformUtility.orderSuccess=false; Event.current.used=false;
            Check(Patch_TransformedAnimalDirectRightClickAttack.Prefix() && !Event.current.used,"failed direct order retains vanilla fallback");

            Reset(); animal=Form(out human,map); map.mapPawns.pawns.Add(animal); Find.Maps=new List<Map> { map };
            var entries=new List<ColonistBar.Entry>(); var locs=new List<Vector2>(); var groups=new List<int>();
            var bar=new ColonistBar(); var finder=new ColonistBarDrawLocsFinder(); float scale=1; bool state;
            Patch_TransformedColonistBar.Prefix(false,out state);
            for(int i=0;i<1000;i++) Patch_TransformedColonistBar.Postfix(bar,state,entries,locs,groups,finder,ref scale);
            Check(map.mapPawns.reads==0 && entries.Count==0,"1000 clean cache reads perform zero pawn scans");
            Patch_TransformedColonistBar.Prefix(true,out state);
            Patch_TransformedColonistBar.Postfix(bar,state,entries,locs,groups,finder,ref scale);
            Check(map.mapPawns.reads==1 && entries.Count==1 && entries[0].pawn==animal && bar.drawer.calls==1,"dirty cache adds transformed portrait once");
            Patch_TransformedColonistBar.Postfix(bar,state,entries,locs,groups,finder,ref scale);
            Check(entries.Count==1 && bar.drawer.calls==1,"existing portrait not duplicated");
            return "PASS: "+assertions+" stability assertions (fault-injecting engine stubs; not an in-game test).";
        }
    }
}
