using System;
using System.Globalization;
using System.Text.Json;
using Shim = WildShiftSharedStateValidation.Json.JsonSerializer;
using Options = WildShiftSharedStateValidation.Json.JsonSerializerOptions;

internal static class JsonShimTests
{
    private static int Main()
    {
        CultureInfo.CurrentCulture = new CultureInfo("ko-KR");
        string path = "C:\\Users\\집\\\"fixture\"\r\n\t\b\f\u0001😀";
        string json = Shim.Serialize(new { Passed = true, Failed = false, Phase = "complete", Path = path,
            Results = new[] { "first", "second" }, Empty = new string[0], Missing = (string)null,
            Count = 731, Fraction = 1.25f, Nested = new { Value = -2.5m } }, new Options { WriteIndented = true });
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            JsonElement root = document.RootElement;
            if (!root.GetProperty("Passed").GetBoolean() || root.GetProperty("Failed").GetBoolean()
                || root.GetProperty("Path").GetString() != path || root.GetProperty("Count").GetInt32() != 731
                || root.GetProperty("Fraction").GetSingle() != 1.25f
                || root.GetProperty("Nested").GetProperty("Value").GetDecimal() != -2.5m
                || root.GetProperty("Results")[1].GetString() != "second"
                || root.GetProperty("Empty").GetArrayLength() != 0 || root.GetProperty("Missing").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Fixture JSON round-trip mismatch.");
        }
        bool rejected = false;
        try { Shim.Serialize(new { Invalid = Double.NaN }, null); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Fixture JSON accepted a non-finite number.");
        Console.WriteLine("PASS: fixture-only net472-compatible JSON serializer round-trips result shapes, Korean paths, escapes, Unicode, numbers, null and arrays; rejects NaN.");
        return 0;
    }
}
