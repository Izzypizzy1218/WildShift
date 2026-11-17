using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security;

internal static class Program
{
    private static int checks, initialized;
    private static MethodInfo find, findInAssembly;

    private sealed class LazyFixture
    {
        static LazyFixture() { initialized++; }
    }

    private sealed class FaultingAssembly : Assembly
    {
        public Exception failure;
        public bool enumerated;
        public override Type GetType(string name, bool throwOnError, bool ignoreCase) { throw failure; }
        public override Type[] GetTypes() { enumerated=true; throw new InvalidOperationException("Unexpected full type enumeration"); }
    }

    private static int Main(string[] args)
    {
        if(args.Length!=3)return 2;
        string production=Path.GetFullPath(args[0]),game=Path.GetFullPath(args[1]),harmony=Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving+=(context,name)=>
        {
            foreach(string dir in new[]{Path.GetDirectoryName(production),game,harmony})
            {
                string path=Path.Combine(dir,name.Name+".dll");
                if(File.Exists(path))return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            Assembly mod=AssemblyLoadContext.Default.LoadFromAssemblyPath(production);
            Type resolver=mod.GetType("WildShift.OptionalModTypes",true);
            find=resolver.GetMethod("FindExact",BindingFlags.Static|BindingFlags.Public);
            findInAssembly=resolver.GetMethod("FindInAssembly",BindingFlags.Static|BindingFlags.NonPublic);
            Check(find!=null&&findInAssembly!=null,"production assembly exposes the exact loaded-assembly resolver");
            Check(Find(null)==null&&Find("")==null,"null and empty optional names return no type");
            Check(Find("System.String")==typeof(string),"fully qualified core type resolves by exact identity");
            Check(Find("String")==null,"short-name fallback does not enumerate or select another namespace");
            Check(Find("system.string")==null,"fully qualified lookup remains case sensitive");
            Type lazy=typeof(LazyFixture);
            Check(Find(lazy.FullName)==lazy&&initialized==0,"nested fully qualified lookup does not execute the target static initializer");

            Assembly engine=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Assembly-CSharp.dll"));
            Type pawn=engine.GetType("Verse.Pawn",true);
            Check(Find("Verse.Pawn")==pawn,"actual native game Pawn resolves from its loaded assembly");
            Check(Find("RimWorld.StatWorker")==engine.GetType("RimWorld.StatWorker",true),"actual native StatWorker resolves by full namespace");

            string late="WildShift.OptionalFixture.LateLoaded"+Guid.NewGuid().ToString("N");
            Check(Find(late)==null,"missing optional assembly returns no type before load");
            AssemblyBuilder optional=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("WildShift.OptionalFixture."+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.Run);
            ModuleBuilder module=optional.DefineDynamicModule("Optional");
            Type lateType=module.DefineType(late,TypeAttributes.Public).CreateTypeInfo().AsType();
            Check(Find(late)==lateType,"a previous miss does not hide a later loaded optional type");
            Type decoy=module.DefineType("WildShift.OptionalFixture.Decoy.HumanlikeAnimals",TypeAttributes.Public).CreateTypeInfo().AsType();
            Check(Find(decoy.FullName)==decoy&&Find("BigAndSmall.HumanlikeAnimals")==null,
                "an unrelated same-short-name type cannot activate B&S compatibility");
            int loads=0;
            AssemblyLoadEventHandler observed=(sender,eventArgs)=>loads++;
            AppDomain.CurrentDomain.AssemblyLoad+=observed;
            try { Check(Find("WildShift.OptionalFixture.NeverInstalledType")==null&&loads==0,"missing lookup does not load an optional assembly"); }
            finally { AppDomain.CurrentDomain.AssemblyLoad-=observed; }

            foreach(Exception failure in new Exception[]{new TypeLoadException(),new ReflectionTypeLoadException(new Type[0],new Exception[0]),
                new FileNotFoundException(),new FileLoadException(),new BadImageFormatException(),new NotSupportedException(),new SecurityException(),new ArgumentException()})
            {
                var faulty=new FaultingAssembly{failure=failure};
                Check(findInAssembly.Invoke(null,new object[]{faulty,"WildShift.OptionalFixture.Missing"})==null&&!faulty.enumerated,
                    "unavailable assembly reflection is skipped without GetTypes: "+failure.GetType().Name);
            }
            var unexpected=new FaultingAssembly{failure=new InvalidOperationException("unexpected")};
            bool propagated=false;
            try { findInAssembly.Invoke(null,new object[]{unexpected,"WildShift.OptionalFixture.Missing"}); }
            catch(TargetInvocationException error){propagated=error.InnerException==unexpected.failure;}
            Check(propagated,"unexpected failures are not swallowed as optional absence");

            MeasureInitializer(mod,"WildShift.SapientAnimalsCompatibility");
            MeasureInitializer(mod,"WildShift.GeneticsCompatibility");
            Console.WriteLine("PASS: "+checks+" production-linked exact optional-type checks.");
            Console.WriteLine("Scope: actual production resolver, native game type identities, synthetic late-loaded optional assembly and reflection failures; no game launch or installed B&S/VGE gameplay validation.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static Type Find(string fullName) { return (Type)find.Invoke(null,new object[]{fullName}); }
    private static void MeasureInitializer(Assembly mod,string name)
    {
        Type type=mod.GetType(name,true);
        long before=GC.GetAllocatedBytesForCurrentThread();
        var timer=Stopwatch.StartNew();
        RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        timer.Stop();
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine("Cold optional initializer: "+name+" milliseconds="+timer.Elapsed.TotalMilliseconds.ToString("F4")+" allocatedBytes="+allocated);
        Check(true,"production optional initializer completes when B&S/VGE assemblies are absent: "+name);
    }
    private static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);checks++;Console.WriteLine("PASS: "+message);}
}
