using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyTaskManager
{
    /* "Remove from this PC" confirmation. Returns false if cancelled. */
    public static class UninstallDialog
    {
        public static bool Ask(Window owner, out bool deleteData)
        {
            deleteData = false;
            Window w = new Window
            {
                Title = "Remove My Task Manager", Width = 500, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = owner == null || !owner.IsVisible, Background = Theme.B("Surface"),
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, UseLayoutRounding = true
            };
            if (owner != null && owner.IsVisible) w.Owner = owner;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            Theme.TitleBar(w);

            StackPanel root = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
            root.Children.Add(new TextBlock { Text = "Remove My Task Manager from this PC?", FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Foreground = Theme.B("Text2"), Margin = new Thickness(0, 0, 0, 14),
                Text = "This will:\n•  stop it starting with Windows\n•  close it (reminders and the sticky note stop)\n\nAfterwards you can delete MyTaskManager.exe yourself. The folder will open for you."
            });
            CheckBox del = new CheckBox
            {
                Style = Theme.S("Check"), Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBlock { Text = "Also delete all my tasks, history, backups and the note photo", TextWrapping = TextWrapping.Wrap, MaxWidth = 400 }
            };
            root.Children.Add(del);
            TextBlock warn = new TextBlock
            {
                Text = "Deleted tasks can't be recovered. Use Export backup first if you might want them later.",
                Foreground = Theme.B("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30, 0, 0, 0), Visibility = Visibility.Collapsed
            };
            root.Children.Add(warn);
            del.Click += delegate { warn.Visibility = del.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; };

            StackPanel foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Button cancel = new Button { Style = Theme.S("Btn"), Content = "Cancel", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            Button go = new Button { Style = Theme.S("Primary"), Content = "Remove", Padding = new Thickness(18, 8, 18, 8), Background = Theme.B("Danger"), BorderBrush = Theme.B("Danger") };
            foot.Children.Add(cancel);
            foot.Children.Add(go);
            root.Children.Add(foot);
            w.Content = root;

            go.Click += delegate { w.DialogResult = true; };
            bool ok = w.ShowDialog() == true;
            deleteData = ok && del.IsChecked == true;
            return ok;
        }
    }
}
