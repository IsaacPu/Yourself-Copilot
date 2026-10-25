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
using System.Windows.Shapes;

namespace LocalDesktopNote
{
    // A presentation of existing recovery files. Loading and selecting records
    // never changes their contents, dates, or the current workboard.
    internal sealed class HistoryArchivePanel
    {
        internal readonly Grid View;
        internal readonly ListBox List;
        internal readonly HistoryReadingView Reader;
        internal readonly TextBlock Notice;
        internal readonly List<HistoryVersion> Entries = new List<HistoryVersion>();

        internal sealed class HistoryVersion
        {
            internal string SourcePath, Title;
            internal DateTime StoredAt;
            internal WorkboardState State;
        }

        private readonly WorkboardStore store;
        private readonly TextBlock empty;
        private readonly ColumnDefinition indexWidth;
        private readonly Dictionary<string, Border> summaries = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DateTime, Ellipse> markers = new Dictionary<DateTime, Ellipse>();
        private readonly Dictionary<ListBoxItem, MinuteGroup> minuteGroups = new Dictionary<ListBoxItem, MinuteGroup>();
        private readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool loading, showMemo = true, showTasks = true;
        private readonly Brush ink = Brush("#25392F"), muted = Brush("#727C73"), green = Brush("#57745F"), pale = Brush("#EBEFE6"), rule = Brush("#DCE1D6");

        private sealed class MinuteGroup
        {
            internal string Key;
            internal DateTime Time;
            internal int Count;
            internal Button Toggle;
            internal readonly List<ListBoxItem> Items = new List<ListBoxItem>();
        }

        internal HistoryArchivePanel(WorkboardStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
            View = new Grid { Background = Brush("#FAF8F3") };
            View.SetValue(TextElement.FontFamilyProperty, AppTypography.Body);
            View.SetValue(TextElement.ForegroundProperty, ink);
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition());
            View.Resources = (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='ListBoxItem'><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='Margin' Value='0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Focus' BorderBrush='Transparent' BorderThickness='1,0' Background='Transparent'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Focus' Property='BorderBrush' Value='#94A38F'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <ControlTemplate x:Key='PageTrackButton' TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
 <Style x:Key='HistoryIndexScrollBar' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Border Background='Transparent' Padding='2,1'>
    <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
     <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Template='{StaticResource PageTrackButton}' Focusable='False' IsTabStop='False'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb x:Name='Grip' MinHeight='28' Background='#C4CDBF'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{TemplateBinding Background}' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Template='{StaticResource PageTrackButton}' Focusable='False' IsTabStop='False'/></Track.IncreaseRepeatButton>
    </Track>
   </Border>
   <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#9DAE98'/></Trigger><Trigger SourceName='Grip' Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#71886F'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='MinuteToggle' TargetType='Button'><Setter Property='Foreground' Value='#727C73'/><Setter Property='FontSize' Value='11'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Padding' Value='5,4'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Body' Background='Transparent' CornerRadius='3' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Background' Value='#EBEFE6'/><Setter Property='Foreground' Value='#57745F'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='Background' Value='#EBEFE6'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>");
            Notice = new TextBlock { Text = "仅保存在本机", FontSize = 12, Foreground = muted, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap };
            Notice.ToolTip = "任务提交后保留版本；随手记按完整句段合并。原始历史文件始终保留。";
            AutomationProperties.SetName(Notice, "历史记录状态"); View.Children.Add(Notice);
            var body = new Grid(); indexWidth = new ColumnDefinition { Width = new GridLength(190), MinWidth = 145 };
            body.ColumnDefinitions.Add(indexWidth); body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1); View.Children.Add(body);
            var index = new Grid { Margin = new Thickness(0, 0, 13, 0) };
            List = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            List.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), View.Resources["HistoryIndexScrollBar"]);
            ScrollViewer.SetCanContentScroll(List, false);
            ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(List, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(List, "历史版本列表");
            index.Children.Add(List);
            empty = new TextBlock { Text = "还没有历史记录", FontSize = 13, Foreground = muted, Margin = new Thickness(8, 14, 6, 0), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            index.Children.Add(empty); body.Children.Add(index);
            Reader = new HistoryReadingView();
            var paper = new Border { Background = Brush("#FDFCF9"), CornerRadius = new CornerRadius(6), Child = Reader.View, BorderThickness = new Thickness(0) };
            Grid.SetColumn(paper, 1); body.Children.Add(paper);
            List.SelectionChanged += delegate { if (!loading) ShowSelection(); };
            View.SizeChanged += delegate { indexWidth.Width = new GridLength(View.ActualWidth < 660 ? 164 : 190); };
        }

        internal void Load()
        {
            var selectedItem = List.SelectedItem as ListBoxItem;
            string selected = selectedItem == null ? null : selectedItem.Tag as string;
            loading = true;
            try
            {
                Entries.Clear(); List.Items.Clear(); summaries.Clear(); markers.Clear(); Reader.Clear();
                var files = new List<FileInfo>(); bool directoryError = false;
                var folders = new Stack<string>(); if (Directory.Exists(store.HistoryFolder)) folders.Push(store.HistoryFolder);
                while (folders.Count > 0)
                {
                    try
                    {
                        string folder = folders.Pop();
                        files.AddRange(Directory.GetFiles(folder, "*.json").Select(path => new FileInfo(path)));
                        foreach (string child in Directory.GetDirectories(folder))
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) folders.Push(child);
                    }
                    catch { directoryError = true; }
                }
                HistoryVersion newer = null; string newerTasks = null; int unreadable = 0;
                foreach (FileInfo file in files.OrderByDescending(value => value.LastWriteTimeUtc).ThenByDescending(value => value.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    var entry = new HistoryVersion { SourcePath = file.FullName, StoredAt = file.LastWriteTime };
                    string tasks = null;
                    try
                    {
                        entry.State = store.ReadHistorySnapshot(file.FullName);
                        tasks = TaskContent(entry.State);
                        if (newer != null && newer.State != null && entry.StoredAt.Date == newer.StoredAt.Date && tasks == newerTasks)
                        {
                            string before = entry.State.Ideas ?? "", after = newer.State.Ideas ?? "";
                            if (before == after || (!String.IsNullOrWhiteSpace(before) && !String.IsNullOrWhiteSpace(after) &&
                                newer.StoredAt - entry.StoredAt <= TimeSpan.FromMinutes(2) &&
                                (!WorkboardStore.IsMeaningfulMemoRevision(before, after) || IsSupersededInput(before, after)))) continue;
                        }
                        entry.Title = SummaryTitle(entry.State);
                    }
                    catch { entry.Title = "暂时无法读取的记录"; entry.State = null; unreadable++; }
                    Entries.Add(entry); newer = entry; newerTasks = tasks;
                    if (Entries.Count == 300) break;
                }
                BuildIndex(selected);
                Notice.Text = Entries.Count.ToString(CultureInfo.InvariantCulture) + " 个版本 · 仅保存在本机";
                if (unreadable > 0) Notice.Text += " · " + unreadable + " 份暂时无法读取，原文件保留";
                if (directoryError) Notice.Text += " · 部分目录暂时无法读取";
                empty.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                int selectedIndex = selected == null ? -1 : Entries.FindIndex(entry => String.Equals(entry.SourcePath, selected, StringComparison.OrdinalIgnoreCase));
                List.SelectedIndex = selectedIndex >= 0 ? selectedIndex : Entries.Count > 0 ? 0 : -1;
            }
            finally { loading = false; }
            ShowSelection();
        }

        // The paper workflow owns its category projections and recovery action;
        // all categories share this date index and read-only reading surface.
        internal void Present(IEnumerable<HistoryVersion> versions, bool includeMemo, bool includeTasks, string notice, string emptyText, string selected)
        {
            loading = true;
            try
            {
                showMemo = includeMemo; showTasks = includeTasks;
                Entries.Clear(); Entries.AddRange(versions);
                Reader.Clear(); BuildIndex(selected);
                Notice.Text = notice; empty.Text = emptyText;
                empty.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                int index = selected == null ? -1 : Entries.FindIndex(entry => String.Equals(entry.SourcePath, selected, StringComparison.Ordinal));
                List.SelectedIndex = index >= 0 ? index : Entries.Count > 0 ? 0 : -1;
            }
            finally { loading = false; }
            ShowSelection();
        }

        internal void ShowSelection()
        {
            var item = List.SelectedItem as ListBoxItem;
            MinuteGroup group;
            if (item != null && item.Visibility == Visibility.Collapsed && minuteGroups.TryGetValue(item, out group)) SetGroupExpanded(group, true);
            string path = item == null ? null : item.Tag as string;
            HistoryVersion entry = Entries.FirstOrDefault(value => String.Equals(value.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            foreach (var pair in summaries)
            {
                bool selected = String.Equals(pair.Key, path, StringComparison.OrdinalIgnoreCase);
                pair.Value.Background = selected ? pale : Brushes.Transparent;
                pair.Value.BorderThickness = new Thickness(selected ? 2 : 0, 0, 0, 0);
            }
            foreach (var pair in markers) pair.Value.Fill = entry != null && pair.Key == entry.StoredAt.Date ? green : Brush("#C9CEC6");
            if (entry == null) { Reader.Clear(); return; }
            if (entry.State == null)
            {
                Reader.Show("这份历史记录暂时无法读取，原文件仍保留。其他记录和当前内容不受影响。", null, entry.StoredAt, true, false);
                Reader.CopyButton.IsEnabled = false; return;
            }
            Reader.Show(entry.State.Ideas, entry.State, entry.StoredAt, showMemo, showTasks);
        }

        private void BuildIndex(string selected)
        {
            List.Items.Clear(); summaries.Clear(); markers.Clear(); minuteGroups.Clear();
            var counts = Entries.GroupBy(entry => entry.StoredAt.Ticks / TimeSpan.TicksPerMinute).ToDictionary(group => group.Key, group => group.Count());
            var groups = new Dictionary<long, MinuteGroup>();
            DateTime? previousDate = null; string previousMonth = null;
            foreach (HistoryVersion entry in Entries)
            {
                long minute = entry.StoredAt.Ticks / TimeSpan.TicksPerMinute;
                MinuteGroup group; bool first = !groups.TryGetValue(minute, out group);
                if (first) { group = new MinuteGroup { Key = entry.SourcePath, Time = entry.StoredAt, Count = counts[minute] }; groups.Add(minute, group); }
                string month = entry.StoredAt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var item = MakeItem(entry, previousDate != entry.StoredAt.Date, previousMonth != month, first && group.Count > 1 ? group : null);
                group.Items.Add(item); minuteGroups.Add(item, group); List.Items.Add(item);
                previousDate = entry.StoredAt.Date; previousMonth = month;
            }
            foreach (MinuteGroup group in groups.Values.Where(value => value.Count > 1))
                SetGroupExpanded(group, expandedGroups.Contains(group.Key) || group.Items.Skip(1).Any(item => String.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase)));
        }

        private void SetGroupExpanded(MinuteGroup group, bool open)
        {
            if (group.Toggle == null) return;
            if (!open && group.Items.Skip(1).Contains(List.SelectedItem as ListBoxItem)) List.SelectedItem = group.Items[0];
            foreach (ListBoxItem item in group.Items.Skip(1)) item.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) expandedGroups.Add(group.Key); else expandedGroups.Remove(group.Key);
            group.Toggle.Content = (open ? "收起 " : "展开 ") + (group.Count - 1) + " 条";
            AutomationProperties.SetName(group.Toggle, (open ? "收起" : "展开") + group.Time.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture) + "的另外" + (group.Count - 1) + "条记录");
        }

        private ListBoxItem MakeItem(HistoryVersion entry, bool newDate, bool newMonth, MinuteGroup group)
        {
            var content = new StackPanel();
            if (newMonth) content.Children.Add(new TextBlock { Text = entry.StoredAt.ToString("yyyy / MM", CultureInfo.InvariantCulture), FontSize = 12, Foreground = muted, Margin = new Thickness(2, newMonth && List.Items.Count == 0 ? 2 : 16, 0, 8) });
            var spine = new Grid();
            spine.Children.Add(new Border { Width = 1, Background = rule, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(7, 0, 0, 0) });
            var text = new StackPanel { Margin = new Thickness(27, 0, 0, 0) };
            if (newDate)
            {
                var marker = new Ellipse { Width = 9, Height = 9, Fill = Brush("#C9CEC6"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(3, 12, 0, 0), IsHitTestVisible = false };
                AutomationProperties.SetName(marker, entry.StoredAt.ToString("yyyy年M月d日", CultureInfo.InvariantCulture) + "日期节点");
                spine.Children.Add(marker); markers.Add(entry.StoredAt.Date, marker);
                var day = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                day.Children.Add(new TextBlock { Text = entry.StoredAt.ToString("dd", CultureInfo.InvariantCulture), FontSize = 18, Foreground = ink, FontWeight = FontWeights.Medium });
                day.Children.Add(new TextBlock { Text = entry.StoredAt.ToString("M月d日", CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = muted, Margin = new Thickness(0, 1, 0, 0) });
                text.Children.Add(day);
            }
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = entry.Title, FontSize = 12.5, Foreground = ink, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 18, MaxHeight = 36 });
            row.Children.Add(new TextBlock { Text = entry.StoredAt.ToString("HH:mm", CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = muted, Margin = new Thickness(0, 3, 0, 0) });
            var summary = new Border { Child = row, BorderBrush = green, CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 4, 7, 4), Margin = new Thickness(-6, 0, 0, 3) };
            summaries.Add(entry.SourcePath, summary); text.Children.Add(summary);
            if (group != null)
            {
                group.Toggle = new Button { Style = (Style)View.Resources["MinuteToggle"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-2, 0, 0, 6), ToolTip = "同一分钟的其他记录；展开后仍可逐条查看和取回" };
                group.Toggle.Click += delegate(object sender, RoutedEventArgs e) { SetGroupExpanded(group, !expandedGroups.Contains(group.Key)); e.Handled = true; };
                text.Children.Add(group.Toggle);
            }
            spine.Children.Add(text); content.Children.Add(spine);
            var item = new ListBoxItem { Content = content, Tag = entry.SourcePath, ToolTip = entry.Title + " · " + entry.StoredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
            AutomationProperties.SetName(item, entry.StoredAt.ToString("yyyy年M月d日 HH:mm", CultureInfo.InvariantCulture) + "，" + entry.Title);
            return item;
        }

        private static string SummaryTitle(WorkboardState state)
        {
            if (!String.IsNullOrWhiteSpace(state.MemoTitle)) return state.MemoTitle;
            foreach (string raw in (state.Ideas ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim().TrimStart('#').Trim();
                if (String.IsNullOrWhiteSpace(line) || new[] { "随手记", "问题", "问题：", "问题:", "我的能力", "我的能力：" }.Contains(line)) continue;
                int start = 0;
                while (start < line.Length && (Char.IsDigit(line[start]) || line[start] == '.' || line[start] == '、' || line[start] == '-' || Char.IsWhiteSpace(line[start]))) start++;
                if (start < line.Length) line = line.Substring(start);
                return line.Length > 30 ? line.Substring(0, 29) + "…" : line;
            }
            WorkTask task = (state.Tasks ?? new List<WorkTask>()).FirstOrDefault(value => value != null && !String.IsNullOrWhiteSpace(value.Text));
            if (task != null) return task.Text.Length > 30 ? task.Text.Substring(0, 29) + "…" : task.Text;
            return (state.Removed ?? new List<WorkTask>()).Any(value => value != null) ? "任务移除记录" : "日常记录";
        }

        private static string TaskContent(WorkboardState snapshot)
        {
            var text = new StringBuilder();
            foreach (var group in new[] { snapshot.Tasks, snapshot.Removed })
            {
                text.Append('|'); if (group == null) continue;
                foreach (var task in group.Where(value => value != null))
                    foreach (string part in new[] { task.Id, task.Text, task.Done ? "1" : "0", task.PlanDate, task.CompletedAt, task.RemovedIndex.HasValue ? task.RemovedIndex.Value.ToString(CultureInfo.InvariantCulture) : "" })
                    { string value = part ?? ""; text.Append(value.Length).Append(':').Append(value); }
            }
            return text.ToString();
        }

        private static bool Boundary(char value) { return "。！？!?；;\n.".IndexOf(value) >= 0; }
        private static bool IsSupersededInput(string older, string newer)
        {
            string before = older.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd(' ', '\t');
            string after = newer.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd(' ', '\t');
            if (before.Length == 0 || Boundary(before[before.Length - 1])) return false;
            int boundary = before.Length - 1; while (boundary >= 0 && !Boundary(before[boundary])) boundary--;
            string prefix = before.Substring(0, boundary + 1); if (!after.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string input = before.Substring(boundary + 1).TrimStart(), completed = after.Substring(boundary + 1).TrimStart();
            int common = 0; while (common < input.Length && common < completed.Length && input[common] == completed[common]) common++;
            int removed = input.Length - common;
            if (removed >= 20 && removed >= input.Length * 0.3) return false;
            return common > 0 && (common == input.Length || common >= 2);
        }

        private static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromInvariantString(value); }
    }
}
