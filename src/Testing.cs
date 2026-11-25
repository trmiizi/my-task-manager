using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyTaskManager
{
    /* MyTaskManager.exe --selftest <report.txt>  : logic tests, in memory only (never touches real data) */
    static class SelfTest
    {
        static bool CanOpenExclusive(string f)
        {
            try { using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None)) return true; }
            catch { return false; }
        }

        public static int Run(string outFile)
        {
            StringBuilder sb = new StringBuilder();
            int fails = 0;
            Action<string, bool> check = (name, ok) => { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + name); if (!ok) fails++; };
            try
            {
                DateTime noon = new DateTime(2026, 11, 25, 12, 0, 0);
                TaskItem late = new TaskItem { Date = "2026-11-25", Time = "08:30" };
                check("earlier today counts as overdue", late.IsLate(noon));
                check("later today is not overdue", !new TaskItem { Date = "2026-11-25", Time = "17:00" }.IsLate(noon));
                check("yesterday is overdue", new TaskItem { Date = "2026-11-24", Time = "17:00" }.IsLate(noon));
                late.Done = true; check("ticked task is never overdue", !late.IsLate(noon));
                late.Done = false; late.Skipped = true; check("not-done task is never overdue", !late.IsLate(noon));
                check("month clamp 31 Jan -> 28 Feb", U.AddMonths("2026-01-31", 1, 31) == "2026-02-28");
                check("month clamp keeps anchor 28 Feb -> 31 Mar", U.AddMonths("2026-02-28", 1, 31) == "2026-03-31");
                check("leap year 31 Jan 2028 -> 29 Feb", U.AddMonths("2028-01-31", 1, 31) == "2028-02-29");
                check("year wrap Dec -> Jan", U.AddMonths("2026-12-15", 1, 15) == "2027-01-15");

                TimeSpan ts;
                check("time '2:30 PM'", U.TryParseTime("2:30 PM", out ts) && ts == new TimeSpan(14, 30, 0));
                check("time '14:30'", U.TryParseTime("14:30", out ts) && ts == new TimeSpan(14, 30, 0));
                check("time '12am'", U.TryParseTime("12am", out ts) && ts == TimeSpan.Zero);
                check("time '12:15 pm'", U.TryParseTime("12:15 pm", out ts) && ts == new TimeSpan(12, 15, 0));
                check("time '1430'", U.TryParseTime("1430", out ts) && ts == new TimeSpan(14, 30, 0));
                check("time '9'", U.TryParseTime("9", out ts) && ts == new TimeSpan(9, 0, 0));
                check("time '25:00' rejected", !U.TryParseTime("25:00", out ts));
                check("time 'abc' rejected", !U.TryParseTime("abc", out ts));

                TaskService svc = new TaskService(false);
                DateTime now = DateTime.Now;
                Func<DateTime, string> hm = d => U.HHmm(new TimeSpan(d.Hour, d.Minute, 0));

                DateTime d2 = now.AddHours(2);
                TaskItem b = svc.Add("Two hours away", U.Ymd(d2), hm(d2), 15, "none", "");
                check("future reminder not fired early", !svc.CollectDue(now).Contains(b));
                check("future reminder fires at remind time", svc.CollectDue(b.RemindAt.AddSeconds(1)).Contains(b));
                check("reminder fires only once", !svc.CollectDue(b.RemindAt.AddSeconds(30)).Contains(b));
                svc.Snooze(b, 10);
                check("snoozed: silent before snooze ends", !svc.CollectDue(DateTime.Now.AddMinutes(5)).Contains(b));
                check("snoozed: fires after snooze ends", svc.CollectDue(DateTime.Now.AddMinutes(11)).Contains(b));

                DateTime d10 = now.AddMinutes(10);
                TaskItem a = svc.Add("Due in 10 min, remind 15 before", U.Ymd(d10), hm(d10), 15, "none", "");
                check("late-added task: no instant pop-up", !svc.CollectDue(now).Contains(a));
                check("late-added task: reminds at due time", svc.CollectDue(a.Due.AddSeconds(1)).Contains(a));

                DateTime y = now.AddDays(-1);
                TaskItem past = svc.Add("Yesterday", U.Ymd(y), "09:00", 15, "none", "");
                check("task created in the past never pops up", !svc.CollectDue(now).Contains(past));

                TaskItem none = svc.Add("No reminder", U.Ymd(d2), hm(d2), -1, "none", "");
                check("'No reminder' never fires", !svc.CollectDue(d2.AddMinutes(5)).Contains(none));

                TaskItem done = svc.Add("Done one", U.Ymd(d2), hm(d2), 0, "none", "");
                svc.SetDone(done, true);
                check("done task never fires", !svc.CollectDue(d2.AddMinutes(5)).Contains(done));

                svc.Update(b, "Two hours away (moved)", U.Ymd(now.AddDays(1)), "10:00", 15, "none", "", "");
                check("editing the time re-arms the reminder", svc.CollectDue(b.RemindAt.AddSeconds(1)).Contains(b));

                TaskItem m = svc.Add("Monthly bill", "2026-01-31", "09:00", 0, "monthly", "");
                string next = svc.SetDone(m, true);
                check("monthly: completing creates next month (28 Feb)", next == "2026-02-28" && svc.Tasks.Count(t => t.SeriesId == m.SeriesId) == 2);
                svc.SetDone(m, false); svc.SetDone(m, true);
                check("monthly: re-ticking does not duplicate", svc.Tasks.Count(t => t.SeriesId == m.SeriesId) == 2);

                string start = U.AddMonths(U.Today, -3, DateTime.Today.Day);
                TaskItem r = svc.Add("Never ticked monthly", start, "09:00", 0, "monthly", "");
                svc.Rollover();
                List<TaskItem> series = svc.Tasks.Where(t => t.SeriesId == r.SeriesId).OrderBy(t => t.Date).ToList();
                check("rollover: unticked monthly task still creates this month's copy (" + series.Count + " copies)",
                    series.Count == 4 && string.CompareOrdinal(series.Last().Date, U.Today) <= 0 &&
                    string.CompareOrdinal(U.AddMonths(series.Last().Date, 1, r.AnchorDay), U.Today) > 0);
                svc.Rollover();
                check("rollover is idempotent", svc.Tasks.Count(t => t.SeriesId == r.SeriesId) == 4);

                // three states
                DateTime d3 = now.AddHours(3);
                TaskItem n1 = svc.Add("Not done test", U.Ymd(d3), hm(d3), 0, "none", "");
                svc.SetStatus(n1, "notdone");
                check("not done: recorded as closed + skipped", n1.Closed && n1.Skipped && !n1.Done && n1.CompletedAt != null);
                check("not done: reminder never fires", !svc.CollectDue(d3.AddMinutes(5)).Contains(n1));
                svc.SetStatus(n1, "done");
                check("not done -> done switches cleanly", n1.Done && !n1.Skipped);
                svc.SetStatus(n1, "pending");
                check("back to pending clears both", !n1.Closed && n1.CompletedAt == null);
                TaskItem m2 = svc.Add("Monthly skipped", "2026-03-10", "09:00", 0, "monthly", "");
                string nx = svc.SetStatus(m2, "notdone");
                check("monthly marked not done still schedules next month", nx == "2026-04-10");

                // quick add on the sticky note
                string qt, qd, qtime;
                U.QuickParse("Call Ali tomorrow 3pm", out qt, out qd, out qtime);
                check("quick add 'Call Ali tomorrow 3pm'", qt == "Call Ali" && qd == U.Tomorrow && qtime == "15:00");
                U.QuickParse("Submit report at 23:59", out qt, out qd, out qtime);
                check("quick add 'Submit report at 23:59'", qt == "Submit report" && qtime == "23:59" && (qd == U.Today || DateTime.Now.TimeOfDay > new TimeSpan(23, 58, 0)));
                U.QuickParse("Hantar borang esok 9.30am", out qt, out qd, out qtime);
                check("quick add Malay 'esok 9.30am'", qt == "Hantar borang" && qd == U.Tomorrow && qtime == "09:30");
                U.QuickParse("Buy 2 printers", out qt, out qd, out qtime);
                check("quick add without time keeps numbers in title", qt == "Buy 2 printers" && qtime != null);
                U.QuickParse("Gym 12:01am", out qt, out qd, out qtime);
                check("quick add time already past today -> tomorrow", qd == U.Tomorrow && qtime == "00:01" || DateTime.Now.TimeOfDay < new TimeSpan(0, 2, 0));

                string json = Store.Ser().Serialize(svc.Data);
                AppData back = Store.Ser().Deserialize<AppData>(json);
                Store.Normalize(back);
                check("save/load round trip keeps every task", back.Tasks.Count == svc.Tasks.Count);
                check("computed fields are not written to disk", !json.Contains("RemindAt") && !json.Contains("\"Due\""));

                string tmp = Path.Combine(Path.GetTempPath(), "mtm-import-test.json");
                File.WriteAllText(tmp, "{\"app\":\"MyTaskManager\",\"type\":\"backup\",\"tasks\":[{\"id\":\"x1\",\"title\":\"From HTML\",\"notes\":\"\",\"date\":\"2026-11-24\",\"time\":\"09:00\",\"remindBefore\":15,\"repeat\":\"monthly\",\"anchorDay\":24,\"seriesId\":\"x1\",\"done\":true,\"completedAt\":\"2026-11-24T07:38:00.000Z\",\"remarks\":\"paid\",\"createdAt\":\"2026-11-24T07:00:00.000Z\",\"spawnedNext\":true},{\"id\":\"bad\"}]}");
                List<TaskItem> imp = Store.Import(tmp);
                File.Delete(tmp);
                check("imports HTML-version backup", imp.Count == 1 && imp[0].Done && imp[0].Monthly && imp[0].Remarks == "paid" && imp[0].CompletedTime.Year == 2026);
                int added = svc.Merge(imp);
                check("merge adds new tasks and skips duplicates", added == 1 && svc.Merge(imp) == 0);

                // sticky note photo: missing or broken files fall back to plain colour instead of crashing
                check("note resize: corners and edges", StickyNote.EdgeHit(3, 3, 330, 430) == 13 && StickyNote.EdgeHit(327, 427, 330, 430) == 17
                    && StickyNote.EdgeHit(2, 200, 330, 430) == 10 && StickyNote.EdgeHit(325, 200, 330, 430) == 11
                    && StickyNote.EdgeHit(160, 4, 330, 430) == 12 && StickyNote.EdgeHit(160, 426, 330, 430) == 15
                    && StickyNote.EdgeHit(327, 5, 330, 430) == 14 && StickyNote.EdgeHit(5, 427, 330, 430) == 16);
                check("note resize: middle and scrollbar stay clickable", StickyNote.EdgeHit(160, 200, 330, 430) == 0 && StickyNote.EdgeHit(330 - 19, 200, 330, 430) == 0);
                check("note photo: missing file -> no photo", StickyNote.TryLoadPhoto(Path.Combine(Path.GetTempPath(), "mtm-no-such-photo.jpg")) == null);
                string junk = Path.Combine(Path.GetTempPath(), "mtm-not-a-photo.jpg");
                File.WriteAllText(junk, "this is not a picture");
                check("note photo: broken file -> no photo", StickyNote.TryLoadPhoto(junk) == null);
                File.Delete(junk);
                string wp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows\img0.jpg");
                if (File.Exists(wp))
                {
                    check("note photo: real JPG loads and file stays unlocked", StickyNote.TryLoadPhoto(wp) != null && CanOpenExclusive(wp));
                }
                string sjson = Store.Ser().Serialize(new AppSettings { NoteImage = @"C:\x\note-photo-1.jpg", NotePhotoStrength = "soft" });
                AppSettings sback = Store.Ser().Deserialize<AppSettings>(sjson);
                check("note photo settings save and load", sback.NoteImage == @"C:\x\note-photo-1.jpg" && sback.NotePhotoStrength == "soft");

                // ---- repeat options (2026-11-27 is a Friday)
                check("daily: next day", U.NextRepeat("2026-11-27", "daily", 27, "2026-11-27") == "2026-11-28");
                check("weekdays: Friday -> Monday", U.NextRepeat("2026-11-27", "weekdays", 27, "2026-11-27") == "2026-11-30");
                check("weekdays: Saturday -> Monday", U.NextRepeat("2026-11-28", "weekdays", 28, "2026-11-28") == "2026-11-30");
                check("weekly: +7 days", U.NextRepeat("2026-11-27", "weekly", 27, "2026-11-27") == "2026-12-04");
                check("daily: skips missed days up to today", U.NextRepeat("2026-11-20", "daily", 20, "2026-11-27") == "2026-11-27");
                check("weekly: skips missed weeks, keeps the weekday", U.NextRepeat("2026-11-06", "weekly", 6, "2026-11-25") == "2026-11-27");
                check("monthly unchanged by the new code", U.NextRepeat("2026-01-31", "monthly", 31, "2026-11-25") == "2026-02-28");

                TaskService rs = new TaskService(false);
                TaskItem dly = rs.Add("Daily standup", U.Ymd(DateTime.Today.AddDays(-5)), "09:00", 0, "daily", "");
                rs.Rollover();
                List<TaskItem> dser = rs.Tasks.Where(t => t.SeriesId == dly.SeriesId).OrderBy(t => t.Date).ToList();
                check("daily rollover: 5 missed days -> just the old one + today's (" + dser.Count + ")", dser.Count == 2 && dser[1].Date == U.Today);
                rs.Rollover();
                check("daily rollover is idempotent", rs.Tasks.Count(t => t.SeriesId == dly.SeriesId) == 2);
                string dnext = rs.SetStatus(dser[1], "done");
                check("daily: ticking today's creates tomorrow's", dnext == U.Tomorrow && rs.Tasks.Count(t => t.SeriesId == dly.SeriesId) == 3);

                TaskItem wd = rs.Add("Weekday check", U.Today, "10:00", 0, "weekdays", "");
                string wnext = rs.SetStatus(wd, "done");
                DayOfWeek wdow = U.ParseYmd(wnext).DayOfWeek;
                check("weekdays: next copy never on a weekend", wdow != DayOfWeek.Saturday && wdow != DayOfWeek.Sunday && string.CompareOrdinal(wnext, U.Today) > 0);
                TaskItem fixr = new TaskItem { Id = "r1", Title = "x", Date = U.Today, Time = "09:00", Repeat = "weekly" };
                Store.Fix(fixr);
                TaskItem fixbad = new TaskItem { Id = "r2", Title = "x", Date = U.Today, Time = "09:00", Repeat = "hourly" };
                Store.Fix(fixbad);
                check("saved repeat kept: weekly stays, unknown -> none", fixr.Repeat == "weekly" && fixr.SeriesId == "r1" && fixbad.Repeat == "none");

                // ---- checklist steps
                TaskItem ck = rs.Add("Prepare event", U.Today, "15:00", 0, "weekly", "");
                rs.Update(ck, ck.Title, ck.Date, ck.Time, 0, "weekly", "", "", new List<StepItem> { new StepItem { Text = "Book room" }, new StepItem { Text = "Send invites" } });
                rs.SetStep(ck, ck.Steps[0], true);
                check("steps: saved on the task, one ticked", ck.Steps.Count == 2 && ck.StepsDone == 1);
                string cnext = rs.SetStatus(ck, "done");
                TaskItem ckn = rs.Tasks.First(t => t.SeriesId == ck.SeriesId && t.Date == cnext);
                check("steps: next repeat copies the checklist, all unticked", ckn.Steps.Count == 2 && ckn.StepsDone == 0 && ck.StepsDone == 1);
                ckn.Steps[1].Done = true;
                check("steps: copies are independent", ck.Steps[1].Done == false);
                rs.Update(ck, ck.Title, ck.Date, ck.Time, 0, "weekly", "", "");
                check("steps: editing without a checklist keeps it", ck.Steps.Count == 2);
                string sj = Store.Ser().Serialize(rs.Data);
                AppData sb2 = Store.Ser().Deserialize<AppData>(sj);
                Store.Normalize(sb2);
                TaskItem ckb = sb2.Tasks.First(t => t.Id == ck.Id);
                check("steps: save/load round trip", ckb.Steps.Count == 2 && ckb.Steps[0].Done && ckb.Steps[1].Text == "Send invites");
                string stmp = Path.Combine(Path.GetTempPath(), "mtm-steps-test.json");
                Store.Export(stmp, new List<TaskItem> { ck });
                List<TaskItem> simp = Store.Import(stmp);
                File.Delete(stmp);
                check("steps: survive export + import", simp.Count == 1 && simp[0].Steps.Count == 2 && simp[0].Steps[0].Done && simp[0].Repeat == "weekly");
                TaskItem oldTask = new TaskItem { Id = "o1", Title = "old", Date = U.Today, Time = "09:00" };
                Store.Fix(oldTask);
                check("steps: tasks saved before this version get an empty list", oldTask.Steps != null && oldTask.Steps.Count == 0);

                // ---- nag: unanswered reminders come back
                TaskService ns = new TaskService(false);
                DateTime nd2 = now.AddHours(2);
                TaskItem ng = ns.Add("Nag me", U.Ymd(nd2), hm(nd2), 15, "none", "");
                DateTime fire1 = ng.RemindAt.AddSeconds(1);
                TaskService asv = new TaskService(false);
                TaskItem a1 = asv.Add("Archive me", U.Today, "09:00", 0, "none", "");
                TaskItem a2 = asv.Add("Still pending", U.Today, "10:00", 0, "none", "");
                asv.SetStatus(a1, "done");
                check("archive: only finished tasks are archived", asv.Archive(new[] { a1, a2 }) == 1 && a1.Archived && !a2.Archived);
                check("archive: archiving twice counts nothing", asv.Archive(new[] { a1 }) == 0);
                AppData aback = Store.Ser().Deserialize<AppData>(Store.Ser().Serialize(asv.Data)); Store.Normalize(aback);
                check("archive: saved and loaded", aback.Tasks.First(x => x.Title == "Archive me").Archived);
                asv.SetStatus(a1, "pending");
                check("archive: back to pending un-archives", !a1.Archived);
                TaskItem a3 = asv.Add("Weekly archived", U.Today, "09:00", 0, "weekly", "");
                asv.SetStatus(a3, "done"); asv.Archive(new[] { a3 });
                check("archive: next repeat copy is not archived", asv.Tasks.Any(x => x.SeriesId == a3.SeriesId && x != a3 && !x.Archived && !x.Closed));
                string relJson = "{\"tag_name\":\"v9.2.1\",\"body\":\"Archive button\",\"assets\":[{\"name\":\"README.txt\",\"browser_download_url\":\"https://x/r\",\"size\":10},{\"name\":\"MyTaskManager.exe\",\"browser_download_url\":\"https://x/e\",\"size\":360448,\"digest\":\"sha256:ABC\"}]}";
                Updater.Release rel = Updater.Parse(relJson);
                check("update: reads version, exe link, size and checksum", rel.Version == new Version(9, 2, 1) && rel.Url == "https://x/e" && rel.Size == 360448 && rel.Sha256 == "abc");
                check("update: newer version is offered", Updater.IsNewer(rel));
                check("update: same or older version is not offered", !Updater.IsNewer(Updater.Parse("{\"tag_name\":\"v" + Updater.CurrentText + "\",\"assets\":[{\"name\":\"MyTaskManager.exe\",\"browser_download_url\":\"https://x/e\"}]}"))
                    && !Updater.IsNewer(Updater.Parse("{\"tag_name\":\"v1.0.0\",\"assets\":[{\"name\":\"MyTaskManager.exe\",\"browser_download_url\":\"https://x/e\"}]}")));
                check("update: release without the exe is not offered", !Updater.IsNewer(Updater.Parse("{\"tag_name\":\"v9.9.9\",\"assets\":[]}")));
                string fake = Path.Combine(Path.GetTempPath(), "mtm-fake-update.exe");
                File.WriteAllBytes(fake, new byte[60000]);
                check("update: a file that isn't a program is rejected", Updater.Verify(fake, new Updater.Release()) != null);
                File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, fake, true);
                check("update: a real program passes, a wrong checksum fails", Updater.Verify(fake, new Updater.Release()) == null && Updater.Verify(fake, new Updater.Release { Sha256 = "00" }) != null);
                File.Delete(fake);
                check("nag: first reminder fires", ns.CollectDue(fire1).Contains(ng));
                check("nag: quiet before 10 min", !ns.CollectDue(fire1.AddMinutes(9)).Contains(ng));
                check("nag: comes back after 10 min if ignored", ns.CollectDue(fire1.AddMinutes(10).AddSeconds(1)).Contains(ng));
                ns.Dismiss(ng);
                check("nag: dismiss (X) stops it", !ns.CollectDue(fire1.AddHours(3)).Contains(ng));
                TaskItem ng2 = ns.Add("Nag then done", U.Ymd(nd2), hm(nd2), 15, "none", "");
                ns.CollectDue(ng2.RemindAt.AddSeconds(1));
                ns.SetStatus(ng2, "done");
                check("nag: marking done stops it", !ns.CollectDue(ng2.RemindAt.AddHours(1)).Contains(ng2));
                ns.Settings.NagMinutes = 0;
                TaskItem ng3 = ns.Add("No nag", U.Ymd(nd2), hm(nd2), 15, "none", "");
                ns.CollectDue(ng3.RemindAt.AddSeconds(1));
                check("nag: off means one reminder only", !ns.CollectDue(ng3.RemindAt.AddHours(1)).Contains(ng3));
                check("settings from older versions: nag 10 min, note shows 2 days, unlocked",
                    Store.Ser().Deserialize<AppSettings>("{\"Sound\":true}").NagMinutes == 10 &&
                    Store.Ser().Deserialize<AppSettings>("{}").NoteRange == "2days" && !Store.Ser().Deserialize<AppSettings>("{}").NoteLocked);
            }
            catch (Exception ex) { sb.AppendLine("FAIL  exception: " + ex); fails++; }
            sb.AppendLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            File.WriteAllText(outFile, sb.ToString());
            return fails;
        }
    }

    /* MyTaskManager.exe --screenshot <folder> : renders every screen with demo data to PNG, off-screen */
    static class Shots
    {
        /* MyTaskManager.exe --notetest : a demo note on screen for 15 s (no real data), for testing mouse resize */
        static List<ContextMenu> FindOpenMenus()
        {
            var list = new List<ContextMenu>();
            foreach (PresentationSource ps in PresentationSource.CurrentSources)
            {
                var root = ps.RootVisual as FrameworkElement;
                if (root == null) continue;
                DependencyObject d = root;
                while (d != null && !(d is ContextMenu)) d = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(d, 0) : null;
                if (d is ContextMenu && ((ContextMenu)d).IsOpen) list.Add((ContextMenu)d);
            }
            return list;
        }

        public static int NoteTest(string[] args)
        {
            bool dark = args.Length > 1 && args[1] == "dark";
            string shot = args.Length > 2 ? args[2] : null;
            TaskService svc = new TaskService(false);
            Demo(svc);
            if (dark) { Theme.Apply(true); svc.Settings.NoteColor = "night"; }
            StickyNote n = new StickyNote(svc);
            n.Win.Left = 300; n.Win.Top = 200; n.Win.Title = "MTM note test";
            n.Win.Show();
            int step = 0;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(shot != null ? 1 : 15) };
            t.Tick += delegate
            {
                if (shot == null) { Application.Current.Shutdown(); return; }
                step++;
                if (step == 1) ((Button)n.Win.FindName("ColorBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (step == 2)
                {
                    // the ... menu is built on click; find the open one and open its Colour submenu
                    foreach (object o in FindOpenMenus())
                        foreach (object it in ((ContextMenu)o).Items)
                            if (it is MenuItem && (string)((MenuItem)it).Header == "Colour") ((MenuItem)it).IsSubmenuOpen = true;
                }
                if (step == 3)
                {
                    using (var bmp = new System.Drawing.Bitmap(900, 650))
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(250, 150, 0, 0, bmp.Size);
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Application.Current.Shutdown();
                }
            };
            t.Start();
            return Application.Current.Run();
        }

        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);
            TaskService svc = new TaskService(false);
            Demo(svc);
            MainView mv = new MainView(svc);
            Window w = mv.Win;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -30000; w.Top = 0; w.ShowActivated = false; w.ShowInTaskbar = false;
            w.Show();
            foreach (string v in new[] { "today", "tomorrow", "upcoming", "completed", "settings" })
            {
                mv.SetView(v);
                Save(w, Path.Combine(dir, v + ".png"));
                if (v == "today" || v == "settings")
                {
                    ScrollViewer sv = (ScrollViewer)w.FindName("MainScroll");
                    sv.ScrollToEnd(); Pump(); w.UpdateLayout();
                    Save(w, Path.Combine(dir, v + "-bottom.png"));
                    sv.ScrollToTop();
                }
            }
            mv.SetView("today"); FocusShot(w, Path.Combine(dir, "focus.png"));
            ReminderCenter rc = new ReminderCenter(svc);
            TaskItem t = svc.Tasks.First(x => x.Title.StartsWith("Pay electricity"));
            Window card = rc.MakeCard(t);
            card.Left = -30000; card.Top = 0;
            card.Show();
            Save(card, Path.Combine(dir, "reminder.png"));
            card.Close();

            Window dlg = Dialog.Build(null, "Delete this task?", "\u201CRenew road tax\u201D will be removed. This can't be undone.", "Delete", "Cancel", Dialog.Kind.Danger);
            dlg.WindowStartupLocation = WindowStartupLocation.Manual; dlg.Left = -30000; dlg.Top = 0; dlg.ShowActivated = false;
            dlg.Show();
            Save(dlg, Path.Combine(dir, "dialog-delete.png"));
            dlg.Close();

            StickyNote note = new StickyNote(svc);
            note.Win.Left = -30000; note.Win.Top = 0; note.Win.ShowActivated = false;
            note.Win.Show();
            Save(note.Win, Path.Combine(dir, "note.png"));
            note.TypeQuickAdd("Call Ali tomorrow 3pm");
            Save(note.Win, Path.Combine(dir, "note-add-options.png"));
            note.Win.Close();

            svc.Settings.NoteRange = "week"; svc.Settings.NoteLocked = true;
            StickyNote wn = new StickyNote(svc);
            wn.Win.Left = -30000; wn.Win.Top = 0; wn.Win.ShowActivated = false; wn.Win.Height = 640;
            wn.Win.Show();
            Save(wn.Win, Path.Combine(dir, "note-week-locked.png"));
            wn.Win.Close();
            svc.Settings.NoteRange = "2days"; svc.Settings.NoteLocked = false;

            // same note with a photo behind it (a stock Windows wallpaper), once per strength
            string pic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows\img0.jpg");
            if (File.Exists(pic))
            {
                svc.Settings.NoteImage = pic;
                foreach (string k in new[] { "soft", "medium", "strong" })
                {
                    svc.Settings.NotePhotoStrength = k;
                    StickyNote pn = new StickyNote(svc);
                    pn.Win.Left = -30000; pn.Win.Top = 0; pn.Win.ShowActivated = false;
                    pn.Win.Show();
                    Save(pn.Win, Path.Combine(dir, "note-photo-" + k + ".png"));
                    pn.Win.Close();
                }
                svc.Settings.NoteImage = null;
            }

            // dark mode, switched live on the already-open main window
            Theme.Apply(true);
            svc.Settings.NoteColor = "night";
            foreach (string v in new[] { "today", "settings", "completed" })
            {
                mv.SetView(v);
                Save(w, Path.Combine(dir, "dark-" + v + ".png"));
            }
            mv.SetView("today"); FocusShot(w, Path.Combine(dir, "dark-focus.png"));
            Window dcard = rc.MakeCard(t);
            dcard.Left = -30000; dcard.Top = 0;
            dcard.Show();
            Save(dcard, Path.Combine(dir, "dark-reminder.png"));
            dcard.Close();
            Window ddlg = Dialog.Build(null, "Delete this task?", "“Renew road tax” will be removed. This can't be undone.", "Delete", "Cancel", Dialog.Kind.Danger);
            ddlg.WindowStartupLocation = WindowStartupLocation.Manual; ddlg.Left = -30000; ddlg.Top = 0; ddlg.ShowActivated = false;
            ddlg.Show();
            Save(ddlg, Path.Combine(dir, "dark-dialog.png"));
            ddlg.Close();
            StickyNote dn = new StickyNote(svc);
            dn.Win.Left = -30000; dn.Win.Top = 0; dn.Win.ShowActivated = false;
            dn.Win.Show();
            dn.TypeQuickAdd("Call Ali tomorrow 3pm");
            Save(dn.Win, Path.Combine(dir, "dark-note.png"));
            dn.Win.Close();
            // a yellow note while the app is dark keeps its own light controls
            svc.Settings.NoteColor = "yellow";
            StickyNote yn = new StickyNote(svc);
            yn.Win.Left = -30000; yn.Win.Top = 0; yn.Win.ShowActivated = false;
            yn.Win.Show();
            yn.TypeQuickAdd("Call Ali tomorrow 3pm");
            Save(yn.Win, Path.Combine(dir, "dark-app-yellow-note.png"));
            yn.Win.Close();
            Theme.Apply(false);

            // What's new page, light and dark
            mv.SetView("whatsnew");
            Pump(); w.UpdateLayout();
            Save(w, Path.Combine(dir, "whatsnew.png"));
            Theme.Apply(true); Pump(); w.UpdateLayout();
            Save(w, Path.Combine(dir, "whatsnew-dark.png"));
            Theme.Apply(false);

            // archive: Today with an "Archive ticked" button, then History > Archived
            mv.SetView("settings");
            mv.SetUpdateStatus("Version 2.2.0 is available.\nWhat's new: Bigger text option and a weekly summary.", false, "Update to 2.2.0");
            Pump(); w.UpdateLayout();
            Save(w, Path.Combine(dir, "settings-update.png"));
            mv.SetView("today");
            Save(w, Path.Combine(dir, "archive-today.png"));
            mv.SetView("completed");
            Save(w, Path.Combine(dir, "archive-history-all.png"));
            svc.Archive(svc.Tasks.Where(x => x.Closed).ToList());
            mv.SetView("completed");
            ((Button)w.FindName("FilterArch")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(); w.UpdateLayout();
            Save(w, Path.Combine(dir, "archive-history.png"));
            mv.SetView("today");
            Save(w, Path.Combine(dir, "archive-today-after.png"));

            Drawing_Check(dir);
            w.Hide();
            return 0;
        }

        static void Drawing_Check(string dir)
        {
            System.Drawing.Icon ic = AppIcon_TrayProbe();
            File.WriteAllText(Path.Combine(dir, "icon.txt"), ic == null ? "tray icon: FAILED" : "tray icon: " + ic.Width + "x" + ic.Height);
        }

        static System.Drawing.Icon AppIcon_TrayProbe()
        {
            try
            {
                using (Stream s = typeof(Shots).Assembly.GetManifestResourceStream("app.ico"))
                    return new System.Drawing.Icon(s, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch { return null; }
        }

        static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
        }

        /* Draws the keyboard focus ring on a few controls (as if reached with Tab) and captures the whole window incl. the adorner layer. */
        static void FocusShot(Window w, string path)
        {
            List<FrameworkElement> targets = new List<FrameworkElement>();
            targets.Add((FrameworkElement)w.FindName("AddTime"));
            targets.Add((FrameworkElement)w.FindName("AddBtn"));
            FrameworkElement icon = FindFirst(w, x => x is Button && ((Button)x).Style == Theme.S("Icon") && x.IsVisible);
            FrameworkElement box = FindFirst(w, x => x is CheckBox && x.IsVisible);
            if (icon != null) targets.Add(icon);
            if (box != null) targets.Add(box);
            List<KeyValuePair<System.Windows.Documents.AdornerLayer, RingAdorner>> added = new List<KeyValuePair<System.Windows.Documents.AdornerLayer, RingAdorner>>();
            foreach (FrameworkElement t in targets)
            {
                if (t == null) continue;
                System.Windows.Documents.AdornerLayer layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(t);
                if (layer == null) continue;
                RingAdorner a = new RingAdorner(t, t.FocusVisualStyle);
                layer.Add(a);
                added.Add(new KeyValuePair<System.Windows.Documents.AdornerLayer, RingAdorner>(layer, a));
            }
            Pump(); w.UpdateLayout(); Pump();
            FrameworkElement root = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            Size size = new Size(Math.Ceiling(root.ActualWidth), Math.Ceiling(root.ActualHeight));
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Theme.B("Bg"), null, new Rect(size));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(size));
            }
            rtb.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
            foreach (KeyValuePair<System.Windows.Documents.AdornerLayer, RingAdorner> kv in added) kv.Key.Remove(kv.Value);
        }

        static FrameworkElement FindFirst(DependencyObject d, Func<FrameworkElement, bool> pick)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(d, i);
                FrameworkElement fe = c as FrameworkElement;
                if (fe != null && pick(fe)) return fe;
                FrameworkElement r = FindFirst(c, pick);
                if (r != null) return r;
            }
            return null;
        }

        class RingAdorner : System.Windows.Documents.Adorner
        {
            readonly Control ring;
            public RingAdorner(UIElement el, Style style) : base(el)
            {
                ring = new Control { Style = style, IsHitTestVisible = false };
                AddVisualChild(ring);
            }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index) { return ring; }
            protected override Size MeasureOverride(Size constraint) { ring.Measure(AdornedElement.RenderSize); return AdornedElement.RenderSize; }
            protected override Size ArrangeOverride(Size finalSize) { ring.Arrange(new Rect(finalSize)); return finalSize; }
        }

        static void Save(Window w, string path)
        {
            Pump(); w.UpdateLayout(); Pump();
            FrameworkElement fe = (FrameworkElement)w.Content;
            Size size = new Size(Math.Ceiling(fe.ActualWidth + fe.Margin.Left + fe.Margin.Right), Math.Ceiling(fe.ActualHeight + fe.Margin.Top + fe.Margin.Bottom));
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                Brush bg = w.Background is SolidColorBrush && ((SolidColorBrush)w.Background).Color.A == 0 ? Theme.B("Bg") : w.Background;
                dc.DrawRectangle(bg, null, new Rect(size));
                dc.DrawRectangle(new VisualBrush(fe), null, new Rect(new Point(fe.Margin.Left, fe.Margin.Top), new Size(fe.ActualWidth, fe.ActualHeight)));
            }
            rtb.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
        }

        static void Demo(TaskService svc)
        {
            DateTime now = DateTime.Now;
            string td = U.Today, tm = U.Tomorrow;
            Func<int, string> inH = h => U.HHmm(new TimeSpan(Math.Min(23, now.Hour + h), now.Hour + h > 23 ? 59 : 0, 0));
            svc.Add("Renew road tax", U.Ymd(now.AddDays(-1)), "10:00", 60, "none", "");
            svc.Add("Call supplier about the invoice", td, "08:30", 15, "none", "Ask for the corrected PDF");
            svc.Add("Pay electricity bill", td, inH(1), 30, "monthly", "Account 2041 7783 · due on the 24th");
            TaskItem slides = svc.Add("Prepare slides for Monday review", td, inH(3), 15, "none", "");
            slides.Steps = new List<StepItem> { new StepItem { Text = "Get sales numbers from Aina", Done = true }, new StepItem { Text = "Draft 10 slides" }, new StepItem { Text = "Send to boss for review" } };
            svc.Add("Check the shared inbox", td, inH(2), 0, "weekdays", "");
            TaskItem sent = svc.Add("Send weekly report", td, "09:00", 15, "none", "");
            svc.SetDone(sent, true);
            svc.Add("Dentist appointment", tm, "09:30", 60, "none", "Bring the insurance card");
            svc.Add("Submit claim form", tm, "14:00", 15, "none", "");
            svc.Add("Mum's birthday dinner", U.Ymd(now.AddDays(3)), "19:30", 1440, "none", "Book a table for 6");
            svc.Add("Quarterly tax filing", U.Ymd(now.AddDays(9)), "10:00", 1440, "monthly", "");
            TaskItem c1 = svc.Add("Pay internet bill", U.Ymd(now.AddDays(-6)), "09:00", 15, "none", "");
            svc.SetDone(c1, true); c1.Remarks = "Paid online, ref TX-55120"; c1.CompletedAt = U.Stamp(now.AddDays(-6).Date.AddHours(9.5));
            TaskItem c2 = svc.Add("Book car service", U.Ymd(now.AddDays(-3)), "11:00", 15, "none", "");
            svc.SetDone(c2, true); c2.CompletedAt = U.Stamp(now.AddDays(-3).Date.AddHours(11.2));
            sent.Remarks = "";
            TaskItem nd = svc.Add("Follow up with the bank", td, "11:00", 15, "none", "");
            svc.SetStatus(nd, "notdone");
            TaskItem c3 = svc.Add("Visit the site office", U.Ymd(now.AddDays(-2)), "15:00", 15, "none", "");
            svc.SetStatus(c3, "notdone"); c3.Remarks = "Rescheduled, contractor not available"; c3.CompletedAt = U.Stamp(now.AddDays(-2).Date.AddHours(17));
        }
    }
}
