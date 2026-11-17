// TEST ONLY. Reflection keeps net472 compilation separate from Mono API support.
using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace WildShiftCombatPerformance
{
    internal static class Allocation16
    {
        private static readonly Func<long> current=CurrentReader();
        private static readonly Func<bool,long> total=TotalReader();
        public static bool CurrentSupported { get { return current!=null; } }
        public static bool TotalSupported { get { return total!=null; } }

        private static Func<long> CurrentReader()
        {
            MethodInfo method=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null);
            return method==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),method);
        }
        private static Func<bool,long> TotalReader()
        {
            MethodInfo method=typeof(GC).GetMethod("GetTotalAllocatedBytes",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(bool)},null);
            return method==null?null:(Func<bool,long>)Delegate.CreateDelegate(typeof(Func<bool,long>),method);
        }
        public static long Current()
        {
            if(current==null)throw new NotSupportedException("This native Mono lacks current-thread allocation counters; allocation numbers will not be fabricated.");
            return current();
        }
        public static long? Total() { return total==null?(long?)null:total(false); }
        public static void Validate()
        {
            if(VersionControl.CurrentMajor!=1||VersionControl.CurrentMinor!=6)
                throw new InvalidOperationException("Combat16 helper requires actual native 1.6, found "+VersionControl.CurrentVersionString);
            // The selected Unity 1.6 mscorlib exposes this real internal call.
            // If another runtime cannot execute it, fail explicitly instead
            // of reporting zeros as measured allocation counts.
            Current();
            Boot.Json("mono-allocation-support.json",new{GameVersion=VersionControl.CurrentVersionString,UnityVersion=Application.unityVersion,Runtime=Environment.Version.ToString(),CurrentThreadAllocatedBytesSupported=CurrentSupported,ProcessTotalAllocatedBytesSupported=TotalSupported,ProcessTotalUnsupportedMeaning="null, never substituted with process heap size or zeros"});
        }
    }
}
