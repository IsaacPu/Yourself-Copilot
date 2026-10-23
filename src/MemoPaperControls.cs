using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalDesktopNote
{
    // Text selection belongs to the editor. Only this separate paper grip can
    // start a whole-sheet drag, and only our own bin accepts that payload.
    internal sealed class MemoPaperControls : IDisposable
    {
        internal readonly Grid View;
        internal readonly Button Grabber, ArchiveButton, TrashButton, UndoButton;
        internal readonly Border Bin;
        internal FrameworkElement PaperSurface;
        private readonly Image binImage;
        private readonly WorkboardStore store;
        private readonly Func<bool> flush;
        private readonly Action<string, bool> report;
        private readonly Action focus;
        private readonly bool compact;
        private Point origin;
        private bool armed, busy, disposed;
        private string lastPaper;
        private static readonly Brush Ink = Brush("#344438"), Muted = Brush("#7D877C"), Pale = Brush("#E7EBE1");

        internal sealed class PaperDrag
        {
            internal readonly WorkboardStore Store;
            internal readonly string Text;
            internal readonly long Generation;
            internal PaperDrag(WorkboardStore store, string text) { Store = store; Text = text; Generation = store.MemoPaperGeneration; }
        }

        internal MemoPaperControls(WorkboardStore store, Func<bool> flush, Action<string, bool> report, Action focus, bool compact)
        {
            this.store = store; this.flush = flush; this.report = report; this.focus = focus; this.compact = compact;
            View = new Grid(); View.ColumnDefinitions.Add(new ColumnDefinition()); View.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grabber = Button(compact ? "note" : "more", compact ? "随手记" : "拖起这一页", "抓起整张随手记，拖到纸篓；正文仍可选字编辑");
            Grabber.HorizontalAlignment = HorizontalAlignment.Left; Grabber.Cursor = Cursors.Hand;
            Grabber.PreviewMouseLeftButtonDown += BeginDrag; Grabber.PreviewMouseMove += MoveDrag;
            Grabber.PreviewMouseLeftButtonUp += delegate { armed = false; };
            View.Children.Add(Grabber);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(actions, 1); View.Children.Add(actions);
            UndoButton = Button(null, "撤销", "取回刚才的纸页；正在写的内容会先收好");
            UndoButton.Visibility = Visibility.Collapsed; UndoButton.Click += delegate { Undo(); }; actions.Children.Add(UndoButton);
            ArchiveButton = Button("archive", compact ? null : "收起，换一张", "收起这一页并换一张空白；可在历史记录中取回");
            ArchiveButton.Click += delegate { StorePaper("archive", null); };
            if (!compact)
            {
                ArchiveButton.Background = Brush("#64806B"); ArchiveButton.Foreground = Brushes.White;
                ArchiveButton.Content = new TextBlock { Text = "收起，换一张", FontSize = 14, Foreground = Brushes.White };
                ArchiveButton.Padding = new Thickness(18, 11, 18, 11); ArchiveButton.Margin = new Thickness(8, 0, 0, 0);
            }
            actions.Children.Add(ArchiveButton);
            TrashButton = Button("trash", compact ? null : "纸篓", "放入纸篓并换一张空白；旧纸仍可找回");
            if (!compact)
            {
                var binContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                binImage = new Image { Source = AppVisuals.IllustrationSource("memo-bin.png"), Width = 78, Height = 92, Stretch = Stretch.Uniform, IsHitTestVisible = false };
                binContent.Children.Add(binImage);
                binContent.Children.Add(new TextBlock { Text = "纸篓", FontSize = 13, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
                binContent.Children.Add(new TextBlock { Text = "旧纸可以找回", FontSize = 11, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                TrashButton.Content = binContent; TrashButton.Padding = new Thickness(9, 3, 9, 3);
            }
            TrashButton.Click += delegate { StorePaper("trash", null); };
            Bin = new Border { Child = TrashButton, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, AllowDrop = true };
            Bin.DragOver += DragOver; Bin.DragLeave += delegate { ResetBin(); };
            Bin.Drop += Drop; actions.Children.Add(Bin);
            store.Changed += Changed; Refresh();
        }

        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private Button Button(string icon, string label, string hint)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null) content.Children.Add(AppVisuals.Icon(icon, compact ? 16 : 18, Muted));
            if (label != null) content.Children.Add(new TextBlock { Text = label, FontSize = compact ? 12 : 13, Margin = new Thickness(icon == null ? 0 : 7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Background = Brushes.Transparent, Foreground = Ink, BorderThickness = new Thickness(0), Padding = compact ? new Thickness(5, 2, 5, 2) : new Thickness(10, 8, 10, 8), ToolTip = hint, Cursor = Cursors.Hand };
            AutomationProperties.SetName(button, hint.Split('；')[0]);
            return button;
        }
        private void Changed(object sender, EventArgs e) { Refresh(); }
        internal void Refresh()
        {
            bool hasText = !String.IsNullOrWhiteSpace(store.State.Ideas);
            Grabber.IsEnabled = ArchiveButton.IsEnabled = TrashButton.IsEnabled = hasText && !busy;
        }
        internal bool StorePaper(string kind, string expectedText, long? expectedGeneration = null)
        {
            if (busy || disposed) return false;
            busy = true;
            try
            {
                if (!flush()) return false;
                MemoPaper paper = store.StoreCurrentMemo(kind, expectedText, expectedGeneration);
                if (paper == null) return false;
                lastPaper = paper.Text; UndoButton.Visibility = Visibility.Visible;
                report(kind == "trash" ? "已放入纸篓，旧纸可以找回。新的一页准备好了。" : "这一页已收好。给下一个念头，留一张空白。", false);
                if (focus != null) View.Dispatcher.BeginInvoke(new Action(focus));
                return true;
            }
            catch (Exception error) { report("没有换页，原文仍保留。" + error.Message, true); return false; }
            finally { busy = false; ResetBin(); Refresh(); }
        }
        internal bool Undo()
        {
            if (busy || disposed || lastPaper == null) return false;
            busy = true;
            try
            {
                if (!flush()) return false;
                bool changed = store.OpenMemoText(lastPaper);
                lastPaper = null; UndoButton.Visibility = Visibility.Collapsed;
                report("已取回刚才的纸页；期间写下的新内容也已收好。", false);
                if (focus != null) View.Dispatcher.BeginInvoke(new Action(focus));
                return changed;
            }
            catch (Exception error) { report("暂时无法取回，当前内容仍保留。" + error.Message, true); return false; }
            finally { busy = false; Refresh(); }
        }
        private void BeginDrag(object sender, MouseButtonEventArgs e) { origin = e.GetPosition(Grabber); armed = e.ClickCount == 1; }
        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (!armed || e.LeftButton != MouseButtonState.Pressed || busy) return;
            Point point = e.GetPosition(Grabber);
            if (Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            armed = false;
            if (!flush() || String.IsNullOrWhiteSpace(store.State.Ideas)) return;
            var payload = new PaperDrag(store, store.State.Ideas);
            Grabber.Opacity = 0.6;
            Transform previousTransform = PaperSurface == null ? null : PaperSurface.RenderTransform;
            if (PaperSurface != null && SystemParameters.ClientAreaAnimation)
                PaperSurface.RenderTransform = new TranslateTransform(0, -3);
            try { DragDrop.DoDragDrop(Grabber, new DataObject(typeof(PaperDrag), payload), DragDropEffects.Move); }
            finally
            {
                Grabber.Opacity = 1;
                if (PaperSurface != null && previousTransform != null) PaperSurface.RenderTransform = previousTransform;
                ResetBin();
            }
        }
        private bool Accept(IDataObject data, out PaperDrag paper)
        {
            paper = data.GetDataPresent(typeof(PaperDrag)) ? data.GetData(typeof(PaperDrag)) as PaperDrag : null;
            return paper != null && Object.ReferenceEquals(paper.Store, store) && paper.Generation == store.MemoPaperGeneration &&
                !String.IsNullOrWhiteSpace(paper.Text) && paper.Text == store.State.Ideas;
        }
        private void DragOver(object sender, DragEventArgs e)
        {
            PaperDrag paper; bool accepted = Accept(e.Data, out paper);
            e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
            Bin.Background = accepted ? Pale : Brushes.Transparent;
            if (binImage != null) binImage.Effect = accepted ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(151, 184, 165), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.65 } : null;
            Bin.ToolTip = accepted ? "松手放入纸篓，旧纸仍可找回" : "这里只接收本应用的整张随手记";
        }
        private void Drop(object sender, DragEventArgs e)
        {
            PaperDrag paper; e.Handled = true; e.Effects = DragDropEffects.None;
            if (Accept(e.Data, out paper) && StorePaper("trash", paper.Text, paper.Generation)) e.Effects = DragDropEffects.Move;
            ResetBin();
        }
        private void ResetBin() { Bin.Background = Brushes.Transparent; Bin.ToolTip = null; if (binImage != null) binImage.Effect = null; }
        public void Dispose()
        {
            if (disposed) return; disposed = true; store.Changed -= Changed;
        }
    }
}
