using System;
using System.IO;
using System.Reflection;
using System.Security;

namespace WildShift
{
    internal static class OptionalModTypes
    {
        // Callers already know the full namespace. A missing optional mod must
        // not fall back to enumerating every type in every loaded assembly.
        // Do not cache misses: a later call may follow an optional assembly load.
        public static Type FindExact(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = FindInAssembly(assemblies[i], fullName);
                if (found != null) return found;
            }
            return null;
        }

        private static Type FindInAssembly(Assembly assembly, string fullName)
        {
            try { return assembly.GetType(fullName, false, false); }
            catch (Exception error) when (error is TypeLoadException || error is ReflectionTypeLoadException
                || error is FileNotFoundException || error is FileLoadException || error is BadImageFormatException
                || error is NotSupportedException || error is SecurityException || error is ArgumentException)
            {
                // Optional/dynamic assemblies can have unavailable dependencies
                // or incomplete reflection support. Continue to the next one.
                return null;
            }
        }
    }
}
