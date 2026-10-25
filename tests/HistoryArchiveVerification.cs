using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    internal static class HistoryArchiveVerification
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int passed, failed;
        private const string Note = "关于产品与业务的思考\n1. 如何发现有潜力的产品？\n2. 业务线与产品线如何区分？\n3. 产品经理如何与团队协作？\n\n我的能力\n1. 数据采集与分析。\n2. 把观察整理成清晰的问题。";

        private static void Check(bool condition, string message)
        {
            Report.AppendLine((condition ? "PASS: " : "FAIL: ") + message);
            if (condition) passed++; else failed++;
        }

        private static void Run(string name, Action action)
        {
            Report.AppendLine("CASE: " + name);
            try { action(); } catch (Exception error) { failed++; Report.AppendLine("FAIL: " + error); }
        }

        private static DateTime Local(int day, int hour, int minute)
        {
            return new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Local);
        }

        private static WorkboardState SnapshotState(string memo)
        {
            var state = new WorkboardState { Ideas = memo, TaskDraft = "PRIVATE UNCOMMITTED DRAFT", DraftPlanDate = "2026-11-01" };
            state.Tasks.Add(new WorkTask { Id = "meal", Text = "吃饭", Done = true });
            state.Tasks.Add(new WorkTask { Id = "reply", Text = "查看岗位公司并回复消息", Done = true, PlanDate = "2026-10-22" });
            state.Tasks.Add(new WorkTask { Id = "read", Text = "阅读郭昱华的博客", Done = false, PlanDate = "2026-10-25" });
            return state;
        }

        private sealed class Fixture
        {
            internal readonly WorkboardStore Store;
            internal readonly string Folder;
            private int sequence;
            internal Fixture(string name)
            {
                Folder = System.IO.Path.Combine(Storage.Folder, "f", name);
                Store = new WorkboardStore(Folder);
                Store.State.Ideas = "Current working note must remain unchanged.";
                Store.Save();
                // Tests browse an already existing document. The seed is not a
                // live editing session that navigation should checkpoint.
                Store = new WorkboardStore(Folder);
            }

            internal string Add(string memo, DateTime time, bool completeLast = false)
            {
                var state = SnapshotState(memo);
                state.Tasks[2].Done = completeLast;
                return AddState(state, time);
            }

            internal string AddState(WorkboardState state, DateTime time)
            {
                string folder = System.IO.Path.Combine(Store.HistoryFolder, time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, (++sequence).ToString("D4", CultureInfo.InvariantCulture) + ".json");
                LibraryStore.Write(path, state);
                File.SetLastWriteTimeUtc(path, time.ToUniversalTime());
                return path;
            }

            internal Dictionary<string, string> Hashes()
            {
                return Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
            }
        }

        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path)) return Convert.ToBase64String(algorithm.ComputeHash(stream));
        }

        private static bool SameFiles(Fixture fixture, Dictionary<string, string> before)
        {
            Dictionary<string, string> after = fixture.Hashes();
            return before.Count == after.Count && before.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value);
        }

        private static IEnumerable<DependencyObject> Visuals(DependencyObject value)
        {
            yield return value;
            int children = VisualTreeHelper.GetChildrenCount(value);
            for (int index = 0; index < children; index++)
                foreach (DependencyObject child in Visuals(VisualTreeHelper.GetChild(value, index))) yield return child;
        }

        private static IEnumerable<DependencyObject> Logicals(DependencyObject value)
        {
            yield return value;
            foreach (object child in LogicalTreeHelper.GetChildren(value))
            {
                var item = child as DependencyObject;
                if (item != null) foreach (DependencyObject nested in Logicals(item)) yield return nested;
            }
        }

        private static string DocumentText(HistoryReadingView reader)
        {
            return new TextRange(reader.Document.Document.ContentStart, reader.Document.Document.ContentEnd).Text;
        }

        private static void Render(FrameworkElement view, string name, int width, int height)
        {
            view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(System.IO.Path.Combine(Storage.Folder, name + ".png"))) encoder.Save(stream);
        }

        private static void PumpDocument(RichTextBox document)
        {
            document.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            Forms.Application.DoEvents();
            document.UpdateLayout();
        }

        private static Fixture Sample(string name)
        {
            var fixture = new Fixture(name);
            fixture.Add("读后感：技术与产品\n记录使用过程中的观察。", Local(21, 21, 15));
            fixture.Add("一些想法\n先整理真实的用户任务。", Local(22, 14, 36));
            fixture.Add("市场信息收集\n核对公开材料和证据。", Local(22, 20, 7));
            fixture.Add("与团队沟通的要点\n先明确目标与分工。", Local(22, 23, 12));
            fixture.Add("产品资料整理\n把事实和假设分开。", Local(23, 16, 20));
            fixture.Add("本周工作梳理\n复核未完成的事项。", Local(23, 23, 48));
            fixture.Add(Note, Local(25, 0, 33));
            return fixture;
        }

        private static void TimelineAndReading()
        {
            var fixture = Sample("sample"); Dictionary<string, string> before = fixture.Hashes();
            var panel = new HistoryArchivePanel(fixture.Store); panel.Load();
            Render(panel.View, "history-archive", 770, 550);
            Check(panel.List.Items.Count == 7 && panel.Entries.Count == 7, "dated navigation retains every distinct supplied version");
            Check(panel.Entries[0].StoredAt == Local(25, 0, 33) && panel.Entries[0].Title == "关于产品与业务的思考", "latest date opens with a content-derived summary rather than a full timestamp-only label");
            Check(panel.List.Items.Cast<ListBoxItem>().All(item => item.Tag is string && File.Exists((string)item.Tag)), "version rows retain source-path identities for exact selection");
            var dateMarkers = Visuals(panel.List).OfType<Ellipse>().Where(marker => AutomationProperties.GetName(marker).Contains("日期节点")).ToList();
            Check(dateMarkers.Count >= 3 && dateMarkers.Any(marker => AutomationProperties.GetName(marker).Contains("25日")), "visible date groups have accessible circular date locators");
            Check(Visuals(panel.List).OfType<Border>().Any(border => border.Width == 1 && border.ActualHeight > 40), "date groups are connected by a restrained continuous vertical spine");
            string listText = String.Join("\n", Visuals(panel.List).OfType<TextBlock>().Select(label => label.Text));
            Check(listText.Contains("2026 / 10") && listText.Contains("00:33") && listText.Contains("关于产品与业务的思考"), "date navigation visibly combines month, summary and minute-level time");
            Check(panel.Reader.Document.IsReadOnly && panel.Reader.MemoText == Note && panel.Reader.TaskRowCount == 3, "reading surface presents the unchanged note and all three read-only task rows");
            string text = DocumentText(panel.Reader);
            Check(text.IndexOf("关于产品与业务的思考", StringComparison.Ordinal) < text.IndexOf("任务", StringComparison.Ordinal), "selected composition preserves note-before-task reading order");
            Check(text.IndexOf("吃饭", StringComparison.Ordinal) < text.IndexOf("查看岗位公司", StringComparison.Ordinal) && text.IndexOf("查看岗位公司", StringComparison.Ordinal) < text.IndexOf("阅读郭", StringComparison.Ordinal), "task presentation preserves original saved task order");
            Check(!text.Contains("[已完成]") && !text.Contains("[待办]") && !text.Contains("PRIVATE") && !text.Contains("草稿"), "display text has neither boxed status prefixes nor unsaved draft leakage");
            var symbols = Logicals(panel.Reader.Document.Document).OfType<UIElement>().Select(element => AutomationProperties.GetName(element)).Where(name => name.Contains("只读历史状态")).ToList();
            Check(symbols.Count == 3 && symbols.Count(name => name.Contains("已完成")) == 2 && symbols.Count(name => name.Contains("待办")) == 1, "static graphical state marks expose completed and pending meaning to assistive technology");
            Check(!Visuals(panel.Reader.View).OfType<CheckBox>().Any() && !Logicals(panel.Reader.Document.Document).OfType<CheckBox>().Any(), "read-only history never exposes editable checkbox controls");
            Check(panel.Reader.CopyText.StartsWith(Note, StringComparison.Ordinal) && panel.Reader.CopyText.Contains("阅读郭昱华的博客"), "copy payload retains the original note and full task wording");
            Check(panel.Reader.Document.ActualWidth >= 350 && panel.Reader.Document.ActualHeight > 300, "normal view keeps a comfortable reading width and height");
            panel.List.SelectedIndex = 2;
            string selected = (string)((ListBoxItem)panel.List.SelectedItem).Tag;
            Check(panel.Reader.MemoText.StartsWith("产品资料整理", StringComparison.Ordinal), "selecting another version immediately opens its real historical text");
            panel.Load();
            Check((string)((ListBoxItem)panel.List.SelectedItem).Tag == selected, "reloading keeps the selected version when its source still exists");
            Render(panel.View, "history-archive-minimum", 600, 400);
            Check(panel.List.ActualWidth >= 140 && panel.Reader.Document.ActualWidth > 300 && panel.Reader.Document.ActualHeight > 200, "small viewport preserves usable date navigation and reading surface");
            Check(SameFiles(fixture, before), "loading, rendering and selecting versions leave primary data and every history file byte-for-byte unchanged");
        }

        private static void LongMemoAndEmpty()
        {
            var fixture = new Fixture("long");
            string longMemo = "完整的长篇观察\r\n\r\n" + String.Join("\r\n", Enumerable.Range(1, 100).Select(index => index + ". 保留原始文字与标点，逐项核对真正的用户工作流程。"));
            fixture.Add(longMemo, Local(25, 1, 10));
            var panel = new HistoryArchivePanel(fixture.Store); panel.Load();
            Render(panel.View, "history-archive-long", 770, 550);
            panel.Reader.Document.SelectAll();
            Check(panel.Reader.Document.Selection.Text.Contains("100. 保留原始文字") && panel.Reader.MemoText == longMemo, "long historical text remains truly selectable and original copy text remains exact");
            Check(panel.Reader.CopyText.StartsWith(longMemo, StringComparison.Ordinal) && panel.Reader.CopyText.Contains("吃饭"), "long-note copy payload includes final lines and tasks without truncation");
            Check(Visuals(panel.Reader.View).OfType<ScrollViewer>().Count() == 1, "note and task content share one main reading scrollbar");
            panel.Reader.Document.ScrollToEnd();
            PumpDocument(panel.Reader.Document);
            Check(panel.Reader.Document.VerticalOffset > 0, "long note and its tasks remain reachable by ordinary document scrolling");
            panel.Reader.Document.ScrollToHome();
            PumpDocument(panel.Reader.Document);
            Check(panel.Reader.Document.VerticalOffset == 0, "reading can return to the first line without changing the record");
            var empty = new HistoryArchivePanel(new Fixture("empty").Store); empty.Load();
            Render(empty.View, "history-archive-empty", 600, 400);
            Check(empty.Entries.Count == 0 && empty.List.Items.Count == 0 && !empty.Reader.CopyButton.IsEnabled, "absent history is a safe readable empty state with copy disabled");
        }

        private static void CoalescingAndFailure()
        {
            var fixture = new Fixture("merge");
            string memo = "确认真正需要解决的问题。";
            for (int index = 0; index < 7; index++)
            {
                var state = SnapshotState(memo); state.TaskDraft = "拼音 " + index;
                fixture.AddState(state, Local(25, 0, 30).AddSeconds(index));
            }
            var panel = new HistoryArchivePanel(fixture.Store); panel.Load();
            Check(panel.Entries.Count == 1, "old character-by-character task drafts coalesce into one committed-content version");
            fixture.Add(memo + "\n然后梳理工作liu", Local(25, 0, 30).AddSeconds(10));
            fixture.Add(memo + "\n然后梳理工作流程。", Local(25, 0, 30).AddSeconds(12));
            panel.Load();
            Check(panel.Entries.Count == 2 && panel.Entries[0].State.Ideas.EndsWith("工作流程。", StringComparison.Ordinal), "new completed sentence remains a version while its pinyin input is merged");
            fixture.Add(memo + "\n然后梳理工作流程。", Local(25, 0, 30).AddSeconds(15), true);
            panel.Load();
            Check(panel.Entries.Count == 3 && panel.Entries[0].State.Tasks[2].Done, "committed completion remains a visible historical change");
            string corrupt = fixture.Add("placeholder", Local(25, 0, 31)); File.WriteAllText(corrupt, "{ invalid historical json");
            File.SetLastWriteTimeUtc(corrupt, Local(25, 0, 31).ToUniversalTime());
            Dictionary<string, string> originals = fixture.Hashes(); panel.Load();
            Check(panel.Notice.Text.Contains("暂时无法读取") && panel.Entries.Any(entry => entry.State != null), "unreadable history is reported without hiding the other usable versions");
            panel.List.SelectedIndex = 0;
            Check(!panel.Reader.CopyButton.IsEnabled && DocumentText(panel.Reader).Contains("原文件仍保留"), "selecting an unreadable version gives a safe recoverability message");
            Check(SameFiles(fixture, originals), "corrupt history stays unchanged and no original file is rewritten or removed");
        }

        private static void EffectiveLimit()
        {
            var fixture = new Fixture("limit");
            for (int index = 0; index < 305; index++) fixture.Add("独立的工作观察 " + index + "。", Local(22, 0, 0).AddMinutes(index * 3));
            for (int index = 0; index < 10; index++) fixture.Add("独立的工作观察 304。", Local(22, 0, 0).AddMinutes(304 * 3).AddSeconds(index + 1));
            var panel = new HistoryArchivePanel(fixture.Store); panel.Load();
            Check(panel.Entries.Count == 300 && panel.List.Items.Count == 300, "display limit applies to 300 effective versions after duplicate coalescing");
            Check(panel.Entries[0].State.Ideas == "独立的工作观察 304。" && panel.Entries[299].State.Ideas == "独立的工作观察 5。", "duplicate burst does not hide older distinct versions within the effective limit");
            Check(Directory.GetFiles(fixture.Store.HistoryFolder, "*.json", SearchOption.AllDirectories).Length == 315, "the display limit never deletes persisted recovery files");
        }

        private static void WorkbenchFrame()
        {
            var fixture = Sample("workbench");
            using (var window = new WorkbenchWindow(fixture.Store))
            {
                typeof(WorkbenchWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { "history" });
                var categories = (HistoryPanel)typeof(WorkbenchWindow).GetField("historyPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                Check(categories.CurrentFilter == "papers" && window.HistoryList.Items.Count == 0, "integrated workbench keeps stored papers as the default category");
                categories.SelectCategory("memo", "versions");
                Render(window.View, "history-workbench", 1026, 659);
                Check(window.HistoryList.Items.Count == 7 && window.HistoryPreview.IsReadOnly, "existing workbench exposes the real timeline and read-only document");
                Check(new TextRange(window.HistoryPreview.Document.ContentStart, window.HistoryPreview.Document.ContentEnd).Text.Contains("关于产品与业务的思考"), "workbench integration renders selected note through the real document control");
                Check(categories.Reader.CopyText == Note && categories.Reader.TaskRowCount == 0, "integrated memo reader copies exact original text and excludes task rows");
                categories.SelectCategory("task", "versions");
                Render(window.View, "history-workbench-tasks", 1026, 659);
                Check(categories.Reader.TaskRowCount == 3 && categories.Reader.MemoText == "" && categories.RestoreButton.Visibility == Visibility.Collapsed, "integrated task category renders graphical read-only rows without memo recovery");
            }
        }

        private static void MinuteGroupsAndScrollbar()
        {
            var fixture = new Fixture("minute-groups");
            DateTime minute = Local(25, 9, 20);
            DateTime[] times = { minute.AddSeconds(50), minute.AddSeconds(30), minute.AddSeconds(10), minute.AddSeconds(-1), Local(24, 9, 20).AddSeconds(40) };
            var versions = times.Select((time, index) => {
                string note = "独立纸页 " + index + "。\n保留该页的完整原文。";
                string path = fixture.Add(note, time);
                return new HistoryArchivePanel.HistoryVersion { SourcePath = path, Title = "独立纸页 " + index, StoredAt = time, State = fixture.Store.ReadHistorySnapshot(path) };
            }).ToArray();
            Dictionary<string, string> before = fixture.Hashes();
            var panel = new HistoryArchivePanel(fixture.Store);
            panel.Present(versions, true, false, "5 页 · 仅保存在本机", "", null);
            Render(panel.View, "history-minute-groups-collapsed", 770, 550);
            Check(panel.Entries.Count == 5 && panel.List.Items.Count == 5, "minute folding keeps every original version and source identity");
            Check(panel.List.Items.Cast<ListBoxItem>().Count(item => item.Visibility == Visibility.Visible) == 3, "same-minute sheets fold behind the newest while neighboring minutes and days stay visible");
            var toggle = Visuals(panel.List).OfType<Button>().Single(button => AutomationProperties.GetName(button).Contains("另外2条"));
            Check(Convert.ToString(toggle.Content) == "展开 2 条" && panel.Reader.CopyText == versions[0].State.Ideas, "fold control reports the hidden count and initially opens the newest original sheet");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(panel.View, "history-minute-groups-expanded", 770, 550);
            Check(panel.List.Items.Cast<ListBoxItem>().All(item => item.Visibility == Visibility.Visible), "expanding exposes every individual paper for normal selection");
            panel.List.SelectedIndex = 2;
            Check(panel.Reader.CopyText == versions[2].State.Ideas, "expanded older paper selects and copies its own exact original text");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(panel.List.SelectedIndex == 0 && panel.Reader.CopyText == versions[0].State.Ideas && ((ListBoxItem)panel.List.Items[2]).Visibility == Visibility.Collapsed, "collapsing a selected older paper safely selects the visible newest paper");
            panel.List.SelectedIndex = 2;
            Check(((ListBoxItem)panel.List.Items[2]).Visibility == Visibility.Visible && Convert.ToString(toggle.Content) == "收起 2 条", "directly selecting a folded recovery version reveals its group");
            panel.Present(versions, true, false, "5 页", "", versions[2].SourcePath);
            Check(panel.List.SelectedIndex == 2 && ((ListBoxItem)panel.List.SelectedItem).Visibility == Visibility.Visible && panel.Reader.CopyText == versions[2].State.Ideas, "reload preserves the selected older source inside an expanded group");
            Check(SameFiles(fixture, before), "fold expand select and reload never rewrite or delete stored data");

            var many = Enumerable.Range(0, 24).Select(index => new HistoryArchivePanel.HistoryVersion {
                SourcePath = "minute-scroll-" + index, Title = "另一分钟的独立记录 " + index,
                StoredAt = minute.AddMinutes(-index), State = new WorkboardState { Ideas = "完整原文 " + index }
            }).ToArray();
            panel.Present(many, true, false, "24 页", "", null);
            Render(panel.View, "history-rounded-scrollbar", 770, 400);
            var scroll = Visuals(panel.List).OfType<ScrollViewer>().First();
            var bar = Visuals(panel.List).OfType<System.Windows.Controls.Primitives.ScrollBar>().Single(value => value.Orientation == Orientation.Vertical);
            Check(!scroll.CanContentScroll && bar.ActualWidth == 10, "history index uses continuous pixel offsets and a narrow native scroll hit area");
            Check(Visuals(bar).OfType<System.Windows.Controls.Primitives.Thumb>().Any(thumb => Visuals(thumb).OfType<Border>().Any(border => border.CornerRadius.TopLeft == 3)), "scroll thumb has rounded ends instead of the square system grip");
            scroll.ScrollToVerticalOffset(7); panel.View.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            Check(Math.Abs(scroll.VerticalOffset - 7) < 1, "pixel scrolling accepts a small offset rather than jumping to a whole version row");
        }

        private static void MemoPreviewResources()
        {
            foreach (string name in new[] { "memo-bin.png", "memo-bin-filled.png", "memo-paper-corner.png", "memo-drop-arrow.png" })
                Check(AppVisuals.IllustrationSource(name) != null, "full preview decodes required illustration: " + name);
            Check(File.Exists(System.IO.Path.Combine(Storage.Folder, "sounds", "paper-drop.wav")), "full preview includes the paper bin sound resource");
            var fixture = new Fixture("memo-assets");
            using (var window = new WorkbenchWindow(fixture.Store))
            {
                typeof(WorkbenchWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { "memo" });
                Render(window.View, "history-preview-memo-assets", 1026, 659);
                var controls = (MemoPaperControls)typeof(WorkbenchWindow).GetField("fullPaperControls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                var bin = Visuals(controls.Bin).OfType<Image>().Single();
                Check(bin.Source != null && Object.ReferenceEquals(bin.Source, AppVisuals.IllustrationSource("memo-bin.png")), "the actual full memo paper bin displays its illustration");
                fixture.Store.StoreCurrentMemo("trash");
                Render(window.View, "history-preview-memo-filled-bin", 1026, 659);
                Check(bin.Source != null && Object.ReferenceEquals(bin.Source, AppVisuals.IllustrationSource("memo-bin-filled.png")), "the actual bin switches to its filled illustration after storing a paper");
            }
        }

        private static WorkboardStore OpenPreviewStore()
        {
            string folder = System.IO.Path.Combine(Storage.Folder, "f", "preview");
            // A user may continue writing in a visible QA preview. Restarting
            // that preview must never seed over their current paper or history.
            return File.Exists(System.IO.Path.Combine(folder, "workboard.json")) ? new WorkboardStore(folder) : Sample("preview").Store;
        }

        private static void PreviewReopensExistingData()
        {
            var fixture = new Fixture("preview");
            fixture.Store.State.Ideas = "预览里已经写下的原文。"; fixture.Store.State.MemoTitle = "已有标题";
            fixture.Store.Save(); fixture.Store.StoreCurrentMemo("trash");
            fixture.Store.State.Ideas = "还在继续写的下一页。"; fixture.Store.Save();
            var before = fixture.Hashes();
            var reopened = OpenPreviewStore();
            Check(reopened.State.Ideas == "还在继续写的下一页。" && reopened.State.MemoPapers.Single().Title == "已有标题", "reopening a visible preview retains the current paper and existing bin records");
            Check(SameFiles(fixture, before), "preview startup never reseeds or overwrites existing primary and history files");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try { Desktop.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Forms.Application.EnableVisualStyles(); Forms.Application.SetCompatibleTextRenderingDefault(false);
            // Double-clicking a preview must never run destructive fixture setup.
            // Automated verification is opt-in through the wrapper's --verify.
            if (!args.Contains("--verify"))
            {
                var window = new WorkbenchWindow(OpenPreviewStore());
                window.AllowClose = true;
                window.Size = new System.Drawing.Size(1026, 690);
                window.Text = "Yourself Copilot · 完整预览";
                bool historyPage = args.Contains("--preview");
                typeof(WorkbenchWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { historyPage ? "history" : "memo" });
                if (historyPage) ((HistoryPanel)typeof(WorkbenchWindow).GetField("historyPanel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window)).SelectCategory("memo", "versions");
                window.Shown += delegate { window.Size = new System.Drawing.Size(1026, 690); if (historyPage) window.HistoryList.Focus(); else window.FocusQuickMemo(); };
                Forms.Application.Run(window); return 0;
            }
            try
            {
                Run("date spine, summaries, static task states and immutable reading", TimelineAndReading);
                Run("long selectable note and empty history", LongMemoAndEmpty);
                Run("draft/sentence coalescing and corrupt source", CoalescingAndFailure);
                Run("limit after effective version coalescing", EffectiveLimit);
                Run("native workbench integration frame", WorkbenchFrame);
                Run("same-minute folding and rounded pixel scrollbar", MinuteGroupsAndScrollbar);
                Run("complete memo preview resources and visible bin", MemoPreviewResources);
                Run("visible preview preserves existing user data on restart", PreviewReopensExistingData);
                Report.AppendLine("Passed: " + passed + "; Failed: " + failed);
                Report.AppendLine("LIMIT: isolated neutral fixtures, actual hidden WPF/WinForms controls, document text, selection and scrollbar behavior. Rendered workbench image is its 1026x659 client area; no OS title bar, physical keyboard/mouse, production data, registry or clipboard mutation is part of this runner. --preview opens only this fixture-backed workbench.");
                return failed == 0 ? 0 : 1;
            }
            finally { File.WriteAllText(System.IO.Path.Combine(Storage.Folder, "verification-history-archive.txt"), Report.ToString(), new UTF8Encoding(false)); }
        }
    }
}
