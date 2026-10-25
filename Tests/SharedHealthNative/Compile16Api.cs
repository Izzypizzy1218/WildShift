using System;
using Verse;
using WildShift;

// Compile this consumer against the built 1.6 production DLL and its matching
// game reference assemblies. Production source is not compiled a second time.
internal static class Compile16Api
{
    internal static readonly Action<Pawn> Initialize = SharedFormHealth.Initialize;
    internal static readonly Action<Pawn, int> Advance = SharedFormHealth.TickInterval;
    internal static readonly Func<Pawn, bool> Lethal = SharedFormHealth.IsSharedLethalDeath;
    internal static readonly Func<Pawn, bool> Stored = AnimalFormStorage.IsStored;
    internal static readonly Type Projection = typeof(Hediff_SharedFormDrugEffect);
}
