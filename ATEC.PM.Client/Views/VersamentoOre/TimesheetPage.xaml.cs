using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class TimesheetPage : Page
{
    private enum CalView { Month, Week }

    private const int MaxChipsPerDay = 3;
    private const decimal MaxHoursPerDay = 24m;

    private static readonly CultureInfo It = new("it-IT");
    private static readonly string[] DayNames = { "lun", "mar", "mer", "gio", "ven", "sab", "dom" };

    private CalView _view = CalView.Month;
    private DateTime _anchor = DateTime.Today;
    private DateTime _selectedDay = DateTime.Today;
    private int _employeeId;
    private bool _loadingEmp = true;
    private List<TimesheetEntryDto> _entries = new();

    public TimesheetPage()
    {
        InitializeComponent();
        _employeeId = App.UserId;
        StyleToggle();
        Loaded += async (_, _) =>
        {
            await LoadEmployees();
            await Load();
        };
    }

    private static bool IsFutureDate(DateTime day) => day.Date > DateTime.Today;

    private static Brush ResBr(string key) => (Brush)Application.Current.FindResource(key);

    // ── DIPENDENTI (selettore per PM / RESP) ────────────────────────────
    private async Task LoadEmployees()
    {
        if (!App.CurrentUser.IsPm && App.CurrentUser.UserRole != "RESP_REPARTO")
        {
            _loadingEmp = false;
            return;
        }
        try
        {
            List<LookupItem> employees = await ApiClient.GetDataAsync<List<LookupItem>>(
                "/api/timesheet/registrable-employees") ?? new();
            if (employees.Count > 1)
            {
                cmbEmployee.ItemsSource = employees;
                cmbEmployee.SelectedValue = App.UserId;
                cmbEmployee.Visibility = Visibility.Visible;
                lblEmployee.Visibility = Visibility.Visible;
            }
        }
        catch { /* selettore opzionale */ }
        finally { _loadingEmp = false; }
    }

    private void CmbEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEmp || cmbEmployee.SelectedValue is not int id) return;
        _employeeId = id;
        _ = Load();
    }

    // ── INTERVALLO DI DATE per la vista corrente ────────────────────────
    private (DateTime Start, DateTime End) GetRange()
    {
        if (_view == CalView.Week)
        {
            int diff = ((int)_anchor.DayOfWeek + 6) % 7;
            DateTime ws = _anchor.AddDays(-diff).Date;
            return (ws, ws.AddDays(6));
        }
        DateTime first = new(_anchor.Year, _anchor.Month, 1);
        int lead = ((int)first.DayOfWeek + 6) % 7;
        DateTime gridStart = first.AddDays(-lead);
        DateTime lastDay = first.AddMonths(1).AddDays(-1);
        int lastDow = ((int)lastDay.DayOfWeek + 6) % 7;
        DateTime gridEnd = lastDay.AddDays(6 - lastDow);
        return (gridStart, gridEnd);
    }

    // ── CARICAMENTO DATI ────────────────────────────────────────────────
    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            (DateTime start, DateTime end) = GetRange();
            _entries = await ApiClient.GetDataAsync<List<TimesheetEntryDto>>(
                $"/api/timesheet/range?employeeId={_employeeId}&start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}") ?? new();

            UpdateLabel();
            RenderCalendar();

            decimal total = _entries.Sum(e => e.Hours);
            txtStatus.Text = $"{_entries.Count} registrazioni · {total:N1} ore nel periodo";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void UpdateLabel()
    {
        if (_view == CalView.Week)
        {
            (DateTime ws, DateTime we) = GetRange();
            txtLabel.Text = $"{ws.ToString("dd MMM", It)} – {we.ToString("dd MMM yyyy", It)}";
        }
        else
        {
            string s = _anchor.ToString("MMMM yyyy", It);
            txtLabel.Text = char.ToUpper(s[0]) + s[1..];
        }
    }

    private decimal SumHoursOnDay(DateTime date, int excludeId)
    {
        return _entries
            .Where(e => e.WorkDate.Date == date.Date && e.Id != excludeId)
            .Sum(e => e.Hours);
    }

    private void SelectDay(DateTime day)
    {
        _selectedDay = day.Date;
        RenderCalendar();
    }

    // ── RENDER CALENDARIO ───────────────────────────────────────────────
    private void RenderCalendar()
    {
        CalendarRoot.Children.Clear();
        CalendarRoot.RowDefinitions.Clear();
        CalendarRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        CalendarRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        if (_view == CalView.Week) BuildWeek();
        else BuildMonth();
    }

    private static Grid SevenColGrid()
    {
        Grid g = new();
        for (int i = 0; i < 7; i++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    private void BuildMonth()
    {
        (DateTime start, DateTime end) = GetRange();
        int totalDays = (end - start).Days + 1;
        int weeks = totalDays / 7;

        Grid header = SevenColGrid();
        for (int i = 0; i < 7; i++)
        {
            Border hb = new()
            {
                Background = ResBr("Gray50Brush"),
                BorderBrush = ResBr("Gray200Brush"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 6, 8, 6)
            };
            hb.Child = new TextBlock
            {
                Text = DayNames[i],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResBr("Gray500Brush"),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(hb, i);
            header.Children.Add(hb);
        }
        Grid.SetRow(header, 0);
        CalendarRoot.Children.Add(header);

        Grid body = SevenColGrid();
        for (int r = 0; r < weeks; r++)
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int d = 0; d < totalDays; d++)
        {
            DateTime day = start.AddDays(d);
            bool inMonth = day.Month == _anchor.Month;
            Border cell = CreateDayCell(day, inMonth, showDayNumber: true);
            Grid.SetRow(cell, d / 7);
            Grid.SetColumn(cell, d % 7);
            body.Children.Add(cell);
        }
        Grid.SetRow(body, 1);
        CalendarRoot.Children.Add(body);
    }

    private void BuildWeek()
    {
        (DateTime start, _) = GetRange();

        Grid header = SevenColGrid();
        for (int i = 0; i < 7; i++)
        {
            DateTime day = start.AddDays(i);
            bool today = day.Date == DateTime.Today;
            Border hb = new()
            {
                Background = today ? ResBr("InfoBgBrush") : ResBr("Gray50Brush"),
                BorderBrush = ResBr("Gray200Brush"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 6, 8, 6)
            };
            hb.Child = new TextBlock
            {
                Text = $"{DayNames[i]} {day.Day}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = today ? ResBr("PrimaryBrush") : ResBr("Gray700Brush")
            };
            Grid.SetColumn(hb, i);
            header.Children.Add(hb);
        }
        Grid.SetRow(header, 0);
        CalendarRoot.Children.Add(header);

        Grid body = SevenColGrid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 7; i++)
        {
            DateTime day = start.AddDays(i);
            Border cell = CreateDayCell(day, inMonth: true, showDayNumber: false);
            Grid.SetColumn(cell, i);
            body.Children.Add(cell);
        }
        Grid.SetRow(body, 1);
        CalendarRoot.Children.Add(body);
    }

    // ── CELLA GIORNO ────────────────────────────────────────────────────
    private Border CreateDayCell(DateTime day, bool inMonth, bool showDayNumber)
    {
        bool today = day.Date == DateTime.Today;
        bool selected = day.Date == _selectedDay.Date;
        bool future = IsFutureDate(day);

        Brush bg = future
            ? ResBr("Gray100Brush")
            : today
                ? ResBr("InfoBgBrush")
                : selected
                    ? ResBr("PrimarySubtleBrush")
                    : inMonth
                        ? ResBr("WhiteBrush")
                        : ResBr("Gray50Brush");

        Border cell = new()
        {
            Background = bg,
            BorderBrush = selected ? ResBr("PrimaryBrush") : ResBr("Gray200Brush"),
            BorderThickness = selected ? new Thickness(2) : new Thickness(0, 0, 1, 1),
            Cursor = future ? Cursors.Arrow : Cursors.Hand
        };

        DockPanel layout = new() { Margin = new Thickness(4, 3, 4, 4), LastChildFill = true };

        List<TimesheetEntryDto> dayEntries = _entries.Where(x => x.WorkDate.Date == day.Date).ToList();
        decimal dayTotal = dayEntries.Sum(x => x.Hours);

        if (showDayNumber)
        {
            Grid headerRow = new();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock num = new()
            {
                Text = day.Day.ToString(),
                FontSize = 12,
                FontWeight = today || selected ? FontWeights.Bold : FontWeights.Normal,
                Foreground = future
                    ? ResBr("Gray300Brush")
                    : today || selected
                        ? ResBr("PrimaryBrush")
                        : inMonth
                            ? ResBr("Gray700Brush")
                            : ResBr("Gray500Brush")
            };
            Grid.SetColumn(num, 0);
            headerRow.Children.Add(num);

            if (dayTotal > 0)
                headerRow.Children.Add(HoursBadge(dayTotal, 1));

            DockPanel.SetDock(headerRow, Dock.Top);
            layout.Children.Add(headerRow);
        }
        else if (dayTotal > 0)
        {
            Grid topBar = new() { Margin = new Thickness(0, 0, 0, 2) };
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topBar.Children.Add(HoursBadge(dayTotal, 1));
            DockPanel.SetDock(topBar, Dock.Top);
            layout.Children.Add(topBar);
        }

        StackPanel list = new();
        int shown = 0;
        foreach (TimesheetEntryDto en in dayEntries)
        {
            if (shown >= MaxChipsPerDay) break;
            list.Children.Add(CreateEntryChip(en));
            shown++;
        }
        if (dayEntries.Count > MaxChipsPerDay)
        {
            int more = dayEntries.Count - MaxChipsPerDay;
            list.Children.Add(new TextBlock
            {
                Text = $"+{more} altre",
                FontSize = 10,
                Foreground = ResBr("Gray500Brush"),
                Margin = new Thickness(0, 2, 0, 0),
                FontStyle = FontStyles.Italic
            });
        }

        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list,
            Margin = new Thickness(0, 2, 0, 0)
        };
        layout.Children.Add(scroll);
        cell.Child = layout;

        void OnDayClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                SelectDay(day);
        }

        void OnDayDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (future)
            {
                txtStatus.Text = "Non è possibile registrare ore in date future.";
                e.Handled = true;
                return;
            }
            SelectDay(day);
            OpenAdd(day);
            e.Handled = true;
        }

        cell.MouseLeftButtonDown += OnDayClick;
        cell.AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(OnDayDoubleClick));
        layout.AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(OnDayDoubleClick));
        scroll.MouseDoubleClick += OnDayDoubleClick;

        return cell;
    }

    private static UIElement HoursBadge(decimal hours, int column)
    {
        Border badge = new()
        {
            Background = ResBr("PrimarySubtleBrush"),
            Padding = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = $"{hours:0.#}h",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResBr("PrimaryDarkBrush")
            }
        };
        Grid.SetColumn(badge, column);
        return badge;
    }

    // ── CHIP VOCE ───────────────────────────────────────────────────────
    private Border CreateEntryChip(TimesheetEntryDto entry)
    {
        (Brush bg, Brush fg) = TypeColors(entry.EntryType);

        Border chip = new()
        {
            Background = bg,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
            ToolTip = $"{entry.PhaseDisplay}\n{entry.Hours:0.#} ore · {TypeLabel(entry.EntryType)}"
                      + (string.IsNullOrWhiteSpace(entry.Notes) ? "" : $"\n{entry.Notes}")
        };
        chip.Child = new TextBlock
        {
            Text = $"{entry.Hours:0.#}h  {entry.PhaseDisplay}",
            FontSize = 11,
            Foreground = fg,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        chip.MouseLeftButtonUp += (_, e) => { OpenEdit(entry); e.Handled = true; };
        chip.AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler((_, e) =>
        {
            OpenEdit(entry);
            e.Handled = true;
        }));

        ContextMenu cm = new();
        MenuItem miEdit = new() { Header = "Modifica" };
        miEdit.Click += (_, _) => OpenEdit(entry);
        MenuItem miDel = new() { Header = "Elimina", Foreground = ResBr("DangerBrush") };
        miDel.Click += async (_, _) => await DeleteEntry(entry);
        cm.Items.Add(miEdit);
        cm.Items.Add(miDel);
        ATEC.PM.Client.Helpers.ShadcnMenuHelper.ApplyDark(cm);
        chip.ContextMenu = cm;

        return chip;
    }

    private static (Brush bg, Brush fg) TypeColors(string type) => type switch
    {
        "OVERTIME" => (ResBr("WarningBgBrush"), ResBr("WarningBrush")),
        "TRAVEL" => (ResBr("SuccessBgBrush"), ResBr("SuccessBrush")),
        _ => (ResBr("InfoBgBrush"), ResBr("InfoBrush")),
    };

    private static string TypeLabel(string type) => type switch
    {
        "OVERTIME" => "Straordinario",
        "TRAVEL" => "Viaggio",
        _ => "Ordinario",
    };

    // ── AZIONI ──────────────────────────────────────────────────────────
    private void OpenAdd(DateTime date)
    {
        if (IsFutureDate(date))
        {
            txtStatus.Text = "Non è possibile registrare ore in date future.";
            return;
        }
        decimal otherHours = SumHoursOnDay(date, excludeId: 0);
        if (otherHours >= MaxHoursPerDay)
        {
            txtStatus.Text = $"Il giorno {date:dd/MM/yyyy} ha già {otherHours:N1}h registrate (massimo {MaxHoursPerDay:N0}h).";
            return;
        }

        TimesheetEntryDialog dlg = new(date, null, _employeeId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void OpenEdit(TimesheetEntryDto entry)
    {
        // Modifica sempre consentita per oggi e giorni passati (click su chip o menu contestuale).
        if (IsFutureDate(entry.WorkDate))
        {
            txtStatus.Text = "Questa registrazione ha data futura: aprila per correggerla a oggi o a un giorno passato.";
        }

        TimesheetEntryDialog dlg = new(entry.WorkDate, entry, _employeeId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private async Task DeleteEntry(TimesheetEntryDto entry)
    {
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare la registrazione di {entry.Hours:N1}h del {entry.WorkDate:dd/MM/yyyy}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            string result = await ApiClient.DeleteAsync($"/api/timesheet/{entry.Id}");
            if (ApiClient.IsApiSuccess(result, out _)) _ = Load();
            else ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Errore durante l'eliminazione.");
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ── NAVIGAZIONE / VISTA ─────────────────────────────────────────────
    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        _anchor = _view == CalView.Week ? _anchor.AddDays(-7) : _anchor.AddMonths(-1);
        _ = Load();
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        _anchor = _view == CalView.Week ? _anchor.AddDays(7) : _anchor.AddMonths(1);
        _ = Load();
    }

    private void BtnToday_Click(object sender, RoutedEventArgs e)
    {
        _anchor = DateTime.Today;
        _selectedDay = DateTime.Today;
        _ = Load();
    }

    private void BtnMonth_Click(object sender, RoutedEventArgs e) { SetView(CalView.Month); }
    private void BtnWeek_Click(object sender, RoutedEventArgs e) { SetView(CalView.Week); }

    private void SetView(CalView v)
    {
        if (_view == v) return;
        _view = v;
        StyleToggle();
        _ = Load();
    }

    private void StyleToggle()
    {
        bool month = _view == CalView.Month;
        btnMonth.Background = month ? ResBr("WhiteBrush") : Brushes.Transparent;
        btnMonth.Foreground = month ? ResBr("PrimaryBrush") : ResBr("Gray500Brush");
        btnMonth.FontWeight = month ? FontWeights.SemiBold : FontWeights.Normal;
        btnWeek.Background = month ? Brushes.Transparent : ResBr("WhiteBrush");
        btnWeek.Foreground = month ? ResBr("Gray500Brush") : ResBr("PrimaryBrush");
        btnWeek.FontWeight = month ? FontWeights.Normal : FontWeights.SemiBold;
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e) => OpenAdd(_selectedDay);
}
