using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace LocalDesktopNote
{
    // Stored papers and automatic recovery snapshots are two views over their
    // existing sources. Browsing never rewrites or deletes a history file.
    internal sealed class HistoryPanel
    {
        internal readonly Grid View;
        internal readonly ListBox List;
        internal readonly TextBox Preview;
        internal readonly TextBlock Notice;
        internal readonly Button RestoreButton;
        internal readonly List<HistoryEntry> Entries = new List<HistoryEntry>();
        internal string CurrentCategory { get { return category; } }
        internal string CurrentFilter { get { return filter; } }

        private readonly WorkboardStore store;
        private readonly Func<string, bool> openMemo;
        private readonly StackPanel filters;
        private readonly TextBlock previewHeading, previewTime, empty;
        private readonly Dictionary<string, Button> categories = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> filterButtons = new Dictionary<string, Button>();
        private string category = "memo", filter = "archive";
        private string loadNotice;
        private bool loading;
        private readonly Brush ink = Brush("#344438"), muted = Brush("#7D877C"), green = Brush("#64806B"), pale = Brush("#E7EBE1");

        internal sealed class HistoryEntry
        {
            internal string Key, Title, Summary, Text, SourcePath;
            internal DateTime StoredAt;
            internal bool IsMemo;
        }

        internal HistoryPanel(WorkboardStore store, Func<string, bool> openMemo)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store; this.openMemo = openMemo;
            View = new Grid { Background = Brush("#FAF8F3") };
            View.SetValue(TextElement.FontFamilyProperty, AppTypography.Body);
            View.SetValue(TextElement.FontSizeProperty, 13.0);
            View.SetValue(TextElement.ForegroundProperty, ink);
            View.Resources = (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#344438'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='13,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Body' CornerRadius='7' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderBrush='#64806B'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Background' Value='#E7EBE1'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ListBoxItem'><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Body' Background='Transparent' BorderBrush='#E2E5DB' BorderThickness='0,0,0,1' Padding='12,13'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Background' Value='#F0F2EA'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Body' Property='Background' Value='#E7EBE1'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='BorderBrush' Value='#64806B'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 </ResourceDictionary>");
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition());

            var categoryBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            AddCategory(categoryBar, "memo", "随手记", "note"); AddCategory(categoryBar, "task", "任务", "tasks");
            View.Children.Add(categoryBar);
            filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
            AddFilter("archive", "收起的纸页"); AddFilter("trash", "纸篓"); AddFilter("versions", "自动版本");
            Grid.SetRow(filters, 1); View.Children.Add(filters);
            Notice = new TextBlock { Foreground = muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 13) };
            AutomationProperties.SetName(Notice, "历史记录状态"); Grid.SetRow(Notice, 2); View.Children.Add(Notice);

            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.37, GridUnitType.Star), MinWidth = 190, MaxWidth = 280 }); body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 3); View.Children.Add(body);
            var listHost = new Grid { Margin = new Thickness(0, 0, 15, 0) };
            List = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(List, "随手记历史列表"); listHost.Children.Add(List);
            empty = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 20, 12, 0), IsHitTestVisible = false }; listHost.Children.Add(empty); body.Children.Add(listHost);
            var detail = new Grid(); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition()); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var detailHeader = new StackPanel { Margin = new Thickness(19, 17, 19, 12) };
            previewHeading = new TextBlock { Text = "留在这里的文字", FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }; detailHeader.Children.Add(previewHeading);
            previewTime = new TextBlock { Foreground = muted, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) }; detailHeader.Children.Add(previewTime); detail.Children.Add(detailHeader);
            Preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalContentAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Foreground = ink, BorderThickness = new Thickness(0), FontFamily = AppTypography.Body, FontSize = 15, Padding = new Thickness(19, 0, 19, 16), SelectionBrush = Brush("#CBD9C9") };
            AutomationProperties.SetName(Preview, "历史内容只读预览"); Grid.SetRow(Preview, 1); detail.Children.Add(Preview);
            var actions = new DockPanel { Margin = new Thickness(18, 0, 18, 16) };
            RestoreButton = MakeButton("取回这页", RestoreSelected); RestoreButton.Background = pale; RestoreButton.Foreground = green; RestoreButton.IsEnabled = false;
            RestoreButton.ToolTip = "当前纸页会先收好，再把这一页放回随手记"; AutomationProperties.SetName(RestoreButton, "取回这页随手记");
            DockPanel.SetDock(RestoreButton, Dock.Right); actions.Children.Add(RestoreButton); actions.Children.Add(new Border()); Grid.SetRow(actions, 2); detail.Children.Add(actions);
            var paper = new Border { Background = Brush("#FDFCF9"), BorderBrush = Brush("#DFE3D8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = detail }; Grid.SetColumn(paper, 1); body.Children.Add(paper);
            List.SelectionChanged += delegate { if (!loading) ShowSelection(); };
            UpdateNavigation();
        }

        private static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromInvariantString(value); }
        private static Button MakeButton(string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 7, 0) };
            button.Click += delegate { action(); }; return button;
        }
        private void AddCategory(Panel target, string key, string title, string icon)
        {
            var button = MakeButton(title, delegate { SelectCategory(key, key == "memo" ? "archive" : "versions"); });
            var label = new StackPanel { Orientation = Orientation.Horizontal }; label.Children.Add(AppVisuals.Icon(icon, 16, green)); label.Children.Add(new TextBlock { Text = title, Margin = new Thickness(8, 0, 0, 0) }); button.Content = label;
            AutomationProperties.SetName(button, title + "历史"); categories.Add(key, button); target.Children.Add(button);
        }
        private void AddFilter(string key, string title)
        {
            var button = MakeButton(title, delegate { SelectCategory("memo", key); }); button.FontSize = 12; button.Padding = new Thickness(10, 6, 10, 6);
            AutomationProperties.SetName(button, title); filterButtons.Add(key, button); filters.Children.Add(button);
        }
        private void UpdateNavigation()
        {
            foreach (var pair in categories) pair.Value.Background = pair.Key == category ? pale : Brushes.Transparent;
            foreach (var pair in filterButtons) { pair.Value.Background = pair.Key == filter ? pale : Brushes.Transparent; pair.Value.Foreground = pair.Key == filter ? ink : muted; }
            filters.Visibility = category == "memo" ? Visibility.Visible : Visibility.Collapsed;
            RestoreButton.Visibility = category == "memo" ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(List, category == "memo" ? "随手记历史列表" : "任务历史列表");
        }

        internal void SelectCategory(string category, string filter)
        {
            this.category = category == "task" ? "task" : "memo";
            this.filter = this.category == "task" ? "versions" : filter == "trash" || filter == "versions" ? filter : "archive";
            UpdateNavigation(); Load();
        }

        internal void Load()
        {
            string selected = SelectedEntry == null ? null : SelectedEntry.Key;
            int skipped = 0; string readError = null;
            loading = true;
            try
            {
                Entries.Clear(); List.Items.Clear(); Preview.Text = ""; previewTime.Text = ""; RestoreButton.IsEnabled = false;
                if (category == "memo" && filter != "versions") LoadPapers(ref skipped);
                else LoadSnapshots(ref skipped, out readError);
                foreach (HistoryEntry entry in Entries) List.Items.Add(MakeItem(entry));
                empty.Text = category == "task" ? "任务保存或状态调整后，之前的状态会留在这里。" : filter == "archive" ? "收起一页后，它会留在这里。" : filter == "trash" ? "纸篓是空的。\n丢进来的纸页仍可以取回。" : "写完一句或一段后，自动版本会留在这里。";
                empty.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                string source = category == "task" ? "任务历史只记录已保存的任务及状态变化；未提交的草稿和随手记不会增加任务记录。" : filter == "archive" ? "收起的纸页可随时取回；当前写着的内容会先收好。" : filter == "trash" ? "丢进纸篓的纸页仍保留在本机，可随时取回。" : "自动版本按完整句段保留；两分钟内同一句的小改字和输入过程合并为最新内容，原文件仍保留。";
                loadNotice = (Entries.Count == 0 ? "" : "显示最近 " + Entries.Count + " 页 · ") + source;
                if (skipped > 0) loadNotice += " 已跳过 " + skipped + " 条暂时无法读取的记录，原文件仍保留。";
                if (!String.IsNullOrEmpty(readError)) loadNotice += " 部分历史目录暂时无法读取，当前内容不受影响。";
                Notice.Text = loadNotice;
                int index = selected == null ? -1 : Entries.FindIndex(e => e.Key == selected);
                List.SelectedIndex = index >= 0 ? index : Entries.Count > 0 ? 0 : -1;
            }
            finally { loading = false; }
            ShowSelection();
        }

        private HistoryEntry SelectedEntry { get { var item = List.SelectedItem as ListBoxItem; return item == null ? null : item.Tag as HistoryEntry; } }
        private void LoadPapers(ref int skipped)
        {
            var papers = new List<HistoryEntry>();
            foreach (MemoPaper paper in store.State.MemoPapers ?? new List<MemoPaper>())
            {
                if (paper == null) { skipped++; continue; }
                if (paper.Kind != filter) continue;
                DateTimeOffset time;
                if (String.IsNullOrWhiteSpace(paper.Text) || String.IsNullOrEmpty(paper.Id) || !DateTimeOffset.TryParse(paper.StoredAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time)) { skipped++; continue; }
                papers.Add(MemoEntry("paper:" + paper.Id, paper.Text, time.LocalDateTime, null));
            }
            Entries.AddRange(papers.OrderByDescending(p => p.StoredAt).ThenBy(p => p.Key, StringComparer.Ordinal).Take(300));
        }

        private void LoadSnapshots(ref int skipped, out string readError)
        {
            readError = null;
            var files = new List<FileInfo>();
            // Enumerate directories separately so one unreadable subdirectory
            // does not hide other dates or current saved papers.
            var folders = new Stack<string>(); if (Directory.Exists(store.HistoryFolder)) folders.Push(store.HistoryFolder);
            while (folders.Count > 0)
            {
                string folder = folders.Pop();
                try
                {
                    files.AddRange(Directory.GetFiles(folder, "*.json").Select(p => new FileInfo(p)));
                    foreach (string child in Directory.GetDirectories(folder))
                    {
                        // Avoid recursive junction cycles and targets outside history.
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) folders.Push(child);
                    }
                }
                catch (Exception error) { readError = error.Message; }
            }
            string previous = null;
            HistoryEntry newerMemo = null;
            foreach (FileInfo file in files.OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.FullName, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    WorkboardState snapshot = store.ReadHistorySnapshot(file.FullName);
                    string projection = category == "memo" ? snapshot.Ideas ?? "" : TaskProjection(snapshot);
                    if (String.Equals(previous, projection, StringComparison.Ordinal)) continue;
                    previous = projection;
                    if (category == "memo")
                    {
                        // Blank states have no paper to take back. They still
                        // separate two identical nonblank editing sessions.
                        if (String.IsNullOrWhiteSpace(projection)) newerMemo = null;
                        else if (newerMemo == null || !MergeMemoStep(projection, newerMemo.Text, file.LastWriteTime, newerMemo.StoredAt))
                        {
                            newerMemo = MemoEntry("snapshot:" + file.FullName, projection, file.LastWriteTime, file.FullName); Entries.Add(newerMemo);
                        }
                    }
                    else
                    {
                        var tasks = (snapshot.Tasks ?? new List<WorkTask>()).Where(t => t != null).ToList();
                        int pending = tasks.Count(t => !t.Done), done = tasks.Count(t => t.Done);
                        string title = pending + " 项待办 · " + done + " 项已完成";
                        int removed = (snapshot.Removed ?? new List<WorkTask>()).Count(t => t != null);
                        string summary = tasks.Select(t => t.Text).FirstOrDefault(t => !String.IsNullOrWhiteSpace(t)) ?? (removed > 0 ? "已移除 " + removed + " 项任务" : "当时没有任务");
                        Entries.Add(new HistoryEntry { Key = "task:" + file.FullName, Title = title, Summary = Compact(summary, 88), StoredAt = file.LastWriteTime, Text = TaskPreview(snapshot), SourcePath = file.FullName, IsMemo = false });
                    }
                    // The sequence is newest first. Fill the visible range
                    // after projection/coalescing, then leave older JSON unread.
                    if (Entries.Count >= 300) break;
                }
                catch { skipped++; }
            }
        }

        private static bool MergeMemoStep(string older, string newer, DateTime olderTime, DateTime newerTime)
        {
            // Only merge nearby editing steps on the same local day. The
            // newest original snapshot remains the group's recovery source.
            TimeSpan gap = newerTime - olderTime;
            if (olderTime.Date != newerTime.Date || gap < TimeSpan.Zero || gap > TimeSpan.FromMinutes(2)) return false;
            return !WorkboardStore.IsMeaningfulMemoRevision(older, newer) || IsSupersededInput(older, newer);
        }
        private static bool MemoBoundary(char value)
        {
            return "。！？!?；;\n".IndexOf(value) >= 0 || value == '.';
        }
        private static bool IsSupersededInput(string older, string newer)
        {
            string before = older.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd(' ', '\t');
            string after = newer.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd(' ', '\t');
            if (before.Length == 0 || MemoBoundary(before[before.Length - 1])) return false;
            int boundary = before.Length - 1;
            while (boundary >= 0 && !MemoBoundary(before[boundary])) boundary--;
            string completePrefix = before.Substring(0, boundary + 1);
            if (!after.StartsWith(completePrefix, StringComparison.Ordinal)) return false;
            string input = before.Substring(boundary + 1).TrimStart(), completed = after.Substring(boundary + 1).TrimStart();
            if (input.Length == 0 || completed.Length == 0) return false;
            int common = 0;
            while (common < input.Length && common < completed.Length && input[common] == completed[common]) common++;
            int removed = input.Length - common;
            if (removed >= 20 && removed >= input.Length * 0.3) return false;
            // A matching unfinished tail distinguishes completing the same
            // sentence from adding a new sentence after an already saved one.
            return common > 0 && (common == input.Length || common >= 2);
        }

        private static HistoryEntry MemoEntry(string key, string text, DateTime time, string source)
        {
            string first = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').FirstOrDefault(s => !String.IsNullOrWhiteSpace(s)) ?? "未命名纸页";
            string trimmed = text.Trim(); int firstBreak = trimmed.IndexOfAny(new[] { '\r', '\n' });
            string remainder = firstBreak < 0 ? "" : trimmed.Substring(firstBreak).Trim();
            return new HistoryEntry { Key = key, Title = Compact(first.Trim(), 48), Summary = Compact(remainder, 100), Text = text, StoredAt = time, IsMemo = true, SourcePath = source };
        }
        private static string Compact(string text, int limit)
        {
            string value = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length > limit ? value.Substring(0, limit) + "…" : value;
        }
        private ListBoxItem MakeItem(HistoryEntry entry)
        {
            var lines = new StackPanel();
            lines.Children.Add(new TextBlock { Text = entry.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
            lines.Children.Add(new TextBlock { Text = entry.StoredAt.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture), Foreground = muted, FontSize = 10.5, Margin = new Thickness(0, 5, 0, 0) });
            if (!String.IsNullOrEmpty(entry.Summary)) lines.Children.Add(new TextBlock { Text = entry.Summary, Foreground = muted, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36, LineHeight = 18, Margin = new Thickness(0, 7, 0, 0) });
            var item = new ListBoxItem { Content = lines, Tag = entry, ToolTip = entry.Title };
            AutomationProperties.SetName(item, entry.Title + "，" + entry.StoredAt.ToString("yyyy年M月d日 HH:mm", CultureInfo.InvariantCulture)); return item;
        }

        internal void ShowSelection()
        {
            HistoryEntry entry = SelectedEntry;
            if (loadNotice != null) Notice.Text = loadNotice;
            RestoreButton.IsEnabled = entry != null && entry.IsMemo && openMemo != null;
            if (entry == null)
            {
                Preview.Text = ""; previewHeading.Text = category == "memo" ? "留在这里的文字" : "之前的任务状态"; previewTime.Text = ""; return;
            }
            previewHeading.Text = entry.Title;
            previewTime.Text = entry.StoredAt.ToString("yyyy年M月d日  HH:mm", CultureInfo.InvariantCulture) + (entry.IsMemo ? filter == "trash" ? " · 放入纸篓" : filter == "versions" ? " · 自动保留" : " · 收起" : " · 只读记录");
            Preview.FontFamily = entry.IsMemo ? AppTypography.Memo : AppTypography.Body;
            Preview.Text = entry.Text; Preview.ScrollToHome();
        }
        private void RestoreSelected()
        {
            HistoryEntry entry = SelectedEntry; if (entry == null || !entry.IsMemo || openMemo == null) return;
            try
            {
                if (openMemo(entry.Text)) { Load(); Notice.Text = "这页已取回，当前内容已先收好。"; }
                else Notice.Text = (store.State.Ideas ?? "") == entry.Text ? "这页已经是当前随手记，无需重复取回。" : "这页暂时未能取回，当前内容与原记录均已保留。";
            }
            catch { Notice.Text = "这页暂时未能取回，当前内容与原记录均已保留。"; }
        }

        private static void AppendPart(StringBuilder text, string value)
        {
            value = value ?? ""; text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }
        private static string TaskProjection(WorkboardState state)
        {
            var result = new StringBuilder();
            foreach (IEnumerable<WorkTask> group in new[] { state.Tasks, state.Removed })
            {
                var tasks = (group ?? Enumerable.Empty<WorkTask>()).Where(t => t != null).ToList(); result.Append('|').Append(tasks.Count).Append('|');
                foreach (WorkTask task in tasks)
                {
                    AppendPart(result, task.Id); AppendPart(result, task.Text); AppendPart(result, task.Done ? "1" : "0"); AppendPart(result, task.PlanDate); AppendPart(result, task.CompletedAt); AppendPart(result, task.RemovedIndex.HasValue ? task.RemovedIndex.Value.ToString(CultureInfo.InvariantCulture) : "");
                }
            }
            return result.ToString();
        }
        private static string PlanLabel(string value)
        {
            DateTime date; return WorkboardStore.TryPlanDate(value, out date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : String.IsNullOrEmpty(value) ? "未安排" : "日期待确认：" + value;
        }
        private static string TaskPreview(WorkboardState state)
        {
            var text = new StringBuilder(); var tasks = (state.Tasks ?? new List<WorkTask>()).Where(t => t != null).ToList();
            text.AppendLine("任务"); if (tasks.Count == 0) text.AppendLine("（当时没有任务）");
            foreach (WorkTask task in tasks) { text.AppendLine((task.Done ? "[已完成] " : "[待办] ") + task.Text); text.AppendLine("计划日期：" + PlanLabel(task.PlanDate)); text.AppendLine(); }
            var removed = (state.Removed ?? new List<WorkTask>()).Where(t => t != null).ToList();
            if (removed.Count > 0) { text.AppendLine("当时已移除的任务"); foreach (WorkTask task in removed) { text.AppendLine((task.Done ? "[已完成] " : "[待办] ") + task.Text); text.AppendLine("计划日期：" + PlanLabel(task.PlanDate)); } }
            return text.ToString().TrimEnd();
        }
    }
}
