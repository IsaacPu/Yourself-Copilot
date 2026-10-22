using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LocalDesktopNote
{
    // File-only recovery fixtures. No NoteContext, windows, registry or user files.
    internal static class LibraryRecoveryVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private static readonly string Root = Path.Combine(Storage.Folder, "fixtures");
        private static void Check(bool result, string description)
        {
            Report.AppendLine((result ? "PASS: " : "FAIL: ") + description);
            if (result) passed++; else failed++;
        }
        private static byte[] Serialize(LibraryState state)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(LibraryState)).WriteObject(stream, state);
                return stream.ToArray();
            }
        }
        private static LibraryState KnownGoodState()
        {
            var state = new LibraryState();
            state.Items.Add(new Preferences { Id = "known-good-image", Title = "原来的桌面图片", ImagePath = "images/original.png", X = 123, Y = 456, Width = 480, Positioned = true, Hidden = true, Locked = true });
            state.SelectedId = state.Items[0].Id;
            return state;
        }
        private static bool HasExactBytes(string folder, byte[] expected)
        {
            return Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Any(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && File.ReadAllBytes(path).SequenceEqual(expected));
        }
        private static void Run(string name, Action check)
        {
            Report.AppendLine("CASE: " + name);
            try { check(); }
            catch (Exception error) { failed++; Report.AppendLine("FAIL: fixture exception: " + error); }
        }
        private static void CorruptPrimaryGoodBackup()
        {
            string folder = Path.Combine(Root, "bad-primary-good-backup"); Directory.CreateDirectory(folder);
            string primary = Path.Combine(folder, "library.json");
            byte[] broken = new UTF8Encoding(false).GetBytes("{broken primary library bytes}\n");
            byte[] good = Serialize(KnownGoodState());
            File.WriteAllBytes(primary, broken); File.WriteAllBytes(primary + ".bak", good);
            var store = new LibraryStore(folder);
            Check(store.State.Items.Count == 1 && store.State.SelectedId == "known-good-image" && !String.IsNullOrEmpty(store.Notice),
                "corrupt primary loads the known-good backup with a recovery notice");
            store.Save();
            Check(File.ReadAllBytes(primary + ".bak").SequenceEqual(good),
                "startup save after recovery never replaces the only known-good backup with corrupt primary bytes");
            store.Save();
            Check(HasExactBytes(folder, broken), "corrupt primary raw bytes remain available after repeated successful saves");
            Check(new LibraryStore(folder).State.Items.Single().Id == "known-good-image", "recovered primary remains readable on the next restart");
        }
        private static void BothCorrupt()
        {
            string folder = Path.Combine(Root, "both-corrupt"); Directory.CreateDirectory(folder);
            string primary = Path.Combine(folder, "library.json");
            byte[] brokenPrimary = new UTF8Encoding(false).GetBytes("{unreadable primary}\n");
            byte[] brokenBackup = new UTF8Encoding(false).GetBytes("{unreadable backup, distinct bytes}\n");
            File.WriteAllBytes(primary, brokenPrimary); File.WriteAllBytes(primary + ".bak", brokenBackup);
            var store = new LibraryStore(folder);
            Check(store.State.Items.Count == 0 && !String.IsNullOrEmpty(store.Notice), "two unreadable files produce an empty recoverable state and an explicit notice");
            store.Save(); store.Save();
            Check(HasExactBytes(folder, brokenPrimary), "when both files are unreadable their original primary bytes are preserved after save");
            Check(HasExactBytes(folder, brokenBackup), "when both files are unreadable their original backup bytes are preserved after save");
        }
        private static void BackupOnly(bool withLegacySettings)
        {
            string folder = Path.Combine(Root, withLegacySettings ? "backup-only-with-legacy" : "backup-only-with-fallback"); Directory.CreateDirectory(folder);
            string primary = Path.Combine(folder, "library.json");
            byte[] good = Serialize(KnownGoodState()); File.WriteAllBytes(primary + ".bak", good);
            string image = Path.Combine(folder, "fallback.png");
            using (var bitmap = new Bitmap(2, 2)) bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
            string legacy = Path.Combine(folder, "settings.json");
            if (withLegacySettings) File.WriteAllBytes(legacy, SerializePreferences(new Preferences { ImagePath = image, X = 999, Y = 999, Width = 123, Positioned = true }));
            var store = new LibraryStore(folder);
            Check(store.State.Items.Count == 1 && store.State.SelectedId == "known-good-image" && !String.IsNullOrEmpty(store.Notice),
                "missing primary recovers the valid backup before " + (withLegacySettings ? "legacy settings" : "default image") + " migration is considered");
            bool migrated = store.Migrate(legacy, image);
            Check(!migrated && store.State.Items.Count == 1 && store.State.SelectedId == "known-good-image" && store.State.Items[0].X == 123,
                "backup recovery prevents " + (withLegacySettings ? "legacy settings" : "default image") + " from replacing or appending to the recovered library");
            store.Save();
            Check(File.ReadAllBytes(primary + ".bak").SequenceEqual(good) && new LibraryStore(folder).State.SelectedId == "known-good-image",
                "saving a backup-only recovery materializes the primary and retains the valid backup");
        }
        private static byte[] SerializePreferences(Preferences preferences)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, preferences);
                return stream.ToArray();
            }
        }
        private static int Main()
        {
            try
            {
                Run("bad primary with good backup", CorruptPrimaryGoodBackup);
                Run("both files corrupt", BothCorrupt);
                Run("backup only, fallback present", delegate { BackupOnly(false); });
                Run("backup only, legacy settings present", delegate { BackupOnly(true); });
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: pure isolated files only; no windows, NoteContext constructor, registry, desktop attach or production JSON.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(Path.Combine(Storage.Folder, "verification-library-recovery.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
