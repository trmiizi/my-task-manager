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
