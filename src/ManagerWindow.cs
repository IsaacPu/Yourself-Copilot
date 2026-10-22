using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LocalDesktopNote
{
    internal sealed class PictureRow : INotifyPropertyChanged
    {
        internal readonly Preferences Source;
        private readonly LibraryStore store;
        private string loadedPath;
        private BitmapSource thumbnail;
        internal PictureRow(Preferences source, LibraryStore storage) { Source = source; store = storage; }
        public string Title { get { return Source.Title; } }
        public string Subtitle { get { return !File.Exists(store.Resolve(Source)) ? "图片暂不可用" : (Source.Hidden ? "已隐藏" : "桌面显示") + (Source.Locked ? " · 已锁定" : ""); } }
        public BitmapSource Thumbnail
        {
            get
            {
                string path = store.Resolve(Source);
                if (loadedPath == path) return thumbnail;
                loadedPath = path;
                try { thumbnail = LoadBitmap(path, 180); } catch { thumbnail = null; }
                return thumbnail;
            }
        }
        internal static BitmapSource LoadBitmap(string path, int decodeWidth)
        {
            using (var stream = File.OpenRead(path))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decodeWidth; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        internal void Refresh() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(null)); }
    }

    internal sealed class ManagerWindow : Window
    {
        private readonly NoteContext context;
        private bool updating;
        private string previewPath;
        private readonly ListBox list;
        private readonly TextBox name;
        private readonly Image preview;
        private readonly CheckBox shown, locked;
        private readonly Slider scale;
        private readonly TextBlock percentage, meta, saveStatus, message;
        private readonly Border messageBar;
        private readonly FrameworkElement inspector;
        private readonly Button actual, reposition, replace, remove;
        private readonly DispatcherTimer messageTimer;
        internal bool AllowClose;
        internal ManagerWindow(NoteContext owner)
        {
            context = owner;
            using (var stream = File.OpenRead(Path.Combine(Storage.Folder, "CompactWindow.xaml")))
            {
                Window shell = (Window)XamlReader.Load(stream);
                Title = shell.Title; Width = shell.Width; Height = shell.Height; MinWidth = shell.MinWidth; MinHeight = shell.MinHeight;
                WindowStartupLocation = shell.WindowStartupLocation; Background = shell.Background; Foreground = shell.Foreground;
                WindowStyle = shell.WindowStyle; ResizeMode = shell.ResizeMode;
                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(shell);
                if (chrome != null) System.Windows.Shell.WindowChrome.SetWindowChrome(this, (System.Windows.Shell.WindowChrome)chrome.Clone());
                FontFamily = AppTypography.Body; FontSize = shell.FontSize; UseLayoutRounding = true;
                Resources = shell.Resources; object content = shell.Content;
                var scope = NameScope.GetNameScope(shell);
                shell.Content = null; Content = content;
                if (scope != null) NameScope.SetNameScope((DependencyObject)content, scope);
            }
            list = Find<ListBox>("LibraryList"); name = Find<TextBox>("NameInput"); preview = Find<Image>("Preview");
            shown = Find<CheckBox>("VisibleSwitch"); locked = Find<CheckBox>("LockSwitch"); scale = Find<Slider>("ScaleSlider");
            percentage = Find<TextBlock>("ScaleValue"); meta = Find<TextBlock>("ImageMeta"); saveStatus = Find<TextBlock>("SaveStatus");
            message = Find<TextBlock>("MessageText"); messageBar = Find<Border>("MessageBar"); inspector = Find<FrameworkElement>("Inspector");
            actual = Find<Button>("ActualSizeButton"); reposition = Find<Button>("RepositionButton"); replace = Find<Button>("ReplaceButton"); remove = Find<Button>("RemoveButton");
            list.ItemsSource = owner.Rows;
            Find<Button>("AddButton").Click += delegate { context.ChooseImages(); };
            Find<Button>("PasteButton").Click += delegate { context.PasteImage(); };
            Find<Button>("WorkboardButton").Click += delegate { context.ShowWorkboard(true); };
            Find<Button>("MinimizeButton").Click += delegate { WindowState = WindowState.Minimized; };
            Find<Button>("MaximizeButton").Click += delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            Find<Button>("CloseButton").Click += delegate { Close(); };
            Find<Button>("EmptyAddButton").Click += delegate { context.ChooseImages(); };
            Find<Button>("HelpButton").Click += delegate { ShowMessage("点击“想法与任务”打开工作卡。图片可拖入或 Ctrl+V 粘贴；桌面拖动移动、滚轮缩放。关闭此窗口后卡片和图片继续显示，退出请用托盘菜单。", false); };
            Find<Button>("UndoButton").Click += delegate { context.UndoRemove(); };
            Find<Button>("DismissMessageButton").Click += delegate { messageBar.Visibility = Visibility.Collapsed; };
            list.SelectionChanged += delegate { if (!updating && list.SelectedItem != null) { CommitName(); context.Select(((PictureRow)list.SelectedItem).Source.Id); } };
            name.LostKeyboardFocus += delegate { CommitName(); };
            name.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { CommitName(); e.Handled = true; Keyboard.ClearFocus(); } };
            shown.Click += delegate { if (!updating) context.SetVisible(shown.IsChecked == true); };
            locked.Click += delegate { if (!updating) context.SetLocked(locked.IsChecked == true); };
            scale.ValueChanged += delegate { if (!updating) context.SetScale(scale.Value); };
            actual.Click += delegate { context.OriginalSize(); };
            reposition.Click += delegate { context.Reposition(); };
            replace.Click += delegate { context.ChooseReplacement(); };
            remove.Click += delegate { context.RemoveSelected(); };
            AllowDrop = true;
            PreviewDragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { context.AddImages((string[])e.Data.GetData(DataFormats.FileDrop)); e.Handled = true; } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (e.Key == Key.O) { context.ChooseImages(); e.Handled = true; }
                if (e.Key == Key.V && !(Keyboard.FocusedElement is TextBox)) { context.PasteImage(); e.Handled = true; }
            };
            messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            messageTimer.Tick += delegate { messageTimer.Stop(); messageBar.Visibility = Visibility.Collapsed; };
            Closing += delegate(object sender, CancelEventArgs e)
            {
                CommitName();
                if (!AllowClose) { e.Cancel = true; Hide(); context.ManagerHidden(); }
                else messageTimer.Stop();
            };
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
            Refresh();
        }
        private T Find<T>(string key) where T : FrameworkElement
        {
            var root = (FrameworkElement)Content;
            T value = root.FindName(key) as T;
            if (value == null) throw new InvalidOperationException("界面控件缺失：" + key);
            return value;
        }
        private void CommitName()
        {
            if (updating || context.Selected == null) return;
            string value = name.Text.Trim();
            if (value.Length == 0) { name.Text = context.Selected.Title; return; }
            context.Rename(value);
        }
        internal void Refresh()
        {
            updating = true;
            try
            {
                Find<TextBlock>("LibraryCount").Text = "共 " + context.Rows.Count + " 张";
                Find<Button>("UndoButton").IsEnabled = context.CanUndo;
                Find<Button>("UndoButton").Visibility = context.CanUndo ? Visibility.Visible : Visibility.Collapsed;
                Preferences item = context.Selected;
                PictureRow selected = null;
                foreach (var row in context.Rows) if (item != null && row.Source.Id == item.Id) selected = row;
                list.SelectedItem = selected;
                bool hasImage = item != null;
                inspector.IsEnabled = hasImage; name.IsEnabled = hasImage;
                replace.IsEnabled = hasImage; remove.IsEnabled = hasImage;
                Find<StackPanel>("EmptyState").Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
                if (!hasImage) { name.Text = ""; meta.Text = "选择或添加一张图片"; preview.Source = null; previewPath = null; return; }
                if (!name.IsKeyboardFocusWithin) name.Text = item.Title;
                meta.Text = item.PixelWidth + " × " + item.PixelHeight + " 像素";
                shown.IsChecked = !item.Hidden; locked.IsChecked = item.Locked;
                scale.IsEnabled = !item.Locked; actual.IsEnabled = !item.Locked; reposition.IsEnabled = !item.Locked;
                double value = item.PixelWidth > 0 ? 100.0 * item.Width / item.PixelWidth : 100;
                scale.Minimum = Math.Min(10, Math.Max(1, Math.Floor(value)));
                Find<TextBlock>("ScaleMin").Text = Math.Round(scale.Minimum) + "%";
                scale.Value = Math.Max(scale.Minimum, Math.Min(scale.Maximum, value));
                percentage.Text = Math.Round(value).ToString() + "%";
                string path = context.Store.Resolve(item);
                if (previewPath != path)
                {
                    previewPath = path;
                    try { preview.Source = PictureRow.LoadBitmap(path, 1400); }
                    catch { preview.Source = null; ShowMessage("这张图片暂时无法读取，可以点击“更换这张图片”恢复。", true); }
                }
            }
            finally { updating = false; }
        }
        internal void SetSaveStatus(string text) { saveStatus.Text = text; }
        internal void VerifyLayout()
        {
            UpdateLayout();
            if (Find<Border>("DropStage").ActualHeight < 190) throw new InvalidOperationException("预览区域高度不足。");
            if (Find<Button>("PasteButton").ActualWidth < 100) throw new InvalidOperationException("粘贴按钮被压缩。");
            if (!Find<Button>("CloseButton").IsVisible) throw new InvalidOperationException("窗口控制按钮未显示。");
            if (Content == null || ActualWidth < MinWidth) throw new InvalidOperationException("窗口布局未就绪。");
        }
        internal void ShowMessage(string text, bool error)
        {
            message.Text = text;
            message.Foreground = new SolidColorBrush(error ? Color.FromRgb(155, 55, 69) : Color.FromRgb(62, 96, 60));
            messageBar.Visibility = Visibility.Visible;
            if (messageTimer != null) { messageTimer.Stop(); if (!error) messageTimer.Start(); }
        }
    }
}
