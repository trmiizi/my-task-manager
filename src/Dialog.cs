using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MyTaskManager
{
    /* App-styled replacement for MessageBox. Same layout as the reminder card:
       a grey panel on the left with an icon, heading + message + buttons on the right. */
    public static class Dialog
    {
        public enum Kind { Question, Danger, Info, Ok, Warning }

        /* Yes/No style question. Returns true only when the main button is clicked. */
        public static bool Confirm(Window owner, string heading, string message, string yesText, string noText, Kind kind)
        {
            return Show(owner, heading, message, yesText, noText, kind);
        }

        /* Message with a single OK button */
        public static void Info(Window owner, string heading, string message, Kind kind)
        {
            Show(owner, heading, message, "OK", null, kind);
        }

        static bool Show(Window owner, string heading, string message, string yesText, string noText, Kind kind)
        {
            return Build(owner, heading, message, yesText, noText, kind).ShowDialog() == true;
        }

        public static Window Build(Window owner, string heading, string message, string yesText, string noText, Kind kind)
        {
            bool hasOwner = owner != null && owner.IsVisible;
            Window w = new Window
            {
                Title = "My Task Manager", Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                WindowStartupLocation = hasOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = !hasOwner, Topmost = !hasOwner || owner.Topmost,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, UseLayoutRounding = true, Foreground = Theme.B("Text")
            };
            if (hasOwner) w.Owner = owner;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);

            // icon in the side panel: power for Exit-type questions, bin for delete, tick for done, i for info, ! for problems
            string glyph; string fgKey = "Text2";
            switch (kind)
            {
                case Kind.Danger: glyph = ""; fgKey = "Danger"; break;
                case Kind.Warning: glyph = ""; fgKey = "Warn"; break;
                case Kind.Ok: glyph = ""; break;
                case Kind.Info: glyph = ""; break;
                default: glyph = ""; break;
            }

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            Border side = new Border
            {
                Background = Theme.B("Surface2"), CornerRadius = new CornerRadius(9, 0, 0, 9),
                Child = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 20, Foreground = Theme.B(fgKey), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            grid.Children.Add(side);

            StackPanel body = new StackPanel { Margin = new Thickness(18, 18, 20, 18) };
            body.Children.Add(new TextBlock { Text = heading, FontSize = 16.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(message))
                body.Children.Add(new TextBlock { Text = message, Foreground = Theme.B("Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 19 });

            StackPanel foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            // main button: dark "ink" like the reminder's Done, or red for something that deletes
            Button yes = new Button { Style = Theme.S("Btn"), Content = yesText, MinWidth = 72, Padding = new Thickness(16, 7, 16, 7) };
            Brush main = kind == Kind.Danger ? Theme.B("Danger") : Theme.B("Text");
            yes.Background = main; yes.BorderBrush = main; yes.Foreground = Theme.B("Surface");
            if (noText != null)
            {
                Button no = new Button { Style = Theme.S("Btn"), Content = noText, MinWidth = 72, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
                foot.Children.Add(no);
                // a risky action should not be one stray Enter away
                if (kind == Kind.Danger) no.IsDefault = true; else yes.IsDefault = true;
                w.Loaded += delegate { (kind == Kind.Danger ? no : yes).Focus(); };
            }
            else
            {
                yes.IsDefault = true; yes.IsCancel = true;
                w.Loaded += delegate { yes.Focus(); };
            }
            foot.Children.Add(yes);
            yes.Click += delegate { w.DialogResult = true; };
            body.Children.Add(foot);
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);

            Border card = new Border
            {
                Margin = new Thickness(16), CornerRadius = new CornerRadius(10),
                Background = Theme.B("Surface"), BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(1), Child = grid,
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.24 }
            };
            card.MouseLeftButtonDown += (o, e) => { try { w.DragMove(); } catch { } };
            w.Content = card;
            w.PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape && noText == null) { w.DialogResult = true; e.Handled = true; } };
            return w;
        }
    }
}
