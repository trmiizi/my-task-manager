using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MyTaskManager
{
    public class TaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string Date { get; set; }        // yyyy-MM-dd
        public string Time { get; set; }        // HH:mm (24h)
        public int RemindBefore { get; set; }   // minutes before due; -1 = no reminder
        public string Repeat { get; set; }      // "none" | "daily" | "weekdays" | "weekly" | "monthly"
        public int AnchorDay { get; set; }      // day of month a monthly task belongs to (31 -> clamps to 30/28)
        public string SeriesId { get; set; }
        public bool Done { get; set; }
        public bool Skipped { get; set; }       // marked "Not done"
        public bool Archived { get; set; }      // done / not done, tucked away out of the lists
        public string CompletedAt { get; set; }
        public string Remarks { get; set; }
        public string CreatedAt { get; set; }
        public bool SpawnedNext { get; set; }
        public string NotifiedKey { get; set; }
        public string SnoozeUntil { get; set; }
        public List<StepItem> Steps { get; set; }   // checklist inside the task

        [ScriptIgnore] public DateTime Due { get { return U.Due(Date, Time); } }
        [ScriptIgnore] public bool Monthly { get { return Repeat == "monthly"; } }
        [ScriptIgnore] public bool Repeats { get { return U.IsRepeat(Repeat) && Repeat != "none"; } }
        [ScriptIgnore] public bool HasSteps { get { return Steps != null && Steps.Count > 0; } }
        [ScriptIgnore] public int StepsDone { get { return Steps == null ? 0 : Steps.Count(s => s.Done); } }
        [ScriptIgnore] public bool HasReminder { get { return RemindBefore >= 0; } }
        [ScriptIgnore] public DateTime RemindAt { get { return Due.AddMinutes(-Math.Max(0, RemindBefore)); } }
        [ScriptIgnore] public string ReminderKey { get { return Date + "T" + Time + "|" + RemindBefore; } }
        [ScriptIgnore] public bool Closed { get { return Done || Skipped; } }
        /* Not ticked and its time has passed (earlier today counts too). */
        public bool IsLate(DateTime now) { return !Closed && Due < now; }
        [ScriptIgnore] public DateTime CompletedTime { get { return U.ParseStamp(CompletedAt); } }

        /* copies the task; the checklist is copied too (not shared) */
        public TaskItem Clone()
        {
            TaskItem c = (TaskItem)MemberwiseClone();
            c.Steps = Steps == null ? new List<StepItem>() : Steps.Select(s => new StepItem { Text = s.Text, Done = s.Done }).ToList();
            return c;
        }
    }

    public class StepItem
    {
        public string Text { get; set; }
        public bool Done { get; set; }
    }

    public class AppSettings
    {
        public bool StartWithWindows { get; set; }
        public bool Sound { get; set; }
        public bool TrayTipShown { get; set; }
        public bool NoteVisible { get; set; }
        public bool NoteTopmost { get; set; }
        public string NoteColor { get; set; }
        public double NoteLeft { get; set; }
        public double NoteTop { get; set; }
        public double NoteWidth { get; set; }
        public double NoteHeight { get; set; }
        /* full path of the note's background photo (a copy kept in the data folder), or null for a plain colour */
        public string NoteImage { get; set; }
        /* how much of the photo shows through: soft / medium / strong */
        public string NotePhotoStrength { get; set; }
        /* sticky note can't be moved or resized while locked */
        public bool NoteLocked { get; set; }
        /* what the note shows: "2days" (today + tomorrow) or "week" (next 7 days) */
        public string NoteRange { get; set; }
        /* if a reminder is left unanswered, show it again after this many minutes; 0 = don't */
        public int NagMinutes { get; set; }
        public string Theme { get; set; }   // light | dark | system
        /* newest version whose "What's new" page has been opened; the sidebar shows "New" until then */
        public string SeenVersion { get; set; }
        public string NoteColorDay { get; set; }   // note colour to go back to when leaving dark mode
        public AppSettings() { StartWithWindows = true; Sound = true; NoteVisible = true; NoteColor = "yellow"; NoteRange = "2days"; NagMinutes = 10; Theme = "light"; }
    }

    public class AppData
    {
        public int Version { get; set; }
        public List<TaskItem> Tasks { get; set; }
        public AppSettings Settings { get; set; }
        public AppData() { Version = 1; Tasks = new List<TaskItem>(); Settings = new AppSettings(); }
    }

    /* ------------------------------------------------------------------ helpers */
    public static class U
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly CultureInfo En = new CultureInfo("en-US");

        public static string Ymd(DateTime d) { return d.ToString("yyyy-MM-dd", Inv); }
        public static DateTime ParseYmd(string s) { return DateTime.ParseExact(s, "yyyy-MM-dd", Inv); }
        public static bool IsYmd(string s) { DateTime d; return s != null && DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out d); }
        public static string Today { get { return Ymd(DateTime.Today); } }
        public static string Tomorrow { get { return Ymd(DateTime.Today.AddDays(1)); } }
        public static string Now() { return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv); }
        public static string Stamp(DateTime d) { return d.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv); }

        public static DateTime ParseStamp(string s)
        {
            DateTime d;
            if (string.IsNullOrEmpty(s) || !DateTime.TryParse(s, Inv, DateTimeStyles.RoundtripKind, out d)) return DateTime.MinValue;
            return d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;
        }

        public static DateTime Due(string date, string time)
        {
            DateTime d = IsYmd(date) ? ParseYmd(date) : DateTime.Today;
            TimeSpan t;
            if (!TryParseTime(time, out t)) t = new TimeSpan(9, 0, 0);
            return d + t;
        }

        /* Accepts 14:30, 2:30 PM, 2:30pm, 2pm, 1430, 9 */
        public static bool TryParseTime(string s, out TimeSpan t)
        {
            t = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().ToLowerInvariant().Replace(".", ":").Replace(" ", "");
            bool pm = s.EndsWith("pm") || s.EndsWith("p");
            bool am = s.EndsWith("am") || s.EndsWith("a");
            s = s.TrimEnd('a', 'p', 'm');
            int h, m = 0;
            if (s.Contains(":"))
            {
                string[] p = s.Split(':');
                if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m)) return false;
            }
            else
            {
                int n;
                if (s.Length == 0 || !int.TryParse(s, out n)) return false;
                if (s.Length <= 2) h = n; else { h = n / 100; m = n % 100; }
            }
            if (am || pm)
            {
                if (h < 1 || h > 12) return false;
                if (h == 12) h = 0;
                if (pm) h += 12;
            }
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            t = new TimeSpan(h, m, 0);
            return true;
        }

        public static readonly string[] RepeatKeys = { "none", "daily", "weekdays", "weekly", "monthly" };
        public static bool IsRepeat(string r) { return Array.IndexOf(RepeatKeys, r) >= 0; }
        public static string RepeatLabel(string r)
        {
            switch (r)
            {
                case "daily": return "Daily";
                case "weekdays": return "Weekdays (Mon–Fri)";
                case "weekly": return "Weekly";
                case "monthly": return "Monthly";
                default: return "No repeat";
            }
        }
        public static string RepeatShort(string r) { return r == "weekdays" ? "Weekdays" : RepeatLabel(r); }

        /* The next date in a repeating series. Daily / weekday / weekly series skip ahead to today
           (an unticked task from last week does not spawn a pile of overdue copies). */
        public static string NextRepeat(string date, string repeat, int anchorDay, string today)
        {
            if (repeat == "monthly") return AddMonths(date, 1, anchorDay);
            DateTime d = ParseYmd(date);
            DateTime floor = IsYmd(today) ? ParseYmd(today) : DateTime.Today;
            Func<DateTime, DateTime> step;
            if (repeat == "weekly") step = x => x.AddDays(7);
            else if (repeat == "weekdays")
                step = x => { x = x.AddDays(1); while (x.DayOfWeek == DayOfWeek.Saturday || x.DayOfWeek == DayOfWeek.Sunday) x = x.AddDays(1); return x; };
            else step = x => x.AddDays(1);
            d = step(d);
            while (d < floor) d = step(d);
            return Ymd(d);
        }

        public static string HHmm(TimeSpan t) { return string.Format("{0:00}:{1:00}", t.Hours, t.Minutes); }

        public static string AddMonths(string date, int n, int anchorDay)
        {
            DateTime d = ParseYmd(date);
            DateTime first = new DateTime(d.Year, d.Month, 1).AddMonths(n);
            int last = DateTime.DaysInMonth(first.Year, first.Month);
            int day = anchorDay > 0 ? anchorDay : d.Day;
            return Ymd(new DateTime(first.Year, first.Month, Math.Min(day, last)));
        }

        public static string FmtDate(string s)
        {
            if (s == Today) return "Today";
            if (s == Tomorrow) return "Tomorrow";
            if (s == Ymd(DateTime.Today.AddDays(-1))) return "Yesterday";
            DateTime d = ParseYmd(s);
            return d.ToString(d.Year == DateTime.Today.Year ? "ddd, d MMM" : "ddd, d MMM yyyy", En);
        }
        public static string FmtLong(string s) { return ParseYmd(s).ToString("ddd, d MMM yyyy", En); }
        public static string FmtTime(string hhmm) { return Due("2000-01-01", hhmm).ToString("h:mm tt", En); }
        public static string FmtTime(TimeSpan t) { return (DateTime.Today + t).ToString("h:mm tt", En); }
        public static string FmtStamp(DateTime d) { return d == DateTime.MinValue ? "" : d.ToString("d MMM yyyy, h:mm tt", En); }

        public static string Rel(DateTime due)
        {
            TimeSpan diff = due - DateTime.Now;
            int m = (int)Math.Round(Math.Abs(diff.TotalMinutes));
            if (m < 1) return "due now";
            string s;
            if (m < 60) s = m + " min";
            else if (m < 1440) { int h = m / 60, r = m % 60; s = r > 0 ? h + " h " + r + " min" : h + " h"; }
            else { int d = (int)Math.Round(m / 1440.0); s = d + (d == 1 ? " day" : " days"); }
            return diff.TotalMilliseconds < 0 ? s + " overdue" : "in " + s;
        }

        public static readonly int[] RemindValues = { 0, 5, 10, 15, 30, 60, 120, 1440, -1 };
        public static string RemindLabel(int v)
        {
            switch (v)
            {
                case -1: return "No reminder";
                case 0: return "At due time";
                case 60: return "1 hour before";
                case 120: return "2 hours before";
                case 1440: return "1 day before";
                default: return v + " min before";
            }
        }

        /* "Call Ali tomorrow 3pm" -> "Call Ali", tomorrow, 15:00.
           No time given -> next full hour today (or 09:00 tomorrow). A time already past today -> tomorrow. */
        public static void QuickParse(string input, out string title, out string date, out string time)
        {
            string s = " " + (input ?? "").Trim() + " ";
            date = Today;
            Regex dayRx = new Regex(@"\s(tomorrow|tmrw|tmr|esok|today|hari ini)(?=\s)", RegexOptions.IgnoreCase);
            Match dm = dayRx.Match(s);
            bool explicitDay = dm.Success;
            if (dm.Success)
            {
                string w = dm.Groups[1].Value.ToLowerInvariant();
                if (w != "today" && w != "hari ini") date = Tomorrow;
                s = s.Remove(dm.Index, dm.Length);
            }
            Regex timeRx = new Regex(@"\s(?:at\s+|@\s*|pukul\s+)?(\d{1,2}(?:[:.]\d{2})?\s*(?:am|pm)|\d{1,2}[:.]\d{2})(?=\s)", RegexOptions.IgnoreCase);
            MatchCollection tms = timeRx.Matches(s);
            TimeSpan ts;
            time = null;
            if (tms.Count > 0)
            {
                Match tm = tms[tms.Count - 1];
                if (TryParseTime(tm.Groups[1].Value, out ts))
                {
                    time = HHmm(ts);
                    s = s.Remove(tm.Index, tm.Length);
                    if (!explicitDay && DateTime.Today + ts < DateTime.Now.AddMinutes(-1)) date = Tomorrow;
                }
            }
            if (time == null)
            {
                DateTime next = DateTime.Now.AddHours(1);
                if (date == Today && next.Date == DateTime.Today) time = HHmm(new TimeSpan(next.Hour, 0, 0));
                else { if (date == Today) date = Tomorrow; time = "09:00"; }
            }
            title = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('-', ',', ':').Trim();
            if (title.Length == 0) title = (input ?? "").Trim();
        }

        public static string NewId() { return DateTime.UtcNow.Ticks.ToString("x") + Guid.NewGuid().ToString("N").Substring(0, 6); }
    }

    /* ------------------------------------------------------------------ storage */
    public static class Store
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTaskManager");
        public static string DataFile { get { return Path.Combine(Folder, "data.json"); } }
        public static string BackupFolder { get { return Path.Combine(Folder, "Backups"); } }

        public static JavaScriptSerializer Ser()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static AppData Load()
        {
            Directory.CreateDirectory(Folder);
            foreach (string f in new[] { DataFile, DataFile + ".bak" })
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    AppData d = Ser().Deserialize<AppData>(File.ReadAllText(f, Encoding.UTF8));
                    if (d != null) { Normalize(d); return d; }
                }
                catch { }
            }
            return new AppData();
        }

        public static void Normalize(AppData d)
        {
            if (d.Tasks == null) d.Tasks = new List<TaskItem>();
            if (d.Settings == null) d.Settings = new AppSettings();
            d.Tasks.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.Title) || !U.IsYmd(t.Date));
            foreach (TaskItem t in d.Tasks) Fix(t);
        }

        public static void Fix(TaskItem t)
        {
            TimeSpan ts;
            if (!U.TryParseTime(t.Time, out ts)) ts = new TimeSpan(9, 0, 0);
            t.Time = U.HHmm(ts);
            if (!U.IsRepeat(t.Repeat)) t.Repeat = "none";
            if (t.AnchorDay <= 0) t.AnchorDay = U.ParseYmd(t.Date).Day;
            if (t.Repeats && string.IsNullOrEmpty(t.SeriesId)) t.SeriesId = t.Id;
            if (t.Steps == null) t.Steps = new List<StepItem>();
            t.Steps.RemoveAll(s => s == null || string.IsNullOrWhiteSpace(s.Text));
            if (t.Notes == null) t.Notes = "";
            if (t.Remarks == null) t.Remarks = "";
            if (t.RemindBefore < -1) t.RemindBefore = -1;
        }

        public static void Save(AppData d)
        {
            Directory.CreateDirectory(Folder);
            string json = Ser().Serialize(d);
            string tmp = DataFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(DataFile))
            {
                try { File.Replace(tmp, DataFile, DataFile + ".bak", true); }
                catch { File.Copy(tmp, DataFile, true); File.Delete(tmp); }
            }
            else File.Move(tmp, DataFile);
            DailyBackup(json);
        }

        static void DailyBackup(string json)
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                string today = Path.Combine(BackupFolder, "backup-" + U.Today + ".json");
                File.WriteAllText(today, json, new UTF8Encoding(false));
                foreach (string old in Directory.GetFiles(BackupFolder, "backup-*.json").OrderByDescending(x => x).Skip(14))
                    File.Delete(old);
            }
            catch { }
        }

        public static void Export(string path, List<TaskItem> tasks)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["app"] = "MyTaskManager";
            o["type"] = "backup";
            o["exportedAt"] = U.Now();
            o["tasks"] = tasks;
            File.WriteAllText(path, Ser().Serialize(o), new UTF8Encoding(false));
        }

        /* Reads backups from this app AND from the earlier HTML version */
        public static List<TaskItem> Import(string path)
        {
            object root = Ser().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            IEnumerable arr = null;
            IDictionary<string, object> dict = root as IDictionary<string, object>;
            if (dict != null)
            {
                foreach (KeyValuePair<string, object> kv in dict)
                    if (kv.Key.Equals("tasks", StringComparison.OrdinalIgnoreCase)) arr = kv.Value as IEnumerable;
            }
            else arr = root as IEnumerable;
            if (arr == null || arr is string) throw new InvalidDataException("This file is not a My Task Manager backup.");

            List<TaskItem> list = new List<TaskItem>();
            foreach (object o in arr)
            {
                IDictionary<string, object> d = o as IDictionary<string, object>;
                if (d == null) continue;
                Func<string, object> g = key =>
                {
                    foreach (KeyValuePair<string, object> kv in d) if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                    return null;
                };
                Func<string, string> gs = key => { object v = g(key); return v == null ? null : Convert.ToString(v, U.Inv); };
                Func<string, int, int> gi = (key, def) => { object v = g(key); try { return v == null ? def : Convert.ToInt32(v, U.Inv); } catch { return def; } };
                Func<string, bool> gb = key => { object v = g(key); return v is bool && (bool)v; };

                TaskItem t = new TaskItem
                {
                    Id = gs("id"), Title = gs("title"), Notes = gs("notes"), Date = gs("date"), Time = gs("time"),
                    RemindBefore = gi("remindBefore", 15), Repeat = gs("repeat"), AnchorDay = gi("anchorDay", 0),
                    SeriesId = gs("seriesId"), Done = gb("done"), Skipped = gb("skipped"), Remarks = gs("remarks"), SpawnedNext = gb("spawnedNext"),
                    NotifiedKey = gs("notifiedKey"), SnoozeUntil = gs("snoozeUntil")
                };
                IEnumerable steps = g("steps") as IEnumerable;
                t.Steps = new List<StepItem>();
                if (steps != null && !(steps is string))
                    foreach (object so in steps)
                    {
                        IDictionary<string, object> sd = so as IDictionary<string, object>;
                        if (sd == null) continue;
                        string st = null; bool sdone = false;
                        foreach (KeyValuePair<string, object> kv in sd)
                        {
                            if (kv.Key.Equals("text", StringComparison.OrdinalIgnoreCase)) st = Convert.ToString(kv.Value, U.Inv);
                            if (kv.Key.Equals("done", StringComparison.OrdinalIgnoreCase)) sdone = kv.Value is bool && (bool)kv.Value;
                        }
                        if (!string.IsNullOrWhiteSpace(st)) t.Steps.Add(new StepItem { Text = st, Done = sdone });
                    }
                DateTime c = U.ParseStamp(gs("completedAt"));
                t.CompletedAt = c == DateTime.MinValue ? null : U.Stamp(c);
                DateTime cr = U.ParseStamp(gs("createdAt"));
                t.CreatedAt = cr == DateTime.MinValue ? U.Now() : U.Stamp(cr);
                if (string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.Title) || !U.IsYmd(t.Date)) continue;
                if (t.Done) t.Skipped = false;
                if (t.Closed && t.CompletedAt == null) t.CompletedAt = U.Now();
                Fix(t);
                list.Add(t);
            }
            return list;
        }
    }

    /* ------------------------------------------------------------------ logic */
    public class TaskService
    {
        public AppData Data;
        bool persist;
        public string LastError;
        public event Action Changed;

        public TaskService(bool persist)
        {
            this.persist = persist;
            Data = persist ? Store.Load() : new AppData();
        }

        public List<TaskItem> Tasks { get { return Data.Tasks; } }
        public AppSettings Settings { get { return Data.Settings; } }

        public void Save()
        {
            if (persist)
            {
                try { Store.Save(Data); LastError = null; }
                catch (Exception ex) { LastError = ex.Message; }
            }
            if (Changed != null) Changed();
        }

        /* A reminder whose time has already passed when the task is created/edited
           should not pop up instantly; if the due time is still ahead, remind at the due time instead. */
        void PrimeReminder(TaskItem t)
        {
            t.SnoozeUntil = null;
            t.NotifiedKey = null;
            if (!t.HasReminder) return;
            DateTime now = DateTime.Now;
            if (t.RemindAt <= now)
                t.NotifiedKey = t.Due > now ? t.ReminderKey + "|atdue" : t.ReminderKey;
        }

        public TaskItem Add(string title, string date, string time, int remind, string repeat, string notes)
        {
            TaskItem t = new TaskItem
            {
                Id = U.NewId(), Title = title.Trim(), Notes = (notes ?? "").Trim(), Date = date, Time = time,
                RemindBefore = remind, Repeat = repeat, AnchorDay = U.ParseYmd(date).Day,
                Remarks = "", CreatedAt = U.Now()
            };
            t.Steps = new List<StepItem>();
            if (t.Repeats) t.SeriesId = t.Id;
            PrimeReminder(t);
            Tasks.Add(t);
            Save();
            return t;
        }

        public void Update(TaskItem t, string title, string date, string time, int remind, string repeat, string notes, string remarks, List<StepItem> steps = null)
        {
            bool timingChanged = t.Date != date || t.Time != time || t.RemindBefore != remind;
            if (t.Date != date) t.AnchorDay = U.ParseYmd(date).Day;
            t.Title = title.Trim(); t.Date = date; t.Time = time; t.RemindBefore = remind;
            t.Notes = (notes ?? "").Trim(); t.Remarks = remarks ?? "";
            if (repeat != "none" && string.IsNullOrEmpty(t.SeriesId)) t.SeriesId = t.Id;
            t.Repeat = U.IsRepeat(repeat) ? repeat : "none";
            if (steps != null) t.Steps = steps;
            if (timingChanged) PrimeReminder(t);
            Save();
        }

        public void SetRemarks(TaskItem t, string remarks) { t.Remarks = remarks ?? ""; Save(); }

        public string SetDone(TaskItem t, bool done) { return SetStatus(t, done ? "done" : "pending"); }

        /* status: "pending" | "done" | "notdone". Closing a repeating task either way schedules the next one. */
        public string SetStatus(TaskItem t, string status)
        {
            string spawned = null;
            t.Done = status == "done";
            t.Skipped = status == "notdone";
            t.CompletedAt = t.Closed ? U.Now() : null;
            if (!t.Closed) t.Archived = false;
            t.SnoozeUntil = null;
            if (t.Closed && t.Repeats && !t.SpawnedNext) spawned = SpawnNext(t);
            Save();
            return spawned;
        }

        string SpawnNext(TaskItem t)
        {
            string next = U.NextRepeat(t.Date, t.Repeat, t.AnchorDay, U.Today);
            if (!Tasks.Any(x => x.SeriesId == t.SeriesId && x.Date == next))
            {
                TaskItem n = t.Clone();
                n.Id = U.NewId(); n.Date = next; n.Done = false; n.Skipped = false; n.Archived = false; n.CompletedAt = null; n.Remarks = "";
                n.CreatedAt = U.Now(); n.SpawnedNext = false;
                foreach (StepItem s in n.Steps) s.Done = false;
                PrimeReminder(n);
                Tasks.Add(n);
            }
            t.SpawnedNext = true;
            return next;
        }

        public void Delete(TaskItem t) { Tasks.Remove(t); Save(); }

        public int ClearHistory() { int n = Tasks.RemoveAll(t => t.Closed); Save(); return n; }

        /* Archive: only done / not-done tasks can be archived. Returns how many were archived. */
        public int Archive(IEnumerable<TaskItem> items)
        {
            int n = 0;
            foreach (TaskItem t in items.ToList()) if (t.Closed && !t.Archived) { t.Archived = true; n++; }
            if (n > 0) Save();
            return n;
        }

        public void Unarchive(TaskItem t) { t.Archived = false; Save(); }

        public int Merge(List<TaskItem> incoming)
        {
            HashSet<string> ids = new HashSet<string>(Tasks.Select(t => t.Id));
            int added = 0;
            foreach (TaskItem t in incoming)
            {
                if (ids.Contains(t.Id)) continue;
                if (!t.Closed && t.NotifiedKey == null && t.HasReminder && t.RemindAt < DateTime.Now) t.NotifiedKey = t.ReminderKey;
                Tasks.Add(t); ids.Add(t.Id); added++;
            }
            Save();
            return added;
        }

        /* Repeating tasks keep coming even if the last copy was never ticked off. */
        public bool Rollover()
        {
            bool changed = false;
            string today = U.Today;
            for (int guard = 0; guard < 36; guard++)
            {
                TaskItem t = Tasks.FirstOrDefault(x => !x.Closed && x.Repeats && !x.SpawnedNext &&
                    string.CompareOrdinal(U.NextRepeat(x.Date, x.Repeat, x.AnchorDay, today), today) <= 0);
                if (t == null) break;
                SpawnNext(t);
                changed = true;
            }
            if (changed) Save();
            return changed;
        }

        public List<TaskItem> CollectDue(DateTime now)
        {
            List<TaskItem> due = new List<TaskItem>();
            foreach (TaskItem t in Tasks)
            {
                if (t.Closed || !t.HasReminder) continue;
                bool fire = false;
                if (!string.IsNullOrEmpty(t.SnoozeUntil))
                {
                    fire = now >= U.ParseStamp(t.SnoozeUntil);
                }
                else if (t.NotifiedKey != t.ReminderKey)
                {
                    DateTime target = t.NotifiedKey == t.ReminderKey + "|atdue" ? t.Due : t.RemindAt;
                    fire = now >= target;
                }
                if (fire)
                {
                    t.NotifiedKey = t.ReminderKey;
                    // left unanswered, the reminder comes back after NagMinutes (Done / Not done / Dismiss stop it)
                    t.SnoozeUntil = Settings.NagMinutes > 0 ? U.Stamp(now.AddMinutes(Settings.NagMinutes)) : null;
                    due.Add(t);
                }
            }
            if (due.Count > 0) Save();
            return due.OrderBy(t => t.Due).ToList();
        }

        /* Reminder card closed with X: no more nagging for this reminder */
        public void Dismiss(TaskItem t) { t.SnoozeUntil = null; Save(); }

        public void SetStep(TaskItem t, StepItem s, bool done) { s.Done = done; Save(); }

        /* Stops saving (used right before removing the app's data folder) */
        public void Detach() { persist = false; }

        public void Snooze(TaskItem t, int minutes)
        {
            t.SnoozeUntil = U.Stamp(DateTime.Now.AddMinutes(minutes));
            Save();
        }
    }
}
