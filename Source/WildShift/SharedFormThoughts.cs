using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WildShift
{
    // Personal thought queries see the person's relations/ideology, while
    // their Pawn argument remains the physical animal for map/bed/apparel.
    // Outside this short query the animal retains its own taming/bond data.
    public static class SharedFormThoughts
    {
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static List<SavedFrame> frameStack;
        [ThreadStatic] private static int frameCount;

        private struct SavedFrame
        {
            public Pawn first, second;
            public Pawn_RelationsTracker firstRelations, secondRelations;
            public Pawn_IdeoTracker firstIdeo, secondIdeo;
            public Pawn_StyleTracker firstStyle, secondStyle;
            public Pawn_RoyaltyTracker firstRoyalty, secondRoyalty;
            public Pawn_DrugPolicyTracker firstDrugs, secondDrugs;
            public Pawn_ConnectionsTracker firstConnections, secondConnections;
        }

        // Lightweight 4-byte token scope for high-frequency Harmony hooks so
        // ordinary pawns skip 120-byte struct zeroing and finalizer copying.
        internal static int Enter(Pawn a, Pawn b)
        {
            bool aMay = TransformUtility.MightBeTransformed(a);
            bool bMay = b != null && b != a && TransformUtility.MightBeTransformed(b);
            if (!aMay && !bMay) return 0;
            Pawn sourceA = aMay ? SharedFormIdentity.HumanFor(a) : null;
            Pawn sourceB = bMay ? SharedFormIdentity.HumanFor(b) : null;
            if (sourceA == null && sourceB == null) return 0;

            SavedFrame frame = default(SavedFrame);
            if (sourceA != null)
            {
                frame.first = a; frame.firstRelations = a.relations; frame.firstIdeo = a.ideo;
                frame.firstStyle = a.style; frame.firstRoyalty = a.royalty; frame.firstDrugs = a.drugs;
                frame.firstConnections = a.connections;
                a.relations = sourceA.relations; a.ideo = sourceA.ideo;
                a.style = sourceA.style; a.royalty = sourceA.royalty; a.drugs = sourceA.drugs;
                a.connections = sourceA.connections;
                Initialize(a, sourceA);
            }
            if (sourceB != null)
            {
                frame.second = b; frame.secondRelations = b.relations; frame.secondIdeo = b.ideo;
                frame.secondStyle = b.style; frame.secondRoyalty = b.royalty; frame.secondDrugs = b.drugs;
                frame.secondConnections = b.connections;
                b.relations = sourceB.relations; b.ideo = sourceB.ideo;
                b.style = sourceB.style; b.royalty = sourceB.royalty; b.drugs = sourceB.drugs;
                b.connections = sourceB.connections;
                Initialize(b, sourceB);
            }
            if (frameStack == null) frameStack = new List<SavedFrame>(4);
            if (frameCount < frameStack.Count) frameStack[frameCount] = frame;
            else frameStack.Add(frame);
            frameCount++;
            depth++;
            return frameCount;
        }

        internal static void Exit(int token)
        {
            if (token <= 0 || frameStack == null) return;
            while (frameCount >= token)
            {
                frameCount--;
                SavedFrame frame = frameStack[frameCount];
                frameStack[frameCount] = default(SavedFrame);
                if (frame.second != null)
                {
                    frame.second.relations = frame.secondRelations; frame.second.ideo = frame.secondIdeo;
                    frame.second.style = frame.secondStyle; frame.second.royalty = frame.secondRoyalty;
                    frame.second.drugs = frame.secondDrugs; frame.second.connections = frame.secondConnections;
                }
                if (frame.first != null)
                {
                    frame.first.relations = frame.firstRelations; frame.first.ideo = frame.firstIdeo;
                    frame.first.style = frame.firstStyle; frame.first.royalty = frame.firstRoyalty;
                    frame.first.drugs = frame.firstDrugs; frame.first.connections = frame.firstConnections;
                }
                depth--;
            }
        }

        public struct QueryScope : IDisposable
        {
            private Pawn first, second;
            private Pawn_RelationsTracker firstRelations, secondRelations;
            private Pawn_IdeoTracker firstIdeo, secondIdeo;
            private Pawn_StyleTracker firstStyle, secondStyle;
            private Pawn_RoyaltyTracker firstRoyalty, secondRoyalty;
            private Pawn_DrugPolicyTracker firstDrugs, secondDrugs;
            private Pawn_ConnectionsTracker firstConnections, secondConnections;
            private bool active;
            public QueryScope(Pawn a, Pawn b)
            {
                first=second=null;firstRelations=secondRelations=null;firstIdeo=secondIdeo=null;
                firstStyle=secondStyle=null;firstRoyalty=secondRoyalty=null;firstDrugs=secondDrugs=null;active=false;
                firstConnections=secondConnections=null;
                Pawn source=SharedFormIdentity.HumanFor(a);
                if(source!=null)
                {
                    first=a;firstRelations=a.relations;firstIdeo=a.ideo;
                    firstStyle=a.style;firstRoyalty=a.royalty;firstDrugs=a.drugs;
                    firstConnections=a.connections;
                    a.relations=source.relations;a.ideo=source.ideo;
                    a.style=source.style;a.royalty=source.royalty;a.drugs=source.drugs;
                    a.connections=source.connections;
                    Initialize(a,source);active=true;
                }
                source=SharedFormIdentity.HumanFor(b);
                if(source!=null&&b!=a)
                {
                    second=b;secondRelations=b.relations;secondIdeo=b.ideo;
                    secondStyle=b.style;secondRoyalty=b.royalty;secondDrugs=b.drugs;
                    secondConnections=b.connections;
                    b.relations=source.relations;b.ideo=source.ideo;
                    b.style=source.style;b.royalty=source.royalty;b.drugs=source.drugs;
                    b.connections=source.connections;
                    Initialize(b,source);active=true;
                }
                if(active)depth++;
            }
            public void Dispose()
            {
                if(!active)return;
                if(second!=null){second.relations=secondRelations;second.ideo=secondIdeo;second.style=secondStyle;second.royalty=secondRoyalty;second.drugs=secondDrugs;second.connections=secondConnections;}
                if(first!=null){first.relations=firstRelations;first.ideo=firstIdeo;first.style=firstStyle;first.royalty=firstRoyalty;first.drugs=firstDrugs;first.connections=firstConnections;}
                depth--;active=false;
            }
        }
        public static bool InPersonalQuery { get { return depth>0; } }
        public static void Initialize(Pawn body)
        {
            if(body==null||(body.styleObserver!=null&&body.surroundings!=null))return;
            Initialize(body,SharedFormIdentity.HumanFor(body));
        }
        private static void Initialize(Pawn body,Pawn human)
        {
            if(body==null||human==null)return;
            if(body.styleObserver==null&&human.Ideo!=null)
                body.styleObserver=new Pawn_StyleObserverTracker(body);
            if(body.surroundings==null&&human.surroundings!=null)
                body.surroundings=new Pawn_SurroundingsTracker(body);
        }
        public static bool IsPersonalColonist(Pawn pawn)
        { return (SharedFormIdentity.HumanFor(pawn)??pawn)?.IsColonist==true; }
    }

    [HarmonyPatch(typeof(ThoughtWorker),"CurrentState")]
    public static class Patch_SharedPersonalThought
    {
        public static void Prefix(Pawn __0,out int __state)
        {__state=SharedFormThoughts.Enter(__0,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(ThoughtWorker),"CurrentSocialState")]
    public static class Patch_SharedPersonalSocialThought
    {
        public static void Prefix(Pawn __0,Pawn __1,out int __state)
        {__state=SharedFormThoughts.Enter(__0,__1);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(ThoughtHandler),"TotalMoodOffset")]
    public static class Patch_SharedPersonalMoodCalculation
    {
        public static void Prefix(Pawn ___pawn,out int __state)
        {__state=SharedFormThoughts.Enter(___pawn,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch]
    public static class Patch_SharedSituationalThoughtCreation
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SituationalThoughtHandler),"TryCreateThought");
            yield return AccessTools.Method(typeof(SituationalThoughtHandler),"UpdateAllMoodThoughts");
        }
        public static void Prefix(Pawn ___pawn,out int __state)
        {__state=SharedFormThoughts.Enter(___pawn,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(SituationalThoughtHandler),"TryCreateSocialThought")]
    public static class Patch_SharedSituationalSocialCreation
    {
        public static void Prefix(Pawn ___pawn,Pawn otherPawn,out int __state)
        {__state=SharedFormThoughts.Enter(___pawn,otherPawn);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch]
    public static class Patch_SharedPersonalThoughtText
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(Type type in typeof(ThoughtWorker).Assembly.GetTypes())
                if(typeof(ThoughtWorker).IsAssignableFrom(type))
                    foreach(MethodInfo method in AccessTools.GetDeclaredMethods(type))
                        if(!method.IsStatic&&!method.IsAbstract
                            &&(method.Name=="PostProcessLabel"||method.Name=="PostProcessDescription")
                            &&method.GetParameters().Length==2&&method.GetParameters()[0].ParameterType==typeof(Pawn)
                            &&method.GetParameters()[1].ParameterType==typeof(string))yield return method;
        }
        public static void Prefix(Pawn __0,out int __state)
        {__state=SharedFormThoughts.Enter(__0,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(Pawn_StyleObserverTracker),"StyleObserverTickInterval")]
    public static class Patch_SharedPersonalStyleObservation
    {
        public static void Prefix(Pawn ___pawn,out int __state)
        {__state=SharedFormThoughts.Enter(___pawn,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(Pawn_SurroundingsTracker),"SurroundingsTrackerTickInterval")]
    public static class Patch_SharedPersonalSurroundings
    {
        public static void Prefix(Pawn ___pawn,out int __state)
        {__state=SharedFormThoughts.Enter(___pawn,null);}
        public static Exception Finalizer(int __state,Exception __exception)
        {if(__state!=0)SharedFormThoughts.Exit(__state);return __exception;}
    }
    [HarmonyPatch(typeof(Pawn_RelationsTracker),"DirectRelationExists",new[]{typeof(PawnRelationDef),typeof(Pawn)})]
    public static class Patch_SharedPersonalRelationshipIdentity
    {
        public static void Prefix(ref Pawn __1)
        {if(SharedFormThoughts.InPersonalQuery)__1=SharedFormIdentity.HumanFor(__1)??__1;}
    }
    [HarmonyPatch]
    public static class Patch_SharedPersonalRelationWorker
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(Type type in typeof(PawnRelationWorker).Assembly.GetTypes())
                if(typeof(PawnRelationWorker).IsAssignableFrom(type))
                    foreach(MethodInfo method in AccessTools.GetDeclaredMethods(type))
                        if(!method.IsStatic&&!method.IsAbstract&&method.Name=="InRelation"&&method.GetParameters().Length==2
                            &&method.GetParameters()[0].ParameterType==typeof(Pawn)&&method.GetParameters()[1].ParameterType==typeof(Pawn))
                            yield return method;
        }
        public static void Prefix(ref Pawn __0,ref Pawn __1)
        {
            if(!SharedFormThoughts.InPersonalQuery)return;
            __0=SharedFormIdentity.HumanFor(__0)??__0;
            __1=SharedFormIdentity.HumanFor(__1)??__1;
        }
    }
    [HarmonyPatch(typeof(ThoughtUtility),"CanGetThought")]
    public static class Patch_SharedPersonalThoughtEligibility
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter=AccessTools.PropertyGetter(typeof(Pawn),"IsColonist");
            MethodInfo personal=AccessTools.Method(typeof(SharedFormThoughts),"IsPersonalColonist");
            foreach(CodeInstruction instruction in instructions)
            {
                if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=personal;}
                yield return instruction;
            }
        }
    }
}
