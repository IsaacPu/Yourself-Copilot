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
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace LocalDesktopNote
{
    // A normal, dismissible management window. The desktop card and this window
    // deliberately share one Store; neither keeps a second copy of user content.
    internal sealed class WorkbenchWindow : Forms.Form
    {
        internal readonly WorkboardStore Store;
        internal readonly Grid View;
        internal readonly TextBox NewTaskEditor, IdeasEditor;
        internal readonly DatePicker DraftDatePicker;
        internal readonly CheckBox UnplannedCheck;
        internal readonly Expander CompletedGroup;
        internal readonly List<TextBox> TaskEditors = new List<TextBox>();
        private readonly HashSet<TextBox> editedTaskTitles = new HashSet<TextBox>();
        internal readonly List<CheckBox> TaskChecks = new List<CheckBox>();
        internal readonly List<CheckBox> CompletedChecks = new List<CheckBox>();
        internal readonly Dictionary<string, Button> ScopeButtons = new Dictionary<string, Button>();
        internal readonly ListBox HistoryList;
        internal readonly TextBox HistoryPreview;
        internal readonly CheckBox LaunchAtLoginCheck, StartupCardCheck;
        internal Action OpenPictures, ShowDesktopCard;
        internal Func<bool> GetAutoStart;
        internal Action<bool> SetAutoStart;
        internal bool AllowClose;

        private readonly System.Windows.Forms.Integration.ElementHost host;
        private readonly Forms.Timer saveTimer;
        private readonly Grid content;
        private readonly StackPanel taskRows, completedRows;
        private readonly TextBlock status, taskSummary, taskEmpty, historyNotice, settingsNotice;
        private readonly Button undoButton;
        private readonly Border compactMemoHost, fullMemoHost;
        private readonly MemoSplitLayout memoSplit;
        private readonly MemoPaperControls compactPaperControls, fullPaperControls;
        private readonly HistoryPanel historyPanel;
        private readonly TextBlock compactMemoHint, fullMemoHint;
        private readonly TextBlock newTaskPlaceholder;
        private TextBlock taskHeading, taskDate, draftDateSummary, memoDate;
        private readonly Dictionary<string, FrameworkElement> pages = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, Button> pageButtons = new Dictionary<string, Button>();
        private bool loading, saving, rendering, dirty, viewDirty, pendingRefresh, disposed, loadingSettings, saveFailed;
        private string page = "tasks", lastMemoText, lastMemoDate, lastMemoTaskId;
        private string saveFailureMessage;
        private string pendingMemoTaskId;
        private ContextMenu activeTaskMenu;
        private DateTime lastMemoConvertedAt;
        private DateTime displayedDay;
        private readonly Brush ink = Brush("#344438"), muted = Brush("#7D877C"), green = Brush("#64806B"), pale = Brush("#E7EBE1");

        internal string CurrentScope { get { return Store.State.TaskScope; } }

        internal WorkbenchWindow(WorkboardStore store)
        {
            Store = store;
            displayedDay = Store.Today;
            Text = "Yourself Copilot · 工作台";
            Icon = AppVisuals.ApplicationIcon;
            AccessibleName = "Yourself Copilot 工作台";
            StartPosition = Forms.FormStartPosition.CenterScreen;
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            // WinForms runtime-created forms have no designer DPI baseline.
            // Keep the WPF layout readable while fitting the primary work area.
            double initialScale;
            using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) initialScale = graphics.DpiX / 96.0;
            var initialArea = Forms.Screen.PrimaryScreen.WorkingArea;
            int initialWidth = Math.Min((int)Math.Round(1040 * initialScale), Math.Max(1, initialArea.Width - 48));
            int initialHeight = Math.Min((int)Math.Round(840 * initialScale), Math.Max(1, initialArea.Height - 48));
            MinimumSize = new System.Drawing.Size(Math.Min((int)Math.Round(850 * initialScale), initialWidth), Math.Min((int)Math.Round(620 * initialScale), initialHeight));
            Size = new System.Drawing.Size(initialWidth, initialHeight);
            BackColor = System.Drawing.Color.FromArgb(250, 248, 243);

            View = new Grid { Background = Brush("#FAF8F3") };
            View.SetValue(TextElement.FontFamilyProperty, AppTypography.Body);
            View.SetValue(TextElement.FontSizeProperty, 13.0);
            View.SetValue(TextElement.ForegroundProperty, ink);
            View.Resources = (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#344438'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='12,8'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Body' CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderBrush='#64806B'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Background' Value='#E7EBE1'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <ControlTemplate x:Key='NavigationButtonTemplate' TargetType='Button'><Border x:Name='Body' CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderBrush='#64806B'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><ContentPresenter Content='{TemplateBinding Tag}' VerticalAlignment='Center' Margin='0,0,12,0'/><ContentPresenter Grid.Column='1' Content='{TemplateBinding Content}' VerticalAlignment='Center'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Background' Value='#E7EBE1'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate>
 <ControlTemplate x:Key='IconButtonTemplate' TargetType='Button'><Border x:Name='Body' CornerRadius='8' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderBrush='#64806B'><ContentPresenter Content='{TemplateBinding Tag}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Body' Property='Opacity' Value='0.72'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Body' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate>
 <Style TargetType='TextBox'><Setter Property='Foreground' Value='#344438'/><Setter Property='Background' Value='#FDFCF9'/><Setter Property='BorderBrush' Value='#E1E3DA'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='9,6'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='SelectionBrush' Value='#CBD9C9'/><Setter Property='CaretBrush' Value='#64806B'/></Style>
 <Style TargetType='CheckBox'><Setter Property='Foreground' Value='#64806B'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='16'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><Border x:Name='Box' Width='16' Height='16' CornerRadius='3' BorderBrush='#97A58E' BorderThickness='1' Background='Transparent' VerticalAlignment='Center'><Path x:Name='Tick' Data='M 2,7 L 5.5,10.5 L 12,3.5' Stroke='White' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed'/></Border><ContentPresenter x:Name='Label' Grid.Column='1' Margin='7,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/></Grid><ControlTemplate.Triggers><Trigger Property='HasContent' Value='False'><Setter TargetName='Label' Property='Visibility' Value='Collapsed'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#64806B'/><Setter TargetName='Box' Property='BorderBrush' Value='#64806B'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#344438'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#344438'/><Setter TargetName='Box' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 </ResourceDictionary>");
            View.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            View.ColumnDefinitions.Add(new ColumnDefinition());

            var sidebar = new DockPanel { Margin = new Thickness(14, 28, 14, 18) };
            View.Children.Add(new Border { Background = Brush("#F1F2EB"), BorderBrush = Brush("#E4E6DC"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 34) };
            var companion = AppVisuals.BrandIcon(27); companion.VerticalAlignment = VerticalAlignment.Center; brand.Children.Add(companion);
            var wordmark = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            wordmark.Children.Add(new TextBlock { Text = "Yourself", FontFamily = new FontFamily("Segoe UI"), FontSize = 15, FontWeight = FontWeights.SemiBold });
            wordmark.Children.Add(new TextBlock { Text = "Copilot", FontFamily = new FontFamily("Segoe UI"), FontSize = 15, Foreground = green });
            brand.Children.Add(wordmark);
            DockPanel.SetDock(brand, System.Windows.Controls.Dock.Top); sidebar.Children.Add(brand);
            var links = new StackPanel(); DockPanel.SetDock(links, System.Windows.Controls.Dock.Top); sidebar.Children.Add(links);
            AddPageButton(links, "tasks", "任务"); AddPageButton(links, "memo", "随手记"); AddPageButton(links, "history", "历史记录"); AddPageButton(links, "settings", "设置");
            var desktop = MakeButton("显示桌面卡片", delegate { if (Flush() && ShowDesktopCard != null) ShowDesktopCard(); });
            SetNavigationIcon(desktop, "expand"); desktop.FontSize = 11;
            desktop.Margin = new Thickness(0, 8, 0, 0); DockPanel.SetDock(desktop, System.Windows.Controls.Dock.Bottom); sidebar.Children.Add(desktop);
            var pictures = MakeButton("管理桌面图片", delegate { if (Flush() && OpenPictures != null) OpenPictures(); });
            SetNavigationIcon(pictures, "images"); pictures.FontSize = 11;
            DockPanel.SetDock(pictures, System.Windows.Controls.Dock.Bottom); sidebar.Children.Add(pictures);
            sidebar.Children.Add(new Border());

            var main = new Grid { Margin = new Thickness(34, 27, 32, 16) };
            Grid.SetColumn(main, 1); View.Children.Add(main);
            main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content = new Grid(); main.Children.Add(content);
            status = new TextBlock { Text = "仅保存在本机 · 内容自动保存", FontSize = 10.5, Foreground = muted, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(status, 1); main.Children.Add(status);

            var tasks = NewPage("tasks", "今天", "");
            tasks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tasks.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tasks.RowDefinitions.Add(new RowDefinition());
            var scopeBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            AddScopeButton(scopeBar, "today", "今天"); AddScopeButton(scopeBar, "tomorrow", "明天"); AddScopeButton(scopeBar, "later", "以后／未安排"); AddScopeButton(scopeBar, "all", "全部");
            Grid.SetRow(scopeBar, 1); tasks.Children.Add(scopeBar);
            var draftPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var draftLine = new Grid(); draftLine.ColumnDefinitions.Add(new ColumnDefinition()); draftLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            NewTaskEditor = new TextBox { MaxLength = 240, MinHeight = 44, FontSize = 14, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(14, 7, 8, 7), ToolTip = "输入任务，按 Enter 添加；内容会作为草稿保存" };
            AutomationProperties.SetName(NewTaskEditor, "工作台新任务"); draftLine.Children.Add(NewTaskEditor);
            newTaskPlaceholder = new TextBlock { Text = "添加一个任务…", FontSize = 14, Foreground = Brush("#9AA195"), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; draftLine.Children.Add(newTaskPlaceholder);
            var add = MakeButton("添加任务", delegate { AddDraft(); }); SetIconOnly(add, "plus", Brushes.White, 20); add.Background = green; add.Width = 34; add.Height = 34; add.Padding = new Thickness(6); add.Margin = new Thickness(5); add.ToolTip = "添加任务"; Grid.SetColumn(add, 1); draftLine.Children.Add(add);
            draftPanel.Children.Add(new Border { CornerRadius = new CornerRadius(9), BorderBrush = Brush("#DDE1D7"), BorderThickness = new Thickness(1), Background = Brush("#FDFCF9"), Child = draftLine });
            var dateLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            DraftDatePicker = new DatePicker { Width = 142, SelectedDateFormat = DatePickerFormat.Short, VerticalContentAlignment = VerticalAlignment.Center, Language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN") };
            AutomationProperties.SetName(DraftDatePicker, "新任务计划日期"); dateLine.Children.Add(DraftDatePicker);
            dateLine.Children.Add(MakeButton("今天", delegate { SetDraftDate(Store.Today); }));
            dateLine.Children.Add(MakeButton("明天", delegate { SetDraftDate(Store.Today.AddDays(1)); }));
            UnplannedCheck = new CheckBox { Content = "未安排", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) }; dateLine.Children.Add(UnplannedCheck);
            var dateHeader = new StackPanel { Orientation = Orientation.Horizontal }; dateHeader.Children.Add(AppVisuals.Icon("calendar", 13, muted));
            draftDateSummary = new TextBlock { Foreground = muted, FontSize = 11, Margin = new Thickness(7, 0, 0, 0) }; dateHeader.Children.Add(draftDateSummary);
            var dateExpander = new Expander { Header = dateHeader, Content = dateLine, Margin = new Thickness(0, 7, 0, 0), Foreground = muted, ToolTip = "展开修改这份草稿的计划日期；日期不会随浏览悄悄变化" };
            AutomationProperties.SetName(dateExpander, "新任务计划日期，展开设置"); draftPanel.Children.Add(dateExpander); Grid.SetRow(draftPanel, 2); tasks.Children.Add(draftPanel);
            var splitGrid = new Grid(); splitGrid.RowDefinitions.Add(new RowDefinition()); splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); splitGrid.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(splitGrid, 3); tasks.Children.Add(splitGrid);
            var list = new StackPanel();
            var countLine = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            undoButton = MakeButton("撤销移除", delegate { UndoRemove(); }); undoButton.FontSize = 11; undoButton.Padding = new Thickness(6, 2, 6, 2); DockPanel.SetDock(undoButton, System.Windows.Controls.Dock.Right); countLine.Children.Add(undoButton);
            taskSummary = new TextBlock { Foreground = muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; countLine.Children.Add(taskSummary); list.Children.Add(countLine);
            taskEmpty = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = muted, Margin = new Thickness(0, 15, 0, 18) }; list.Children.Add(taskEmpty);
            taskRows = new StackPanel(); list.Children.Add(taskRows);
            completedRows = new StackPanel(); CompletedGroup = new Expander { Header = "已完成", Foreground = muted, Margin = new Thickness(0, 17, 0, 5), Padding = new Thickness(0, 8, 0, 0), Content = completedRows };
            AutomationProperties.SetName(CompletedGroup, "工作台已完成任务，展开或收起"); list.Children.Add(CompletedGroup);
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
            splitGrid.Children.Add(scroll);
            var splitLine = new Border { Height = 1, Background = Brush("#DDE2D7"), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Grid.SetRow(splitLine, 1); splitGrid.Children.Add(splitLine);
            var divider = MemoSplitLayout.CreateDivider(); Grid.SetRow(divider, 1); splitGrid.Children.Add(divider);

            var memoCard = new Grid(); memoCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); memoCard.RowDefinitions.Add(new RowDefinition());
            var memoCardHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var expandMemo = MakeButton("展开随手记", delegate { Navigate("memo"); }); SetIconOnly(expandMemo, "expand", muted, 15); expandMemo.Padding = new Thickness(3); expandMemo.ToolTip = "打开完整随手记"; DockPanel.SetDock(expandMemo, System.Windows.Controls.Dock.Right); memoCardHeader.Children.Add(expandMemo);
            compactPaperControls = new MemoPaperControls(Store, Flush, ShowStatus, FocusMemoEditor, true);
            memoCardHeader.Children.Add(compactPaperControls.View); memoCard.Children.Add(memoCardHeader);
            compactMemoHost = new Border(); Grid.SetRow(compactMemoHost, 1); memoCard.Children.Add(compactMemoHost);
            compactMemoHint = new TextBlock { Text = "先记下来，写完可以收起换新。", Foreground = muted, Margin = new Thickness(0, 4, 0, 0), IsHitTestVisible = false, FontFamily = AppTypography.Memo, FontSize = 16 };
            Grid.SetRow(compactMemoHint, 1); memoCard.Children.Add(compactMemoHint);
            var compactMemo = new Border { CornerRadius = new CornerRadius(10), Background = Brush("#FDFCF9"), BorderBrush = Brush("#E5E7DE"), BorderThickness = new Thickness(1), Padding = new Thickness(18), Child = memoCard };
            compactPaperControls.PaperSurface = compactMemo;
            Grid.SetRow(compactMemo, 2); splitGrid.Children.Add(compactMemo);
            memoSplit = new MemoSplitLayout(splitGrid, list, divider, 220, () => Store.State.WorkbenchMemoTaskRatio, value => { Store.State.WorkbenchMemoTaskRatio = value; QueueViewSave(); Flush(); });

            var memo = NewPage("memo", "随手记", "写完收好，给下一个念头留一张空白。");
            memo.RowDefinitions.Add(new RowDefinition()); memo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            IdeasEditor = new TextBox { FontFamily = AppTypography.Memo, FontSize = 16, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 2, 0, 0), ToolTip = "自动保存在本机。转为任务时保留原备忘；一次最多 240 字。" };
            AutomationProperties.SetName(IdeasEditor, "工作台随手记");
            var fullPaper = new Grid(); fullPaper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); fullPaper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); fullPaper.RowDefinitions.Add(new RowDefinition());
            fullPaperControls = new MemoPaperControls(Store, Flush, ShowStatus, FocusMemoEditor, false);
            fullPaperControls.View.Children.Remove(fullPaperControls.Grabber);
            ((Panel)fullPaperControls.Bin.Parent).Children.Remove(fullPaperControls.Bin);
            fullPaperControls.View.Margin = new Thickness(0, 0, 0, 20); fullPaper.Children.Add(fullPaperControls.View);
            var paperIntro = new TextBlock { Text = "把念头先放在这里", FontFamily = AppTypography.Body, FontSize = 28, FontWeight = FontWeights.Normal, Margin = new Thickness(4, 0, 0, 20), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(paperIntro, 1); fullPaper.Children.Add(paperIntro);
            fullMemoHost = new Border { Background = Brushes.Transparent, Margin = new Thickness(4, 0, 6, 42) }; Grid.SetRow(fullMemoHost, 2); fullPaper.Children.Add(fullMemoHost);
            fullMemoHint = new TextBlock { Text = "写下此刻想到的，不用整理。", Foreground = Brush("#9AA195"), FontFamily = AppTypography.Body, FontSize = 18, Margin = new Thickness(4, 4, 0, 0), IsHitTestVisible = false };
            Grid.SetRow(fullMemoHint, 2); fullPaper.Children.Add(fullMemoHint);
            var paperSurface = new Border { CornerRadius = new CornerRadius(13, 13, 82, 13), Background = Brush("#FDFCF9"), BorderBrush = Brush("#E5E7DE"), BorderThickness = new Thickness(1), Padding = new Thickness(24), Child = fullPaper, Margin = new Thickness(0, 12, 0, 63), Effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(180, 179, 166), Opacity = 0.18, BlurRadius = 18, ShadowDepth = 5 } };
            fullPaperControls.PaperSurface = paperSurface;
            var paperStage = new Grid { Margin = new Thickness(8, 0, 30, 0) }; paperStage.Children.Add(paperSurface);
            var fold = new Image { Source = AppVisuals.IllustrationSource("memo-paper-corner.png"), Width = 158, Height = 145, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, 54), IsHitTestVisible = false, Opacity = 0.68 };
            paperStage.Children.Add(fold);
            var paperGrip = fullPaperControls.Grabber; paperGrip.Width = 136; paperGrip.Height = 32; paperGrip.Padding = new Thickness(10, 4, 10, 4); paperGrip.Background = Brush("#FDFCF9"); paperGrip.HorizontalAlignment = HorizontalAlignment.Center; paperGrip.VerticalAlignment = VerticalAlignment.Top;
            var gripHost = new Border { CornerRadius = new CornerRadius(16), Background = Brush("#FDFCF9"), BorderBrush = Brush("#E5E7DE"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = paperGrip, Effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(160, 160, 148), Opacity = 0.2, BlurRadius = 8, ShadowDepth = 2 } };
            paperStage.Children.Add(gripHost);
            fullPaperControls.Bin.HorizontalAlignment = HorizontalAlignment.Right; fullPaperControls.Bin.VerticalAlignment = VerticalAlignment.Bottom; fullPaperControls.Bin.Margin = new Thickness(0, 0, -30, -45); fullPaperControls.Bin.CornerRadius = new CornerRadius(48); paperStage.Children.Add(fullPaperControls.Bin);
            var dropArrow = new Image { Source = AppVisuals.IllustrationSource("memo-drop-arrow.png"), Width = 45, Height = 54, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, 98), IsHitTestVisible = false, Opacity = 0.75 };
            paperStage.Children.Add(dropArrow);
            Grid.SetRow(paperStage, 1); memo.Children.Add(paperStage);
            var paperFooter = new DockPanel { Margin = new Thickness(8, -32, 0, 42) };
            var convertButton = MakeButton("转成任务", delegate { }); convertButton.Foreground = muted;
            var convertMenu = new ContextMenu();
            var selectedConversion = new MenuItem { Header = "选中文字建为今日任务" }; selectedConversion.Click += delegate { ConvertMemo(true); }; convertMenu.Items.Add(selectedConversion);
            var fullConversion = new MenuItem { Header = "整段建为今日任务" }; fullConversion.Click += delegate { ConvertMemo(false); }; convertMenu.Items.Add(fullConversion);
            convertButton.Click += delegate { convertMenu.PlacementTarget = convertButton; convertMenu.IsOpen = true; };
            DockPanel.SetDock(convertButton, System.Windows.Controls.Dock.Right); convertButton.Margin = new Thickness(0, 0, 126, 0); paperFooter.Children.Add(convertButton);
            var storedLink = MakeButton("看看收起的纸页", delegate { OpenPaperHistory("archive"); }); storedLink.HorizontalAlignment = HorizontalAlignment.Left; storedLink.Foreground = muted; paperFooter.Children.Add(storedLink);
            Grid.SetRow(paperFooter, 2); memo.Children.Add(paperFooter);
            compactMemoHost.Child = IdeasEditor;

            var history = NewPage("history", "留在这里的文字", "随手记与任务分开保存和查找。旧纸可以取回，任务历史只读查看。");
            history.RowDefinitions.Add(new RowDefinition());
            historyPanel = new HistoryPanel(Store, OpenMemoFromHistory);
            HistoryList = historyPanel.List; HistoryPreview = historyPanel.Preview; historyNotice = historyPanel.Notice;
            Grid.SetRow(historyPanel.View, 1); history.Children.Add(historyPanel.View);

            var settings = NewPage("settings", "按你的习惯出现", "桌面卡片、工作台与图片管理共用本机内容。");
            settings.RowDefinitions.Add(new RowDefinition());
            var options = new StackPanel();
            LaunchAtLoginCheck = new CheckBox { Content = "登录 Windows 后自动运行", Margin = new Thickness(0, 4, 0, 7) }; options.Children.Add(LaunchAtLoginCheck);
            options.Children.Add(new TextBlock { Text = "自动运行时不弹出工作台，也不抢当前窗口焦点。", Foreground = muted, FontSize = 12, Margin = new Thickness(20, 0, 0, 24), TextWrapping = TextWrapping.Wrap });
            StartupCardCheck = new CheckBox { Content = "启动时显示桌面任务卡片", Margin = new Thickness(0, 0, 0, 7) }; options.Children.Add(StartupCardCheck);
            options.Children.Add(new TextBlock { Text = "关闭后仍可从托盘菜单显示卡片；桌面图片沿用各自的显示状态。", Foreground = muted, FontSize = 12, Margin = new Thickness(20, 0, 0, 24), TextWrapping = TextWrapping.Wrap });
            settingsNotice = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap }; options.Children.Add(settingsNotice);
            options.Children.Add(new TextBlock { Text = "关闭工作台会收起窗口；只有托盘菜单的“退出”才停止程序。\n内容保存在本机，暂不提供云端同步。", Foreground = muted, FontSize = 12, Margin = new Thickness(0, 22, 0, 0), TextWrapping = TextWrapping.Wrap, LineHeight = 22 });
            Grid.SetRow(options, 1); settings.Children.Add(options);

            host = new System.Windows.Forms.Integration.ElementHost { Dock = Forms.DockStyle.Fill, Child = View }; Controls.Add(host);
            saveTimer = new Forms.Timer { Interval = 600 }; saveTimer.Tick += delegate { saveTimer.Stop(); Flush(); };
            NewTaskEditor.TextChanged += delegate { newTaskPlaceholder.Visibility = NewTaskEditor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            NewTaskEditor.TextChanged += delegate { if (!loading) { Store.State.TaskDraft = NewTaskEditor.Text; if (String.IsNullOrWhiteSpace(NewTaskEditor.Text)) { Store.State.DraftPlanDate = null; RefreshDraftDate(); } else if (Store.State.DraftPlanDate == null) Store.State.DraftPlanDate = UnplannedCheck.IsChecked == true ? "" : WorkboardStore.FormatPlanDate(DraftDatePicker.SelectedDate); QueueSave(); } };
            IdeasEditor.TextChanged += delegate { UpdateMemoHints(); if (!loading) { Store.State.Ideas = IdeasEditor.Text; lastMemoConvertedAt = DateTime.MinValue; QueueSave(); } };
            NewTaskEditor.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddDraft(); e.Handled = true; } };
            DraftDatePicker.SelectedDateChanged += delegate { if (!loading) { if (DraftDatePicker.SelectedDate.HasValue) UnplannedCheck.IsChecked = false; UpdateDraftDate(); } };
            DraftDatePicker.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; ShowStatus("日期无法识别，请从日历选择日期，或勾选“未安排”。", true); };
            UnplannedCheck.Checked += delegate { if (!loading) { DraftDatePicker.IsEnabled = false; UpdateDraftDate(); } };
            UnplannedCheck.Unchecked += delegate { if (!loading) { DraftDatePicker.IsEnabled = true; if (!DraftDatePicker.SelectedDate.HasValue) DraftDatePicker.SelectedDate = Store.Today; UpdateDraftDate(); } };
            CompletedGroup.Expanded += delegate { if (!rendering && !Store.State.CompletedExpanded) { Store.State.CompletedExpanded = true; QueueViewSave(); } if (memoSplit != null) memoSplit.Refresh(); };
            CompletedGroup.Collapsed += delegate { if (!rendering && Store.State.CompletedExpanded) { Store.State.CompletedExpanded = false; QueueViewSave(); } if (memoSplit != null) memoSplit.Refresh(); };
            LaunchAtLoginCheck.Click += delegate { ApplyAutoStart(); };
            StartupCardCheck.Click += delegate { if (!loadingSettings && Store.State.ShowCardOnStartup != (StartupCardCheck.IsChecked == true)) { Store.State.ShowCardOnStartup = StartupCardCheck.IsChecked == true; QueueViewSave(); Flush(); } };
            View.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.S && (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) { FinishMemoHistory(); e.Handled = true; } };
            Store.Changed += StoreChanged;
            Store.Saved += StoreSaved;
            Store.MemoReplaced += MemoReplaced;
            Deactivate += delegate { FinishMemoHistory(); };
            FormClosing += delegate(object sender, Forms.FormClosingEventArgs e)
            {
                if (!FinishMemoHistory()) { e.Cancel = true; return; }
                if (!AllowClose && e.CloseReason == Forms.CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };
            RefreshContent(); Navigate("tasks");
        }

        private static Brush Brush(string color) { return new BrushConverter().ConvertFromString(color) as Brush; }
        private void SetNavigationIcon(Button button, string icon)
        {
            button.Template = (ControlTemplate)View.Resources["NavigationButtonTemplate"];
            button.Tag = AppVisuals.Icon(icon, 18, muted);
            button.Padding = new Thickness(12, 12, 8, 12);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
        }
        private void SetIconOnly(Button button, string icon, Brush foreground, double size)
        {
            button.Template = (ControlTemplate)View.Resources["IconButtonTemplate"];
            button.Tag = AppVisuals.Icon(icon, size, foreground);
        }
        private Button MakeButton(string label, Action action)
        {
            var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(button, label);
            button.Click += delegate { action(); }; return button;
        }
        private Grid NewPage(string key, string title, string subtitle)
        {
            var grid = new Grid { Visibility = Visibility.Collapsed };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, key == "memo" ? 10 : 24) };
            if (key == "tasks" || key == "memo")
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                var pageTitle = new TextBlock { Text = title, FontSize = 34, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 17, 0) };
                var pageDate = new TextBlock { Foreground = muted, FontSize = 14, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
                if (key == "tasks") { taskHeading = pageTitle; taskDate = pageDate; }
                else { memoDate = pageDate; memoDate.Text = Store.Today.ToString("yyyy年M月d日  dddd", CultureInfo.GetCultureInfo("zh-CN")).Replace("星期", "周"); }
                line.Children.Add(pageTitle); line.Children.Add(pageDate); heading.Children.Add(line);
            }
            else
            {
                heading.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 9) });
                heading.Children.Add(new TextBlock { Text = subtitle, Foreground = muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            }
            grid.Children.Add(heading); pages.Add(key, grid); content.Children.Add(grid); return grid;
        }
        private void AddPageButton(StackPanel panel, string key, string label)
        {
            var button = MakeButton(label, delegate { Navigate(key); });
            SetNavigationIcon(button, key == "memo" ? "note" : key);
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 5);
            pageButtons.Add(key, button); panel.Children.Add(button);
        }
        private void AddScopeButton(StackPanel panel, string key, string label)
        {
            var button = MakeButton(label, delegate { SelectScope(key); });
            button.Margin = new Thickness(0, 0, 9, 0); button.Padding = new Thickness(18, 8, 18, 8); button.FontSize = 14;
            ScopeButtons.Add(key, button); panel.Children.Add(button);
        }
        private void Navigate(string key)
        {
            if (!pages.ContainsKey(key)) return;
            CommitTitles();
            if (!(page == key ? Flush() : FinishMemoHistory())) return;
            page = key;
            MoveMemoEditor(key == "memo");
            foreach (var item in pages) item.Value.Visibility = item.Key == key ? Visibility.Visible : Visibility.Collapsed;
            foreach (var item in pageButtons) item.Value.Background = item.Key == key ? pale : Brushes.Transparent;
            if (key == "tasks") RenderTasks();
            if (key == "history") LoadHistory();
            if (key == "settings") RefreshSettings();
        }
        private void MoveMemoEditor(bool fullPage)
        {
            Border target = fullPage ? fullMemoHost : compactMemoHost;
            if (IdeasEditor.Parent == target) return;
            var previous = IdeasEditor.Parent as Border;
            if (previous != null) previous.Child = null;
            target.Child = IdeasEditor;
            IdeasEditor.FontSize = fullPage ? 18 : 16;
            IdeasEditor.FontFamily = fullPage ? AppTypography.Body : AppTypography.Memo;
            IdeasEditor.FontWeight = FontWeights.Normal;
            TextBlock.SetLineHeight(IdeasEditor, fullPage ? 35 : Double.NaN);
        }
        private void UpdateDraftPlanSummary()
        {
            if (draftDateSummary == null) return;
            draftDateSummary.Text = UnplannedCheck.IsChecked == true ? "计划：未安排 · 点击修改"
                : DraftDatePicker.SelectedDate.HasValue ? "计划：" + DraftDatePicker.SelectedDate.Value.ToString("M月d日") + " · 点击修改"
                : "选择计划日期";
        }
        internal void SelectScope(string scope)
        {
            if (!ScopeButtons.ContainsKey(scope) || scope == Store.State.TaskScope) return;
            CommitTitles();
            if (dirty && !Flush()) return;
            Store.State.TaskScope = scope;
            if (String.IsNullOrWhiteSpace(NewTaskEditor.Text))
            {
                RefreshDraftDate();
            }
            RenderTasks(); QueueViewSave();
        }
        internal void RefreshDay()
        {
            if (disposed || displayedDay == Store.Today) return;
            displayedDay = Store.Today;
            if (Store.State.DraftPlanDate == null && String.IsNullOrWhiteSpace(NewTaskEditor.Text) && !DraftDatePicker.IsKeyboardFocusWithin && !DraftDatePicker.IsDropDownOpen) RefreshDraftDate();
            if (TaskEditors.Any(t => t.IsKeyboardFocusWithin)) pendingRefresh = true; else RenderTasks();
        }
        internal void FocusQuickTask()
        {
            Navigate("tasks"); Show(); Activate(); host.Focus(); if (page == "tasks") { NewTaskEditor.Focus(); Keyboard.Focus(NewTaskEditor); }
        }
        internal void FocusQuickMemo()
        {
            Navigate("memo"); Show(); Activate(); host.Focus(); if (page == "memo") { IdeasEditor.Focus(); Keyboard.Focus(IdeasEditor); IdeasEditor.CaretIndex = IdeasEditor.Text.Length; }
        }
        private void SetDraftDate(DateTime date)
        {
            loading = true; DraftDatePicker.SelectedDate = date.Date; UnplannedCheck.IsChecked = false; DraftDatePicker.IsEnabled = true; loading = false;
            UpdateDraftDate();
        }
        private void UpdateDraftDate()
        {
            string date = UnplannedCheck.IsChecked == true ? "" : WorkboardStore.FormatPlanDate(DraftDatePicker.SelectedDate);
            if (Store.State.DraftPlanDate == date) { UpdateDraftPlanSummary(); return; }
            Store.State.DraftPlanDate = date;
            QueueSave();
            UpdateDraftPlanSummary();
        }
        private void RefreshDraftDate()
        {
            if (Store.State.DraftPlanDate == null && !String.IsNullOrWhiteSpace(NewTaskEditor.Text) && DraftDatePicker.SelectedDate.HasValue) { UpdateDraftPlanSummary(); return; }
            bool wasLoading = loading; loading = true;
            try
            {
                DateTime date;
                DateTime? fallback = DefaultDraftDate();
                bool unplanned = Store.State.DraftPlanDate == "" || (Store.State.DraftPlanDate == null && !fallback.HasValue);
                DraftDatePicker.SelectedDate = unplanned ? (DateTime?)null : WorkboardStore.TryPlanDate(Store.State.DraftPlanDate, out date) ? (DateTime?)date : fallback;
                UnplannedCheck.IsChecked = unplanned; DraftDatePicker.IsEnabled = !unplanned;
            }
            finally { loading = wasLoading; }
            UpdateDraftPlanSummary();
        }
        private DateTime? DefaultDraftDate()
        {
            return CurrentScope == "tomorrow" ? Store.Today.AddDays(1) : CurrentScope == "later" ? (DateTime?)null : Store.Today;
        }
        internal void AddDraft()
        {
            CommitTitles();
            if (NewTaskEditor.Text != Store.State.TaskDraft)
            {
                bool wasLoading = loading; loading = true;
                try { NewTaskEditor.Text = Store.State.TaskDraft; }
                finally { loading = wasLoading; }
            }
            RefreshDraftDate();
            if (String.IsNullOrWhiteSpace(NewTaskEditor.Text)) { NewTaskEditor.Focus(); return; }
            if (UnplannedCheck.IsChecked != true && !DraftDatePicker.SelectedDate.HasValue) { ShowStatus("请选择计划日期，或勾选“未安排”。", true); return; }
            CommitTitles();
            DateTime? date = UnplannedCheck.IsChecked == true ? (DateTime?)null : DraftDatePicker.SelectedDate;
            WorkTask task = Store.AddTask(NewTaskEditor.Text, date); if (task == null) return;
            loading = true; NewTaskEditor.Clear(); loading = false;
            // Show the newly added task, even when its date differs from the current filter.
            string scope = ScopeFor(task); Store.State.TaskScope = scope; Store.State.DraftPlanDate = null; RefreshDraftDate();
            RenderTasks(); QueueSave(); Flush(); NewTaskEditor.Focus();
            if (!dirty) ShowSavedAction("已添加到“" + ScopeLabel(scope) + "” · " + DateLabel(task));
        }
        private string ScopeFor(WorkTask task)
        {
            DateTime date; if (!WorkboardStore.TryPlanDate(task.PlanDate, out date)) return "later";
            if (date < Store.Today && task.Done && !Store.SelectTasks("today").Contains(task)) return "all";
            return date <= Store.Today ? "today" : date == Store.Today.AddDays(1) ? "tomorrow" : "later";
        }
        private string ScopeLabel(string scope)
        {
            return scope == "today" ? "今天" : scope == "tomorrow" ? "明天" : scope == "later" ? "以后／未安排" : "全部";
        }
        private string DateLabel(WorkTask task)
        {
            DateTime date;
            if (!WorkboardStore.TryPlanDate(task.PlanDate, out date)) return String.IsNullOrEmpty(task.PlanDate) ? "未安排日期" : "日期待确认 · 原值：" + task.PlanDate;
            string prefix = date < Store.Today && !task.Done ? "之前未完成 · 原计划 " : date == Store.Today ? "今天 · " : date == Store.Today.AddDays(1) ? "明天 · " : "计划 ";
            return prefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        private void RenderTasks()
        {
            if (disposed) return;
            if (editedTaskTitles.Count > 0) { pendingRefresh = true; return; }
            rendering = true;
            try
            {
                if (activeTaskMenu != null) activeTaskMenu.IsOpen = false;
                TaskEditors.Clear(); TaskChecks.Clear(); CompletedChecks.Clear(); taskRows.Children.Clear(); completedRows.Children.Clear();
                var selected = Store.SelectTasks(CurrentScope).ToList(); var active = selected.Where(t => !t.Done).ToList(); var done = selected.Where(t => t.Done).ToList();
                foreach (var item in ScopeButtons) item.Value.Background = item.Key == CurrentScope ? pale : Brushes.Transparent;
                taskHeading.Text = ScopeLabel(CurrentScope);
                DateTime headingDate = CurrentScope == "tomorrow" ? Store.Today.AddDays(1) : Store.Today;
                taskDate.Text = headingDate.ToString("M月d日  dddd", CultureInfo.GetCultureInfo("zh-CN")).Replace("星期", "周");
                taskSummary.Text = active.Count + " 项待办";
                UpdateDraftPlanSummary();
                taskEmpty.Visibility = active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                int unplannedCount = Store.State.Tasks.Count(t => !t.Done && String.IsNullOrEmpty(t.PlanDate));
                taskEmpty.Text = done.Count > 0 ? "这一组的待办都完成了，给自己一点空白。" : CurrentScope == "today" && unplannedCount > 0 ? "今天还没有安排。还有 " + unplannedCount + " 项未安排任务，保存在“以后／未安排”中，可在那里安排日期。" : "这里还没有任务。写下一件要做的事，安排到合适的日期。";
                if (CurrentScope == "today")
                {
                    var earlier = active.Where(delegate(WorkTask task) { DateTime date; return WorkboardStore.TryPlanDate(task.PlanDate, out date) && date < Store.Today; }).ToList();
                    var today = active.Where(t => !earlier.Contains(t)).ToList();
                    if (earlier.Count > 0) { taskRows.Children.Add(SectionLabel("之前未完成", earlier.Count)); foreach (var task in earlier) taskRows.Children.Add(BuildTaskRow(task, false)); }
                    if (today.Count > 0) taskRows.Children.Add(SectionLabel("今天", today.Count));
                    foreach (var task in today) taskRows.Children.Add(BuildTaskRow(task, false));
                }
                else foreach (var task in active) taskRows.Children.Add(BuildTaskRow(task, false));
                foreach (var task in done) completedRows.Children.Add(BuildTaskRow(task, true));
                CompletedGroup.Header = "已完成（" + done.Count + "）"; CompletedGroup.Visibility = done.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                CompletedGroup.IsExpanded = Store.State.CompletedExpanded;
                AutomationProperties.SetName(CompletedGroup, "已完成 " + done.Count + " 项，展开或收起");
                undoButton.Visibility = Store.State.Removed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                pendingRefresh = false;
            }
            finally { rendering = false; }
            if (memoSplit != null) memoSplit.Refresh();
        }
        private TextBlock SectionLabel(string text, int count)
        {
            var label = new TextBlock { Foreground = ink, FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 13, 0, 5) };
            label.Inlines.Add(new Run(text)); label.Inlines.Add(new Run("   " + count) { Foreground = muted, FontSize = 11, FontWeight = FontWeights.Normal });
            return label;
        }
        private FrameworkElement BuildTaskRow(WorkTask task, bool completed)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 1), MinHeight = 62 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var check = new CheckBox { IsChecked = completed, Tag = task, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetName(check, (completed ? "恢复为待办：" : "完成任务：") + task.Text);
            check.Click += delegate { bool done = check.IsChecked == true; if (task.Done == done) return; CommitTitles(); Store.SetCompleted(task, done); RenderTasks(); QueueSave(); Flush(); NewTaskEditor.Focus(); };
            grid.Children.Add(check);
            if (completed)
            {
                CompletedChecks.Add(check);
                var title = new TextBlock { Text = task.Text, FontSize = 15, TextDecorations = TextDecorations.Strikethrough, Foreground = Brush("#969D91"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 10, 8, 2) };
                Grid.SetColumn(title, 1); grid.Children.Add(title);
            }
            else
            {
                TaskChecks.Add(check);
                var title = new TextBox { Text = task.Text, Tag = task, MaxLength = 240, FontSize = 15, Padding = new Thickness(1, 9, 6, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 7, 0) };
                AutomationProperties.SetName(title, "任务标题：" + task.Text); TaskEditors.Add(title); Grid.SetColumn(title, 1); grid.Children.Add(title);
                title.TextChanged += delegate { if (!loading && !rendering) editedTaskTitles.Add(title); };
                title.LostKeyboardFocus += delegate { if (loading || rendering) return; CommitTitle(title); Flush(); if (pendingRefresh) title.Dispatcher.BeginInvoke(new Action(RenderTasks)); };
                title.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitTitle(title); Flush(); NewTaskEditor.Focus(); e.Handled = true; } };
            }
            Button more = null; more = MakeButton("任务选项", delegate { ShowTaskMenu(task, more); });
            SetIconOnly(more, "more", muted, 18); more.Padding = new Thickness(5); more.ToolTip = "任务选项：改期、移除";
            AutomationProperties.SetName(more, "任务选项：" + task.Text); Grid.SetColumn(more, 2); grid.Children.Add(more);
            if (completed)
            {
                DateTimeOffset finished;
                string label = DateTimeOffset.TryParse(task.CompletedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out finished)
                    ? "完成于 " + finished.LocalDateTime.ToString("M月d日") : DateLabel(task);
                var dateText = new TextBlock { Text = label, FontSize = 11, Foreground = Brush("#969D91"), Margin = new Thickness(1, 0, 0, 10) };
                Grid.SetColumn(dateText, 1); Grid.SetRow(dateText, 1); grid.Children.Add(dateText);
            }
            else
            {
                Button dateChip = null; dateChip = MakeButton(ShortDateLabel(task), delegate { ShowTaskMenu(task, dateChip); });
                dateChip.Foreground = muted; dateChip.FontSize = 11; dateChip.Padding = new Thickness(1, 1, 6, 6); dateChip.HorizontalAlignment = HorizontalAlignment.Left; dateChip.ToolTip = DateLabel(task) + " · 点击改期";
                AutomationProperties.SetName(dateChip, "改期：" + task.Text); Grid.SetColumn(dateChip, 1); Grid.SetRow(dateChip, 1); grid.Children.Add(dateChip);
            }
            return new Border { BorderBrush = Brush("#E5E7DE"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        }
        private string ShortDateLabel(WorkTask task)
        {
            DateTime date;
            if (!WorkboardStore.TryPlanDate(task.PlanDate, out date)) return String.IsNullOrEmpty(task.PlanDate) ? "未安排日期" : "日期待确认";
            return (date < Store.Today ? "原计划 " : date == Store.Today ? "今天 · " : date == Store.Today.AddDays(1) ? "明天 · " : "") + date.ToString("M月d日");
        }
        private void ChangeTaskDate(WorkTask task, DateTime? date)
        {
            if (!Store.State.Tasks.Contains(task)) return;
            if (task.PlanDate == WorkboardStore.FormatPlanDate(date)) return;
            CommitTitles(); Store.SetPlanDate(task, date); RenderTasks(); QueueSave();
            if (Flush()) ShowSavedAction("已改期 · " + DateLabel(task) + "。可在“" + ScopeLabel(ScopeFor(task)) + "”中查看。");
        }
        private void ShowTaskMenu(WorkTask task, Button anchor)
        {
            if (activeTaskMenu != null) activeTaskMenu.IsOpen = false;
            var menu = new ContextMenu { FontFamily = AppTypography.Body, Foreground = ink, Background = Brush("#FAF8F3") }; activeTaskMenu = menu;
            if (!task.Done)
            {
                foreach (string key in new[] { "today", "tomorrow", "later" })
                {
                    string choice = key;
                    var item = new MenuItem { Header = key == "today" ? "安排到今天" : key == "tomorrow" ? "安排到明天" : "暂不安排日期", Icon = AppVisuals.Icon("calendar", 14, muted) };
                    item.Click += delegate { ChangeTaskDate(task, choice == "later" ? (DateTime?)null : choice == "tomorrow" ? Store.Today.AddDays(1) : Store.Today); };
                    menu.Items.Add(item);
                }
                DateTime planned;
                var picker = new DatePicker { Width = 156, SelectedDate = WorkboardStore.TryPlanDate(task.PlanDate, out planned) ? (DateTime?)planned : null, Language = XmlLanguage.GetLanguage("zh-CN"), ToolTip = "选择具体日期；清空日期即为未安排" };
                AutomationProperties.SetName(picker, "改期日期：" + task.Text);
                picker.SelectedDateChanged += delegate { if (rendering || loading) return; menu.IsOpen = false; ChangeTaskDate(task, picker.SelectedDate); };
                picker.DateValidationError += delegate(object sender, DatePickerDateValidationErrorEventArgs e) { e.ThrowException = false; ShowStatus("日期无法识别，请从日历选择；清空日期表示未安排。", true); };
                var custom = new MenuItem { Header = "选择其他日期" }; custom.Items.Add(new MenuItem { Header = picker, StaysOpenOnClick = true }); menu.Items.Add(custom);
                menu.Items.Add(new Separator());
            }
            var remove = new MenuItem { Header = "移除任务", Icon = AppVisuals.Icon("remove", 14, muted) };
            AutomationProperties.SetName(remove, "移除任务：" + task.Text);
            remove.Click += delegate { if (!Store.State.Tasks.Contains(task)) return; CommitTitles(); Store.Remove(task); RenderTasks(); QueueSave(); if (Flush()) ShowSavedAction("已移除“" + task.Text + "”，可撤销恢复原位置。"); };
            menu.Items.Add(remove); menu.PlacementTarget = anchor; menu.IsOpen = true;
        }
        private void CommitTitle(TextBox editor)
        {
            var task = editor.Tag as WorkTask; if (task == null || !Store.State.Tasks.Contains(task)) { editedTaskTitles.Remove(editor); return; }
            string value = editor.Text.Trim();
            bool edited = editedTaskTitles.Remove(editor);
            bool changed = edited && value.Length > 0 && value != task.Text;
            bool wasLoading = loading; loading = true;
            try
            {
                // Only an actual local edit may replace the committed model.
                // Deferred peer projections are never another title submission.
                if (changed) task.Text = value;
                if (editor.Text != task.Text) editor.Text = task.Text;
                if (edited && value.Length == 0) ShowStatus("任务标题不能为空，已保留原内容。", true);
            }
            finally { loading = wasLoading; }
            if (changed) QueueSave();
        }
        private void CommitTitles() { foreach (var editor in TaskEditors.ToArray()) CommitTitle(editor); }
        internal void UndoRemove()
        {
            CommitTitles(); var task = Store.Undo(); if (task == null) return;
            Store.State.TaskScope = ScopeFor(task); if (task.Done) Store.State.CompletedExpanded = true;
            if (Store.State.DraftPlanDate == null && String.IsNullOrWhiteSpace(Store.State.TaskDraft)) RefreshDraftDate();
            RenderTasks(); QueueSave(); if (Flush()) ShowSavedAction("已恢复到原来的顺序 · " + DateLabel(task));
        }
        internal bool ConvertMemo(bool selectedOnly)
        {
            string text = (selectedOnly ? IdeasEditor.SelectedText : IdeasEditor.Text).Trim();
            if (text.Length == 0) { ShowStatus(selectedOnly ? "请先选中想转为任务的文字。" : "先写下一段备忘，再转成任务。", true); return false; }
            if (text.Length > 240) { ShowStatus("一次最多转入 240 字，请选中具体要执行的部分；原备忘会保留。", true); return false; }
            string date = WorkboardStore.FormatPlanDate(Store.Today);
            var pending = Store.State.Tasks.FirstOrDefault(t => t.Id == pendingMemoTaskId && t.Text == text && t.PlanDate == date);
            if (pending != null)
            {
                QueueSave(); if (!Flush()) return false;
                lastMemoText = text; lastMemoDate = date; lastMemoTaskId = pending.Id; lastMemoConvertedAt = DateTime.UtcNow; pendingMemoTaskId = null;
                ShowSavedAction("已加入今天的任务，原备忘与正在输入的任务草稿均保留。"); return true;
            }
            if (lastMemoText == text && lastMemoDate == date && DateTime.UtcNow - lastMemoConvertedAt < TimeSpan.FromSeconds(2) && Store.State.Tasks.Any(t => t.Id == lastMemoTaskId && t.Text == text && t.PlanDate == date))
            { ShowStatus("这段内容刚刚已加入今天，已避免重复添加；原备忘保留。", false); return false; }
            string draft = Store.State.TaskDraft; var task = Store.AddTask(text, Store.Today); Store.State.TaskDraft = draft;
            if (task == null) return false;
            pendingMemoTaskId = task.Id;
            QueueSave(); if (!Flush()) return false;
            lastMemoText = text; lastMemoDate = date; lastMemoTaskId = task.Id; lastMemoConvertedAt = DateTime.UtcNow; pendingMemoTaskId = null;
            ShowSavedAction("已加入今天的任务，原备忘与正在输入的任务草稿均保留。"); return true;
        }
        private void QueueSave()
        {
            if (loading || disposed) return;
            dirty = true; if (!saveFailed) ShowStatus("修改待保存 · 停止输入后自动保存", false); saveTimer.Stop(); saveTimer.Start();
            try { saving = true; Store.NotifyChanged(); }
            finally { saving = false; }
        }
        private void QueueViewSave()
        {
            if (loading || disposed) return;
            viewDirty = true;
            try { saving = true; Store.NotifyChanged(); }
            finally { saving = false; }
        }
        internal bool Flush()
        {
            if (disposed || saving) return true;
            saveTimer.Stop();
            if (!dirty && !viewDirty) return true;
            bool contentSave = dirty;
            try
            {
                saving = true; Store.Save(); dirty = viewDirty = false;
                if (!contentSave) return true;
                saveFailed = false;
                ShowStatus(String.IsNullOrEmpty(Store.HistoryNotice) ? "已保存在本机 · " + DateTime.Now.ToString("HH:mm") : Store.HistoryNotice, !String.IsNullOrEmpty(Store.HistoryNotice));
                return true;
            }
            catch (Exception error)
            {
                if (!contentSave) { Storage.Log("视图偏好暂未保存：" + error.Message); return true; }
                dirty = true; saveFailed = true; Storage.Log(error.ToString());
                saveFailureMessage = "保存失败，内容仍保留在当前窗口。请检查磁盘空间或权限，按 Ctrl+S 重试。" + error.Message;
                ShowStatus(saveFailureMessage, true); return false;
            }
            finally { saving = false; }
        }
        internal bool FinishMemoHistory()
        {
            CommitTitles();
            if (!Flush()) return false;
            bool hadNotice = !String.IsNullOrEmpty(Store.HistoryNotice);
            if (!Store.CheckpointMemoHistory(true) || hadNotice)
                ShowStatus(Store.HistoryNotice ?? "已保存在本机 · " + DateTime.Now.ToString("HH:mm"), !String.IsNullOrEmpty(Store.HistoryNotice));
            return true;
        }
        internal void RefreshHistoryStatus()
        {
            if (dirty || saveFailed || disposed) return;
            ShowStatus(Store.HistoryNotice ?? "已保存在本机 · " + DateTime.Now.ToString("HH:mm"), !String.IsNullOrEmpty(Store.HistoryNotice));
        }
        private void ShowStatus(string text, bool error)
        {
            // Validation can add context, but only successful persistence may
            // dismiss an outstanding save failure. Never accumulate old hints.
            if (saveFailed && !String.IsNullOrEmpty(saveFailureMessage) && text != saveFailureMessage)
            { text = saveFailureMessage + "\n" + text; error = true; }
            status.Text = text; status.Foreground = error ? Brush("#9A623A") : muted; status.ToolTip = text;
        }
        private void ShowSavedAction(string text)
        {
            bool historyWarning = !String.IsNullOrEmpty(Store.HistoryNotice);
            ShowStatus(text + (historyWarning ? "\n" + Store.HistoryNotice : ""), historyWarning);
        }
        private void StoreChanged(object sender, EventArgs e)
        {
            if (saving || disposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(RefreshContent)); return; }
            RefreshContent();
        }
        private void FocusMemoEditor()
        {
            if (disposed || !Visible) return;
            IdeasEditor.Focus(); Keyboard.Focus(IdeasEditor);
        }
        private void UpdateMemoHints()
        {
            bool blank = IdeasEditor == null || IdeasEditor.Text.Length == 0;
            if (compactMemoHint != null) compactMemoHint.Visibility = blank ? Visibility.Visible : Visibility.Collapsed;
            if (fullMemoHint != null) fullMemoHint.Visibility = blank ? Visibility.Visible : Visibility.Collapsed;
        }
        private void MemoReplaced(object sender, EventArgs e)
        {
            if (disposed) return;
            saveTimer.Stop(); loading = true;
            try { IdeasEditor.Text = Store.State.Ideas ?? ""; }
            finally { loading = false; }
            dirty = viewDirty = saveFailed = false; saveFailureMessage = null;
            UpdateMemoHints();
            if (SystemParameters.ClientAreaAnimation)
                IdeasEditor.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(160)));
        }
        private void OpenPaperHistory(string filter)
        {
            Navigate("history");
            if (page == "history") historyPanel.SelectCategory("memo", filter);
        }
        internal bool OpenMemoFromHistory(string text)
        {
            if (!Flush()) return false;
            if (!Store.OpenMemoText(text)) return false;
            Navigate("memo");
            ShowSavedAction("已取回这页；之前正在写的内容也已收好。");
            FocusMemoEditor(); return true;
        }
        private void StoreSaved(object sender, EventArgs e)
        {
            if (saving || disposed) return;
            bool contentSave = dirty;
            dirty = viewDirty = false; saveTimer.Stop();
            if (contentSave) saveFailed = false;
            if (contentSave) ShowStatus(String.IsNullOrEmpty(Store.HistoryNotice) ? "已保存在本机 · " + DateTime.Now.ToString("HH:mm") : Store.HistoryNotice, !String.IsNullOrEmpty(Store.HistoryNotice));
        }
        internal void RefreshContent()
        {
            if (disposed) return;
            loading = true;
            try
            {
                if (!IdeasEditor.IsKeyboardFocusWithin && IdeasEditor.Text != Store.State.Ideas) IdeasEditor.Text = Store.State.Ideas;
                if (!NewTaskEditor.IsKeyboardFocusWithin && NewTaskEditor.Text != Store.State.TaskDraft) NewTaskEditor.Text = Store.State.TaskDraft;
                if (!DraftDatePicker.IsKeyboardFocusWithin && !UnplannedCheck.IsKeyboardFocusWithin)
                {
                    RefreshDraftDate();
                }
            }
            finally { loading = false; }
            if (TaskEditors.Any(t => t.IsKeyboardFocusWithin)) pendingRefresh = true; else RenderTasks();
            if (memoSplit != null) memoSplit.Refresh();
            UpdateMemoHints();
            if (page == "settings") RefreshSettings();
            if (!dirty && !String.IsNullOrEmpty(Store.HistoryNotice)) ShowStatus(Store.HistoryNotice, true);
            else if (!dirty && !String.IsNullOrEmpty(Store.Notice)) ShowStatus(Store.Notice, true);
        }
        private void LoadHistory()
        {
            historyPanel.Load();
        }
        private void ShowHistorySelection()
        {
            historyPanel.ShowSelection();
        }
        private void RefreshSettings()
        {
            loadingSettings = true;
            try
            {
                StartupCardCheck.IsChecked = Store.State.ShowCardOnStartup;
                LaunchAtLoginCheck.IsEnabled = GetAutoStart != null && SetAutoStart != null;
                if (GetAutoStart != null) { LaunchAtLoginCheck.IsChecked = GetAutoStart(); settingsNotice.Text = "启动设置按当前 Windows 登录用户生效。"; }
                else { LaunchAtLoginCheck.IsChecked = false; settingsNotice.Text = "自动运行设置暂不可用；桌面卡片的启动显示设置仍可保存。"; }
            }
            catch (Exception error) { LaunchAtLoginCheck.IsEnabled = false; settingsNotice.Text = "无法读取自动运行设置：" + error.Message; }
            finally { loadingSettings = false; }
        }
        private void ApplyAutoStart()
        {
            if (loadingSettings || SetAutoStart == null || GetAutoStart == null) return;
            bool requested = LaunchAtLoginCheck.IsChecked == true;
            try
            {
                SetAutoStart(requested);
                if (GetAutoStart() != requested) throw new InvalidOperationException("Windows 未保存该设置。");
                settingsNotice.Text = requested ? "已启用登录后自动运行。下次登录时静默启动。" : "已关闭登录后自动运行。";
            }
            catch (Exception error)
            {
                loadingSettings = true;
                try { LaunchAtLoginCheck.IsChecked = GetAutoStart(); } catch { LaunchAtLoginCheck.IsChecked = !requested; }
                loadingSettings = false; settingsNotice.Text = "设置失败：" + error.Message; ShowStatus(settingsNotice.Text, true);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                FinishMemoHistory(); disposed = true; if (activeTaskMenu != null) activeTaskMenu.IsOpen = false; Store.Changed -= StoreChanged; Store.Saved -= StoreSaved; Store.MemoReplaced -= MemoReplaced; saveTimer.Dispose(); if (memoSplit != null) memoSplit.Dispose();
                compactPaperControls.Dispose(); fullPaperControls.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
