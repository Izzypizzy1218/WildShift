using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Steamworks;

internal static class Program
{
    private const uint AppId = 294100;
    private const ulong ItemId = 3796025407;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    private sealed class Input
    {
        public string Command, Language, Title, Description, Note, Output, ExpectedTitle;
    }

    private sealed class Snapshot
    {
        public string Item { get; set; }
        public string Title { get; set; }
        public bool TitlePresent { get; set; }
        public string ExpectedTitle { get; set; }
        public bool? TitleMatchesExpected { get; set; }
        public bool OwnerMatches { get; set; }
        public uint Updated { get; set; }
        public string Description { get; set; }
        public int DescriptionCharacters { get; set; }
        public int DescriptionUtf8Bytes { get; set; }
        public string DescriptionSHA256 { get; set; }
        public string Language { get; set; }
        public bool Cached { get; set; }
        public string SteamUILanguage { get; set; }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "--help") { Usage(); return 0; }
            // Validate every argument and local input before initializing Steam.
            // A missing title must never reach StartItemUpdate.
            Input input = Parse(args);
            string game = Environment.GetEnvironmentVariable("RIMWORLD_DIR");
            if (string.IsNullOrWhiteSpace(game))
                game = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "RimWorld");
            game = Path.GetFullPath(game);
            string api = Path.Combine(game, "steam_api64.dll");
            if (!File.Exists(api)) throw new FileNotFoundException("Set RIMWORLD_DIR to the installed RimWorld directory.", api);
            NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (name, assembly, path) =>
                name.Contains("steam_api") ? NativeLibrary.Load(api) : IntPtr.Zero);
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            if (!SteamAPI.Init()) throw new InvalidOperationException("Steam API initialization failed.");
            try { return Execute(input); }
            finally { SteamAPI.Shutdown(); }
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static Input Parse(string[] args)
    {
        var input = new Input { Command = args[0] };
        if (input.Command == "inspect" && (args.Length == 3 || args.Length == 4))
        {
            input.Language = args[1]; input.Output = args[2];
            if (args.Length == 4) { ValidateText(args[3], "expected title", Constants.k_cchPublishedDocumentTitleMax); input.ExpectedTitle = args[3].Trim(); }
        }
        else if (input.Command == "update" && args.Length == 6)
        {
            input.Language = args[1];
            ValidateText(args[2], "title", Constants.k_cchPublishedDocumentTitleMax);
            input.Title = args[2].Trim();
            input.Description = Normalize(File.ReadAllText(args[3]).Trim());
            ValidateText(input.Description, "description", Constants.k_cchPublishedDocumentDescriptionMax);
            input.Note = args[4] == "-" ? "" : File.ReadAllText(args[4]).Trim();
            if (args[4] != "-") ValidateText(input.Note, "change note", int.MaxValue);
            input.Output = args[5];
        }
        else throw new ArgumentException("Use --help. Language and an explicit nonblank title are mandatory for update.");
        if (input.Language != "english" && input.Language != "koreana")
            throw new ArgumentException("Language must explicitly be english or koreana.");
        if (string.IsNullOrWhiteSpace(input.Output)) throw new ArgumentException("JSON output path is required.");
        input.Output = Path.GetFullPath(input.Output);
        if (File.Exists(input.Output)) throw new IOException("Preserve existing evidence; choose a new JSON output path: " + input.Output);
        if (!Directory.Exists(Path.GetDirectoryName(input.Output))) throw new DirectoryNotFoundException("Create the JSON output directory before running the CLI.");
        return input;
    }

    private static void ValidateText(string value, string name, int bufferSize)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            throw new ArgumentException(name + " must be nonblank and contain no NUL characters.");
        if (Encoding.UTF8.GetByteCount(value) >= bufferSize)
            throw new ArgumentException(name + " exceeds the Steam UTF-8 buffer limit (including its terminator): " + bufferSize);
    }

    private static int Execute(Input input)
    {
        if (SteamUtils.GetAppID().m_AppId != AppId) throw new InvalidOperationException("Unexpected Steam app.");
        Snapshot before = Inspect(input.Language, input.Command == "inspect" ? input.ExpectedTitle : null);
        if (input.Command == "inspect")
        {
            WriteNew(input.Output, before);
            Console.WriteLine("Inspected " + before.Item + " language=" + before.Language + "; title=" + JsonSerializer.Serialize(before.Title)
                + "; titlePresent=" + before.TitlePresent + "; descriptionUtf8Bytes=" + before.DescriptionUtf8Bytes + "; cached=" + before.Cached);
            return before.TitleMatchesExpected == false ? 2 : 0;
        }

        UGCUpdateHandle_t update = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(ItemId));
        // Steam localization is a title/description pair. Always set both;
        // a description-only localized update previously left koreana blank.
        if (!SteamUGC.SetItemUpdateLanguage(update, input.Language)) throw new InvalidOperationException("Language update refused.");
        if (!SteamUGC.SetItemTitle(update, input.Title)) throw new InvalidOperationException("Title update refused.");
        if (!SteamUGC.SetItemDescription(update, input.Description)) throw new InvalidOperationException("Description update refused.");
        bool done = false, ioFailure = false;
        SubmitItemUpdateResult_t submitted = default;
        using (var call = CallResult<SubmitItemUpdateResult_t>.Create((value, io) => { submitted = value; ioFailure = io; done = true; }))
        {
            call.Set(SteamUGC.SubmitItemUpdate(update, input.Note));
            Pump(() => done, 90);
        }
        bool accepted = !ioFailure && submitted.m_eResult == EResult.k_EResultOK && !submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement;
        Snapshot after = null;
        string verificationError = null;
        if (accepted)
        {
            try
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    after = Inspect(input.Language, input.Title);
                    if (after.TitleMatchesExpected == true && Normalize(after.Description) == input.Description) break;
                    if (attempt < 4) Thread.Sleep(1000);
                }
            }
            catch (Exception error) { verificationError = error.Message; }
        }
        bool verified = after != null && after.TitleMatchesExpected == true && Normalize(after.Description) == input.Description;
        WriteNew(input.Output, new { Passed = accepted && verified, Submitted = accepted, Result = submitted.m_eResult.ToString(),
            IOFailure = ioFailure, LegalAgreementRequired = submitted.m_bUserNeedsToAcceptWorkshopLegalAgreement,
            ContentChanged = false, input.Language, input.Title, DescriptionUtf8Bytes = Encoding.UTF8.GetByteCount(input.Description),
            DescriptionSHA256 = Hash(input.Description), Before = before, After = after, Verified = verified, VerificationError = verificationError });
        if (!accepted || !verified) throw new InvalidOperationException("Metadata update or uncached title/description verification failed; inspect the saved result. An accepted submission may already be public.");
        Console.WriteLine("Verified title and full description for " + input.Language + "; runtime content unchanged.");
        return 0;
    }

    private static Snapshot Inspect(string language, string expectedTitle)
    {
        UGCQueryHandle_t query = SteamUGC.CreateQueryUGCDetailsRequest(new[] { new PublishedFileId_t(ItemId) }, 1);
        try
        {
            if (!SteamUGC.SetLanguage(query, language) || !SteamUGC.SetAllowCachedResponse(query, 0)
                || !SteamUGC.SetReturnLongDescription(query, true)) throw new InvalidOperationException("Uncached language-specific long-description query unavailable.");
            bool done = false, ioFailure = false;
            SteamUGCQueryCompleted_t reply = default;
            using (var call = CallResult<SteamUGCQueryCompleted_t>.Create((value, io) => { reply = value; ioFailure = io; done = true; }))
            {
                call.Set(SteamUGC.SendQueryUGCRequest(query));
                Pump(() => done, 30);
            }
            if (ioFailure || reply.m_eResult != EResult.k_EResultOK || reply.m_unNumResultsReturned != 1
                || !SteamUGC.GetQueryUGCResult(query, 0, out SteamUGCDetails_t details)) throw new InvalidOperationException("Workshop details query failed.");
            bool owns = details.m_ulSteamIDOwner == SteamUser.GetSteamID().m_SteamID;
            if (details.m_nPublishedFileId.m_PublishedFileId != ItemId || details.m_nConsumerAppID.m_AppId != AppId
                || details.m_eResult != EResult.k_EResultOK || !owns || reply.m_bCachedData) throw new InvalidOperationException("Wrong item, owner, app, result or cached response.");
            string description = details.m_rgchDescription ?? "";
            return new Snapshot { Item = ItemId.ToString(), Title = details.m_rgchTitle,
                TitlePresent = !string.IsNullOrWhiteSpace(details.m_rgchTitle), ExpectedTitle = expectedTitle,
                TitleMatchesExpected = expectedTitle == null ? null : string.Equals(details.m_rgchTitle, expectedTitle, StringComparison.Ordinal),
                OwnerMatches = owns, Updated = details.m_rtimeUpdated, Description = description,
                DescriptionCharacters = description.Length, DescriptionUtf8Bytes = Encoding.UTF8.GetByteCount(description),
                DescriptionSHA256 = Hash(Normalize(description)), Language = language, Cached = reply.m_bCachedData, SteamUILanguage = SteamUtils.GetSteamUILanguage() };
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    private static string Normalize(string value) { return value.Replace("\r\n", "\n").Replace('\r', '\n'); }
    private static string Hash(string value) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))); }
    private static void WriteNew(string path, object value)
    {
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }
    private static void Pump(Func<bool> done, int seconds)
    {
        var timer = Stopwatch.StartNew();
        while (!done()) { SteamAPI.RunCallbacks(); if (timer.Elapsed.TotalSeconds > seconds) throw new TimeoutException("Steam callback timed out."); Thread.Sleep(20); }
    }
    private static void Usage()
    {
        Console.WriteLine("WildShift item " + ItemId + ", app " + AppId);
        Console.WriteLine("inspect LANGUAGE JSON_OUTPUT [EXPECTED_TITLE]");
        Console.WriteLine("update LANGUAGE TITLE DESCRIPTION_FILE NOTE_FILE_OR_- JSON_OUTPUT");
        Console.WriteLine("LANGUAGE must be english or koreana. Existing JSON evidence is never overwritten.");
        Console.WriteLine("RIMWORLD_DIR overrides the installed native Steam API directory. Run using dotnet WorkshopMetadata.dll.");
    }
}
