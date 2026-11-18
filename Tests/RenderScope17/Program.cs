using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;
using Verse;

internal static class Program
{
    private delegate void OldPrefix(out IDisposable state);
    private delegate void NewPrefix(out int state);
    private static Func<bool> isNative;
    private static int observations;
    private static int checks;
    private static int gateMode;
    private static Func<int> appearanceDepth;
    private static int appearanceMode;
    private static bool appearanceNested;
    private static System.Collections.IEnumerator appearanceTerminal;

    private static int Main(string[] args)
    {
        if (args.Length != 4) return 2;
        string game = args[0], harmonyDir = args[1], production = args[2], output = args[3];
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            foreach (string dir in new[] { game, harmonyDir, Path.GetDirectoryName(production) }) {
                string path = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try { Run(production, output); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string production, string output)
    {
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(production);
        Type gear = assembly.GetType("WildShift.SharedFormGear", true);
        Type patch = assembly.GetType("WildShift.Patch_SharedGearPhysicalRendering", true);
        MethodInfo prefix = patch.GetMethod("Prefix"), finalizer = patch.GetMethod("Finalizer");
        isNative = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), gear.GetProperty("IsNativeOnly").GetMethod);
        var nativeApi = (Func<IDisposable>)Delegate.CreateDelegate(typeof(Func<IDisposable>), gear.GetMethod("NativeOnly"));
        FieldInfo depth = gear.GetField("nativeOnly", BindingFlags.Static | BindingFlags.NonPublic);
        bool candidate = prefix.GetParameters()[0].ParameterType == typeof(int).MakeByRefType();
        Action roundTrip;
        Action nested;
        Action exceptional;
        if (candidate) {
            var begin = (NewPrefix)Delegate.CreateDelegate(typeof(NewPrefix), prefix);
            var end = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), finalizer);
            roundTrip = () => { int state; begin(out state); end(state); };
            nested = () => { int outer, inner; begin(out outer); Check(isNative(), "outer native scope active"); begin(out inner); Check((int)depth.GetValue(null) == 2, "nested depth is two"); end(inner); Check((int)depth.GetValue(null) == 1, "inner finalizer restores outer depth"); end(outer); };
            exceptional = () => { int state; begin(out state); try { throw new InvalidOperationException("fixture"); } finally { end(state); } };
        } else {
            var begin = (OldPrefix)Delegate.CreateDelegate(typeof(OldPrefix), prefix);
            var end = (Action<IDisposable>)Delegate.CreateDelegate(typeof(Action<IDisposable>), finalizer);
            roundTrip = () => { IDisposable state; begin(out state); end(state); };
            nested = () => { IDisposable outer, inner; begin(out outer); Check(isNative(), "outer native scope active"); begin(out inner); Check((int)depth.GetValue(null) == 2, "nested depth is two"); end(inner); Check((int)depth.GetValue(null) == 1, "inner finalizer restores outer depth"); end(outer); };
            exceptional = () => { IDisposable state; begin(out state); try { throw new InvalidOperationException("fixture"); } finally { end(state); } };
        }
        Check(!isNative() && (int)depth.GetValue(null) == 0, "initial scope clear");
        nested();
        Check(!isNative() && (int)depth.GetValue(null) == 0, "nested scope restored");
        try { exceptional(); } catch (InvalidOperationException) { Check(true, "fixture exception propagated"); }
        Check(!isNative(), "exception scope restored");
        using (nativeApi()) { roundTrip(); Check(isNative() && (int)depth.GetValue(null) == 1, "render inside public API preserves its outer scope"); }
        Check(!isNative(), "public scope restored");

        var harmony = new Harmony("wildshift.audit.render-scope");
        harmony.CreateClassProcessor(patch).Patch();
        MethodInfo target = AccessTools.Method(typeof(PawnRenderTree), "TrySetupGraphIfNeeded");
        Check(target != null, "actual native target found");
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(Program), nameof(ObserveNative)));
        var call = AccessTools.MethodDelegate<Action<PawnRenderTree>>(target);
        PawnRenderTree resolved = Bare<PawnRenderTree>();
        AccessTools.Field(typeof(PawnRenderTree), "rootNode").SetValue(resolved, Bare<PawnRenderNode>());
        call(resolved);
        Check(observations == 1, "actual native resolved-graph postfix observes native scope");
        Check(!isNative(), "actual native resolved-graph finalizer restores scope");
        try { call(Bare<PawnRenderTree>()); Check(false, "invalid uninitialized fixture should throw"); }
        catch (NullReferenceException) { Check(true, "actual native unresolved-graph fixture exception propagated"); }
        Check(!isNative() && (int)depth.GetValue(null) == 0, "actual native exception finalizer restores scope");
        // External prefixes run before the production prefix. Harmony still
        // runs the production finalizer with default state when it was skipped.
        var externalPrefix = new HarmonyMethod(typeof(Program), nameof(BeforeRender));
        externalPrefix.priority = Priority.First;
        harmony.Patch(target, prefix: externalPrefix,
            postfix: new HarmonyMethod(typeof(Program), nameof(AfterRender)));
        using (nativeApi()) {
            gateMode = 1;
            try { call(resolved); Check(false, "earlier prefix should throw"); }
            catch (InvalidOperationException e) { Check(e.Message == "before-render-fixture", "earlier prefix exception propagated"); }
            finally { gateMode = 0; }
            Check(isNative() && (int)depth.GetValue(null) == 1, "earlier prefix exception preserves outer public NativeOnly");
        }
        Check(!isNative() && (int)depth.GetValue(null) == 0, "outer scope restored after earlier-prefix exception");
        using (nativeApi()) {
            gateMode = 2;
            try { call(resolved); }
            finally { gateMode = 0; }
            Check(isNative() && (int)depth.GetValue(null) == 1, "earlier prefix false preserves outer public NativeOnly");
        }
        Check(!isNative() && (int)depth.GetValue(null) == 0, "outer scope restored after earlier-prefix false");
        using (nativeApi()) {
            gateMode = 3;
            try { call(resolved); Check(false, "external postfix should throw"); }
            catch (ApplicationException e) { Check(e.Message == "after-render-fixture", "postfix exception propagated"); }
            finally { gateMode = 0; }
            Check(isNative() && (int)depth.GetValue(null) == 1, "postfix exception restores outer public NativeOnly");
        }
        Check(!isNative() && (int)depth.GetValue(null) == 0, "outer scope restored after postfix exception");
        harmony.Unpatch(target, typeof(Program).GetMethod(nameof(ObserveNative), BindingFlags.NonPublic | BindingFlags.Static));

        // Regression-only executable: no allocation/timing workload while the
        // parent owns population/game measurement windows.
        Check(!isNative() && (int)depth.GetValue(null) == 0, "measured calls leave scope clear");
        int appearanceStart = checks;
        string[] appearanceTargets = CheckAppearanceScope(assembly);
        var result = new {
            Passed = true, Mode = candidate ? "candidate" : "baseline", Checks = checks,
            AppearanceChecks = checks - appearanceStart, AppearanceTargets = appearanceTargets,
            Production = production, ProductionSHA256 = Hash(production),
            NativeAssemblySHA256 = Hash(typeof(Pawn).Assembly.Location),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ActualTarget = target.DeclaringType.FullName + "." + target.Name,
            MeasurementsPerformed = false,
            Scope = "Actual compiled production prefix/finalizer and Harmony-patched native 1.7 resolved-render-tree and gene-appearance iterator methods in an isolated regression console. Covers earlier-prefix throw/false, native and postfix exceptions, nesting and public API interoperability. Appearance fixtures exercise the real completed-iterator branch and deliberately injected boundary faults only, not normal dynamic-node generation. No timing or allocation measurement; no game was started. Not initialized-game/1.6 Unity/rendered-gameplay validation."
        };
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string[] CheckAppearanceScope(Assembly assembly)
    {
        Type biology = assembly.GetType("WildShift.SharedFormBiology", true);
        Type patch = assembly.GetType("WildShift.Patch_SharedBiologicalAppearanceScope", true);
        FieldInfo depth = biology.GetField("nativeGenesOnly", BindingFlags.NonPublic | BindingFlags.Static);
        appearanceDepth = () => (int)depth.GetValue(null);
        var publicBegin = (Action)Delegate.CreateDelegate(typeof(Action), biology.GetMethod("BeginNativeAppearance"));
        var publicEnd = (Action)Delegate.CreateDelegate(typeof(Action), biology.GetMethod("EndNativeAppearance"));
        MethodInfo prefix = patch.GetMethod("Prefix"), finalizer = patch.GetMethod("Finalizer");
        Func<object> enter;
        Action<object> exit;
        if (prefix.GetParameters().Length == 0) {
            var begin = (Action)Delegate.CreateDelegate(typeof(Action), prefix);
            var end = (Func<Exception,Exception>)Delegate.CreateDelegate(typeof(Func<Exception,Exception>), finalizer);
            enter = () => { begin(); return null; };
            exit = state => end(null);
        } else {
            var begin = (NewPrefix)Delegate.CreateDelegate(typeof(NewPrefix), prefix);
            var end = (Func<int,Exception,Exception>)Delegate.CreateDelegate(typeof(Func<int,Exception,Exception>), finalizer);
            enter = () => { int state; begin(out state); return state; };
            exit = state => end((int)state, null);
        }
        Check(appearanceDepth() == 0, "appearance scope initially clear");
        publicBegin();
        try {
            object outer = enter(), inner = enter();
            Check(appearanceDepth() == 3, "appearance prefix nesting retains public outer scope");
            exit(inner); Check(appearanceDepth() == 2, "appearance inner finalizer restores enclosing prefix");
            exit(outer); Check(appearanceDepth() == 1, "appearance finalizer preserves public API outer scope");
        } finally { publicEnd(); }
        Check(appearanceDepth() == 0, "appearance public API scope restores after direct nesting");

        var targets = new List<MethodBase>((IEnumerable<MethodBase>)patch.GetMethod("TargetMethods").Invoke(null, null));
        Check(targets.Count > 0, "actual native gene appearance iterator target exists");
        var names = new List<string>();
        var harmony = new Harmony("wildshift.audit.appearance-scope");
        harmony.CreateClassProcessor(patch).Patch();
        foreach (MethodBase target in targets) {
            names.Add(target.DeclaringType.FullName + "." + target.Name);
            try {
                var first = new HarmonyMethod(typeof(Program), nameof(BeforeAppearance)) { priority = int.MaxValue };
                harmony.Patch(target, prefix:first, postfix:new HarmonyMethod(typeof(Program), nameof(AfterAppearance)));
                FieldInfo state = AccessTools.Field(target.DeclaringType, "<>1__state");
                Check(state != null, "appearance native iterator exposes its completed-state field");
                appearanceTerminal = (System.Collections.IEnumerator)RuntimeHelpers.GetUninitializedObject(target.DeclaringType);
                state.SetValue(appearanceTerminal, -1);
                publicBegin();
                try {
                    appearanceMode = 0;
                    Check(!appearanceTerminal.MoveNext(), "actual completed appearance iterator terminates natively");
                    Check(appearanceDepth() == 1, "native completed appearance iterator restores public scope");
                    appearanceMode = 1;
                    try { appearanceTerminal.MoveNext(); Check(false, "earlier appearance prefix should throw"); }
                    catch (InvalidOperationException e) { Check(e.Message == "before-appearance-fixture", "earlier appearance prefix exception propagates"); }
                    Check(appearanceDepth() == 1, "earlier appearance prefix exception preserves outer public scope");
                    appearanceMode = 2;
                    Check(!appearanceTerminal.MoveNext(), "earlier appearance prefix can skip native iterator");
                    Check(appearanceDepth() == 1, "earlier appearance prefix false preserves outer public scope");
                    appearanceMode = 3;
                    try { appearanceTerminal.MoveNext(); Check(false, "appearance postfix should throw"); }
                    catch (ApplicationException e) { Check(e.Message == "after-appearance-fixture", "appearance postfix exception propagates"); }
                    Check(appearanceDepth() == 1, "appearance postfix exception restores outer public scope");
                    appearanceMode = 4;
                    Check(!appearanceTerminal.MoveNext(), "actual appearance iterator remains usable after fault hooks");
                    Check(appearanceDepth() == 1, "nested actual appearance iterator restores outer public scope");
                } finally { appearanceMode = 0; appearanceNested = false; publicEnd(); }
                Check(appearanceDepth() == 0, "appearance scope fully restores after native target cases");
            } finally {
                appearanceMode = 0; appearanceNested = false; appearanceTerminal = null;
                harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            }
        }
        return names.ToArray();
    }

    private static bool BeforeAppearance(ref bool __result)
    {
        if (appearanceMode == 1) throw new InvalidOperationException("before-appearance-fixture");
        if (appearanceMode == 2) { __result = false; return false; }
        return true;
    }
    private static void AfterAppearance()
    {
        if (appearanceMode == 2) return;
        Check(appearanceDepth() == (appearanceNested ? 3 : 2), "native appearance target runs in entered scope");
        if (appearanceMode == 3) throw new ApplicationException("after-appearance-fixture");
        if (appearanceMode == 4 && !appearanceNested) {
            appearanceNested = true;
            try { Check(!appearanceTerminal.MoveNext(), "nested native appearance iterator terminates"); }
            finally { appearanceNested = false; }
            Check(appearanceDepth() == 2, "nested native appearance finalizer restores enclosing iterator");
        }
    }

    private static bool BeforeRender()
    {
        if (gateMode == 1) throw new InvalidOperationException("before-render-fixture");
        return gateMode != 2;
    }
    private static void AfterRender()
    {
        if (gateMode == 3) throw new ApplicationException("after-render-fixture");
    }
    private static void ObserveNative() { Check(isNative(), "native target runs within physical-gear scope"); observations++; }
    private static T Bare<T>() { return (T)RuntimeHelpers.GetUninitializedObject(typeof(T)); }
    private static string Hash(string path) { return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))); }
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
}
