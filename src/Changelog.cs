namespace MyTaskManager
{
    /* What the "What's new" page shows. Add the new version at the TOP before each release.
       Write it for the person using the app: what they can now do, in plain words. */
    public static class Changelog
    {
        public class Entry
        {
            public string Version, Date;
            public string[] Items;
            public Entry(string version, string date, params string[] items) { Version = version; Date = date; Items = items; }
        }

        public static readonly Entry[] All =
        {
            new Entry("2.1.5", "2026-11-25",
                "Adding a task on the sticky note is easier: click \"+ Add a task\", then tap a day (Today, Tomorrow or another day) and a time (9am, 12pm, 3pm, 6pm or any time). Typing \"Call Ali esok 3pm\" still works.",
                "Tasks whose time has passed today now show under Overdue, in the app and on the note.",
                "Easier-to-read grey text and labels, and a clear outline when you move around with the Tab key.",
                "The header button is now \"Switch to sticky note\". On reminders, Not done sits apart from Done so it's harder to click by mistake."),
            new Entry("2.1.4", "2026-11-25",
                "New look for reminder pop-ups: the time on the left, Done and Snooze on the right.",
                "Other pop-ups match it. Exit now offers Keep running, so reminders don't stop by mistake."),
            new Entry("2.1.3", "2026-11-25",
                "Settings are shorter and easier to scan."),
            new Entry("2.1.2", "2026-11-25",
                "New app icon: a yellow sticky note with a tick.",
                "This What's new page, so you can see what changed after each update."),
            new Entry("2.1.1", "2026-11-25",
                "The app only updates when you ask: Settings & backup > Check for updates, then click Update. It never checks by itself."),
            new Entry("2.1.0", "2026-11-25",
                "The app can update itself from Settings & backup.",
                "Archive finished tasks to tidy up History. Find them again under History > Archived.",
                "Right-click menus now follow dark mode.",
                "Resize the sticky note from any edge or corner."),
        };
    }
}
