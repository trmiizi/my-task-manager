using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace MyTaskManager
{
    /* Shows reminder cards stacked in the bottom-right corner of the screen. */
    public class ReminderCenter
    {
        readonly TaskService svc;
        readonly List<Window> open = new List<Window>();
        readonly Dictionary<string, Window> byTask = new Dictionary<string, Window>();
        Window summary;
        public Action OpenMain;
        const int MaxCards = 4;

        public ReminderCenter(TaskService svc) { this.svc = svc; }

        public void Show(List<TaskItem> due, bool forceSound)
        {
            if (due.Count == 0) return;
            // a reminder coming back (ignored earlier) replaces its old card instead of stacking a second one
            foreach (TaskItem t in due)
            {
                Window old;
                if (byTask.TryGetValue(t.Id, out old)) { byTask.Remove(t.Id); old.Close(); }
            }
            if (summary != null) { summary.Close(); summary = null; }
            int slots = Math.Max(1, MaxCards - open.Count);
            if (due.Count <= slots) foreach (TaskItem t in due) Card(t);
            else
            {
                foreach (TaskItem t in due.Take(slots - 1)) Card(t);
                Summary(due.Skip(slots - 1).ToList());
            }
            if (svc.Settings.Sound || forceSound) PlaySound();
        }

        public void ShowTest()
        {
            Window w = Build(null, null, "Reminders are working", "They'll look like this.", "", false);
            Button ok = (Button)w.FindName("DoneBtn");
            ok.Content = "Got it";
            ((Button)w.FindName("NotDoneBtn")).Visibility = Visibility.Collapsed;
            ok.Click += delegate { w.Close(); };
            ((Button)w.FindName("Snooze1")).Visibility = Visibility.Collapsed;
            ((Button)w.FindName("Snooze2")).Visibility = Visibility.Collapsed;
            Present(w);
            PlaySound();
        }

        void Card(TaskItem t)
        {
            Window w = MakeCard(t);
            byTask[t.Id] = w;
            w.Closed += delegate { Window cur; if (byTask.TryGetValue(t.Id, out cur) && cur == w) byTask.Remove(t.Id); };
            Present(w);
        }

        public Window MakeCard(TaskItem t)
        {
            DateTime now = DateTime.Now;
            bool late = t.Due < now.AddMinutes(-1);
            // big time on the left ("3:00" over "PM"), day + relative time under the title
            string time = U.FmtTime(t.Time);
            int sp = time.LastIndexOf(' ');
            string hm = sp > 0 ? time.Substring(0, sp) : time, ap = sp > 0 ? time.Substring(sp + 1) : "";
            string fd = U.FmtDate(t.Date);
            string rel = fd == "Today" ? U.Rel(t.Due) : fd + " · " + U.Rel(t.Due);
            Window w = Build(hm, ap, t.Title, rel, t.Notes, late);

            ((Button)w.FindName("DoneBtn")).Click += delegate
            {
                TaskItem live = svc.Tasks.FirstOrDefault(x => x.Id == t.Id);
                if (live != null && !live.Closed) svc.SetStatus(live, "done");
                w.Close();
            };
            ((Button)w.FindName("NotDoneBtn")).Click += delegate
            {
                TaskItem live = svc.Tasks.FirstOrDefault(x => x.Id == t.Id);
                if (live != null && !live.Closed) svc.SetStatus(live, "notdone");
                w.Close();
            };
            ((Button)w.FindName("Snooze1")).Click += delegate { Snooze(t, 10); w.Close(); };
            ((Button)w.FindName("Snooze2")).Click += delegate { Snooze(t, 60); w.Close(); };
            ((FrameworkElement)w.FindName("TitleText")).MouseLeftButtonUp += delegate { if (OpenMain != null) OpenMain(); };
            Button close = (Button)w.FindName("CloseBtn");
            close.ToolTip = svc.Settings.NagMinutes > 0 ? "Dismiss (stops reminding me about this one)" : "Dismiss";
            close.Click += delegate
            {
                TaskItem live = svc.Tasks.FirstOrDefault(x => x.Id == t.Id);
                if (live != null && !live.Closed) svc.Dismiss(live);
            };
            if (svc.Settings.NagMinutes > 0)
            {
                TextBlock nag = (TextBlock)w.FindName("NagText");
                nag.Text = "No answer? This comes back in " + svc.Settings.NagMinutes + " min.";
                nag.Visibility = Visibility.Visible;
            }
            return w;
        }

        void Snooze(TaskItem t, int minutes)
        {
            TaskItem live = svc.Tasks.FirstOrDefault(x => x.Id == t.Id);
            if (live != null && !live.Closed) svc.Snooze(live, minutes);
        }

        void Summary(List<TaskItem> rest)
        {
            string lines = string.Join("\n", rest.Take(5).Select(t => "•  " + t.Title + "  (" + U.FmtDate(t.Date) + ", " + U.FmtTime(t.Time) + ")"));
            if (rest.Count > 5) lines += "\n…and " + (rest.Count - 5) + " more";
            Window w = summary = Build(null, null, rest.Count + " more task" + (rest.Count == 1 ? " is" : "s are") + " due", "", lines, true);
            w.Closed += delegate { if (summary == w) summary = null; };
            Button open = (Button)w.FindName("DoneBtn");
            open.Content = "Open My Task Manager";
            ((Button)w.FindName("NotDoneBtn")).Visibility = Visibility.Collapsed;
            open.Click += delegate { if (OpenMain != null) OpenMain(); w.Close(); };
            ((Button)w.FindName("Snooze1")).Visibility = Visibility.Collapsed;
            ((Button)w.FindName("Snooze2")).Visibility = Visibility.Collapsed;
            Present(w);
        }

        /* hm/ap = the due time for the left column ("3:00", "PM"); null shows a bell instead (test card, summary) */
        Window Build(string hm, string ap, string title, string rel, string notes, bool late)
        {
            Window w = (Window)XamlReader.Parse(Xaml);
            TextBlock hmText = (TextBlock)w.FindName("HmText"), apText = (TextBlock)w.FindName("ApText");
            if (hm == null)
            {
                hmText.Visibility = Visibility.Collapsed; apText.Visibility = Visibility.Collapsed;
                ((TextBlock)w.FindName("BellIcon")).Visibility = Visibility.Visible;
            }
            else
            {
                hmText.Text = hm; apText.Text = ap;
                if (late) { hmText.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); apText.SetResourceReference(TextBlock.ForegroundProperty, "Danger"); }
            }
            ((TextBlock)w.FindName("TitleText")).Text = title;
            TextBlock dueText = (TextBlock)w.FindName("DueText");
            dueText.Text = rel;
            if (late) dueText.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            if (string.IsNullOrEmpty(rel)) dueText.Visibility = Visibility.Collapsed;
            TextBlock notesText = (TextBlock)w.FindName("NotesText");
            notesText.Text = notes ?? "";
            if (string.IsNullOrEmpty(notes)) notesText.Visibility = Visibility.Collapsed;
            ((Button)w.FindName("CloseBtn")).Click += delegate { w.Close(); };
            return w;
        }

        void Present(Window w)
        {
            w.Closed += delegate { open.Remove(w); Layout(); };
            w.SizeChanged += delegate { Layout(); };
            open.Add(w);
            w.Left = -10000; w.Top = -10000;
            w.Show();
            Layout();
        }

        public void Layout()
        {
            Rect wa = SystemParameters.WorkArea;
            double y = wa.Bottom - 6;
            foreach (Window w in open)
            {
                y -= w.ActualHeight;
                w.Left = wa.Right - w.ActualWidth - 6;
                w.Top = Math.Max(wa.Top, y);
            }
        }

        static void PlaySound()
        {
            try
            {
                string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Notify Calendar.wav");
                if (File.Exists(wav)) new SoundPlayer(wav).Play();
                else SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        WindowStyle='None' AllowsTransparency='True' Background='Transparent' Topmost='True' ShowInTaskbar='False'
        ShowActivated='False' ResizeMode='NoResize' SizeToContent='Height' Width='500' FontFamily='Segoe UI' FontSize='13.5'
        Foreground='{DynamicResource Text}' UseLayoutRounding='True' TextOptions.TextFormattingMode='Display' Title='Reminder'>
  <Border Margin='12' Background='{DynamicResource Surface}' CornerRadius='10' BorderBrush='{DynamicResource Line}' BorderThickness='1'>
    <Border.Effect><DropShadowEffect BlurRadius='24' ShadowDepth='6' Direction='270' Opacity='0.22'/></Border.Effect>
    <Grid>
      <Grid.ColumnDefinitions><ColumnDefinition Width='92'/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Border Background='{DynamicResource Surface2}' CornerRadius='9,0,0,9'>
        <StackPanel VerticalAlignment='Center' HorizontalAlignment='Center'>
          <TextBlock x:Name='HmText' FontSize='28' FontWeight='SemiBold' HorizontalAlignment='Center' Typography.NumeralAlignment='Tabular'/>
          <TextBlock x:Name='ApText' FontSize='12' FontWeight='SemiBold' Foreground='{DynamicResource Text2}' HorizontalAlignment='Center' Margin='0,1,0,0'/>
          <TextBlock x:Name='BellIcon' Text='&#xEA8F;' FontFamily='{StaticResource IconFont}' FontSize='22' Foreground='{DynamicResource Text2}' HorizontalAlignment='Center' Visibility='Collapsed'/>
        </StackPanel>
      </Border>
      <StackPanel Grid.Column='1' Margin='16,10,8,14'>
        <DockPanel>
          <Button x:Name='CloseBtn' DockPanel.Dock='Right' VerticalAlignment='Top' Style='{StaticResource Icon}' Content='&#xE711;' FontSize='10' Width='28' Height='28' Foreground='{DynamicResource Text3}' ToolTip='Dismiss'/>
          <TextBlock x:Name='TitleText' FontSize='16' FontWeight='SemiBold' TextWrapping='Wrap' Margin='0,4,6,0' Cursor='Hand' ToolTip='Open My Task Manager'/>
        </DockPanel>
        <TextBlock x:Name='DueText' Foreground='{DynamicResource Text2}' TextWrapping='Wrap' Margin='0,1,8,0'/>
        <TextBlock x:Name='NotesText' Foreground='{DynamicResource Text2}' TextWrapping='Wrap' Margin='0,2,8,0'/>
        <WrapPanel Margin='0,12,0,0'>
          <Button x:Name='DoneBtn' Style='{StaticResource Ink}' Margin='0,0,6,0' Padding='11,6' Content='Done'/>
          <Button x:Name='Snooze1' Style='{StaticResource Btn}' Content='Snooze 10 min' Margin='0,0,6,0' Padding='11,6'/>
          <Button x:Name='Snooze2' Style='{StaticResource Btn}' Content='1 hour' Margin='0,0,16,0' Padding='11,6'/>
          <!-- Not done is final, so it sits apart from Done to avoid a slip of the mouse -->
          <Button x:Name='NotDoneBtn' Style='{StaticResource Btn}' Padding='11,6' Content='Not done'/>
        </WrapPanel>
        <TextBlock x:Name='NagText' FontSize='11.5' Foreground='{DynamicResource Text3}' Margin='0,8,0,0' Visibility='Collapsed'/>
      </StackPanel>
    </Grid>
  </Border>
</Window>";
    }
}
