using UnityEngine;
using Verse;

namespace WildShift
{
    [StaticConstructorOnStartup]
    public static class WildShiftTextures
    {
        public static readonly Texture2D Transform =
            ContentFinder<Texture2D>.Get("UI/Commands/WildShift_Transform");
    }
}
