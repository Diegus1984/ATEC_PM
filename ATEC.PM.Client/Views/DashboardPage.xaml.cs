using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

// ── CONVERTERS ────────────────────────────────────────────

public class NotifTypeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value?.ToString() ?? "") switch
    {
        "DDP_STATUS_CHANGED" => "DDP",
        "DDP_OVERDUE" => "SCAD.",
        "PHASE_ASSIGNED" => "FASE",
        "TIMESHEET_ANOMALY" => "ORE",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public class ReadCheckConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? "✓" : "○";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public class SeverityIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => (value?.ToString() ?? "") switch
    {
        "ALARM" => "⚠",
        "WARNING" => "⚡",
        "SUCCESS" => "✓",
        _ => "ℹ"
    };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
// ── PAGE ──────────────────────────────────────────────────

public partial class DashboardPage : Page
{
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };
    private DispatcherTimer? _notifTimer;

    public DashboardPage()
    {
        InitializeComponent();
        ApplyAlarmRowStyle();
        chkUnreadOnly.IsChecked = true;
        Loaded += async (_, _) =>
        {
            await LoadDashboard();
            await LoadNotifications();
            StartNotifPolling();
        };
        Unloaded += (_, _) => _notifTimer?.Stop();
    }

    private static ControlTemplate CreateCellTemplate()
    {
        var template = new ControlTemplate(typeof(DataGridCell));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static DataTrigger MakeSeverityTrigger(string severity, string bgHex, string fgHex)
    {
        var trigger = new DataTrigger { Binding = new Binding("Severity"), Value = severity };
        var bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)); bg.Freeze();
        var fg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)); fg.Freeze();
        trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, bg));
        trigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, fg));
        return trigger;
    }

    private void ApplyAlarmRowStyle()
    {
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(DataGridRow.FontSizeProperty, 12.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 36.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        rowStyle.Triggers.Add(MakeSeverityTrigger("ALARM", "#DC2626", "#FFFFFF"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("WARNING", "#F59E0B", "#000000"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("INFO", "#2E90FA", "#FFFFFF"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("SUCCESS", "#12B76A", "#FFFFFF"));

        // Righe lette → sfondo più tenue
        var readTrigger = new DataTrigger { Binding = new Binding("IsRead"), Value = true };
        readTrigger.Setters.Add(new Setter(DataGridRow.OpacityProperty, 0.55));
        rowStyle.Triggers.Add(readTrigger);

        dgNotifications.RowStyle = rowStyle;

        // CellStyle senza bordi
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, (Style?)null));
        cellStyle.Setters.Add(new Setter(DataGridCell.TemplateProperty, CreateCellTemplate()));
        dgNotifications.CellStyle = cellStyle;
    }

    private async void BtnMarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ApiClient.PutAsync("/api/notifications/read-all", "{}");
            await LoadNotifications();
        }
        catch { }
    }

    private async void ChkUnreadOnly_Changed(object sender, RoutedEventArgs e)
    {
        await LoadNotifications();
    }

    private async void DgNotifications_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgNotifications.SelectedItem is not NotificationListItem notif) return;

        if (!notif.IsRead)
        {
            try
            {
                await ApiClient.PutAsync($"/api/notifications/{notif.Id}/read", "{}");
                await LoadNotifications();
            }
            catch { }
        }

        // TODO: navigare alla commessa/DDP relativa
    }

    private async Task LoadDashboard()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/dashboard");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var d = doc.RootElement.GetProperty("data");
                txtActiveProjects.Text = d.GetProperty("activeProjects").GetInt32().ToString();
                txtHoursWeek.Text = d.GetProperty("hoursThisWeek").GetDecimal().ToString("N1");
                txtHoursMonth.Text = d.GetProperty("hoursThisMonth").GetDecimal().ToString("N1");
                txtRevenue.Text = d.GetProperty("totalRevenue").GetDecimal().ToString("N0") + " €";

                if (d.TryGetProperty("recentProjects", out var rp))
                {
                    var projects = JsonSerializer.Deserialize<List<DashboardProjectRow>>(rp.GetRawText(), _jsonOpt) ?? new();
                    dgRecent.ItemsSource = projects;
                }
            }
        }
        catch { }
    }

    // ── ALARM ROW STYLE (colori per severità) ─────────────────
    // ── LOAD DATA ─────────────────────────────────────────────
    private async Task LoadNotifications()
    {
        try
        {
            bool unreadOnly = chkUnreadOnly.IsChecked == true;
            string url = $"/api/notifications?unreadOnly={unreadOnly}&limit=50";
            string json = await ApiClient.GetAsync(url);
            var response = JsonSerializer.Deserialize<ApiResponse<List<NotificationListItem>>>(json, _jsonOpt);

            if (response?.Success == true)
                dgNotifications.ItemsSource = response.Data ?? new();
        }
        catch { }
    }

    private void StartNotifPolling()
    {
        _notifTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _notifTimer.Tick += async (_, _) => await LoadNotifications();
        _notifTimer.Start();
    }
    // ── AZIONI NOTIFICHE ──────────────────────────────────────
}
