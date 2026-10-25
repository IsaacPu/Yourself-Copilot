using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace LocalDesktopNote
{
    internal static class MemoTitlePrompt
    {
        internal static string Ask(FrameworkElement source, string current)
        {
            var content = new StackPanel { Margin = new Thickness(22) };
            content.Children.Add(new TextBlock { Text = "给这篇随手记起个标题", FontSize = 15, Margin = new Thickness(0, 0, 0, 13) });
            var editor = new TextBox { Text = current ?? "", MaxLength = WorkboardStore.MemoTitleLimit, FontSize = 14, Padding = new Thickness(8, 6, 8, 6) };
            System.Windows.Automation.AutomationProperties.SetName(editor, "随手记标题"); content.Children.Add(editor);
            content.Children.Add(new TextBlock { Text = "留空则使用正文开头，正文不会改变。", Foreground = Brushes.Gray, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 16) });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = "保存标题", IsDefault = true, Padding = new Thickness(14, 5, 14, 5) };
            actions.Children.Add(cancel); actions.Children.Add(save); content.Children.Add(actions);
            var dialog = new Window { Title = "修改标题", Content = content, Width = 356, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Background = (Brush)new BrushConverter().ConvertFromString("#FAF8F3"), FontFamily = AppTypography.Body };
            dialog.SetValue(TextElement.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#344438"));
            var host = PresentationSource.FromVisual(source) as HwndSource;
            var control = host == null ? null : System.Windows.Forms.Control.FromChildHandle(host.Handle);
            var owner = control == null ? null : control.FindForm();
            if (owner != null) new WindowInteropHelper(dialog).Owner = owner.Handle;
            save.Click += delegate { dialog.DialogResult = true; };
            dialog.Loaded += delegate { editor.Focus(); editor.SelectAll(); };
            return dialog.ShowDialog() == true ? editor.Text : null;
        }
    }
}
