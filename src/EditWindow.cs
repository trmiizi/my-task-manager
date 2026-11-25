using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MyTaskManager
{
    public static class EditWindow
    {
        public static void Open(Window owner, TaskService svc, TaskItem t) { Open(owner, svc, t, false); }

        /* focusSteps: open with the cursor in "Add a step" (from the "Add checklist" menu item) */
        public static void Open(Window owner, TaskService svc, TaskItem t, bool focusSteps)
        {
            Window w = new Window
            {
                Title = "Edit task", Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = owner, ShowInTaskbar = false,
                Background = Theme.B("Surface"), FontFamily = owner.FontFamily, FontSize = 13.5, UseLayoutRounding = true
            };
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            Theme.TitleBar(w);

            TextBox title = new TextBox { Style = Theme.S("Input"), Text = t.Title, MaxLength = 200, Tag = "Task" };
            DateField date = new DateField();
            date.Value = t.Date;
            ComboBox time = Ui.TimeCombo(null); Ui.SetTime(time, t.Time);
            ComboBox remind = Ui.RemindCombo(null); Ui.SetRemind(remind, t.RemindBefore);
            ComboBox repeat = Ui.RepeatCombo(null); Ui.SetRepeat(repeat, t.Repeat);
            TextBox notes = new TextBox { Style = Theme.S("Multi"), Text = t.Notes ?? "", Tag = "Notes (optional)" };
            TextBox remarks = new TextBox { Style = Theme.S("Multi"), Text = t.Remarks ?? "", Tag = "Remarks" };
            TextBlock error = new TextBlock { Foreground = Theme.B("Danger"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };

            StackPanel root = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
            root.Children.Add(new TextBlock { Text = "Edit task", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 16) });
            root.Children.Add(Spaced(Ui.Field("Task", title)));

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            Place(grid, Spaced(Ui.Field("Date", date.Root)), 0, 0);
            Place(grid, Spaced(Ui.Field("Time", time)), 0, 2);
            Place(grid, Spaced(Ui.Field("Remind me", remind)), 1, 0);
            Place(grid, Spaced(Ui.Field("Repeat", repeat)), 1, 2);
            root.Children.Add(grid);
            root.Children.Add(Spaced(Ui.Field("Notes", notes)));

            // ---- checklist
            List<StepItem> work = (t.Steps ?? new List<StepItem>()).Select(s => new StepItem { Text = s.Text, Done = s.Done }).ToList();
            List<TextBox> stepBoxes = new List<TextBox>();
            StackPanel stepList = new StackPanel();
            TextBox newStep = new TextBox { Style = Theme.S("Input"), Tag = "Add a step, then press Enter", MaxLength = 200 };
            Button addStep = new Button { Style = Theme.S("Btn"), Content = "Add", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 0, 14, 0) };
            System.Action renderSteps = null;
            System.Action syncText = delegate
            {
                for (int i = 0; i < stepBoxes.Count && i < work.Count; i++) work[i].Text = stepBoxes[i].Text;
            };
            renderSteps = delegate
            {
                stepList.Children.Clear();
                stepBoxes.Clear();
                foreach (StepItem s in work)
                {
                    StepItem step = s;
                    Grid row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    CheckBox cb = new CheckBox { Style = Theme.S("StepCheck"), IsChecked = step.Done, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0), ToolTip = "Tick when this step is done" };
                    cb.Click += delegate { step.Done = cb.IsChecked == true; };
                    TextBox tb = new TextBox { Style = Theme.S("Input"), Text = step.Text, MaxLength = 200 };
                    stepBoxes.Add(tb);
                    Button del = Ui.IconButton("", "Remove this step");
                    del.FontSize = 11;
                    del.Click += delegate { syncText(); work.Remove(step); renderSteps(); };
                    Grid.SetColumn(tb, 1); Grid.SetColumn(del, 2);
                    row.Children.Add(cb); row.Children.Add(tb); row.Children.Add(del);
                    stepList.Children.Add(row);
                }
            };
            System.Action commitNew = delegate
            {
                string s = newStep.Text.Trim();
                if (s.Length == 0) return;
                syncText();
                work.Add(new StepItem { Text = s });
                newStep.Text = "";
                renderSteps();
                newStep.Focus();
            };
            newStep.PreviewKeyDown += (o, e) => { if (e.Key == Key.Enter) { commitNew(); e.Handled = true; } };
            addStep.Click += delegate { commitNew(); };
            renderSteps();

            DockPanel addRow = new DockPanel();
            DockPanel.SetDock(addStep, Dock.Right);
            addRow.Children.Add(addStep);
            addRow.Children.Add(newStep);
            StackPanel checklist = new StackPanel();
            checklist.Children.Add(new ScrollViewer { Content = stepList, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
            checklist.Children.Add(addRow);
            root.Children.Add(Spaced(Ui.Field("Checklist (optional)", checklist)));

            if (t.Closed) root.Children.Add(Spaced(Ui.Field("Remarks", remarks)));
            root.Children.Add(error);

            StackPanel foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            Button cancel = new Button { Style = Theme.S("Btn"), Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            Button save = new Button { Style = Theme.S("Primary"), Content = "Save changes", IsDefault = true, Padding = new Thickness(18, 8, 18, 8) };
            foot.Children.Add(cancel);
            foot.Children.Add(save);
            root.Children.Add(foot);
            w.Content = root;

            save.Click += delegate
            {
                string hhmm;
                if (title.Text.Trim().Length == 0) { error.Text = "The task needs a title."; error.Visibility = Visibility.Visible; title.Focus(); return; }
                if (!Ui.ReadTime(time, out hhmm)) { error.Text = "Time not recognised. Try 2:30 PM or 14:30."; error.Visibility = Visibility.Visible; time.Focus(); return; }
                syncText();
                if (newStep.Text.Trim().Length > 0) work.Add(new StepItem { Text = newStep.Text.Trim() });   // typed but not added yet
                List<StepItem> steps = work.Where(s => !string.IsNullOrWhiteSpace(s.Text)).Select(s => new StepItem { Text = s.Text.Trim(), Done = s.Done }).ToList();
                w.DialogResult = true;
                svc.Update(t, title.Text, date.Value, hhmm, Ui.GetRemind(remind), Ui.GetRepeat(repeat), notes.Text, t.Closed ? remarks.Text : t.Remarks, steps);
            };
            w.Loaded += delegate
            {
                if (focusSteps) newStep.Focus();
                else { title.Focus(); title.SelectAll(); }
            };
            w.ShowDialog();
        }

        static FrameworkElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 0, 0, 12); return e; }
        static void Place(Grid g, UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); }
    }
}
