using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
    private readonly DashboardViewModel _vm = new();
    private NotificationPollingService? _notifPolling;

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = _vm;
        ApplyAlarmRowStyle();
        Loaded += async (_, _) =>
        {
            await _vm.LoadDashboardCommand.ExecuteAsync(null);
            await _vm.LoadNotificationsCommand.ExecuteAsync(null);
            StartNotifPolling();
        };
        Unloaded += (_, _) => _notifPolling?.Stop();
    }

    private static ControlTemplate CreateCellTemplate()
    {
        ControlTemplate template = new ControlTemplate(typeof(DataGridCell));
        FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static DataTrigger MakeSeverityTrigger(string severity, string bgHex, string fgHex)
    {
        DataTrigger trigger = new DataTrigger { Binding = new Binding("Severity"), Value = severity };
        SolidColorBrush bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgHex)); bg.Freeze();
        SolidColorBrush fg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgHex)); fg.Freeze();
        trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, bg));
        trigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, fg));
        return trigger;
    }

    private void ApplyAlarmRowStyle()
    {
        Style rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(DataGridRow.FontSizeProperty, 12.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 36.0));
        rowStyle.Setters.Add(new Setter(DataGridRow.VerticalContentAlignmentProperty, VerticalAlignment.Center));

        rowStyle.Triggers.Add(MakeSeverityTrigger("ALARM", "#DC2626", "#FFFFFF"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("WARNING", "#F59E0B", "#000000"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("INFO", "#2E90FA", "#FFFFFF"));
        rowStyle.Triggers.Add(MakeSeverityTrigger("SUCCESS", "#12B76A", "#FFFFFF"));

        // Righe lette → sfondo più tenue
        DataTrigger readTrigger = new DataTrigger { Binding = new Binding("IsRead"), Value = true };
        readTrigger.Setters.Add(new Setter(DataGridRow.OpacityProperty, 0.55));
        rowStyle.Triggers.Add(readTrigger);

        dgNotifications.RowStyle = rowStyle;

        // CellStyle senza bordi
        Style cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, (Style?)null));
        cellStyle.Setters.Add(new Setter(DataGridCell.TemplateProperty, CreateCellTemplate()));
        dgNotifications.CellStyle = cellStyle;
    }

    private async void DgNotifications_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgNotifications.SelectedItem is not NotificationListItem notif) return;

        await _vm.MarkAsReadCommand.ExecuteAsync(notif);

        if (notif.ProjectId.HasValue && notif.ProjectId > 0)
        {
            MainWindow? mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateToProject(notif.ProjectId.Value, notif.ReferenceType);
        }
    }

    private void StartNotifPolling()
    {
        _notifPolling = new NotificationPollingService(async () =>
        {
            await _vm.LoadNotificationsCommand.ExecuteAsync(null);
            int unreadCount = 0;
            foreach (NotificationListItem n in _vm.Notifications)
            {
                if (!n.IsRead) unreadCount++;
            }
            return unreadCount;
        });
        _notifPolling.Start();
    }

    private async void BtnGoToReference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not NotificationListItem notif) return;

        await _vm.MarkAsReadCommand.ExecuteAsync(notif);

        if (notif.ProjectId.HasValue && notif.ProjectId > 0)
        {
            MainWindow? mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.NavigateToProject(notif.ProjectId.Value, notif.ReferenceType);
        }
    }
}
