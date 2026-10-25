using System;
using System.Collections.Generic;
using System.Globalization;
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
    // A history record is a document, never an editor or a second task controller.
    // The original note is kept separately for copying; presentation never writes
    // back to the snapshot or the shared store.
    internal sealed class HistoryReadingView
    {
        internal readonly Grid View;
        internal readonly RichTextBox Document;
        internal readonly Button CopyButton;
        internal string CopyText { get; private set; }
        internal string MemoText { get; private set; }
        internal int TaskRowCount { get; private set; }

        private readonly TextBlock date, copyLabel;
        private readonly Brush ink = Brush("#25392F");
        private readonly Brush muted = Brush("#68746B");
        private readonly Brush completed = Brush("#5E6D62");
        private readonly Brush green = Brush("#58715E");
        private readonly Brush rule = Brush("#E3E6DD");
        private readonly Brush paper = Brush("#FDFCF9");
        private DateTime recordDate;

        internal HistoryReadingView()
        {
            View = new Grid { Background = paper, ClipToBounds = true };
            View.SetValue(TextElement.FontFamilyProperty, AppTypography.Body);
            View.SetValue(TextElement.ForegroundProperty, ink);
            View.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            View.RowDefinitions.Add(new RowDefinition());

            var header = new Grid { Margin = new Thickness(24, 18, 24, 13) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            date = new TextBlock { Foreground = ink, FontSize = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 12, 0), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(date, "历史版本时间");
            header.Children.Add(date);
            var tools = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            tools.Children.Add(new TextBlock { Text = "只读历史", FontSize = 10.5, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) });
            var copyContent = new StackPanel { Orientation = Orientation.Horizontal };
            copyContent.Children.Add(AppVisuals.Icon("copy", 15, green));
            copyLabel = new TextBlock { Text = "复制内容", FontSize = 12.5, Foreground = green, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            copyContent.Children.Add(copyLabel);
            CopyButton = new Button { Content = copyContent, Padding = new Thickness(5, 3, 0, 3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, Cursor = System.Windows.Input.Cursors.Hand, IsEnabled = false };
            CopyButton.Template = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}' BorderBrush='#879985' BorderThickness='0' CornerRadius='3'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#EEF1E9'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            CopyButton.Click += delegate { CopyCurrentText(); };
            CopyButton.ToolTip = "复制这份历史记录的完整文字";
            AutomationProperties.SetName(CopyButton, "复制历史记录内容");
            tools.Children.Add(CopyButton); Grid.SetColumn(tools, 1); header.Children.Add(tools); View.Children.Add(header);

            Document = new RichTextBox
            {
                IsReadOnly = true, IsReadOnlyCaretVisible = false, IsDocumentEnabled = true,
                Background = Brushes.Transparent, Foreground = ink, BorderThickness = new Thickness(0),
                Padding = new Thickness(24, 0, 24, 22), FontFamily = AppTypography.Body, FontSize = 15,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                SelectionBrush = Brush("#CBD9C9"), FocusVisualStyle = null
            };
            TextOptions.SetTextFormattingMode(Document, TextFormattingMode.Display);
            AutomationProperties.SetName(Document, "历史内容只读预览");
            Grid.SetRow(Document, 1); View.Children.Add(Document);
            Clear();
        }

        internal void Clear()
        {
            CopyText = MemoText = ""; TaskRowCount = 0;
            date.Text = "选择一份记录"; copyLabel.Text = "复制内容"; CopyButton.IsEnabled = false;
            var document = NewDocument();
            document.Blocks.Add(new Paragraph(new Run("选择左侧的日期，查看当时写下的内容。")) { Foreground = muted, FontSize = 13, Margin = new Thickness(0, 20, 0, 0) });
            Document.Document = document;
        }

        internal void Show(string memo, WorkboardState snapshot, DateTime storedAt, bool showMemo, bool showTasks)
        {
            recordDate = storedAt;
            MemoText = showMemo ? memo ?? "" : "";
            TaskRowCount = 0;
            date.Text = storedAt.ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture);
            copyLabel.Text = "复制内容";
            CopyButton.ToolTip = "复制这份历史记录的完整文字";
            var document = NewDocument();
            bool hasMemo = showMemo && !String.IsNullOrWhiteSpace(MemoText);
            if (showMemo && snapshot != null && !String.IsNullOrWhiteSpace(snapshot.MemoTitle))
                document.Blocks.Add(new Paragraph(new Run(snapshot.MemoTitle)) { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 14), KeepWithNext = true });
            if (hasMemo) AppendMemo(document, MemoText);
            if (showTasks)
            {
                var tasks = (snapshot == null ? Enumerable.Empty<WorkTask>() : snapshot.Tasks ?? Enumerable.Empty<WorkTask>()).Where(task => task != null).ToList();
                var removed = (snapshot == null ? Enumerable.Empty<WorkTask>() : snapshot.Removed ?? Enumerable.Empty<WorkTask>()).Where(task => task != null).ToList();
                if (hasMemo) document.Blocks.Add(SectionRule());
                AppendTasks(document, tasks, "任务", false);
                if (removed.Count > 0)
                {
                    document.Blocks.Add(SectionRule());
                    AppendTasks(document, removed, "当时已移除的任务", true);
                }
            }
            if (!hasMemo && !showTasks)
                document.Blocks.Add(new Paragraph(new Run("这份记录没有随手记内容。")) { Foreground = muted, FontSize = 13, Margin = new Thickness(0, 8, 0, 0) });
            Document.Document = document;
            Document.ScrollToHome();
            CopyText = MakeCopyText(MemoText, snapshot, showMemo, showTasks);
            CopyButton.IsEnabled = CopyText.Length > 0;
        }

        private FlowDocument NewDocument()
        {
            return new FlowDocument
            {
                FontFamily = AppTypography.Body, FontSize = 15, Foreground = ink,
                PagePadding = new Thickness(0), ColumnWidth = Double.PositiveInfinity,
                TextAlignment = TextAlignment.Left, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
        }

        private void AppendMemo(FlowDocument document, string original)
        {
            string[] lines = original.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool first = true, separated = false;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (String.IsNullOrWhiteSpace(line)) { separated = true; continue; }
                bool followed = lines.Skip(index + 1).Any(value => !String.IsNullOrWhiteSpace(value));
                string trimmed = line.Trim();
                bool numbered = trimmed.Length > 0 && (Char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '•');
                bool heading = followed && !numbered && trimmed.Length <= (first ? 36 : 14) && (first || separated || trimmed.EndsWith("：", StringComparison.Ordinal) || trimmed.EndsWith(":", StringComparison.Ordinal));
                var paragraph = new Paragraph(new Run(line))
                {
                    FontSize = heading ? (first ? 18 : 16) : 15,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                    LineHeight = heading ? 28 : 26,
                    Margin = new Thickness(0, first ? 3 : separated ? 16 : 0, 0, heading ? 6 : 1),
                    KeepWithNext = heading
                };
                document.Blocks.Add(paragraph); first = false; separated = false;
            }
        }

        private Block SectionRule()
        {
            return new Paragraph { BorderBrush = rule, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 20, 0, 14), FontSize = 1, LineHeight = 1 };
        }

        private void AppendTasks(FlowDocument document, IList<WorkTask> tasks, string heading, bool removed)
        {
            var headingText = new Paragraph { Margin = new Thickness(0, 0, 0, 8), LineHeight = 26, KeepWithNext = true };
            headingText.Inlines.Add(new Run(heading) { FontSize = removed ? 14 : 16, FontWeight = FontWeights.SemiBold });
            headingText.Inlines.Add(new Run("  " + tasks.Count.ToString(CultureInfo.InvariantCulture) + " 项") { FontSize = 11.5, Foreground = muted });
            document.Blocks.Add(headingText);
            if (tasks.Count == 0)
            {
                document.Blocks.Add(new Paragraph(new Run("当时没有任务")) { FontSize = 13, Foreground = muted, Margin = new Thickness(0, 0, 0, 6) });
                return;
            }
            var table = new Table { CellSpacing = 0, Margin = new Thickness(0) };
            table.Columns.Add(new TableColumn { Width = new GridLength(32) });
            table.Columns.Add(new TableColumn { Width = GridLength.Auto });
            table.Columns.Add(new TableColumn { Width = new GridLength(91) });
            var group = new TableRowGroup(); table.RowGroups.Add(group);
            for (int index = 0; index < tasks.Count; index++)
            {
                WorkTask task = tasks[index];
                bool last = index == tasks.Count - 1;
                var row = new TableRow();
                var icon = AppVisuals.Icon(task.Done ? "history-done" : "history-pending", 19, task.Done ? green : ink);
                icon.IsHitTestVisible = true;
                AutomationProperties.SetName(icon, (task.Done ? "已完成" : "待办") + "，只读历史状态");
                icon.ToolTip = task.Done ? "当时已完成" : "当时待办";
                var iconCell = NewCell(last); iconCell.Padding = new Thickness(1, 9, 8, 9);
                iconCell.Blocks.Add(new BlockUIContainer(icon) { Margin = new Thickness(0) }); row.Cells.Add(iconCell);
                var titleCell = NewCell(last);
                titleCell.Blocks.Add(new Paragraph(new Run(task.Text ?? "")) { Foreground = task.Done || removed ? completed : ink, FontSize = 14, Margin = new Thickness(0), LineHeight = 23 });
                row.Cells.Add(titleCell);
                var planCell = NewCell(last); planCell.Padding = new Thickness(6, 9, 0, 9);
                planCell.Blocks.Add(new Paragraph(new Run(PlanLabel(task.PlanDate))) { Foreground = muted, FontSize = 11.5, TextAlignment = TextAlignment.Right, Margin = new Thickness(0), LineHeight = 23 });
                row.Cells.Add(planCell); group.Rows.Add(row); TaskRowCount++;
            }
            document.Blocks.Add(table);
        }

        private TableCell NewCell(bool last)
        {
            return new TableCell { Padding = new Thickness(0, 9, 0, 9), BorderBrush = rule, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 0.7) };
        }

        private string PlanLabel(string value)
        {
            DateTime plan;
            if (!WorkboardStore.TryPlanDate(value, out plan)) return String.IsNullOrEmpty(value) ? "未安排" : "日期待确认";
            return plan.ToString(plan.Year == recordDate.Year ? "M月d日" : "yyyy年M月d日", CultureInfo.InvariantCulture);
        }

        private static string MakeCopyText(string memo, WorkboardState snapshot, bool showMemo, bool showTasks)
        {
            if (showMemo && !showTasks) return memo ?? "";
            var text = new StringBuilder();
            if (showMemo && !String.IsNullOrEmpty(memo)) text.Append(memo);
            if (showTasks)
            {
                if (text.Length > 0) text.AppendLine().AppendLine();
                text.AppendLine("任务");
                AppendCopiedTasks(text, snapshot == null ? null : snapshot.Tasks);
                if (snapshot != null && snapshot.Removed != null && snapshot.Removed.Any(task => task != null))
                {
                    text.AppendLine().AppendLine("当时已移除的任务"); AppendCopiedTasks(text, snapshot.Removed);
                }
            }
            return text.ToString().TrimEnd('\r', '\n');
        }

        private static void AppendCopiedTasks(StringBuilder text, IEnumerable<WorkTask> tasks)
        {
            foreach (WorkTask task in (tasks ?? Enumerable.Empty<WorkTask>()).Where(value => value != null))
            {
                DateTime plan;
                string dateText = WorkboardStore.TryPlanDate(task.PlanDate, out plan) ? plan.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : String.IsNullOrEmpty(task.PlanDate) ? "未安排" : "日期待确认：" + task.PlanDate;
                text.Append(task.Done ? "已完成：" : "待办：").AppendLine(task.Text ?? "");
                text.Append("计划日期：").AppendLine(dateText);
            }
        }

        private void CopyCurrentText()
        {
            if (String.IsNullOrEmpty(CopyText)) return;
            try
            {
                Clipboard.SetText(CopyText);
                copyLabel.Text = "已复制";
                CopyButton.ToolTip = "已复制这份记录的完整文字";
            }
            catch
            {
                copyLabel.Text = "复制内容";
                CopyButton.ToolTip = "暂时无法访问剪贴板，可选中文字后按 Ctrl+C 复制。";
            }
        }

        private static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromInvariantString(value); }
    }
}
