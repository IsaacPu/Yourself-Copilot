using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace LocalDesktopNote
{
    // Only the grip captures the pointer. The actual paper follows it in an
    // app-local, non-hit-testable layer, without the OS file-drop cursor.
    internal sealed class MemoPaperControls : IDisposable
    {
        internal readonly Grid View;
        internal readonly Button Grabber, TrashButton;
        internal readonly Border Bin;
        internal FrameworkElement PaperSurface, PaperDecoration;
        internal bool IsPaperLifted { get { return lifted; } }
        internal FrameworkElement DragPreview { get { return preview; } }
        internal Action PaperDropSound = MemoSounds.PlayDrop;
        private readonly Image binImage;
        private readonly WorkboardStore store;
        private readonly Func<bool> flush;
        private readonly Action<string, bool> report;
        private readonly Action focus;
        private readonly bool compact;
        private Grid dragHost;
        private Canvas dragLayer;
        private Border preview, settlingPreview;
        private Point origin;
        private bool armed, lifted, busy, disposed;
        private string liftedText;
        private long liftedGeneration;
        private double surfaceOpacity, decorationOpacity;
        private static readonly Brush Ink = Brush("#344438"), Muted = Brush("#7D877C"), Pale = Brush("#E7EBE1");

        internal MemoPaperControls(WorkboardStore store, Func<bool> flush, Action<string, bool> report, Action focus, bool compact)
        {
            this.store = store; this.flush = flush; this.report = report; this.focus = focus; this.compact = compact;
            View = new Grid(); View.ColumnDefinitions.Add(new ColumnDefinition()); View.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grabber = Button(compact ? "note" : "more", compact ? "随手记" : "拖起这一页", "抓起整张随手记，拖到纸篓；正文仍可选字编辑");
            Grabber.HorizontalAlignment = HorizontalAlignment.Left; Grabber.Cursor = Cursors.Hand; Grabber.FocusVisualStyle = null;
            Grabber.PreviewMouseLeftButtonDown += BeginDrag; Grabber.PreviewMouseMove += MoveDrag; Grabber.PreviewMouseLeftButtonUp += EndDrag;
            Grabber.LostMouseCapture += delegate { CancelPaperLift(); };
            View.Children.Add(Grabber);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(actions, 1); View.Children.Add(actions);
            TrashButton = Button("trash", compact ? null : "纸篓", "放入纸篓并换一张空白；旧纸仍可找回");
            if (!compact)
            {
                var binContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                binImage = new Image { Source = AppVisuals.IllustrationSource("memo-bin.png"), Width = 78, Height = 92, Stretch = Stretch.Uniform, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.85) };
                binContent.Children.Add(binImage);
                binContent.Children.Add(new TextBlock { Text = "纸篓", FontSize = 13, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) });
                binContent.Children.Add(new TextBlock { Text = "旧纸可以找回", FontSize = 11, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                TrashButton.Content = binContent; TrashButton.Padding = new Thickness(9, 3, 9, 3);
            }
            TrashButton.Click += delegate { if (StorePaper("trash", null)) FeedBin(); };
            Bin = new Border { Child = TrashButton, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            actions.Children.Add(Bin);
            store.Changed += Changed; Refresh();
        }

        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private Button Button(string icon, string label, string hint)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (icon != null) content.Children.Add(AppVisuals.Icon(icon, compact ? 16 : 18, Muted));
            if (label != null) content.Children.Add(new TextBlock { Text = label, FontSize = compact ? 12 : 13, Margin = new Thickness(icon == null ? 0 : 7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, Background = Brushes.Transparent, Foreground = Ink, BorderThickness = new Thickness(0), Padding = compact ? new Thickness(5, 2, 5, 2) : new Thickness(10, 8, 10, 8), ToolTip = hint, Cursor = Cursors.Hand };
            AutomationProperties.SetName(button, hint.Split('；')[0]); return button;
        }
        internal void AttachDragHost(Grid host)
        {
            if (dragHost != null) throw new InvalidOperationException("纸页拖动区域已经连接。");
            dragHost = host; dragLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            Grid.SetColumnSpan(dragLayer, 256); Grid.SetRowSpan(dragLayer, 256); Panel.SetZIndex(dragLayer, 1000); host.Children.Add(dragLayer);
            host.SizeChanged += HostSizeChanged; Grabber.IsVisibleChanged += GripVisibilityChanged;
        }
        private void HostSizeChanged(object sender, SizeChangedEventArgs e) { if (lifted || armed) CancelPaperLift(); }
        private void GripVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!(bool)e.NewValue) CancelPaperLift(); }
        private void Changed(object sender, EventArgs e) { if (lifted && !CurrentLift()) CancelPaperLift(); Refresh(); }
        internal void Refresh()
        {
            bool hasText = !String.IsNullOrWhiteSpace(store.State.Ideas);
            Grabber.IsEnabled = TrashButton.IsEnabled = hasText && !busy;
            if (binImage != null) binImage.Source = AppVisuals.IllustrationSource(store.State.MemoPapers != null && store.State.MemoPapers.Any(p => p.Kind == "trash") ? "memo-bin-filled.png" : "memo-bin.png") ?? AppVisuals.IllustrationSource("memo-bin.png");
        }
        internal bool StorePaper(string kind, string expectedText, long? expectedGeneration = null)
        {
            if (busy || disposed) return false;
            busy = true;
            try
            {
                if (!flush() || store.StoreCurrentMemo(kind, expectedText, expectedGeneration) == null) return false;
                if (kind == "trash")
                {
                    // Feedback follows durable storage; audio is never allowed
                    // to turn a completed save into an apparent failure.
                    try { if (PaperDropSound != null) PaperDropSound(); } catch { }
                }
                report(kind == "trash" ? "这一页已放入纸篓 · Ctrl+Z 可取回" : "这一页已收好 · Ctrl+Z 可取回", false);
                if (focus != null) View.Dispatcher.BeginInvoke(new Action(focus)); return true;
            }
            catch (Exception error) { report("没有换页，原文仍保留。" + error.Message, true); return false; }
            finally { busy = false; ResetBin(); Refresh(); }
        }
        internal bool Undo() { return Travel(false); }
        internal bool Redo() { return Travel(true); }
        private bool Travel(bool redo)
        {
            if (busy || disposed) return false;
            CancelPaperLift(); busy = true;
            try
            {
                if (!flush() || !(redo ? store.RedoMemoPaper() : store.UndoMemoPaper())) return false;
                report(redo ? "已重做换页 · Ctrl+Z 可撤销" : "已撤销换页 · Ctrl+Y 可重做；新写的内容已保护", false);
                if (focus != null) View.Dispatcher.BeginInvoke(new Action(focus)); return true;
            }
            catch (Exception error) { report("暂时无法切换，当前内容仍保留。" + error.Message, true); return false; }
            finally { busy = false; Refresh(); }
        }
        internal bool HandleHistoryKey(Key key, ModifierKeys modifiers, TextBox editor, bool memoScope)
        {
            if (!memoScope || modifiers != ModifierKeys.Control || (key != Key.Z && key != Key.Y)) return false;
            if (lifted || armed) CancelPaperLift();
            bool redo = key == Key.Y;
            if (editor != null && (redo ? editor.CanRedo : editor.CanUndo)) { if (redo) editor.Redo(); else editor.Undo(); return true; }
            if (redo ? store.CanRedoMemoPaper : store.CanUndoMemoPaper) { if (redo) Redo(); else Undo(); return true; }
            return false;
        }
        internal static void ReplaceEditorText(TextBox editor, string text)
        {
            bool undo = editor.IsUndoEnabled; editor.IsUndoEnabled = false;
            try { editor.Text = text ?? ""; } finally { editor.IsUndoEnabled = undo; }
        }
        private void BeginDrag(object sender, MouseButtonEventArgs e)
        {
            if (dragHost == null || busy || disposed || e.ClickCount != 1) return;
            // Let WPF finish any editor IME composition before flushing and
            // freezing the sheet. Handling PreviewDown skips Button's default.
            Grabber.Focus();
            if (Keyboard.Focus(Grabber) != Grabber) return;
            CancelPaperLift(); origin = e.GetPosition(dragHost); armed = Grabber.CaptureMouse(); e.Handled = true;
        }
        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (!armed && !lifted) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelPaperLift(); return; }
            Point point = e.GetPosition(dragHost);
            if (!lifted)
            {
                if (Math.Abs(point.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                try { if (!StartPaperLift(point)) { CancelPaperLift(); return; } }
                catch (Exception error) { CancelPaperLift(); report("暂时无法拿起纸页，内容仍保留。" + error.Message, true); return; }
            }
            MovePaperLift(point); e.Handled = true;
        }
        private void EndDrag(object sender, MouseButtonEventArgs e)
        {
            if (!armed && !lifted) return;
            Point point = e.GetPosition(dragHost); if (lifted) FinishPaperLift(point); else CancelPaperLift(); e.Handled = true;
        }
        internal bool StartPaperLift(Point point)
        {
            if (busy || disposed || lifted || dragHost == null || PaperSurface == null || !InCurrentView(Grabber) || !InCurrentView(PaperSurface) || PaperSurface.ActualWidth <= 0 || PaperSurface.ActualHeight <= 0) return false;
            if (!flush() || String.IsNullOrWhiteSpace(store.State.Ideas)) return false;
            RemoveSettlingPreview(); liftedText = store.State.Ideas; liftedGeneration = store.MemoPaperGeneration;
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(PaperSurface.ActualWidth)), Math.Max(1, (int)Math.Ceiling(PaperSurface.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(PaperSurface); bitmap.Freeze();
            double scale = Math.Min(0.6, Math.Min(Math.Min(260, dragHost.ActualWidth * 0.32) / PaperSurface.ActualWidth, Math.Min(210, dragHost.ActualHeight * 0.32) / PaperSurface.ActualHeight));
            preview = new Border { Width = PaperSurface.ActualWidth * scale, Height = PaperSurface.ActualHeight * scale, Child = new Image { Source = bitmap, Stretch = Stretch.Fill }, IsHitTestVisible = false, Opacity = 0.68,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(60, 72, 57), BlurRadius = 24, ShadowDepth = 12, Opacity = 0.24 }, RenderTransform = new RotateTransform(-3, PaperSurface.ActualWidth * scale / 2, 16) };
            surfaceOpacity = PaperSurface.Opacity; PaperSurface.Opacity = 0.12;
            if (PaperDecoration != null) { decorationOpacity = PaperDecoration.Opacity; PaperDecoration.Opacity = 0.1; }
            Grabber.Opacity = 0.2; lifted = true; armed = false; dragLayer.Children.Add(preview); MovePaperLift(point); return true;
        }
        private bool CurrentLift() { return liftedGeneration == store.MemoPaperGeneration && liftedText == store.State.Ideas; }
        private bool BinContains(Point point)
        {
            if (dragHost == null || !InCurrentView(Bin) || !InCurrentView(Grabber) || !TrashButton.IsEnabled) return false;
            return Bin.TransformToAncestor(dragHost).TransformBounds(new Rect(Bin.RenderSize)).Contains(point);
        }
        private bool InCurrentView(FrameworkElement element)
        {
            // Scope belongs to the WPF page. WinForms presentation visibility
            // is handled by pointer dispatch, deactivation and capture loss.
            for (DependencyObject node = element; node != null; node = VisualTreeHelper.GetParent(node))
            {
                var view = node as UIElement;
                if (view != null && view.Visibility != Visibility.Visible) return false;
                if (node == dragHost) return true;
            }
            return false;
        }
        internal void MovePaperLift(Point point)
        {
            if (!lifted) return;
            if (!CurrentLift()) { CancelPaperLift(); return; }
            double left = Math.Max(8, Math.Min(dragHost.ActualWidth - preview.Width - 8, point.X - preview.Width - 24));
            double top = Math.Max(8, Math.Min(dragHost.ActualHeight - preview.Height - 8, point.Y - preview.Height - 24));
            Rect bin = Bin.TransformToAncestor(dragHost).TransformBounds(new Rect(Bin.RenderSize));
            var occupied = new Rect(left, top, preview.Width, preview.Height); occupied.Inflate(16, 16);
            if (occupied.IntersectsWith(bin))
            {
                // Leave the entire target visible, including the small rotated
                // corners of the ghost. The pointer remains the drop anchor.
                if (bin.Top - preview.Height - 24 >= 8) top = Math.Min(top, bin.Top - preview.Height - 24);
                else if (bin.Left - preview.Width - 24 >= 8) left = Math.Min(left, bin.Left - preview.Width - 24);
            }
            Canvas.SetLeft(preview, left); Canvas.SetTop(preview, top);
            bool over = BinContains(point); preview.Opacity = over ? 0.45 : 0.68; Bin.Background = over ? Pale : Brushes.Transparent;
            if (binImage != null) binImage.Effect = over ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(151, 184, 165), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.7 } : null;
        }
        internal bool FinishPaperLift(Point point)
        {
            if (!lifted) { CancelPaperLift(); return false; }
            bool accepted = CurrentLift() && BinContains(point); string text = liftedText; long generation = liftedGeneration; Border paper = preview;
            ClearLift(false);
            if (!accepted || !StorePaper("trash", text, generation)) { dragLayer.Children.Remove(paper); return false; }
            FeedBin(); SettlePaper(paper); return true;
        }
        internal void CancelPaperLift() { ClearLift(true); }
        private void ClearLift(bool remove)
        {
            armed = false;
            if (lifted)
            {
                lifted = false; PaperSurface.Opacity = surfaceOpacity;
                if (PaperDecoration != null) PaperDecoration.Opacity = decorationOpacity;
                if (remove && preview != null) dragLayer.Children.Remove(preview);
                preview = null; liftedText = null;
            }
            Grabber.Opacity = 1; if (Grabber.IsMouseCaptured) Grabber.ReleaseMouseCapture(); ResetBin();
        }
        private void SettlePaper(Border paper)
        {
            if (!SystemParameters.ClientAreaAnimation) { dragLayer.Children.Remove(paper); return; }
            settlingPreview = paper;
            var scale = new ScaleTransform(1, 1, paper.Width / 2, paper.Height / 2); var translate = new TranslateTransform();
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(translate); paper.RenderTransform = group;
            var bounds = Bin.TransformToAncestor(dragHost).TransformBounds(new Rect(Bin.RenderSize)); var duration = TimeSpan.FromMilliseconds(190);
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(bounds.Left + bounds.Width / 2 - Canvas.GetLeft(paper) - paper.Width / 2, duration));
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(bounds.Top + Math.Min(40, bounds.Height / 2) - Canvas.GetTop(paper) - paper.Height / 2, duration));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.05, duration)); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.05, duration));
            var fade = new DoubleAnimation(0, duration); fade.Completed += delegate { dragLayer.Children.Remove(paper); if (settlingPreview == paper) settlingPreview = null; }; paper.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        private void FeedBin()
        {
            if (binImage == null || !SystemParameters.ClientAreaAnimation) return;
            var turn = new RotateTransform(); binImage.RenderTransform = turn; var motion = new DoubleAnimationUsingKeyFrames();
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240))));
            motion.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)))); turn.BeginAnimation(RotateTransform.AngleProperty, motion);
        }
        private void RemoveSettlingPreview()
        {
            if (settlingPreview == null) return;
            settlingPreview.BeginAnimation(UIElement.OpacityProperty, null); dragLayer.Children.Remove(settlingPreview); settlingPreview = null;
        }
        private void ResetBin() { Bin.Background = Brushes.Transparent; if (binImage != null) binImage.Effect = null; }
        public void Dispose()
        {
            if (disposed) return; CancelPaperLift(); RemoveSettlingPreview(); disposed = true; store.Changed -= Changed; Grabber.IsVisibleChanged -= GripVisibilityChanged;
            if (dragHost != null) { dragHost.SizeChanged -= HostSizeChanged; dragHost.Children.Remove(dragLayer); }
        }
    }
}
