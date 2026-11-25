using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MyTaskManager
{
    public class MainView
    {
        public readonly Window Win;
        readonly TaskService svc;
        public string View = "today";
        string query = "";

        public Action TestReminder;
        public Action<bool> StartupChanged;
        public Action Hidden;

        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        readonly Dictionary<string, TextBlock> navCounts = new Dictionary<string, TextBlock>();
        readonly DateField addDate = new DateField();
        readonly DispatcherTimer feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        TextBlock HeaderTitle, HeaderSub, ProgressText, StatusText, AddFeedback, AddError, Greeting;
        Border ProgressFill, AddCard;
        FrameworkElement ProgressBox, SearchRow, SettingsPanel;
        TextBox AddTitle, AddNotes, SearchBox;
        ComboBox AddTime, AddRemind, AddRepeat;
        Button ChipToday, ChipTomorrow, ToggleNotes, AddBtn, ClearDoneBtn, FilterAll, FilterDone, FilterNot, FilterArch;
        string historyFilter = "all";
        public Action<bool> NoteVisibleChanged, NoteTopmostChanged, NoteLockChanged;
        public Action NoteRangeChanged, UninstallRequested, SwitchToNote;
        public Action<string> ThemeChanged;
        public Action CheckUpdates, InstallUpdate;
        CheckBox NoteCheck, NoteTopCheck, NoteLockCheck, NoteWeekCheck;
        ComboBox NagCombo;
        StackPanel ListHost, NavPanel;
        CheckBox StartupCheck, SoundCheck;
        Ellipse StatusDot;
        ScrollViewer MainScroll;

        T F<T>(string name) where T : class { return Win.FindName(name) as T; }

        public MainView(TaskService svc)
        {
            this.svc = svc;
            Win = (Window)XamlReader.Parse(Xaml);
            HeaderTitle = F<TextBlock>("HeaderTitle"); HeaderSub = F<TextBlock>("HeaderSub");
            ProgressText = F<TextBlock>("ProgressText"); ProgressFill = F<Border>("ProgressFill"); ProgressBox = F<FrameworkElement>("ProgressBox");
            StatusText = F<TextBlock>("StatusText"); StatusDot = F<Ellipse>("StatusDot"); Greeting = F<TextBlock>("BrandDate");
            AddCard = F<Border>("AddCard"); AddTitle = F<TextBox>("AddTitle"); AddNotes = F<TextBox>("AddNotes");
            AddTime = Ui.TimeCombo(F<ComboBox>("AddTime")); AddRemind = Ui.RemindCombo(F<ComboBox>("AddRemind")); AddRepeat = Ui.RepeatCombo(F<ComboBox>("AddRepeat"));
            ChipToday = F<Button>("ChipToday"); ChipTomorrow = F<Button>("ChipTomorrow");
            ToggleNotes = F<Button>("ToggleNotes"); AddBtn = F<Button>("AddBtn"); AddFeedback = F<TextBlock>("AddFeedback"); AddError = F<TextBlock>("AddError");
            SearchRow = F<FrameworkElement>("SearchRow"); SearchBox = F<TextBox>("SearchBox"); ClearDoneBtn = F<Button>("ClearDoneBtn");
            SettingsPanel = F<FrameworkElement>("SettingsPanel"); ListHost = F<StackPanel>("ListHost"); NavPanel = F<StackPanel>("NavPanel");
            NoteCheck = F<CheckBox>("NoteCheck"); NoteTopCheck = F<CheckBox>("NoteTopCheck");
            NoteLockCheck = F<CheckBox>("NoteLockCheck"); NoteWeekCheck = F<CheckBox>("NoteWeekCheck"); NagCombo = F<ComboBox>("NagCombo");
            FilterAll = F<Button>("FilterAll"); FilterDone = F<Button>("FilterDone"); FilterNot = F<Button>("FilterNot"); FilterArch = F<Button>("FilterArch");
            StartupCheck = F<CheckBox>("StartupCheck"); SoundCheck = F<CheckBox>("SoundCheck"); MainScroll = F<ScrollViewer>("MainScroll");
            F<Border>("AddDateHost").Child = addDate.Root;

            F<Image>("Logo").Source = AppIcon.Frame(64);
            BuildNav();
            Wire();
            SetView("today");
        }

        /* ---------------------------------------------------------------- nav */
        void BuildNav()
        {
            string[][] items = {
                new[] { "today", "", "Today" },
                new[] { "tomorrow", "", "Tomorrow" },
                new[] { "upcoming", "", "Upcoming" },
                new[] { "completed", "", "History" },
                new[] { "whatsnew", "", "What's new" },
                new[] { "settings", "", "Settings & backup" },
            };
            foreach (string[] it in items)
            {
                if (it[0] == "whatsnew") NavPanel.Children.Add(Theme.Ref(new Border { Height = 1, Margin = new Thickness(6, 10, 6, 10) }, Border.BackgroundProperty, "Line"));
                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                TextBlock icon = new TextBlock { Text = it[1], FontFamily = Theme.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
                TextBlock label = new TextBlock { Text = it[2], VerticalAlignment = VerticalAlignment.Center };
                TextBlock count = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                Border badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), MinWidth = 24, Child = count, Visibility = Visibility.Collapsed };
                count.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, 1); Grid.SetColumn(badge, 2);
                g.Children.Add(icon); g.Children.Add(label); g.Children.Add(badge);
                Button b = new Button { Style = Theme.S("Nav"), Content = g };
                string key = it[0];
                b.Click += delegate { SetView(key); };
                NavPanel.Children.Add(b);
                navButtons[key] = b;
                navCounts[key] = count;
            }
        }

        public void SetView(string v)
        {
            View = v;
            query = "";
            SearchBox.Text = "";
            historyFilter = "all";
            string def = v == "tomorrow" ? U.Tomorrow : v == "upcoming" ? U.Ymd(DateTime.Today.AddDays(2)) : U.Today;
            addDate.Value = def;
            DateTime next = DateTime.Now.AddHours(1);
            Ui.SetTime(AddTime, def == U.Today && next.Date == DateTime.Today ? U.HHmm(new TimeSpan(next.Hour, 0, 0)) : "09:00");
            AddError.Visibility = Visibility.Collapsed;
            if (v == "whatsnew" && svc.Settings.SeenVersion != Updater.CurrentText)
            {
                svc.Settings.SeenVersion = Updater.CurrentText;
                svc.Save();
            }
            Render();
            MainScroll.ScrollToTop();
        }

        /* ---------------------------------------------------------------- events */
        void Wire()
        {
            Win.Closing += (s, e) => { e.Cancel = true; Win.Hide(); if (Hidden != null) Hidden(); };
            ChipToday.Click += delegate { addDate.Value = U.Today; };
            ChipTomorrow.Click += delegate { addDate.Value = U.Tomorrow; };
            addDate.Changed += UpdateChips;
            ToggleNotes.Click += delegate
            {
                bool show = AddNotes.Visibility != Visibility.Visible;
                AddNotes.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ToggleNotes.Content = show ? "− Hide notes" : "+ Add notes";
                if (show) AddNotes.Focus();
            };
            AddBtn.Click += delegate { SubmitAdd(); };
            AddTitle.KeyDown += (s, e) => { if (e.Key == Key.Enter) { SubmitAdd(); e.Handled = true; } };
            AddTitle.TextChanged += delegate { AddError.Visibility = Visibility.Collapsed; };
            feedbackTimer.Tick += delegate { AddFeedback.Text = ""; feedbackTimer.Stop(); };

            SearchBox.TextChanged += delegate { query = SearchBox.Text; RenderList(); };
            ClearDoneBtn.Click += delegate
            {
                if (Dialog.Confirm(Win, "Clear the whole history?", "All done, not-done and archived tasks and their remarks will be deleted for good.\n\nTip: export a backup first (Settings & backup).", "Clear history", "Cancel", Dialog.Kind.Danger))
                    svc.ClearHistory();
            };

            StartupCheck.IsChecked = svc.Settings.StartWithWindows;
            SoundCheck.IsChecked = svc.Settings.Sound;
            StartupCheck.Click += delegate { svc.Settings.StartWithWindows = StartupCheck.IsChecked == true; svc.Save(); if (StartupChanged != null) StartupChanged(svc.Settings.StartWithWindows); };
            FilterAll.Click += delegate { historyFilter = "all"; UpdateFilterChips(); RenderList(); };
            FilterDone.Click += delegate { historyFilter = "done"; UpdateFilterChips(); RenderList(); };
            FilterNot.Click += delegate { historyFilter = "notdone"; UpdateFilterChips(); RenderList(); };
            FilterArch.Click += delegate { historyFilter = "archived"; UpdateFilterChips(); RenderList(); };
            ((Button)Win.FindName("ToNoteBtn")).Click += delegate { if (SwitchToNote != null) SwitchToNote(); };
            NoteCheck.Click += delegate { if (NoteVisibleChanged != null) NoteVisibleChanged(NoteCheck.IsChecked == true); };
            NoteTopCheck.Click += delegate { if (NoteTopmostChanged != null) NoteTopmostChanged(NoteTopCheck.IsChecked == true); };
            SoundCheck.Click += delegate { svc.Settings.Sound = SoundCheck.IsChecked == true; svc.Save(); };
            NoteLockCheck.Click += delegate { if (NoteLockChanged != null) NoteLockChanged(NoteLockCheck.IsChecked == true); };
            NoteWeekCheck.Click += delegate
            {
                svc.Settings.NoteRange = NoteWeekCheck.IsChecked == true ? "week" : "2days";
                svc.Save();
                if (NoteRangeChanged != null) NoteRangeChanged();
            };
            foreach (int v in new[] { 5, 10, 15, 30, 0 })
                NagCombo.Items.Add(new ComboBoxItem { Content = v == 0 ? "Don't repeat" : "Every " + v + " minutes", Tag = v });
            foreach (ComboBoxItem ci in NagCombo.Items) if ((int)ci.Tag == svc.Settings.NagMinutes) NagCombo.SelectedItem = ci;
            if (NagCombo.SelectedItem == null) NagCombo.SelectedIndex = 1;
            NagCombo.SelectionChanged += delegate
            {
                ComboBoxItem ci = NagCombo.SelectedItem as ComboBoxItem;
                if (ci == null) return;
                svc.Settings.NagMinutes = (int)ci.Tag;
                svc.Save();
            };
            F<Button>("ThemeLight").Click += delegate { PickTheme("light"); };
            F<TextBlock>("VersionText").Text = "You have version " + Updater.CurrentText + ". Your tasks and settings are kept when you update.";
            F<Button>("UpdateBtn").Click += delegate { if (CheckUpdates != null) CheckUpdates(); };
            F<Button>("InstallBtn").Click += delegate { if (InstallUpdate != null) InstallUpdate(); };
            F<Button>("ThemeDark").Click += delegate { PickTheme("dark"); };
            F<Button>("ThemeSystem").Click += delegate { PickTheme("system"); };
            F<Button>("ThemeBtn").Click += delegate { PickTheme(Theme.IsDark ? "light" : "dark"); };
            UpdateThemeChips();
            F<Button>("UninstallBtn").Click += delegate { if (UninstallRequested != null) UninstallRequested(); };
            F<Button>("TestBtn").Click += delegate { if (TestReminder != null) TestReminder(); };
            F<Button>("ExportBtn").Click += delegate { ExportBackup(); };
            F<Button>("ImportBtn").Click += delegate { ImportBackup(); };
            F<Button>("FolderBtn").Click += delegate { try { Process.Start("explorer.exe", "\"" + Store.Folder + "\""); } catch { } };
            F<TextBlock>("DataPath").Text = Store.Folder;

            Win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { FocusAdd(); e.Handled = true; }
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SetView("completed"); SearchBox.Focus(); e.Handled = true; }
            };
            svc.Changed += Render;
        }

        public void FocusAdd()
        {
            if (View == "completed" || View == "settings" || View == "whatsnew") SetView("today");
            AddTitle.Focus();
        }

        void PickTheme(string mode)
        {
            if (ThemeChanged != null) ThemeChanged(mode);
            UpdateThemeChips();
        }

        public void UpdateThemeChips()
        {
            string m = svc.Settings.Theme;
            F<Button>("ThemeLight").Tag = m == "light" ? "on" : null;
            F<Button>("ThemeDark").Tag = m == "dark" ? "on" : null;
            F<Button>("ThemeSystem").Tag = m == "system" ? "on" : null;
            Button b = F<Button>("ThemeBtn");
            b.Content = Theme.IsDark ? "\uE706" : "\uE708";   // sun / moon
            b.ToolTip = Theme.IsDark ? "Switch to light mode" : "Switch to dark mode";
        }

        void UpdateFilterChips()
        {
            FilterAll.Tag = historyFilter == "all" ? "on" : null;
            FilterDone.Tag = historyFilter == "done" ? "on" : null;
            FilterNot.Tag = historyFilter == "notdone" ? "on" : null;
            FilterArch.Tag = historyFilter == "archived" ? "on" : null;
        }

        void UpdateChips()
        {
            ChipToday.Tag = addDate.Value == U.Today ? "on" : null;
            ChipTomorrow.Tag = addDate.Value == U.Tomorrow ? "on" : null;
        }

        void SubmitAdd()
        {
            string title = AddTitle.Text.Trim();
            string time;
            if (title.Length == 0) { ShowAddError("Type what you need to do first."); AddTitle.Focus(); return; }
            if (!Ui.ReadTime(AddTime, out time)) { ShowAddError("Time not recognised. Try 2:30 PM or 14:30."); AddTime.Focus(); return; }
            TaskItem t = svc.Add(title, addDate.Value, time, Ui.GetRemind(AddRemind), Ui.GetRepeat(AddRepeat), AddNotes.Text);
            AddTitle.Text = ""; AddNotes.Text = ""; Ui.SetRepeat(AddRepeat, "none");
            AddFeedback.Text = "✓ Added for " + U.FmtDate(t.Date) + ", " + U.FmtTime(t.Time);
            feedbackTimer.Stop(); feedbackTimer.Start();
            AddTitle.Focus();
        }

        void ShowAddError(string msg) { AddError.Text = msg; AddError.Visibility = Visibility.Visible; }

        void ExportBackup()
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "MyTaskManager-backup-" + U.Today + ".json", Filter = "Backup file (*.json)|*.json", DefaultExt = ".json"
            };
            if (d.ShowDialog(Win) != true) return;
            try { Store.Export(d.FileName, svc.Tasks); Dialog.Info(Win, "Backup saved", d.FileName, Dialog.Kind.Ok); }
            catch (Exception ex) { Dialog.Info(Win, "Could not save the backup", ex.Message, Dialog.Kind.Warning); }
        }

        void ImportBackup()
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = "Backup file (*.json)|*.json|All files|*.*" };
            if (d.ShowDialog(Win) != true) return;
            try
            {
                List<TaskItem> list = Store.Import(d.FileName);
                if (list.Count == 0) { Dialog.Info(Win, "No tasks found in that file", null, Dialog.Kind.Info); return; }
                int added = svc.Merge(list);
                Dialog.Info(Win, added + " task(s) imported", list.Count - added > 0 ? (list.Count - added) + " were already here and skipped." : null, Dialog.Kind.Ok);
            }
            catch (Exception ex) { Dialog.Info(Win, "Could not import that file", ex.Message, Dialog.Kind.Warning); }
        }

        /* ---------------------------------------------------------------- render */
        public void Render()
        {
            string td = U.Today, tm = U.Tomorrow;
            List<TaskItem> pending = svc.Tasks.Where(t => !t.Closed).ToList();
            int overdue = pending.Count(t => string.CompareOrdinal(t.Date, td) < 0);
            SetCount("today", pending.Count(t => string.CompareOrdinal(t.Date, td) <= 0), overdue > 0);
            SetCount("tomorrow", pending.Count(t => t.Date == tm), false);
            SetCount("upcoming", pending.Count(t => string.CompareOrdinal(t.Date, tm) > 0), false);
            SetCount("completed", 0, false);
            SetCount("settings", 0, false);
            SetNewBadge(svc.Settings.SeenVersion != Updater.CurrentText);
            foreach (KeyValuePair<string, Button> kv in navButtons) kv.Value.Tag = kv.Key == View ? "on" : null;
            Greeting.Text = DateTime.Today.ToString("dddd, d MMMM", U.En);
            UpdateChips();

            bool isList = View != "settings";
            AddCard.Visibility = View == "today" || View == "tomorrow" || View == "upcoming" ? Visibility.Visible : Visibility.Collapsed;
            SearchRow.Visibility = View == "completed" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = View == "settings" ? Visibility.Visible : Visibility.Collapsed;
            ListHost.Visibility = isList ? Visibility.Visible : Visibility.Collapsed;
            ProgressBox.Visibility = Visibility.Collapsed;

            int h = DateTime.Now.Hour;
            switch (View)
            {
                case "today":
                    HeaderTitle.Text = h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
                    HeaderSub.Text = DateTime.Today.ToString("dddd, d MMMM yyyy", U.En);
                    List<TaskItem> all = svc.Tasks.Where(t => t.Date == td).ToList();
                    if (all.Count > 0)
                    {
                        int done = all.Count(t => t.Closed);
                        ProgressBox.Visibility = Visibility.Visible;
                        ProgressText.Text = done + " of " + all.Count + " ticked off";
                        ProgressFill.Width = 220.0 * done / all.Count;
                    }
                    break;
                case "tomorrow":
                    HeaderTitle.Text = "Tomorrow";
                    HeaderSub.Text = DateTime.Today.AddDays(1).ToString("dddd, d MMMM yyyy", U.En);
                    break;
                case "upcoming":
                    HeaderTitle.Text = "Upcoming";
                    HeaderSub.Text = "Everything scheduled after tomorrow";
                    break;
                case "completed":
                    int total = svc.Tasks.Count(t => t.Closed);
                    int nd = svc.Tasks.Count(t => t.Skipped);
                    HeaderTitle.Text = "History";
                    int ar = svc.Tasks.Count(t => t.Closed && t.Archived);
                    HeaderSub.Text = (total - nd) + " done · " + nd + " not done" + (ar > 0 ? " · " + ar + " archived" : "") + " · search them and add remarks";
                    UpdateFilterChips();
                    ClearDoneBtn.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "settings":
                    HeaderTitle.Text = "Settings & backup";
                    HeaderSub.Text = "Reminders, start-up and your data";
                    break;
                case "whatsnew":
                    HeaderTitle.Text = "What's new";
                    HeaderSub.Text = "You have version " + Updater.CurrentText + " · what changed in each update";
                    break;
            }
            NoteCheck.IsChecked = svc.Settings.NoteVisible;
            NoteTopCheck.IsChecked = svc.Settings.NoteTopmost;
            NoteLockCheck.IsChecked = svc.Settings.NoteLocked;
            NoteWeekCheck.IsChecked = svc.Settings.NoteRange == "week";
            RenderStatus();
            if (isList) RenderList();
        }

        void SetCount(string key, int n, bool alert)
        {
            TextBlock c = navCounts[key];
            Border badge = (Border)c.Parent;
            badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            c.Text = n.ToString();
            bool active = key == View;
            badge.Background = alert ? Theme.B("DangerSoft") : active ? Theme.B("Surface") : Theme.B("Surface2");
            c.Foreground = alert ? Theme.B("Danger") : active ? Theme.B("Accent") : Theme.B("Text2");
        }

        /* "New" pill on the What's new item until that page has been opened in this version */
        void SetNewBadge(bool on)
        {
            TextBlock c = navCounts["whatsnew"];
            Border badge = (Border)c.Parent;
            badge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            c.Text = "New";
            badge.Background = Theme.B("Accent");
            c.Foreground = Brushes.White;
        }

        void RenderWhatsNew()
        {
            string current = Updater.CurrentText;
            foreach (Changelog.Entry e in Changelog.All)
            {
                DateTime d;
                string when = DateTime.TryParseExact(e.Date, "yyyy-MM-dd", U.En, System.Globalization.DateTimeStyles.None, out d) ? d.ToString("d MMMM yyyy", U.En) : e.Date;
                Section("Version " + e.Version + " · " + when + (e.Version == current ? " · your version" : ""), false);
                StackPanel sp = new StackPanel { Margin = new Thickness(18, 14, 18, 12) };
                foreach (string item in e.Items)
                {
                    Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(Theme.Ref(new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) }, TextBlock.ForegroundProperty, "Accent"));
                    TextBlock t = Theme.Ref(new TextBlock { Text = item, FontSize = 13.5, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "Text");
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    sp.Children.Add(row);
                }
                ListHost.Children.Add(new Border { Style = Theme.S("Card"), Child = sp });
            }
        }

        void RenderStatus()
        {
            bool err = svc.LastError != null;
            StatusDot.Fill = err ? Theme.B("Danger") : Theme.B("Ok");
            StatusText.Text = err ? "Could not save: " + svc.LastError
                : svc.Settings.StartWithWindows ? "Running in the tray. Starts with Windows." : "Running in the tray. Not starting with Windows.";
        }

        public void RenderList()
        {
            ListHost.Children.Clear();
            string td = U.Today, tm = U.Tomorrow;
            Comparison<TaskItem> byDue = (a, b) => a.Due.CompareTo(b.Due);

            if (View == "whatsnew") RenderWhatsNew();
            else if (View == "today")
            {
                List<TaskItem> overdue = svc.Tasks.Where(t => !t.Closed && string.CompareOrdinal(t.Date, td) < 0).ToList();
                overdue.Sort(byDue);
                List<TaskItem> today = svc.Tasks.Where(t => t.Date == td && !t.Archived).OrderBy(t => t.Closed).ThenBy(t => t.Due).ToList();
                if (overdue.Count > 0) { Section("Overdue · " + overdue.Count, true); ListCard(overdue, true, false); }
                Section("Today · " + today.Count(t => !t.Closed) + " left", false, ArchiveAction(today));
                if (today.Count > 0) ListCard(today, false, false);
                else Empty("Nothing planned for today", "Add a task above to get started.");
            }
            else if (View == "tomorrow")
            {
                List<TaskItem> list = svc.Tasks.Where(t => t.Date == tm && !t.Archived).OrderBy(t => t.Closed).ThenBy(t => t.Due).ToList();
                Section("Planned · " + list.Count, false, ArchiveAction(list));
                if (list.Count > 0) ListCard(list, false, false);
                else Empty("Tomorrow is clear", "Plan ahead by adding tasks above.");
            }
            else if (View == "upcoming")
            {
                List<TaskItem> list = svc.Tasks.Where(t => !t.Closed && string.CompareOrdinal(t.Date, tm) > 0).OrderBy(t => t.Due).ToList();
                if (list.Count == 0) { Section("Scheduled", false); Empty("No upcoming tasks", "Tasks for later dates will appear here."); }
                foreach (IGrouping<string, TaskItem> g in list.GroupBy(t => t.Date))
                {
                    Section(U.FmtDate(g.Key), false);
                    ListCard(g.ToList(), false, false);
                }
            }
            else if (View == "completed")
            {
                string q = (query ?? "").Trim().ToLowerInvariant();
                bool arch = historyFilter == "archived";
                List<TaskItem> list = svc.Tasks.Where(t => t.Closed && t.Archived == arch
                    && (historyFilter == "done" ? t.Done : historyFilter == "notdone" ? t.Skipped : true)).OrderByDescending(t => t.CompletedTime).ToList();
                int total = list.Count;
                if (q.Length > 0)
                    list = list.Where(t => string.Join(" ", new[] { t.Title, t.Notes, t.Remarks, t.HasSteps ? string.Join(" ", t.Steps.Select(s => s.Text)) : "", t.Date, U.FmtLong(t.Date), U.FmtStamp(t.CompletedTime), t.Done ? "done" : "not done" }).ToLowerInvariant().Contains(q)).ToList();
                if (list.Count == 0)
                {
                    Section(q.Length > 0 ? "No results" : arch ? "Archive" : "History", false);
                    Empty(q.Length > 0 ? "No matches" : arch ? "No archived tasks" : "Nothing here yet",
                        q.Length > 0 ? "Try a different word or a date like 2026-11."
                        : arch ? "Archive finished tasks to tuck them away here." : "Tick a task once for Done, twice for Not done.");
                }
                else
                {
                    Section(q.Length > 0 ? list.Count + " result" + (list.Count == 1 ? "" : "s") : arch ? "Archived · most recent first" : "Most recent first", false,
                        arch ? null : ArchiveAction(list, "Archive all"));
                    ListCard(list, false, true);
                }
            }
        }

        void Section(string text, bool danger) { Section(text, danger, null); }

        void Section(string text, bool danger, Button action)
        {
            TextBlock label = new TextBlock
            {
                Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = danger ? Theme.B("Danger") : Theme.B("Text3")
            };
            DockPanel row = new DockPanel { Margin = new Thickness(4, action != null ? 14 : 22, 4, 8), LastChildFill = true };
            if (action != null) { DockPanel.SetDock(action, Dock.Right); row.Children.Add(action); }
            row.Children.Add(label);
            ListHost.Children.Add(row);
        }

        /* "Archive ticked (n)" link for a list, or null when nothing in it is finished */
        Button ArchiveAction(List<TaskItem> items) { return ArchiveAction(items, null); }

        Button ArchiveAction(List<TaskItem> items, string label)
        {
            List<TaskItem> closed = items.Where(t => t.Closed && !t.Archived).ToList();
            if (closed.Count == 0) return null;
            Button b = new Button
            {
                Style = Theme.S("Btn"), Height = 30, Padding = new Thickness(10, 0, 12, 0), FontSize = 12.5,
                ToolTip = "Move finished tasks out of the list. Find them in History > Archived.",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) },
                        new TextBlock { Text = (label ?? "Archive ticked") + " (" + closed.Count + ")", VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            b.Click += delegate
            {
                int n = svc.Archive(closed);
                Flash(n + " task" + (n == 1 ? "" : "s") + " archived. Find them in History > Archived.");
            };
            return b;
        }

        void Empty(string title, string text)
        {
            StackPanel sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 34, 20, 34) };
            sp.Children.Add(new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 34, Foreground = Theme.B("Text3"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.B("Text"), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = text, Foreground = Theme.B("Text2"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            ListHost.Children.Add(new Border { Style = Theme.S("Card"), Child = sp });
        }

        void ListCard(List<TaskItem> items, bool showDate, bool remarks)
        {
            StackPanel sp = new StackPanel();
            for (int i = 0; i < items.Count; i++) sp.Children.Add(Row(items[i], showDate, remarks, i == 0));
            ListHost.Children.Add(new Border { Style = Theme.S("Card"), Child = sp });
        }

        UIElement Row(TaskItem t, bool showDate, bool remarks, bool first)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            CheckBox cb = Ui.StatusBox(svc, t, next => Flash("Next one scheduled: " + U.FmtDate(next) + ", " + U.FmtTime(t.Time)));
            cb.Margin = new Thickness(0, 1, 14, 0);
            g.Children.Add(cb);

            StackPanel body = new StackPanel();
            TextBlock title = new TextBlock { Text = t.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = t.Closed ? Theme.B("Text3") : Theme.B("Text") };
            if (t.Closed) title.TextDecorations = TextDecorations.Strikethrough;
            body.Children.Add(title);
            if (!string.IsNullOrEmpty(t.Notes))
                body.Children.Add(new TextBlock { Text = t.Notes, FontSize = 12.5, Foreground = Theme.B("Text2"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (t.HasSteps && !t.Closed)
            {
                StackPanel steps = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
                foreach (StepItem s in t.Steps) steps.Children.Add(Ui.StepBox(svc, t, s, 13, Theme.B("Text"), Theme.B("Text3")));
                body.Children.Add(steps);
            }

            WrapPanel meta = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            if (!t.Closed)
            {
                TimeSpan diff = t.Due - DateTime.Now;
                string kind = diff.TotalMinutes < 0 ? "late" : diff.TotalMinutes < 60 ? "soon" : "";
                string when = U.FmtTime(t.Time);
                if (showDate) when = U.FmtDate(t.Date) + ", " + when;
                if (string.CompareOrdinal(t.Date, U.Tomorrow) <= 0) when += " · " + U.Rel(t.Due);
                meta.Children.Add(Ui.Pill("", when, kind));
                if (t.HasReminder)
                {
                    string r = U.RemindLabel(t.RemindBefore);
                    if (!string.IsNullOrEmpty(t.SnoozeUntil)) r = "Reminding again at " + U.ParseStamp(t.SnoozeUntil).ToString("h:mm tt", U.En);
                    meta.Children.Add(Ui.Pill("", r, ""));
                }
            }
            else
            {
                meta.Children.Add(Ui.Pill("", "Due " + U.FmtDate(t.Date) + ", " + U.FmtTime(t.Time), ""));
                if (t.Done) meta.Children.Add(Ui.Pill("", "Done " + U.FmtStamp(t.CompletedTime), "ok"));
                else meta.Children.Add(Ui.Pill("", "Not done · marked " + U.FmtStamp(t.CompletedTime), "late"));
            }
            if (t.HasSteps) meta.Children.Add(Ui.Pill("", t.StepsDone + "/" + t.Steps.Count + " steps", t.StepsDone == t.Steps.Count ? "ok" : ""));
            if (t.Repeats) meta.Children.Add(Ui.Pill("", U.RepeatShort(t.Repeat), "repeat"));
            body.Children.Add(meta);

            if (remarks) body.Children.Add(RemarksBox(t));
            Grid.SetColumn(body, 1);
            g.Children.Add(body);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -5, 0, 0) };
            if (t.Closed)
            {
                Button restore = Ui.IconButton("", "Move back to pending");
                restore.Click += delegate { svc.SetStatus(t, "pending"); };
                actions.Children.Add(restore);
                Button arc = Ui.IconButton(t.Archived ? "" : "", t.Archived ? "Unarchive (back to History)" : "Archive");
                arc.Click += delegate { if (t.Archived) svc.Unarchive(t); else svc.Archive(new[] { t }); };
                actions.Children.Add(arc);
            }
            Button edit = Ui.IconButton("", "Edit");
            edit.Click += delegate { EditWindow.Open(Win, svc, t); };
            Button del = Ui.IconButton("", "Delete");
            del.Click += delegate
            {
                string msg = "\u201C" + t.Title + "\u201D will be removed. This can't be undone.";
                if (t.Repeats && !t.Closed) msg += "\n\nThis also stops the " + U.RepeatShort(t.Repeat).ToLowerInvariant() + " repeat.";
                if (Dialog.Confirm(Win, "Delete this task?", msg, "Delete", "Cancel", Dialog.Kind.Danger)) svc.Delete(t);
            };
            actions.Children.Add(edit);
            actions.Children.Add(del);
            Grid.SetColumn(actions, 2);
            g.Children.Add(actions);

            g.ContextMenu = Ui.StatusMenu(svc, t, Win);
            return new Border
            {
                Child = g, Padding = new Thickness(18, 14, 12, 12), Background = Brushes.Transparent,
                BorderBrush = Theme.B("Line"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0)
            };
        }

        UIElement RemarksBox(TaskItem t)
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            TextBlock saved = new TextBlock { Text = "Saved", FontSize = 12, Foreground = Theme.B("Ok"), FontWeight = FontWeights.Medium, Visibility = Visibility.Hidden };
            DockPanel.SetDock(saved, Dock.Right);
            head.Children.Add(saved);
            head.Children.Add(new TextBlock { Text = "Remarks", Style = Theme.S("Label"), Margin = new Thickness(0) });
            sp.Children.Add(head);
            TextBox box = new TextBox { Style = Theme.S("Multi"), Text = t.Remarks ?? "", Tag = "Add a remark: outcome, reference number, who you spoke to…", MinHeight = 44 };
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            DispatcherTimer hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            hide.Tick += delegate { saved.Visibility = Visibility.Hidden; hide.Stop(); };
            Action commit = delegate
            {
                timer.Stop();
                if ((t.Remarks ?? "") == box.Text) return;
                t.Remarks = box.Text;
                // save without re-rendering (keeps the cursor in the box)
                svc.Changed -= Render;
                svc.Save();
                svc.Changed += Render;
                saved.Visibility = Visibility.Visible; hide.Stop(); hide.Start();
            };
            timer.Tick += delegate { commit(); };
            box.TextChanged += delegate { timer.Stop(); timer.Start(); };
            box.LostKeyboardFocus += delegate { commit(); };
            sp.Children.Add(box);
            return sp;
        }

        /* Status line under Settings > Updates */
        public void SetUpdateStatus(string text, bool error, string installLabel)
        {
            Button ib = F<Button>("InstallBtn");
            ib.Visibility = installLabel == null ? Visibility.Collapsed : Visibility.Visible;
            if (installLabel != null) ib.Content = installLabel;
            TextBlock st = F<TextBlock>("UpdateStatus");
            st.Text = text ?? "";
            st.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            st.SetResourceReference(TextBlock.ForegroundProperty, error ? "Danger" : "Text2");
        }

        void Flash(string text)
        {
            AddFeedback.Text = "↻ " + text;
            feedbackTimer.Stop(); feedbackTimer.Start();
            if (View == "completed") Dialog.Info(Win, text, null, Dialog.Kind.Info);
        }

        public bool IsBusy
        {
            get
            {
                IInputElement f = Keyboard.FocusedElement;
                return Win.IsActive && (f is TextBox || f is ComboBox || f is ComboBoxItem) || View == "completed" && Win.IsVisible;
            }
        }

        public void ShowAndActivate()
        {
            if (!Win.IsVisible) Win.Show();
            if (Win.WindowState == WindowState.Minimized) Win.WindowState = WindowState.Normal;
            Win.Activate();
            Win.Topmost = true; Win.Topmost = false;
            Win.Focus();
        }

        /* ---------------------------------------------------------------- layout */
        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='My Task Manager' Width='1120' Height='760' MinWidth='980' MinHeight='560'
        Background='{DynamicResource Bg}' FontFamily='Segoe UI' FontSize='13.5' Foreground='{DynamicResource Text}'
        UseLayoutRounding='True' SnapsToDevicePixels='True' TextOptions.TextFormattingMode='Display' WindowStartupLocation='CenterScreen'>
  <Grid>
    <Grid.ColumnDefinitions><ColumnDefinition Width='236'/><ColumnDefinition/></Grid.ColumnDefinitions>

    <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='0,0,1,0'>
      <DockPanel Margin='14,20,14,16'>
        <StackPanel DockPanel.Dock='Top' Orientation='Horizontal' Margin='8,0,0,22'>
          <Image x:Name='Logo' Width='36' Height='36' RenderOptions.BitmapScalingMode='HighQuality'/>
          <StackPanel Margin='10,0,0,0' VerticalAlignment='Center'>
            <TextBlock Text='My Task Manager' FontWeight='Bold' FontSize='14.5'/>
            <TextBlock x:Name='BrandDate' FontSize='12' Foreground='{DynamicResource Text3}'/>
          </StackPanel>
        </StackPanel>
        <Border DockPanel.Dock='Bottom' Background='{DynamicResource Surface2}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='10' Padding='12,10'>
          <StackPanel>
            <StackPanel Orientation='Horizontal' Margin='0,0,0,3'>
              <Ellipse x:Name='StatusDot' Width='8' Height='8' Fill='{DynamicResource Ok}' VerticalAlignment='Center'/>
              <TextBlock Text='Reminders are on' FontWeight='SemiBold' FontSize='12.5' Margin='8,0,0,0'/>
            </StackPanel>
            <TextBlock x:Name='StatusText' FontSize='12' Foreground='{DynamicResource Text2}' TextWrapping='Wrap'/>
          </StackPanel>
        </Border>
        <StackPanel x:Name='NavPanel'/>
      </DockPanel>
    </Border>

    <ScrollViewer x:Name='MainScroll' Grid.Column='1' VerticalScrollBarVisibility='Auto' Focusable='False'>
      <StackPanel Margin='40,30,40,48' MaxWidth='940'>
        <Grid Margin='0,0,0,20'>
          <StackPanel>
            <TextBlock x:Name='HeaderTitle' FontSize='26' FontWeight='Bold'/>
            <TextBlock x:Name='HeaderSub' Foreground='{DynamicResource Text2}' Margin='0,2,0,0'/>
          </StackPanel>
          <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Bottom'>
          <StackPanel x:Name='ProgressBox' Width='220' VerticalAlignment='Bottom' Margin='0,0,0,6'>
            <DockPanel Margin='0,0,0,6'>
              <TextBlock x:Name='ProgressText' DockPanel.Dock='Right' FontSize='12' Foreground='{DynamicResource Text2}'/>
              <TextBlock Text='Today&#x27;s progress' FontSize='12' Foreground='{DynamicResource Text2}'/>
            </DockPanel>
            <Grid>
              <Border Height='6' CornerRadius='3' Background='{DynamicResource Line}'/>
              <Border x:Name='ProgressFill' Height='6' CornerRadius='3' Background='{DynamicResource Ok}' HorizontalAlignment='Left' Width='0'/>
            </Grid>
          </StackPanel>
          <Button x:Name='ThemeBtn' Style='{StaticResource Btn}' Margin='18,0,0,0' VerticalAlignment='Bottom' Padding='10,7' FontFamily='{StaticResource IconFont}' FontSize='15' FontWeight='Normal'/>
          <Button x:Name='ToNoteBtn' Style='{StaticResource Btn}' Margin='8,0,0,0' VerticalAlignment='Bottom' ToolTip='Hide this window (it stays by the clock) and show the sticky note on the desktop'>
            <StackPanel Orientation='Horizontal'>
              <TextBlock Text='&#xE7C4;' FontFamily='{StaticResource IconFont}' FontSize='14' VerticalAlignment='Center' Margin='0,0,8,0'/>
              <TextBlock Text='Hide &amp; show sticky note' VerticalAlignment='Center'/>
            </StackPanel>
          </Button>
          </StackPanel>
        </Grid>

        <Border x:Name='AddCard' Style='{StaticResource Card}' Padding='16'>
          <StackPanel>
            <TextBox x:Name='AddTitle' Style='{StaticResource Input}' Tag='What do you need to do?' FontSize='15' MinHeight='44' Padding='11,0' MaxLength='200'/>
            <Grid Margin='0,12,0,0'>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='Auto'/><ColumnDefinition Width='10'/>
                <ColumnDefinition Width='1.05*'/><ColumnDefinition Width='10'/>
                <ColumnDefinition Width='0.85*'/><ColumnDefinition Width='10'/>
                <ColumnDefinition Width='1.2*'/><ColumnDefinition Width='10'/>
                <ColumnDefinition Width='0.95*'/>
              </Grid.ColumnDefinitions>
              <StackPanel>
                <TextBlock Text='Day' Style='{StaticResource Label}'/>
                <Border Background='{DynamicResource Surface2}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8' Height='38' Padding='3'>
                  <StackPanel Orientation='Horizontal'>
                    <Button x:Name='ChipToday' Style='{StaticResource Chip}' Content='Today'/>
                    <Button x:Name='ChipTomorrow' Style='{StaticResource Chip}' Content='Tomorrow' Margin='2,0,0,0'/>
                  </StackPanel>
                </Border>
              </StackPanel>
              <StackPanel Grid.Column='2'><TextBlock Text='Date' Style='{StaticResource Label}'/><Border x:Name='AddDateHost'/></StackPanel>
              <StackPanel Grid.Column='4'><TextBlock Text='Time' Style='{StaticResource Label}'/><ComboBox x:Name='AddTime'/></StackPanel>
              <StackPanel Grid.Column='6'><TextBlock Text='Remind me' Style='{StaticResource Label}'/><ComboBox x:Name='AddRemind'/></StackPanel>
              <StackPanel Grid.Column='8'><TextBlock Text='Repeat' Style='{StaticResource Label}'/><ComboBox x:Name='AddRepeat'/></StackPanel>
            </Grid>
            <TextBox x:Name='AddNotes' Style='{StaticResource Multi}' Tag='Notes (optional)' Margin='0,10,0,0' Visibility='Collapsed'/>
            <DockPanel Margin='0,12,0,0'>
              <Button x:Name='AddBtn' DockPanel.Dock='Right' Style='{StaticResource Primary}' Padding='18,8'>
                <StackPanel Orientation='Horizontal'>
                  <TextBlock Text='&#xE710;' FontFamily='{StaticResource IconFont}' FontSize='12' VerticalAlignment='Center' Margin='0,1,8,0'/>
                  <TextBlock Text='Add task'/>
                </StackPanel>
              </Button>
              <Button x:Name='ToggleNotes' Style='{StaticResource Link}' Content='+ Add notes' Margin='-6,0,0,0'/>
              <TextBlock x:Name='AddError' Foreground='{DynamicResource Danger}' VerticalAlignment='Center' Margin='12,0,0,0' Visibility='Collapsed'/>
              <TextBlock x:Name='AddFeedback' Foreground='{DynamicResource Ok}' FontWeight='Medium' VerticalAlignment='Center' Margin='12,0,0,0'/>
            </DockPanel>
          </StackPanel>
        </Border>

        <DockPanel x:Name='SearchRow' Margin='0,0,0,0'>
          <Button x:Name='ClearDoneBtn' DockPanel.Dock='Right' Style='{StaticResource Btn}' Content='Clear history' Margin='10,0,0,0' Height='40'/>
          <Border DockPanel.Dock='Right' Background='{DynamicResource Surface2}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8' Height='40' Padding='3' Margin='10,0,0,0'>
            <StackPanel Orientation='Horizontal'>
              <Button x:Name='FilterAll' Style='{StaticResource Chip}' Content='All'/>
              <Button x:Name='FilterDone' Style='{StaticResource Chip}' Content='Done' Margin='2,0,0,0'/>
              <Button x:Name='FilterNot' Style='{StaticResource Chip}' Content='Not done' Margin='2,0,0,0'/>
              <Button x:Name='FilterArch' Style='{StaticResource Chip}' Content='Archived' Margin='2,0,0,0'/>
            </StackPanel>
          </Border>
          <Grid>
            <TextBox x:Name='SearchBox' Style='{StaticResource Input}' Tag='Search title, notes, remarks or date (e.g. 2026-11)' Padding='34,0,10,0' MinHeight='40' Background='{DynamicResource Surface}'/>
            <TextBlock Text='&#xE721;' FontFamily='{StaticResource IconFont}' FontSize='14' Foreground='{DynamicResource Text3}' VerticalAlignment='Center' Margin='13,0,0,0' IsHitTestVisible='False'/>
          </Grid>
        </DockPanel>

        <StackPanel x:Name='ListHost'/>

        <StackPanel x:Name='SettingsPanel'>
          <Border Style='{StaticResource Card}' Padding='20' Margin='0,0,0,14'>
            <DockPanel>
              <Border DockPanel.Dock='Right' VerticalAlignment='Center' Background='{DynamicResource Surface2}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='8' Height='40' Padding='3'>
                <StackPanel Orientation='Horizontal'>
                  <Button x:Name='ThemeLight' Style='{StaticResource Chip}' Content='Light'/>
                  <Button x:Name='ThemeDark' Style='{StaticResource Chip}' Content='Dark' Margin='2,0,0,0'/>
                  <Button x:Name='ThemeSystem' Style='{StaticResource Chip}' Content='Match Windows' Margin='2,0,0,0'/>
                </StackPanel>
              </Border>
              <StackPanel VerticalAlignment='Center' Margin='0,0,16,0'>
                <TextBlock Text='Appearance' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
                <TextBlock TextWrapping='Wrap' Foreground='{DynamicResource Text2}' Text='Dark mode is easier on the eyes at night. The sticky note switches to its Night colour with it.'/>
              </StackPanel>
            </DockPanel>
          </Border>
          <Border Style='{StaticResource Card}' Padding='20' Margin='0,0,0,14'>
            <DockPanel>
              <StackPanel DockPanel.Dock='Right' Orientation='Horizontal' VerticalAlignment='Center'>
                <Button x:Name='InstallBtn' Style='{StaticResource Primary}' Content='Update now' Height='40' Margin='0,0,8,0' Visibility='Collapsed'/>
                <Button x:Name='UpdateBtn' Style='{StaticResource Btn}' Content='Check for updates' Height='40'/>
              </StackPanel>
              <StackPanel VerticalAlignment='Center' Margin='0,0,16,0'>
                <TextBlock Text='Updates' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
                <TextBlock x:Name='VersionText' TextWrapping='Wrap' Foreground='{DynamicResource Text2}'/>
                <TextBlock x:Name='UpdateStatus' TextWrapping='Wrap' Foreground='{DynamicResource Text2}' FontWeight='Medium' Margin='0,6,0,0' Visibility='Collapsed'/>
              </StackPanel>
            </DockPanel>
          </Border>
          <Border Style='{StaticResource Card}' Padding='20' Margin='0,0,0,14'>
            <StackPanel>
              <TextBlock Text='Reminders' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
              <TextBlock TextWrapping='Wrap' Foreground='{DynamicResource Text2}' Margin='0,0,0,14'>
                My Task Manager keeps running in the system tray (near the clock) when you close this window, and pops up a reminder card
                in the bottom-right corner when a task is due. If the PC was off at reminder time, you get the reminder as soon as it is back on.
              </TextBlock>
              <CheckBox x:Name='StartupCheck' Style='{StaticResource Check}' Content='Start My Task Manager automatically when Windows starts (recommended)' Margin='0,0,0,10'/>
              <CheckBox x:Name='SoundCheck' Style='{StaticResource Check}' Content='Play a sound with reminders' Margin='0,0,0,14'/>
              <StackPanel Orientation='Horizontal' Margin='0,0,0,16'>
                <TextBlock Text='If I ignore a reminder, show it again:' VerticalAlignment='Center' Margin='0,0,10,0'/>
                <ComboBox x:Name='NagCombo' Width='190'/>
              </StackPanel>
              <Button x:Name='TestBtn' Style='{StaticResource Btn}' HorizontalAlignment='Left' Content='Show a test reminder'/>
            </StackPanel>
          </Border>
          <Border Style='{StaticResource Card}' Padding='20' Margin='0,0,0,14'>
            <StackPanel>
              <TextBlock Text='Sticky note' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
              <TextBlock TextWrapping='Wrap' Foreground='{DynamicResource Text2}' Margin='0,0,0,14'>
                A small note on the desktop with today's and tomorrow's tasks. Click a box once for Done, twice for Not done.
                Type at the bottom of the note to add a task, e.g. Call Ali tomorrow 3pm. Drag the top bar to move it, the corner to resize it.
                The ⋯ button on the note has more: show the whole week, lock it in place, change its colour or put a photo from your PC behind it.
              </TextBlock>
              <CheckBox x:Name='NoteCheck' Style='{StaticResource Check}' Content='Show the sticky note on the desktop' Margin='0,0,0,10'/>
              <CheckBox x:Name='NoteTopCheck' Style='{StaticResource Check}' Content='Keep the sticky note on top of other windows' Margin='0,0,0,10'/>
              <CheckBox x:Name='NoteLockCheck' Style='{StaticResource Check}' Content='Lock the sticky note so it can&#x27;t be moved or resized by accident' Margin='0,0,0,10'/>
              <CheckBox x:Name='NoteWeekCheck' Style='{StaticResource Check}' Content='Show the whole week on the note (not just today and tomorrow)'/>
            </StackPanel>
          </Border>
          <Border Style='{StaticResource Card}' Padding='20'>
            <StackPanel>
              <TextBlock Text='Your data' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
              <TextBlock TextWrapping='Wrap' Foreground='{DynamicResource Text2}' Margin='0,0,0,4'>
                Tasks are saved on this PC automatically, and a daily backup is kept for 14 days. Export a backup to move your tasks to another PC.
                Import also accepts backups from the earlier HTML version.
              </TextBlock>
              <TextBlock x:Name='DataPath' FontFamily='Consolas' FontSize='12' Foreground='{DynamicResource Text3}' Margin='0,0,0,14' TextWrapping='Wrap'/>
              <WrapPanel>
                <Button x:Name='ExportBtn' Style='{StaticResource Btn}' Content='Export backup' Margin='0,0,8,0'/>
                <Button x:Name='ImportBtn' Style='{StaticResource Btn}' Content='Import backup' Margin='0,0,8,0'/>
                <Button x:Name='FolderBtn' Style='{StaticResource Ghost}' Content='Open data folder'/>
              </WrapPanel>
            </StackPanel>
          </Border>
          <Border Style='{StaticResource Card}' Padding='20' Margin='0,14,0,0'>
            <StackPanel>
              <TextBlock Text='Remove from this PC' FontSize='16' FontWeight='SemiBold' Margin='0,0,0,4'/>
              <TextBlock TextWrapping='Wrap' Foreground='{DynamicResource Text2}' Margin='0,0,0,14'>
                Stops My Task Manager starting with Windows, closes it, and (if you choose) deletes your tasks and backups.
                After that you can delete MyTaskManager.exe. Nothing else is left behind.
              </TextBlock>
              <Button x:Name='UninstallBtn' Style='{StaticResource Btn}' HorizontalAlignment='Left' Content='Remove My Task Manager from this PC…' Foreground='{DynamicResource Danger}'/>
            </StackPanel>
          </Border>
          <TextBlock Foreground='{DynamicResource Text3}' FontSize='12' Margin='4,14,0,0' TextWrapping='Wrap'>
            Right-click any task for Done / Not done / Edit / Add checklist. Shortcuts: Ctrl+N new task · Ctrl+F search history. To quit completely, right-click the tray icon and choose Exit.
          </TextBlock>
        </StackPanel>
      </StackPanel>
    </ScrollViewer>
  </Grid>
</Window>";
    }
}
