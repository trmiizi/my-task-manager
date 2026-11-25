using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MyTaskManager
{
    /* A button that looks like an input and opens a calendar */
    public class DateField
    {
        public readonly Grid Root = new Grid();
        readonly Button btn;
        readonly TextBlock text;
        readonly Popup pop;
        readonly Calendar cal;
        string value = U.Today;
        bool suppress;
        public event Action Changed;

        public DateField()
        {
            text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            TextBlock glyph = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "Text2");
            DockPanel dp = new DockPanel();
            DockPanel.SetDock(glyph, Dock.Right);
            dp.Children.Add(glyph);
            dp.Children.Add(text);
            btn = new Button { Style = Theme.S("Field"), Content = dp };
            AutomationName("Date");

            cal = new Calendar { FirstDayOfWeek = DayOfWeek.Monday };
            pop = new Popup
            {
                PlacementTarget = btn, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                VerticalOffset = 4, PopupAnimation = PopupAnimation.Fade,
                Child = Theme.Ref(Theme.Ref(new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = cal }, Border.BackgroundProperty, "Surface"), Border.BorderBrushProperty, "Line")
            };
            btn.Click += delegate { OpenAt(btn); };
            cal.SelectedDatesChanged += delegate
            {
                if (suppress || !cal.SelectedDate.HasValue) return;
                Value = U.Ymd(cal.SelectedDate.Value);
                pop.IsOpen = false;
            };
            // WPF quirk: the calendar keeps mouse capture after a click, swallowing the next click elsewhere
            cal.PreviewMouseUp += delegate { if (Mouse.Captured is CalendarItem) Mouse.Capture(null); };
            Root.Children.Add(btn);
            Root.Children.Add(pop);
            Refresh();
        }

        void AutomationName(string n) { System.Windows.Automation.AutomationProperties.SetName(btn, n); }

        /* Opens the calendar under any element (the sticky note's "Other day" button uses this) */
        public void OpenAt(UIElement target)
        {
            suppress = true;
            cal.SelectedDate = U.ParseYmd(value);
            cal.DisplayDate = U.ParseYmd(value);
            suppress = false;
            pop.PlacementTarget = target;
            pop.IsOpen = true;
        }

        public bool IsOpen { get { return pop.IsOpen; } }
        public void Close() { pop.IsOpen = false; }

        public string Value
        {
            get { return value; }
            set { this.value = value; Refresh(); if (Changed != null) Changed(); }
        }

        void Refresh()
        {
            string friendly = U.FmtDate(value);
            DateTime d = U.ParseYmd(value);
            text.Text = d.ToString(d.Year == DateTime.Today.Year ? "ddd, d MMM" : "d MMM yyyy", U.En);
            btn.ToolTip = U.FmtLong(value);
        }
    }

    public static class Ui
    {
        public static ComboBox TimeCombo(ComboBox c)
        {
            if (c == null) c = new ComboBox();
            c.IsEditable = true;
            c.IsTextSearchEnabled = false;
            c.Items.Clear();
            for (int i = 0; i < 48; i++) c.Items.Add(U.FmtTime(TimeSpan.FromMinutes(30 * i)));
            return c;
        }

        public static bool ReadTime(ComboBox c, out string hhmm)
        {
            TimeSpan t;
            hhmm = null;
            if (!U.TryParseTime(c.Text, out t)) return false;
            hhmm = U.HHmm(t);
            return true;
        }

        public static void SetTime(ComboBox c, string hhmm) { c.Text = U.FmtTime(hhmm); }

        public static ComboBox RemindCombo(ComboBox c)
        {
            if (c == null) c = new ComboBox();
            c.Items.Clear();
            foreach (int v in U.RemindValues) c.Items.Add(new ComboBoxItem { Content = U.RemindLabel(v), Tag = v });
            SetRemind(c, 15);
            return c;
        }
        public static int GetRemind(ComboBox c) { ComboBoxItem i = c.SelectedItem as ComboBoxItem; return i == null ? 15 : (int)i.Tag; }
        public static void SetRemind(ComboBox c, int v)
        {
            foreach (ComboBoxItem i in c.Items) if ((int)i.Tag == v) { c.SelectedItem = i; return; }
            c.SelectedIndex = 3;
        }

        public static ComboBox RepeatCombo(ComboBox c)
        {
            if (c == null) c = new ComboBox();
            c.Items.Clear();
            foreach (string k in U.RepeatKeys) c.Items.Add(new ComboBoxItem { Content = U.RepeatLabel(k), Tag = k });
            c.SelectedIndex = 0;
            return c;
        }
        public static string GetRepeat(ComboBox c) { ComboBoxItem i = c.SelectedItem as ComboBoxItem; return i == null ? "none" : (string)i.Tag; }
        public static void SetRepeat(ComboBox c, string v) { c.SelectedIndex = Math.Max(0, Array.IndexOf(U.RepeatKeys, v)); }

        public static Border Pill(string glyph, string text, string kind)
        {
            Brush bg = Theme.B("Surface2"), fg = Theme.B("Text2"), bd = Theme.B("Line");
            switch (kind)
            {
                case "late": bg = Theme.B("DangerSoft"); fg = Theme.B("Danger"); bd = Brushes.Transparent; break;
                case "soon": bg = Theme.B("WarnSoft"); fg = Theme.B("Warn"); bd = Brushes.Transparent; break;
                case "repeat": bg = Theme.B("AccentSoft"); fg = Theme.B("Accent"); bd = Brushes.Transparent; break;
                case "ok": bg = Theme.B("OkSoft"); fg = Theme.B("Ok"); bd = Brushes.Transparent; break;
            }
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (glyph != null)
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 11, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0) });
            sp.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            return new Border { Background = bg, BorderBrush = bd, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 3), Margin = new Thickness(0, 0, 6, 6), Child = sp };
        }

        public static FrameworkElement Field(string label, UIElement control)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Style = Theme.S("Label") });
            sp.Children.Add(control);
            return sp;
        }

        /* Tick box with three states: empty = pending, green tick = Done, red cross = Not done.
           Click cycles pending -> Done -> Not done -> pending. */
        public static CheckBox StatusBox(TaskService svc, TaskItem t, Action<string> onNextMonth)
        {
            CheckBox cb = new CheckBox
            {
                Style = Theme.S("Check"), IsThreeState = true, VerticalAlignment = VerticalAlignment.Top,
                IsChecked = t.Done ? true : t.Skipped ? (bool?)null : false,
                ToolTip = t.Done ? "Done · click to mark Not done" : t.Skipped ? "Not done · click to reset" : "Click once: Done · twice: Not done"
            };
            System.Windows.Automation.AutomationProperties.SetName(cb, t.Title);
            cb.Click += delegate
            {
                string status = cb.IsChecked == true ? "done" : cb.IsChecked == null ? "notdone" : "pending";
                string next = svc.SetStatus(t, status);
                if (next != null && onNextMonth != null) onNextMonth(next);
            };
            return cb;
        }

        public static ContextMenu StatusMenu(TaskService svc, TaskItem t, Window owner)
        {
            ContextMenu m = new ContextMenu();
            Func<string, string, bool, Action, MenuItem> item = (glyph, text, enabled, act) =>
            {
                MenuItem mi = new MenuItem { Header = text, IsEnabled = enabled };
                if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
                mi.Click += delegate { act(); };
                return mi;
            };
            m.Items.Add(item("", "Mark done", !t.Done, () => svc.SetStatus(t, "done")));
            m.Items.Add(item("", "Mark not done", !t.Skipped, () => svc.SetStatus(t, "notdone")));
            m.Items.Add(item("", "Back to pending", t.Closed, () => svc.SetStatus(t, "pending")));
            if (t.Archived) m.Items.Add(item("", "Unarchive", true, () => svc.Unarchive(t)));
            else m.Items.Add(item("", "Archive", t.Closed, () => svc.Archive(new[] { t })));
            m.Items.Add(new Separator());
            m.Items.Add(item("", "Edit…", true, () => EditWindow.Open(owner, svc, t)));
            m.Items.Add(item("", t.HasSteps ? "Edit checklist…" : "Add checklist…", true, () => EditWindow.Open(owner, svc, t, true)));
            m.Items.Add(item("", "Delete", true, () =>
            {
                string msg = "\u201C" + t.Title + "\u201D will be removed. This can't be undone.";
                if (t.Repeats && !t.Closed) msg += "\n\nThis also stops the " + U.RepeatShort(t.Repeat).ToLowerInvariant() + " repeat.";
                if (Dialog.Confirm(owner, "Delete this task?", msg, "Delete", "Cancel", Dialog.Kind.Danger))
                    svc.Delete(t);
            }));
            return m;
        }

        /* One checklist step with its own small tick box */
        public static CheckBox StepBox(TaskService svc, TaskItem t, StepItem s, double fontSize, Brush ink, Brush muted)
        {
            CheckBox cb = new CheckBox
            {
                Style = Theme.S("StepCheck"), IsChecked = s.Done, FontSize = fontSize, Margin = new Thickness(0, 3, 0, 0),
                Content = new TextBlock
                {
                    Text = s.Text, TextWrapping = TextWrapping.Wrap, Foreground = s.Done ? muted : ink,
                    TextDecorations = s.Done ? TextDecorations.Strikethrough : null
                }
            };
            cb.Click += delegate { svc.SetStep(t, s, cb.IsChecked == true); };
            return cb;
        }

        public static Button IconButton(string glyph, string tip)
        {
            Button b = new Button { Style = Theme.S("Icon"), Content = glyph, ToolTip = tip };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            return b;
        }
    }
}
