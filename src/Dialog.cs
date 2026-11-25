using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MyTaskManager
{
    /* App-styled replacement for MessageBox: a rounded card with an icon, heading, message and buttons */
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
                Title = "My Task Manager", Width = 420, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                WindowStartupLocation = hasOwner ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = !hasOwner, Topmost = !hasOwner || owner.Topmost,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, UseLayoutRounding = true, Foreground = Theme.B("Text")
            };
            if (hasOwner) w.Owner = owner;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);

            string glyph; Brush fg, bg;
            switch (kind)
            {
                case Kind.Danger: glyph = ""; fg = Theme.B("Danger"); bg = Theme.B("DangerSoft"); break;
                case Kind.Warning: glyph = ""; fg = Theme.B("Warn"); bg = Theme.B("WarnSoft"); break;
                case Kind.Ok: glyph = ""; fg = Theme.B("Ok"); bg = Theme.B("OkSoft"); break;
                case Kind.Info: glyph = ""; fg = Theme.B("Accent"); bg = Theme.B("AccentSoft"); break;
                default: glyph = ""; fg = Theme.B("Accent"); bg = Theme.B("AccentSoft"); break;
            }

            Border badge = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = bg, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 18, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            StackPanel text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            if (!string.IsNullOrEmpty(message))
                text.Children.Add(new TextBlock { Text = message, Foreground = Theme.B("Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19 });
            DockPanel top = new DockPanel();
            DockPanel.SetDock(badge, Dock.Left);
            top.Children.Add(badge);
            top.Children.Add(text);

            StackPanel foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            Button yes = new Button { Style = Theme.S("Primary"), Content = yesText, MinWidth = 88, Padding = new Thickness(18, 8, 18, 8) };
            if (kind == Kind.Danger) { yes.Background = Theme.B("Danger"); yes.BorderBrush = Theme.B("Danger"); }
            if (noText != null)
            {
                Button no = new Button { Style = Theme.S("Btn"), Content = noText, MinWidth = 88, Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
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

            StackPanel body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(foot);

            Border card = new Border
            {
                Margin = new Thickness(16), Padding = new Thickness(22, 20, 20, 18), CornerRadius = new CornerRadius(12),
                Background = Theme.B("Surface"), BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(1), Child = body,
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.22 }
            };
            card.MouseLeftButtonDown += (o, e) => { try { w.DragMove(); } catch { } };
            w.Content = card;
            w.PreviewKeyDown += (o, e) => { if (e.Key == Key.Escape && noText == null) { w.DialogResult = true; e.Handled = true; } };
            return w;
        }
    }
}
