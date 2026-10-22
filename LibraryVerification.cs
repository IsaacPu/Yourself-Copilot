using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class LibraryVerification
    {
        private static void Check(bool condition, string text, StringBuilder output)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + text);
            output.AppendLine("PASS: " + text);
        }
        internal static int Run()
        {
            var report = new StringBuilder();
            try
            {
                string root = Path.Combine(Storage.Folder, "library-check-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                string source = Path.Combine(root, "source.png");
                File.Copy(Path.Combine(Storage.Folder, "计划表.png"), source);
                var store = new LibraryStore(root);
                var first = store.Import(source);
                Check(File.Exists(store.Resolve(first)) && !Path.IsPathRooted(first.ImagePath), "import creates portable local copy", report);
                Check(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(store.Resolve(first))), "original image bytes preserved", report);
                string moved = Path.Combine(root, "moved-source.png"); File.Move(source, moved);
                Check(LibraryStore.ValidateImage(store.Resolve(first)).Width > 0, "import survives original file moving", report);
                var duplicate = store.Import(moved);
                Check(duplicate.Id == first.Id && store.State.Items.Count == 1, "duplicate image selects existing item", report);
                string secondFile = Path.Combine(root, "fixture.png");
                using (var bitmap = new Bitmap(64, 128)) { using (var canvas = Graphics.FromImage(bitmap)) canvas.Clear(Color.DarkSeaGreen); bitmap.Save(secondFile, System.Drawing.Imaging.ImageFormat.Png); }
                var second = store.Import(secondFile);
                Check(store.State.Items.Count == 2 && second.Id != first.Id, "multiple independent images", report);
                string invalid = Path.Combine(root, "invalid.png"); File.WriteAllText(invalid, "not an image");
                bool rejected = false; try { store.Import(invalid); } catch { rejected = true; }
                Check(rejected && store.State.Items.Count == 2, "invalid image does not modify library", report);
                first.X = 120; first.Y = 90; first.Width = 500; first.Positioned = true; first.Locked = true; first.Hidden = true;
                store.Save(); var reloaded = new LibraryStore(root); var restored = reloaded.State.Items.First(p => p.Id == first.Id);
                Check(restored.X == 120 && restored.Y == 90 && restored.Width == 500 && restored.Locked && restored.Hidden, "position lock and visibility persist", report);
                store.Archive(first); store.Save();
                Check(store.State.Items.Count == 1 && File.Exists(store.Resolve(first)) && store.State.Archived.Count == 1, "remove keeps image and supports undo", report);
                var undone = store.Undo(); store.Save();
                Check(undone.Id == first.Id && undone.Width == 500 && undone.Hidden && store.State.Items.Count == 2, "undo restores complete original state", report);
                string oldAsset = store.Resolve(second); store.Replace(second, moved);
                Check(File.Exists(oldAsset) && File.Exists(store.Resolve(second)) && oldAsset != store.Resolve(second), "replacement retains prior asset for backup", report);
                store.Save(); store.Save();
                File.WriteAllText(store.StatePath, "broken json");
                var recovered = new LibraryStore(root);
                Check(recovered.State.Items.Count == 2 && recovered.Notice != null, "corrupt settings recover from atomic backup", report);
                string migrationRoot = Path.Combine(root, "migration"); Directory.CreateDirectory(migrationRoot);
                var legacy = new Preferences { ImagePath = moved, X = 321, Y = 76, Width = 600, Positioned = true, Locked = true };
                string legacyPath = Path.Combine(migrationRoot, "settings.json"); LibraryStore.Write(legacyPath, legacy);
                var migration = new LibraryStore(migrationRoot); migration.Migrate(legacyPath, moved);
                Check(migration.State.Items.Count == 1 && migration.State.Items[0].X == 321 && migration.State.Items[0].Locked, "v1 migration preserves desktop position and lock", report);
                Check(File.Exists(legacyPath), "migration retains original settings", report);
                Check(!migration.Migrate(legacyPath, moved), "migration is idempotent", report);
                File.WriteAllText(Path.Combine(Storage.Folder, "verification-library.txt"), report.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception e) { report.AppendLine(e.ToString()); File.WriteAllText(Path.Combine(Storage.Folder, "verification-library.txt"), report.ToString(), Encoding.UTF8); return 1; }
        }
        internal static int Smoke()
        {
            NoteContext context = null; int result = 1;
            try
            {
                using (var request = new EventWaitHandle(false, EventResetMode.AutoReset))
                using (var timer = new System.Windows.Forms.Timer { Interval = 700 })
                {
                    context = new NoteContext(request);
                    context.ShowManager();
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        try { context.VerifyUi(); File.WriteAllText(Path.Combine(Storage.Folder, "verification-ui.txt"), "PASS: WPF layout loaded, controls available, desktop images attached.", Encoding.UTF8); result = 0; }
                        catch (Exception e) { File.WriteAllText(Path.Combine(Storage.Folder, "verification-ui.txt"), e.ToString(), Encoding.UTF8); }
                        context.ExitThread();
                    };
                    timer.Start(); Application.Run(context);
                }
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Storage.Folder, "verification-ui.txt"), e.ToString(), Encoding.UTF8); }
            return result;
        }
    }
}
