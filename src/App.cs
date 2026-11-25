using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: AssemblyTitle("My Task Manager")]
[assembly: AssemblyProduct("My Task Manager")]
[assembly: AssemblyDescription("Tasks for today and tomorrow, with reminders")]

namespace MyTaskManager
{
    public static class Program
    {
        const string MutexName = "Local\\MyTaskManager.SingleInstance.v1";
        const string ShowEventName = "Local\\MyTaskManager.ShowWindow.v1";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                App shotApp = new App();
                shotApp.Resources = Theme.Load();
                return Shots.Run(args[1]);
            }
            if (args.Length >= 1 && args[0] == "--notetest")
            {
                App testApp = new App();
                testApp.Resources = Theme.Load();
                return Shots.NoteTest(args);
            }

            bool updated = args.Contains("--after-update");
            if (updated) Updater.AfterUpdate(args);

            bool created;
            using (Mutex m = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: ask the running copy to show its window
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return 0;
                }
                EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                App app = new App();
                app.Resources = Theme.Load();
                app.Boot(args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)), showEvent, updated);
                app.Run();
                GC.KeepAlive(showEvent);
            }
            return 0;
        }
    }

    public class App : Application
    {
        TaskService svc;
        MainView main;
        ReminderCenter rem;
        StickyNote note;
        bool noteTipShown;
        Forms.NotifyIcon tray;
        DispatcherTimer timer;
        string lastDay;
        int lastMinute = -1;

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, e) => { Log.Write(e.Exception); e.Handled = true; };
        }

        public void Boot(bool startHidden, EventWaitHandle showEvent, bool justUpdated)
        {
            svc = new TaskService(true);
            Theme.Apply(Theme.WantsDark(svc.Settings.Theme));
            main = new MainView(svc);
            Theme.TitleBar(main.Win);
            ImageSource icon = AppIcon.Image();
            if (icon != null) main.Win.Icon = icon;
            rem = new ReminderCenter(svc);
            rem.OpenMain = ShowMain;
            main.TestReminder = () => rem.ShowTest();
            main.StartupChanged = Autostart.Apply;
            main.Hidden = OnHidden;

            note = new StickyNote(svc);
            note.OpenMain = ShowMain;
            note.NoteHidden = OnNoteHidden;
            if (icon != null) note.Win.Icon = icon;
            main.NoteVisibleChanged = v => note.SetVisible(v);
            main.NoteTopmostChanged = v => note.SetTopmost(v);
            main.NoteLockChanged = v => note.SetLocked(v);
            main.NoteRangeChanged = () => note.Render();
            note.LockChanged = () => main.Render();
            main.SwitchToNote = () => { note.SetVisible(true); main.Win.Close(); };
            main.UninstallRequested = RemoveFromPc;
            main.ThemeChanged = SetTheme;
            main.CheckUpdates = () => CheckForUpdates(true);
            SystemEvents.UserPreferenceChanged += (s, e) => Dispatcher.BeginInvoke(new Action(delegate
            {
                if (svc.Settings.Theme == "system" && Theme.WantsDark("system") != Theme.IsDark) SetTheme("system");
            }));
            if (svc.Settings.NoteVisible) note.Win.Show();
            Autostart.Apply(svc.Settings.StartWithWindows);   // also fixes the path if the .exe was moved

            BuildTray();
            svc.Changed += UpdateTrayText;
            UpdateTrayText();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(new Action(Tick)); };
            SystemEvents.DisplaySettingsChanged += (s, e) => Dispatcher.BeginInvoke(new Action(rem.Layout));

            Thread listener = new Thread(() =>
            {
                while (true) { showEvent.WaitOne(); Dispatcher.BeginInvoke(new Action(ShowMain)); }
            });
            listener.IsBackground = true;
            listener.Start();

            lastDay = U.Today;
            if (!startHidden) main.ShowAndActivate();
            Dispatcher.BeginInvoke(new Action(Tick), DispatcherPriority.ApplicationIdle);

            // updates: first check shortly after start, then once a day
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(justUpdated ? 3600 : 45) };
            updateTimer.Tick += delegate { updateTimer.Interval = TimeSpan.FromHours(24); CheckForUpdates(false); };
            updateTimer.Start();
            if (justUpdated)
                Dispatcher.BeginInvoke(new Action(() => Dialog.Info(main.Win.IsVisible ? main.Win : null, "Updated to version " + Updater.CurrentText,
                    "My Task Manager is up to date. Your tasks and settings are just as you left them.", Dialog.Kind.Ok)), DispatcherPriority.ApplicationIdle);
        }

        /* ---------------------------------------------------------------- updates */
        DispatcherTimer updateTimer;
        bool checkingUpdate, installingUpdate;
        string offeredTag;

        void CheckForUpdates(bool manual)
        {
            if (checkingUpdate || installingUpdate) return;
            checkingUpdate = true;
            if (manual) main.SetUpdateStatus("Checking for updates…", false);
            Thread t = new Thread(() =>
            {
                Updater.Release r = null;
                string err = null;
                try { r = Updater.CheckLatest(); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    checkingUpdate = false;
                    if (err != null)
                    {
                        if (manual) main.SetUpdateStatus("Couldn't check for updates. Check the internet connection and try again.", true);
                        return;
                    }
                    if (!Updater.IsNewer(r)) { main.SetUpdateStatus("You have the latest version.", false); return; }
                    main.SetUpdateStatus("Version " + r.Version.ToString(3) + " is available.", false);
                    if (!manual && offeredTag == r.Tag) return;   // asked once already today
                    offeredTag = r.Tag;
                    OfferUpdate(r);
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void OfferUpdate(Updater.Release r)
        {
            string notes = (r.Notes ?? "").Trim();
            if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
            Window owner = main.Win.IsVisible ? main.Win : null;
            if (!Dialog.Confirm(owner, "Update available",
                "Version " + r.Version.ToString(3) + " is ready (you have " + Updater.CurrentText + ")." +
                (notes.Length > 0 ? "\n\nWhat's new:\n" + notes : "") +
                "\n\nYour tasks and settings stay as they are. The app restarts by itself.", "Update now", "Later", Dialog.Kind.Info)) return;
            installingUpdate = true;
            main.SetUpdateStatus("Downloading version " + r.Version.ToString(3) + "…", false);
            Thread t = new Thread(() =>
            {
                string err = Updater.Download(r);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (err == null) err = Updater.SwapAndRestart();
                    if (err != null)
                    {
                        installingUpdate = false;
                        main.SetUpdateStatus(err, true);
                        Dialog.Info(main.Win.IsVisible ? main.Win : null, "The update didn't finish",
                            err + "\n\nYou can download the new version yourself from:\n" + Updater.ReleasesPage, Dialog.Kind.Warning);
                        return;
                    }
                    timer.Stop();
                    tray.Visible = false;
                    tray.Dispose();
                    Shutdown();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void Tick()
        {
            try
            {
                svc.Rollover();
                List<TaskItem> due = svc.CollectDue(DateTime.Now);
                if (due.Count > 0) rem.Show(due, false);

                bool dayChanged = U.Today != lastDay;
                lastDay = U.Today;
                int minute = DateTime.Now.Minute;
                if (main.Win.IsVisible && (dayChanged || minute != lastMinute) && !main.IsBusy) main.Render();
                if (note.Win.IsVisible && (dayChanged || minute != lastMinute) && !note.Win.IsKeyboardFocusWithin) note.Render();
                lastMinute = minute;
                if (dayChanged) UpdateTrayText();
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        void ShowMain() { main.ShowAndActivate(); }

        void BuildTray()
        {
            tray = new Forms.NotifyIcon { Icon = AppIcon.Tray(), Text = "My Task Manager", Visible = true };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            Forms.ToolStripMenuItem open = new Forms.ToolStripMenuItem("Open My Task Manager", null, delegate { ShowMain(); });
            open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add("Add a task", null, delegate { ShowMain(); main.FocusAdd(); });
            menu.Items.Add("Show sticky note", null, delegate { note.SetVisible(true); main.Render(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) ShowMain(); };
        }

        void UpdateTrayText()
        {
            int left = svc.Tasks.Count(t => !t.Closed && string.CompareOrdinal(t.Date, U.Today) <= 0);
            string s = left == 0 ? "My Task Manager - nothing left today" : "My Task Manager - " + left + " task" + (left == 1 ? "" : "s") + " left today";
            tray.Text = s.Length > 63 ? s.Substring(0, 63) : s;
        }

        /* Light / dark / match Windows. The note follows: Night in dark mode, back to its old colour in light mode. */
        void SetTheme(string mode)
        {
            bool wasDark = Theme.IsDark;
            bool dark = Theme.WantsDark(mode);
            AppSettings s = svc.Settings;
            s.Theme = mode;
            Theme.Apply(dark);
            if (dark && !wasDark && s.NoteColor != "night") { s.NoteColorDay = s.NoteColor; s.NoteColor = "night"; }
            else if (!dark && wasDark && s.NoteColor == "night") s.NoteColor = string.IsNullOrEmpty(s.NoteColorDay) || s.NoteColorDay == "night" ? "yellow" : s.NoteColorDay;
            svc.Save();
            note.ApplyLook();
            main.UpdateThemeChips();
        }

        void OnHidden()
        {
            if (svc.Settings.TrayTipShown) return;
            svc.Settings.TrayTipShown = true;
            svc.Save();
            tray.ShowBalloonTip(6000, "My Task Manager is still running",
                "It stays here by the clock so your reminders can pop up. Right-click this icon to exit.", Forms.ToolTipIcon.Info);
        }

        void OnNoteHidden()
        {
            main.Render();
            if (noteTipShown) return;
            noteTipShown = true;
            tray.ShowBalloonTip(5000, "Sticky note hidden",
                "Bring it back any time: right-click this icon > Show sticky note.", Forms.ToolTipIcon.Info);
        }

        /* Settings > Remove from this PC */
        void RemoveFromPc()
        {
            bool deleteData;
            if (!UninstallDialog.Ask(main.Win, out deleteData)) return;
            timer.Stop();
            svc.Detach();                    // nothing gets written after this point
            Autostart.Apply(false);
            string err = null;
            if (deleteData)
            {
                try { if (Directory.Exists(Store.Folder)) Directory.Delete(Store.Folder, true); }
                catch (Exception ex) { err = ex.Message; }
            }
            string exe = Assembly.GetExecutingAssembly().Location;
            Dialog.Info(null, "My Task Manager is removed from startup",
                (deleteData ? (err == null ? "Your tasks and backups were deleted." : "Some data could not be deleted: " + err + "\nFolder: " + Store.Folder) : "Your tasks are still saved in:\n" + Store.Folder) +
                "\n\nIt will close now. To finish, delete MyTaskManager.exe (the folder opens next).", Dialog.Kind.Ok);
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + exe + "\""); } catch { }
            tray.Visible = false;
            tray.Dispose();
            Shutdown();
        }

        void ExitApp()
        {
            if (!Dialog.Confirm(null, "Exit My Task Manager?", "Reminders will stop until you open it again (or restart the PC).", "Exit", "Cancel", Dialog.Kind.Question)) return;
            tray.Visible = false;
            tray.Dispose();
            Shutdown();
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "MyTaskManager";

        public static void Apply(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray");
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
                }
            }
            catch (Exception ex) { Log.Write(ex); }
        }
    }

    static class AppIcon
    {
        static Stream Res() { return Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"); }

        public static Drawing.Icon Tray()
        {
            try { using (Stream s = Res()) return new Drawing.Icon(s, Forms.SystemInformation.SmallIconSize); }
            catch (Exception ex) { Log.Write(ex); return Drawing.SystemIcons.Application; }
        }

        public static ImageSource Image()
        {
            try { using (Stream s = Res()) return BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
            catch (Exception ex) { Log.Write(ex); return null; }
        }
    }

    static class Log
    {
        public static void Write(object o)
        {
            try
            {
                Directory.CreateDirectory(Store.Folder);
                string f = Path.Combine(Store.Folder, "app.log");
                if (File.Exists(f) && new FileInfo(f).Length > 512 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + o + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
