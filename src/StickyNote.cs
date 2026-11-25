using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IO = System.IO;
using System.Windows.Threading;

namespace MyTaskManager
{
    /* A sticky note on the desktop: overdue, today and tomorrow, with three-state tick boxes and quick add. */
    public class StickyNote
    {
        public readonly Window Win;
        readonly TaskService svc;
        public Action OpenMain;
        public Action NoteHidden;
        public Action LockChanged;

        readonly StackPanel Rows;
        readonly TextBox QuickAdd;
        readonly TextBlock NoteTitle, NoteSub, QuickHint, LockMark;
        readonly Thumb Grip;
        readonly Border Paper, Header, Wash;
        readonly Button PinBtn, ColorBtn;
        ImageSource photo;
        // add panel: a "+ Add a task" line opens a panel that slides up over the bottom of the note
        readonly Border AddPanel;
        readonly Button AddLine;
        readonly TranslateTransform PanelShift;
        TextBlock panelError;
        Button dayOther, timeCustom, timeOther;
        Button[] dayPills, timePills;
        readonly List<TextBlock> panelLabels = new List<TextBlock>();
        bool showTimeBox;
        Brush PaperColor = Hex("#FFF7CF");
        DateField addDate;
        ComboBox addTime, addRemind, addRepeat;
        bool syncing, dateTouched, timeTouched;
        string photoPath;
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        readonly DispatcherTimer hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        // ink colours: dark on the paper colours, light on "night"
        bool Night { get { return svc.Settings.NoteColor == "night"; } }
        Brush Ink { get { return Night ? Hex("#E8E6DF") : Hex("#2B2718"); } }
        Brush Muted { get { return Night ? Hex("#9FA4B0") : Hex("#625C4A"); } }
        Brush Late { get { return Night ? Hex("#FF858A") : Hex("#C2333A"); } }
        Brush Good { get { return Night ? Hex("#6FD8A3") : Hex("#1F7A4D"); } }

        // paper, header, border
        static readonly Dictionary<string, string[]> Palettes = new Dictionary<string, string[]>
        {
            { "yellow", new[] { "#FFF7CF", "#FCE993", "#E9D57E" } },
            { "blue",   new[] { "#E8F2FF", "#CBE1FD", "#B3CDEE" } },
            { "green",  new[] { "#E8F7EB", "#C9EBD1", "#AFD8BA" } },
            { "pink",   new[] { "#FDEAF1", "#F8CDDD", "#EAB4C8" } },
            { "white",  new[] { "#FFFFFF", "#EEF0F4", "#D9DDE4" } },
            { "night",  new[] { "#242833", "#2E3340", "#3B4152" } },
        };

        static Brush Hex(string hex) { SolidColorBrush b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }

        public StickyNote(TaskService svc)
        {
            this.svc = svc;
            Win = (Window)XamlReader.Parse(Xaml);
            Rows = (StackPanel)Win.FindName("Rows");
            QuickAdd = (TextBox)Win.FindName("QuickAdd");
            NoteTitle = (TextBlock)Win.FindName("NoteTitle");
            NoteSub = (TextBlock)Win.FindName("NoteSub");
            QuickHint = (TextBlock)Win.FindName("QuickHint");
            LockMark = (TextBlock)Win.FindName("LockMark");
            Grip = (Thumb)Win.FindName("Grip");
            Paper = (Border)Win.FindName("Paper");
            Header = (Border)Win.FindName("Header");
            Wash = (Border)Win.FindName("Wash");
            PinBtn = (Button)Win.FindName("PinBtn");
            ColorBtn = (Button)Win.FindName("ColorBtn");
            AddPanel = (Border)Win.FindName("AddPanel");
            AddLine = (Button)Win.FindName("AddLine");
            PanelShift = (TranslateTransform)AddPanel.RenderTransform;

            AppSettings s = svc.Settings;
            if (s.NoteWidth >= 220 && s.NoteHeight >= 180) { Win.Width = s.NoteWidth; Win.Height = s.NoteHeight; }
            Rect wa = SystemParameters.WorkArea;
            bool onScreen = s.NoteWidth > 0 &&
                s.NoteLeft > SystemParameters.VirtualScreenLeft - 50 && s.NoteTop > SystemParameters.VirtualScreenTop - 20 &&
                s.NoteLeft < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
                s.NoteTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60;
            Win.Left = onScreen ? s.NoteLeft : wa.Right - Win.Width - 8;
            Win.Top = onScreen ? s.NoteTop : wa.Top + 8;
            Win.Topmost = s.NoteTopmost;
            ApplyLook();
            BuildAddPanel();
            Wire();
            Render();
        }

        void Wire()
        {
            Header.MouseLeftButtonDown += (o, e) =>
            {
                if (e.ClickCount == 2) { if (OpenMain != null) OpenMain(); return; }
                if (svc.Settings.NoteLocked) return;
                try { Win.DragMove(); } catch { }
                SaveLater();
            };
            LockMark.MouseLeftButtonDown += (o, e) => { SetLocked(false); e.Handled = true; };
            Grip.DragDelta += (o, e) =>
            {
                Win.Width = Math.Max(Win.MinWidth, Win.Width + e.HorizontalChange);
                Win.Height = Math.Max(Win.MinHeight, Win.Height + e.VerticalChange);
                SaveLater();
            };
            // Resize from any edge or corner, like a normal window (Windows does the dragging and shows the arrows).
            Win.SourceInitialized += delegate
            {
                var src = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(Win).Handle);
                if (src != null) src.AddHook(EdgeHook);
            };
            Win.SizeChanged += delegate { SaveLater(); };
            saveTimer.Tick += delegate
            {
                saveTimer.Stop();
                svc.Settings.NoteLeft = Win.Left; svc.Settings.NoteTop = Win.Top;
                svc.Settings.NoteWidth = Win.Width; svc.Settings.NoteHeight = Win.Height;
                Quiet(svc.Save);
            };
            hintTimer.Tick += delegate { hintTimer.Stop(); QuickHint.Visibility = Visibility.Collapsed; };

            ((Button)Win.FindName("AddBtn")).Click += delegate { OpenAddPanel(""); };
            AddLine.Click += delegate { OpenAddPanel(""); };
            ((Button)Win.FindName("OpenBtn")).Click += delegate { if (OpenMain != null) OpenMain(); };
            ((Button)Win.FindName("HideBtn")).Click += delegate { SetVisible(false); if (NoteHidden != null) NoteHidden(); };
            PinBtn.Click += delegate { SetTopmost(!svc.Settings.NoteTopmost); };
            ColorBtn.Click += delegate
            {
                ContextMenu m = new ContextMenu { PlacementTarget = ColorBtn, Placement = PlacementMode.Bottom };
                foreach (string[] opt in new[] { new[] { "2days", "Show today & tomorrow" }, new[] { "week", "Show this week (7 days)" } })
                {
                    string key = opt[0];
                    MenuItem ri = new MenuItem { Header = opt[1], IsChecked = RangeKey() == key };
                    ri.Click += delegate { svc.Settings.NoteRange = key; Quiet(svc.Save); Render(); };
                    m.Items.Add(ri);
                }
                m.Items.Add(new Separator());
                MenuItem lockItem = new MenuItem { Header = "Lock position & size", IsCheckable = true, IsChecked = svc.Settings.NoteLocked };
                lockItem.Click += delegate { SetLocked(!svc.Settings.NoteLocked); };
                m.Items.Add(lockItem);
                List<TaskItem> ticked = svc.Tasks.Where(t => t.Closed && !t.Archived && string.CompareOrdinal(t.Date, U.Today) >= 0
                    && string.CompareOrdinal(t.Date, RangeKey() == "week" ? U.Ymd(DateTime.Today.AddDays(6)) : U.Tomorrow) <= 0).ToList();
                MenuItem archItem = new MenuItem { Header = ticked.Count > 0 ? "Archive ticked tasks (" + ticked.Count + ")" : "Archive ticked tasks", IsEnabled = ticked.Count > 0 };
                archItem.Click += delegate
                {
                    int n = svc.Archive(ticked);
                    Hint(n + " task" + (n == 1 ? "" : "s") + " archived. See them in the full app: History > Archived.");
                };
                m.Items.Add(archItem);
                m.Items.Add(new Separator());
                MenuItem colours = new MenuItem { Header = "Colour" };
                m.Items.Add(colours);
                foreach (KeyValuePair<string, string[]> kv in Palettes)
                {
                    string key = kv.Key;
                    MenuItem mi = new MenuItem
                    {
                        Header = char.ToUpper(key[0]) + key.Substring(1),
                        Icon = new Ellipse { Width = 14, Height = 14, Fill = Hex(kv.Value[1]), Stroke = Hex(kv.Value[2]) },
                        IsChecked = svc.Settings.NoteColor == key
                    };
                    mi.Click += delegate { svc.Settings.NoteColor = key; ApplyLook(); Quiet(svc.Save); };
                    colours.Items.Add(mi);
                }
                bool hasPhoto = photo != null;
                MenuItem pick = new MenuItem { Header = hasPhoto ? "Change photo…" : "Use a photo from PC…" };
                pick.Click += delegate { ChoosePhoto(); };
                m.Items.Add(pick);
                if (hasPhoto)
                {
                    MenuItem strength = new MenuItem { Header = "Photo strength" };
                    foreach (string[] opt in new[] { new[] { "soft", "Soft (easiest to read)" }, new[] { "medium", "Medium" }, new[] { "strong", "Strong (more photo)" } })
                    {
                        string key = opt[0];
                        MenuItem si = new MenuItem { Header = opt[1], IsChecked = StrengthKey() == key };
                        si.Click += delegate { svc.Settings.NotePhotoStrength = key; ApplyLook(); Quiet(svc.Save); };
                        strength.Items.Add(si);
                    }
                    m.Items.Add(strength);
                    MenuItem remove = new MenuItem { Header = "Remove photo" };
                    remove.Click += delegate { RemovePhoto(); };
                    m.Items.Add(remove);
                }
                m.IsOpen = true;
            };

            QuickAdd.TextChanged += delegate { SyncAddOptions(); };
            AddPanel.PreviewKeyDown += (o, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Escape) return;
                if (addTime.IsDropDownOpen || addRemind.IsDropDownOpen || addRepeat.IsDropDownOpen) return;
                if (addDate.IsOpen) { if (e.Key == Key.Escape) { addDate.Close(); e.Handled = true; } return; }
                if (e.Key == Key.Enter && e.OriginalSource is ButtonBase) return;   // Enter on a focused button clicks it
                e.Handled = true;
                if (e.Key == Key.Escape) CloseAddPanel();
                else CommitQuickAdd();
            };
            Paper.SizeChanged += delegate { FitPanel(); };
            Header.SizeChanged += delegate { FitPanel(); };
            svc.Changed += Render;
        }

        /* Save settings without rebuilding every list on screen */
        void Quiet(Action save) { svc.Changed -= Render; save(); svc.Changed += Render; }

        void SaveLater() { saveTimer.Stop(); saveTimer.Start(); }

        /* Add panel: the quick time buttons (9am, 12pm, 3pm, 6pm) */
        public static readonly string[] TimePresets = { "09:00", "12:00", "15:00", "18:00" };

        /* Which time button a time belongs to, or -1 (not a preset / no time) */
        public static int PresetIndex(string hhmm) { return hhmm == null ? -1 : Array.IndexOf(TimePresets, hhmm); }

        /* Which day button a date belongs to: "today", "tomorrow" or "other" */
        public static string DayKind(string ymd) { return ymd == U.Today ? "today" : ymd == U.Tomorrow ? "tomorrow" : "other"; }

        /* Window edge zones: the 10px shadow margin plus a few px into the paper. */
        public const int EdgeBand = 15, CornerBand = 24;

        /* Which resize code a point (in window DIPs) gets: 0 = none, else a WM_NCHITTEST HT* value. */
        public static int EdgeHit(double x, double y, double w, double h)
        {
            bool l = x < EdgeBand, r = x >= w - EdgeBand, t = y < EdgeBand, b = y >= h - EdgeBand;
            bool lc = x < CornerBand, rc = x >= w - CornerBand, tc = y < CornerBand, bc = y >= h - CornerBand;
            if ((t && lc) || (l && tc)) return 13;   // HTTOPLEFT
            if ((t && rc) || (r && tc)) return 14;   // HTTOPRIGHT
            if ((b && lc) || (l && bc)) return 16;   // HTBOTTOMLEFT
            if ((b && rc) || (r && bc)) return 17;   // HTBOTTOMRIGHT
            if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            return 0;
        }

        IntPtr EdgeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != 0x0084 || svc.Settings.NoteLocked) return IntPtr.Zero;   // WM_NCHITTEST
            try
            {
                long lp = lParam.ToInt64();
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                Point p = Win.PointFromScreen(screen);
                int hit = EdgeHit(p.X, p.Y, Win.ActualWidth, Win.ActualHeight);
                if (hit != 0) { handled = true; return new IntPtr(hit); }
            }
            catch { }
            return IntPtr.Zero;
        }

        void BuildAddPanel()
        {
            StackPanel body = (StackPanel)Win.FindName("PanelBody");
            addDate = new DateField();
            addTime = Ui.TimeCombo(null);
            addRemind = Ui.RemindCombo(null);
            addRepeat = Ui.RepeatCombo(null);
            addTime.ToolTip = "Time (pick one or type, e.g. 2:45 PM)";
            addRemind.ToolTip = "When to remind you";
            addRepeat.ToolTip = "Repeat";
            addDate.Changed += delegate { if (!syncing) { dateTouched = true; BackToText(); } RefreshPills(); };
            addTime.SelectionChanged += delegate { if (!syncing) timeTouched = true; Win.Dispatcher.BeginInvoke(new Action(RefreshPills)); };
            addTime.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((o, e) => { if (!syncing) timeTouched = true; RefreshPills(); }));

            body.Children.Add(PanelLabel("DAY"));
            WrapPanel days = new WrapPanel();
            Button today = Pill("Today"), tomorrow = Pill("Tomorrow");
            today.Click += delegate { addDate.Value = U.Today; };
            tomorrow.Click += delegate { addDate.Value = U.Tomorrow; };
            dayOther = Pill("Other day…");
            dayOther.ToolTip = "Pick a date";
            dayOther.Click += delegate { addDate.OpenAt(dayOther); };
            dayPills = new[] { today, tomorrow };
            days.Children.Add(today); days.Children.Add(tomorrow); days.Children.Add(dayOther);
            body.Children.Add(days);

            body.Children.Add(PanelLabel("TIME"));
            WrapPanel times = new WrapPanel();
            string[] names = { "9am", "12pm", "3pm", "6pm" };
            timePills = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string hhmm = TimePresets[i];
                Button b = Pill(names[i]);
                b.ToolTip = U.FmtTime(hhmm);
                b.Click += delegate { showTimeBox = false; Ui.SetTime(addTime, hhmm); timeTouched = true; RefreshPills(); BackToText(); };
                timePills[i] = b;
                times.Children.Add(b);
            }
            timeCustom = Pill("");   // a time that isn't one of the buttons, e.g. 5:00 PM
            timeCustom.ToolTip = "Change the time";
            timeCustom.Click += delegate { ShowTimeBox(); };
            timeOther = Pill("Other…");
            timeOther.ToolTip = "Type any time, e.g. 2:45 PM";
            timeOther.Click += delegate { ShowTimeBox(); };
            times.Children.Add(timeCustom); times.Children.Add(timeOther);
            body.Children.Add(times);
            addTime.Margin = new Thickness(0, 1, 0, 0);
            body.Children.Add(addTime);
            addDate.Root.Visibility = Visibility.Collapsed;   // only its calendar is used; in the tree so it takes the note's colours
            body.Children.Add(addDate.Root);

            Grid g = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Place(g, addRemind, 0, 0); Place(g, addRepeat, 0, 2);
            body.Children.Add(g);

            panelError = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 8, 0, 0), Visibility = Visibility.Collapsed };
            body.Children.Add(panelError);

            StackPanel acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 11, 0, 0) };
            Button cancel = new Button { Style = Theme.S("Btn"), Content = "Cancel", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 6, 0) };
            Button go = new Button { Style = Theme.S("Ink"), Content = "Add task", Padding = new Thickness(14, 5, 14, 5) };
            cancel.Click += delegate { CloseAddPanel(); };
            go.Click += delegate { CommitQuickAdd(); };
            acts.Children.Add(cancel); acts.Children.Add(go);
            body.Children.Add(acts);
            ResetQuickAdd();
        }

        static void Place(Grid g, UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); }

        TextBlock PanelLabel(string text)
        {
            TextBlock t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(1, 9, 0, 4), Foreground = Muted };
            panelLabels.Add(t);
            return t;
        }

        static Button Pill(string text)
        {
            return new Button { Style = Theme.S("Btn"), Content = text, Padding = new Thickness(9, 3, 9, 3), FontSize = 12, Margin = new Thickness(0, 0, 5, 5) };
        }

        /* Selected = ink fill with paper-coloured text; otherwise an outline */
        void SetPill(Button b, bool on)
        {
            b.Background = on ? Ink : Brushes.Transparent;
            b.BorderBrush = on ? Ink : (Night ? Hex("#40FFFFFF") : Hex("#38000000"));
            b.Foreground = on ? PaperColor : Ink;
            b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }

        /* Show which day/time buttons match the current pick */
        void RefreshPills()
        {
            if (dayPills == null) return;
            string kind = DayKind(addDate.Value);
            SetPill(dayPills[0], kind == "today");
            SetPill(dayPills[1], kind == "tomorrow");
            dayOther.Content = kind == "other" ? U.FmtDate(addDate.Value) : "Other day…";
            SetPill(dayOther, kind == "other");

            string hhmm;
            bool ok = Ui.ReadTime(addTime, out hhmm);
            int idx = ok ? PresetIndex(hhmm) : -1;
            for (int i = 0; i < timePills.Length; i++) SetPill(timePills[i], idx == i);
            timeCustom.Content = ok ? U.FmtTime(hhmm) : "";
            timeCustom.Visibility = ok && idx < 0 && !showTimeBox ? Visibility.Visible : Visibility.Collapsed;
            SetPill(timeCustom, true);
            SetPill(timeOther, showTimeBox);
            addTime.Visibility = showTimeBox ? Visibility.Visible : Visibility.Collapsed;
            foreach (TextBlock t in panelLabels) t.Foreground = Muted;
            panelError.Foreground = Late;
        }

        /* After clicking a day/time button or picking a date, typing and Enter go to the task box again */
        void BackToText()
        {
            if (showTimeBox) return;   // the typed-time box keeps the keyboard
            QuickAdd.Focus();
            QuickAdd.CaretIndex = QuickAdd.Text.Length;
        }

        void ShowTimeBox()
        {
            showTimeBox = true;
            RefreshPills();
            addTime.Focus();
        }

        /* The panel may cover the list but never the note's top bar; a short note scrolls the panel */
        void FitPanel() { AddPanel.MaxHeight = Math.Max(120, Paper.ActualHeight - Header.ActualHeight - 2); }

        /* Opens the add panel (from the + line or the + in the top bar), optionally with some text typed in */
        public void OpenAddPanel(string text)
        {
            if (AddPanel.Visibility != Visibility.Visible)
            {
                ResetQuickAdd();
                AddPanel.Visibility = Visibility.Visible;
                AddLine.Visibility = Visibility.Hidden;   // keeps the footer height so the list doesn't jump
                QuickHint.Visibility = Visibility.Collapsed;
                FitPanel();
                PanelShift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            if (!string.IsNullOrEmpty(text)) QuickAdd.Text = text;
            Win.Dispatcher.BeginInvoke(new Action(() => { QuickAdd.Focus(); QuickAdd.CaretIndex = QuickAdd.Text.Length; }), DispatcherPriority.Input);
        }

        void CloseAddPanel()
        {
            addDate.Close();
            ResetQuickAdd();
            AddPanel.Visibility = Visibility.Collapsed;
            AddLine.Visibility = Visibility.Visible;
        }

        /* While typing, follow words like "tomorrow 3pm" unless the user already picked a day/time by hand */
        void SyncAddOptions()
        {
            if (syncing || addDate == null) return;
            string title, date, time;
            U.QuickParse(QuickAdd.Text, out title, out date, out time);
            syncing = true;
            if (!dateTouched) addDate.Value = date;
            if (!timeTouched) Ui.SetTime(addTime, time);
            syncing = false;
            RefreshPills();
        }

        void ResetQuickAdd()
        {
            syncing = true;
            QuickAdd.Text = "";
            Ui.SetRemind(addRemind, 15);
            Ui.SetRepeat(addRepeat, "none");
            syncing = false;
            dateTouched = timeTouched = showTimeBox = false;
            panelError.Visibility = Visibility.Collapsed;
            SyncAddOptions();   // default day and time: next hour today, or tomorrow 9am
        }

        void CommitQuickAdd()
        {
            if (QuickAdd.Text.Trim().Length == 0) { QuickAdd.Focus(); return; }
            string hhmm;
            if (!Ui.ReadTime(addTime, out hhmm))
            {
                panelError.Text = "Time not recognised. Try 2:30 PM or 14:30.";
                panelError.Visibility = Visibility.Visible;
                ShowTimeBox();
                return;
            }
            string title, d, tm;
            U.QuickParse(QuickAdd.Text, out title, out d, out tm);
            TaskItem t = svc.Add(title, addDate.Value, hhmm, Ui.GetRemind(addRemind), Ui.GetRepeat(addRepeat), "");
            CloseAddPanel();
            string msg = "Added for " + U.FmtDate(t.Date) + ", " + U.FmtTime(t.Time) + " · " +
                (t.RemindBefore < 0 ? "no reminder" : "reminder " + U.RemindLabel(t.RemindBefore).ToLowerInvariant());
            if (t.Repeat != null && t.Repeat != "none") msg += " · " + U.RepeatShort(t.Repeat).ToLowerInvariant();
            Hint(msg);
        }

        void Hint(string text) { Hint(text, false); }

        void Hint(string text, bool error)
        {
            QuickHint.Foreground = error ? Late : Good;
            QuickHint.Text = text;
            QuickHint.Visibility = Visibility.Visible;
            hintTimer.Stop(); hintTimer.Start();
        }

        /* Colour only, or photo with a see-through layer of the note colour on top so the text stays readable */
        public void ApplyLook()
        {
            AppSettings s = svc.Settings;
            string[] p;
            if (s.NoteColor == null || !Palettes.TryGetValue(s.NoteColor, out p)) p = Palettes["yellow"];
            Paper.BorderBrush = Hex(p[2]);

            // controls inside the note (add box, pickers, tick boxes) use a palette that suits the paper, whatever the app theme
            Theme.Fill(Win.Resources, Night);
            NoteTitle.Foreground = Ink;
            NoteSub.Foreground = LockMark.Foreground = Night ? Hex("#A9ADB8") : Hex("#5E5846");
            foreach (string b in new[] { "AddBtn", "PinBtn", "ColorBtn", "OpenBtn", "HideBtn" }) ((Button)Win.FindName(b)).Foreground = Night ? Hex("#C9CCD4") : Hex("#4A4436");
            QuickAdd.Background = Night ? Hex("#1FFFFFFF") : Hex("#A6FFFFFF");
            QuickAdd.BorderBrush = Night ? Hex("#2EFFFFFF") : Hex("#26000000");
            PaperColor = Hex(p[0]);
            AddLine.Background = QuickAdd.Background;
            AddLine.BorderBrush = Night ? Hex("#59FFFFFF") : Hex("#4D000000");
            AddLine.Foreground = Muted;
            AddPanel.Background = Hex(p[0]);
            AddPanel.BorderBrush = Hex(p[2]);
            RefreshPills();
            Grip.Foreground = Night ? Hex("#73FFFFFF") : Hex("#59000000");
            if (QuickHint.Visibility == Visibility.Visible) QuickHint.Foreground = Good;
            if (Rows != null && Rows.Children.Count > 0) Render();

            if (string.IsNullOrEmpty(s.NoteImage)) { photo = null; photoPath = null; }
            else if (photoPath != s.NoteImage) { photo = TryLoadPhoto(s.NoteImage); photoPath = s.NoteImage; }

            if (photo == null)
            {
                Paper.Background = Hex(p[0]);
                Wash.Background = Brushes.Transparent;
                Header.Background = Hex(p[1]);
                return;
            }
            ImageBrush ib = new ImageBrush(photo) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            ib.Freeze();
            Paper.Background = ib;
            string k = StrengthKey();
            double wash = k == "soft" ? 0.84 : k == "strong" ? 0.58 : 0.70;
            Wash.Background = Tint(p[0], wash);
            Header.Background = Tint(p[1], 0.6);
        }

        string StrengthKey()
        {
            string k = svc.Settings.NotePhotoStrength;
            return k == "soft" || k == "strong" ? k : "medium";
        }

        static Brush Tint(string hex, double alpha)
        {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            c.A = (byte)Math.Round(alpha * 255);
            SolidColorBrush b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        /* Loads a picture without locking the file, turned the right way up for phone photos. Null if it can't be read. */
        public static ImageSource TryLoadPhoto(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !IO.File.Exists(path)) return null;
                using (IO.FileStream fs = IO.File.OpenRead(path))
                {
                    int orientation = 1;
                    try
                    {
                        BitmapFrame f = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        BitmapMetadata md = f.Metadata as BitmapMetadata;
                        object o = md == null ? null : md.GetQuery("/app1/ifd/{ushort=274}");
                        if (o != null) orientation = Convert.ToInt32(o);
                    }
                    catch { }
                    fs.Position = 0;
                    BitmapImage b = new BitmapImage();
                    b.BeginInit();
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.DecodePixelWidth = 1000;
                    b.StreamSource = fs;
                    b.EndInit();
                    b.Freeze();
                    double angle = orientation == 3 ? 180 : orientation == 6 ? 90 : orientation == 8 ? 270 : 0;
                    if (angle == 0) return b;
                    TransformedBitmap tb = new TransformedBitmap(b, new RotateTransform(angle));
                    tb.Freeze();
                    return tb;
                }
            }
            catch { return null; }
        }

        void ChoosePhoto()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a photo for the sticky note",
                Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            if (dlg.ShowDialog(Win) != true) return;

            ImageSource img = TryLoadPhoto(dlg.FileName);
            if (img == null)
            {
                Dialog.Info(Win, "That file couldn't be opened as a picture", "Try a JPG or PNG photo.", Dialog.Kind.Warning);
                return;
            }
            string dest;
            try
            {
                // keep our own copy, so the note still works if the original is moved or deleted
                IO.Directory.CreateDirectory(Store.Folder);
                dest = IO.Path.Combine(Store.Folder, "note-photo-" + DateTime.Now.Ticks + IO.Path.GetExtension(dlg.FileName).ToLowerInvariant());
                IO.File.Copy(dlg.FileName, dest, true);
            }
            catch (Exception ex)
            {
                Dialog.Info(Win, "Couldn't save the photo", ex.Message, Dialog.Kind.Warning);
                return;
            }
            DeleteOldPhotos(dest);
            svc.Settings.NoteImage = dest;
            photo = img; photoPath = dest;
            ApplyLook();
            Quiet(svc.Save);
        }

        void RemovePhoto()
        {
            svc.Settings.NoteImage = null;
            ApplyLook();
            Quiet(svc.Save);
            DeleteOldPhotos(null);
        }

        static void DeleteOldPhotos(string keep)
        {
            try
            {
                foreach (string f in IO.Directory.GetFiles(Store.Folder, "note-photo-*"))
                    if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase)) { try { IO.File.Delete(f); } catch { } }
            }
            catch { }
        }

        public void SetVisible(bool visible)
        {
            svc.Settings.NoteVisible = visible;
            Quiet(svc.Save);
            if (visible) { if (!Win.IsVisible) Win.Show(); Win.Activate(); }
            else Win.Hide();
        }

        public void SetLocked(bool on)
        {
            svc.Settings.NoteLocked = on;
            Quiet(svc.Save);
            RenderLock();
            Hint(on ? "Note locked. Unlock from the ⋯ menu or the lock icon." : "Note unlocked. Drag the top bar to move it.");
            if (LockChanged != null) LockChanged();
        }

        void RenderLock()
        {
            bool on = svc.Settings.NoteLocked;
            LockMark.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Grip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            Header.Cursor = on ? Cursors.Arrow : Cursors.SizeAll;
            Header.ToolTip = on ? "Locked · double-click to open the full app" : "Drag to move · double-click to open the full app";
        }

        string RangeKey() { return svc.Settings.NoteRange == "week" ? "week" : "2days"; }

        public void SetTopmost(bool on)
        {
            svc.Settings.NoteTopmost = on;
            Win.Topmost = on;
            Quiet(svc.Save);
            RenderPin();
        }

        void RenderPin()
        {
            PinBtn.Tag = svc.Settings.NoteTopmost ? "on" : null;
            PinBtn.Content = svc.Settings.NoteTopmost ? "" : "";
            PinBtn.ToolTip = svc.Settings.NoteTopmost ? "On top of other windows · click to unpin" : "Keep on top of other windows";
        }

        /* ---------------------------------------------------------------- list */
        public void Render()
        {
            RenderPin();
            RenderLock();
            string td = U.Today, tm = U.Tomorrow;
            bool week = RangeKey() == "week";
            string weekEnd = U.Ymd(DateTime.Today.AddDays(6));
            DateTime now = DateTime.Now;
            List<TaskItem> overdue = svc.Tasks.Where(t => t.IsLate(now)).OrderBy(t => t.Due).ToList();
            List<TaskItem> today = svc.Tasks.Where(t => t.Date == td && !t.Archived && !t.IsLate(now)).OrderBy(t => t.Closed).ThenBy(t => t.Due).ToList();
            List<TaskItem> tomorrow = svc.Tasks.Where(t => t.Date == tm && !t.Archived).OrderBy(t => t.Closed).ThenBy(t => t.Due).ToList();

            int left = overdue.Count + today.Count(t => !t.Closed);
            NoteTitle.Text = "Today · " + DateTime.Today.ToString("ddd, d MMM", U.En);
            NoteSub.Text = left == 0 ? "Nothing left to do" : left + " to do" + (overdue.Count > 0 ? " · " + overdue.Count + " overdue" : "");
            if (week)
            {
                int wk = svc.Tasks.Count(t => !t.Closed && string.CompareOrdinal(t.Date, td) >= 0 && string.CompareOrdinal(t.Date, weekEnd) <= 0);
                NoteSub.Text += " · " + wk + " this week";
            }

            Rows.Children.Clear();
            if (overdue.Count > 0) { Section("Overdue", Late); foreach (TaskItem t in overdue) Rows.Children.Add(Row(t, true)); }
            if (overdue.Count > 0 || today.Count > 0) Section("Today", Muted);
            if (today.Count == 0)
            {
                Rows.Children.Add(new TextBlock
                {
                    Text = "Nothing for today. Type below to add a task.", Foreground = Muted, FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, overdue.Count > 0 ? 2 : 10, 8, 6)
                });
            }
            foreach (TaskItem t in today) Rows.Children.Add(Row(t, false));
            if (tomorrow.Count > 0)
            {
                Section("Tomorrow", Muted);
                foreach (TaskItem t in tomorrow) Rows.Children.Add(Row(t, false));
            }
            if (!week) return;
            for (int i = 2; i <= 6; i++)
            {
                DateTime day = DateTime.Today.AddDays(i);
                string ds = U.Ymd(day);
                List<TaskItem> list = svc.Tasks.Where(t => t.Date == ds && !t.Archived).OrderBy(t => t.Closed).ThenBy(t => t.Due).ToList();
                if (list.Count == 0) continue;
                Section(day.ToString("dddd, d MMM", U.En), Muted);
                foreach (TaskItem t in list) Rows.Children.Add(Row(t, false));
            }
        }

        void Section(string text, Brush color)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = color,
                Margin = new Thickness(8, Rows.Children.Count == 0 ? 6 : 12, 0, 3)
            });
        }

        UIElement Row(TaskItem t, bool showDate)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox cb = Ui.StatusBox(svc, t, next => Hint("Next one scheduled: " + U.FmtDate(next)));
            cb.Margin = new Thickness(0, 0, 20, 0);
            g.Children.Add(cb);

            TextBlock title = new TextBlock
            {
                Text = t.Title, TextWrapping = TextWrapping.Wrap, FontWeight = t.Closed ? FontWeights.Normal : FontWeights.Medium,
                Foreground = t.Closed ? Muted : Ink, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0)
            };
            if (t.Closed) title.TextDecorations = TextDecorations.Strikethrough;
            if (!string.IsNullOrEmpty(t.Notes)) title.ToolTip = t.Notes;
            StackPanel body = new StackPanel();
            body.Children.Add(title);
            if (t.HasSteps && !t.Closed)
            {
                // checklist steps, tickable right from the note
                StackPanel steps = new StackPanel { Margin = new Thickness(0, 1, 4, 2) };
                foreach (StepItem s in t.Steps) steps.Children.Add(Ui.StepBox(svc, t, s, 12, Ink, Muted));
                body.Children.Add(steps);
            }
            else if (t.HasSteps)
                body.Children.Add(new TextBlock { Text = t.StepsDone + "/" + t.Steps.Count + " steps", FontSize = 11, Foreground = Muted });
            Grid.SetColumn(body, 1);
            g.Children.Add(body);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            if (t.Repeats) right.Children.Add(new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 10.5, Foreground = Muted, Margin = new Thickness(0, 2, 5, 0), ToolTip = "Repeats: " + U.RepeatShort(t.Repeat).ToLowerInvariant() });
            if (t.Skipped) right.Children.Add(new TextBlock { Text = "Not done", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Late });
            else
            {
                bool late = !t.Closed && t.Due < DateTime.Now;
                string when = U.FmtTime(t.Time);
                if (showDate && t.Date != U.Today) when =U.FmtDate(t.Date).Replace("Yesterday", "Yest.") + " " + when;
                right.Children.Add(new TextBlock { Text = when, FontSize = 11.5, Foreground = late ? Late : Muted, FontWeight = late ? FontWeights.SemiBold : FontWeights.Normal });
            }
            Grid.SetColumn(right, 2);
            g.Children.Add(right);

            Border row = new Border { Child = g, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent };
            row.MouseEnter += delegate { row.Background = Night ? Hex("#14FFFFFF") : Hex("#12000000"); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
            row.ContextMenu = Ui.StatusMenu(svc, t, Win);
            title.MouseLeftButtonDown += (o, e) => { if (e.ClickCount == 2) { EditWindow.Open(Win, svc, t); e.Handled = true; } };
            return row;
        }

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStyle='None' AllowsTransparency='True' Background='#01000000' ShowInTaskbar='False' ResizeMode='CanResize'
        Width='330' Height='430' MinWidth='240' MinHeight='200' Title='My Task Manager - Sticky note'
        FontFamily='Segoe UI' FontSize='13' UseLayoutRounding='True' TextOptions.TextFormattingMode='Display'>
  <Border x:Name='Paper' Margin='10' CornerRadius='10' BorderThickness='1'>
    <Border.Effect><DropShadowEffect BlurRadius='16' ShadowDepth='3' Direction='270' Opacity='0.22'/></Border.Effect>
    <Grid>
      <Border x:Name='Wash' CornerRadius='9'/>
      <DockPanel>
        <Border x:Name='Header' DockPanel.Dock='Top' CornerRadius='9,9,0,0' Padding='12,4,6,7' Cursor='SizeAll' ToolTip='Drag to move · double-click to open the full app'>
          <StackPanel>
          <DockPanel>
            <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center'>
              <Button x:Name='AddBtn' Style='{StaticResource NoteBtn}' Content='&#xE710;' ToolTip='Add a task'/>
              <Button x:Name='PinBtn' Style='{StaticResource NoteBtn}' Content='&#xE718;'/>
              <Button x:Name='ColorBtn' Style='{StaticResource NoteBtn}' Content='&#xE712;' ToolTip='Note options: this week view, lock, colour, photo'/>
              <Button x:Name='OpenBtn' Style='{StaticResource NoteBtn}' Content='&#xE8A7;' ToolTip='Open My Task Manager'/>
              <Button x:Name='HideBtn' Style='{StaticResource NoteBtn}' Content='&#xE711;' ToolTip='Hide note (bring it back from the tray icon)'/>
            </StackPanel>
            <TextBlock x:Name='NoteTitle' FontWeight='Bold' FontSize='14' Foreground='#2B2718' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/>
          </DockPanel>
          <StackPanel Orientation='Horizontal' Margin='0,-3,0,0'>
            <TextBlock x:Name='LockMark' Text='&#xE72E;' FontFamily='{StaticResource IconFont}' FontSize='10.5' Foreground='#6B6450' Margin='0,2,5,0' Cursor='Hand' ToolTip='Locked · click to unlock' Visibility='Collapsed'/>
            <TextBlock x:Name='NoteSub' FontSize='11.5' Foreground='#6B6450' TextTrimming='CharacterEllipsis'/>
          </StackPanel>
          </StackPanel>
        </Border>
        <Border DockPanel.Dock='Bottom' Padding='10,6,10,12'>
          <StackPanel x:Name='AddArea'>
            <Button x:Name='AddLine' Height='34' Cursor='Hand' FocusVisualStyle='{StaticResource FocusRing}' ToolTip='Add a task'>
              <Button.Template>
                <ControlTemplate TargetType='Button'>
                  <Grid Background='Transparent'>
                    <Rectangle x:Name='Dash' RadiusX='8' RadiusY='8' StrokeThickness='1' StrokeDashArray='4 3' Stroke='{TemplateBinding BorderBrush}' Fill='{TemplateBinding Background}'/>
                    <ContentPresenter Margin='11,0' VerticalAlignment='Center'/>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Dash' Property='StrokeDashArray' Value='1 0'/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Button.Template>
              <StackPanel Orientation='Horizontal'>
                <Path Data='M5,0 L5,10 M0,5 L10,5' Width='10' Height='10' Stroke='{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}' StrokeThickness='1.6' SnapsToDevicePixels='True' VerticalAlignment='Center' Margin='0,0,9,0'/>
                <TextBlock Text='Add a task' FontSize='12.5' VerticalAlignment='Center'/>
              </StackPanel>
            </Button>
            <TextBlock x:Name='QuickHint' FontSize='11.5' Foreground='#1F7A4D' Margin='4,5,0,0' TextWrapping='Wrap' Visibility='Collapsed'/>
          </StackPanel>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility='Auto' Focusable='False'>
          <StackPanel x:Name='Rows' Margin='6,0,8,4'/>
        </ScrollViewer>
      </DockPanel>
      <Border x:Name='AddPanel' VerticalAlignment='Bottom' Visibility='Collapsed' CornerRadius='0,0,9,9' BorderThickness='0,1,0,0'>
        <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
        <ScrollViewer VerticalScrollBarVisibility='Auto' Focusable='False'>
          <StackPanel x:Name='PanelBody' Margin='12,10,12,12'>
            <TextBox x:Name='QuickAdd' Style='{StaticResource Input}' Tag='What do you need to do?' MinHeight='34' FontSize='12.5'/>
          </StackPanel>
        </ScrollViewer>
      </Border>
      <Thumb x:Name='Grip' Width='16' Height='16' HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='0,0,1,1' Cursor='SizeNWSE' ToolTip='Drag to resize'>
        <Thumb.Template>
          <ControlTemplate TargetType='Thumb'>
            <Grid Background='Transparent'><Path Data='M13,4 L4,13 M13,8.5 L8.5,13' Stroke='{Binding Foreground, RelativeSource={RelativeSource TemplatedParent}}' StrokeThickness='1.2'/></Grid>
          </ControlTemplate>
        </Thumb.Template>
      </Thumb>
    </Grid>
  </Border>
</Window>";
    }
}
