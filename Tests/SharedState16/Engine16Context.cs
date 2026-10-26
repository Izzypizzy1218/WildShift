using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WildShift;

namespace WildShiftSharedStateValidation
{
    // TEST ONLY. Validate what Unity actually loaded, independently of the
    // runner's Version.txt check and the fixture's compile-time references.
    public static class Engine16Context
    {
        public static string GameVersion { get { return VersionControl.CurrentVersionString; } }
        public static string GameAssemblyPath { get { return typeof(Pawn).Assembly.Location; } }
        public static string GameAssemblySHA256 { get { return Hash(GameAssemblyPath); } }
        public static string HarmonySHA256 { get { return Hash(typeof(Harmony).Assembly.Location); } }

        public static void Validate()
        {
            if (VersionControl.CurrentMajor != 1 || VersionControl.CurrentMinor != 6)
                throw new InvalidOperationException("The shared-state 1.6 helper requires a real 1.6 game, found " + GameVersion);
            if (!Application.isBatchMode)
                throw new InvalidOperationException("The isolated 1.6 runner must launch Unity in batch mode.");
            string profile = GenFilePaths.SaveDataFolderPath;
            var input = new XmlDocument();
            input.Load(Path.Combine(profile, "sharedstate16-inputs.xml"));
            AssertText(input, "GameVersion", GameVersion);
            AssertPath(input, "SaveDataPath", profile);
            AssertPath(input, "GameAssemblyPath", GameAssemblyPath);
            AssertText(input, "GameAssemblySHA256", GameAssemblySHA256);
            AssertPath(input, "WildShiftPath", typeof(TransformUtility).Assembly.Location);
            AssertText(input, "WildShiftSHA256", Hash(typeof(TransformUtility).Assembly.Location));
            AssertPath(input, "FixturePath", typeof(Engine16Context).Assembly.Location);
            AssertText(input, "FixtureSHA256", Hash(typeof(Engine16Context).Assembly.Location));
            AssertText(input, "HarmonySHA256", HarmonySHA256);
        }

        private static string Expected(XmlDocument input, string name)
        {
            XmlNode node = input.SelectSingleNode("/SharedState16Inputs/" + name);
            if (node == null || String.IsNullOrEmpty(node.InnerText))
                throw new InvalidOperationException("Missing isolated 1.6 input " + name);
            return node.InnerText;
        }
        private static void AssertPath(XmlDocument input, string name, string actual)
        {
            string expected = Expected(input, name);
            if (!String.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("In-process 1.6 input path mismatch: " + name);
        }
        private static void AssertText(XmlDocument input, string name, string actual)
        {
            if (!String.Equals(Expected(input, name), actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("In-process 1.6 input mismatch: " + name);
        }
        private static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
