param([string]$HarmonyAssembly='C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll')
$ErrorActionPreference='Stop'
# Run under Windows PowerShell: real Harmony patching of a modeled VEF callback,
# not the VEF assembly or a Unity game session. Exercises private ref injection.
$null=[Reflection.Assembly]::LoadFrom($HarmonyAssembly)
$source=@'
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;
namespace Verse {
    public class ThingComp { public object parent; public virtual void CompTick() {} }
    public class Pawn { public bool Spawned=true, Transformed; public int gender=1; }
    public static class Log { public static void Warning(string s) { throw new Exception(s); } }
}
namespace WildShift {
    public static class TransformUtility {
        public static int reads;
        public static bool IsTransformedAnimal(Pawn p) { reads++; return p!=null && p.Transformed; }
    }
}
namespace VEF.AnimalBehaviours {
    public class CompFixedGender : ThingComp {
        private bool changeGenderOnce=true;
        public int nativeCalls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void CompTick() {
            base.CompTick(); nativeCalls++;
            if(changeGenderOnce && ((Pawn)parent).Spawned) { ((Pawn)parent).gender=2; changeGenderOnce=false; }
        }
    }
}
namespace WildShift.Tests {
    public static class OptionalHarmonyTests {
        public static string Run() {
            var harmony=new Harmony("wildshift.test.optional.gender");
            try {
                AnimalModCompatibility.Install(harmony);
                var p=new Pawn { Transformed=true };
                var comp=new VEF.AnimalBehaviours.CompFixedGender {parent=p};
                for(int i=0;i<10000;i++) comp.CompTick();
                if(p.gender!=1 || comp.nativeCalls!=10000 || TransformUtility.reads!=1) throw new Exception("real Harmony ref-field handling failed");
                var ordinary=new Pawn(); var normal=new VEF.AnimalBehaviours.CompFixedGender {parent=ordinary}; normal.CompTick();
                if(ordinary.gender!=2 || normal.nativeCalls!=1) throw new Exception("ordinary gender rewriting was suppressed");
                return "PASS: real Harmony private-field injection; native callbacks retained; transformed gender preserved; 10000 callbacks use 1 state lookup; ordinary animals unchanged (modeled VEF/engine).";
            } finally { harmony.UnpatchAll(harmony.Id); }
        }
    }
}
'@
$root=Split-Path $PSScriptRoot -Parent
$source += "`n" + ((Get-Content "$root/Source/WildShift/AnimalModCompatibility.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n','')
Add-Type -TypeDefinition $source -ReferencedAssemblies $HarmonyAssembly
[WildShift.Tests.OptionalHarmonyTests]::Run()
