using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _activeProjects = "-";

    [ObservableProperty]
    private string _hoursWeek = "-";

    [ObservableProperty]
    private string _hoursMonth = "-";

    [ObservableProperty]
    private string _revenue = "-";

    [ObservableProperty]
    private bool _unreadOnly = true;

    [ObservableProperty]
    private ObservableCollection<DashboardProjectRow> _recentProjects = new();

    [ObservableProperty]
    private ObservableCollection<NotificationListItem> _notifications = new();

    [RelayCommand]
    private async Task LoadDashboardAsync()
    {
        try
        {
            DashboardData? d = await ApiClient.GetDataAsync<DashboardData>("/api/dashboard");
            if (d != null)
            {
                ActiveProjects = d.ActiveProjects.ToString();
                HoursWeek = d.HoursThisWeek.ToString("N1");
                HoursMonth = d.HoursThisMonth.ToString("N1");
                Revenue = d.TotalRevenue.ToString("N0") + " €";
                RecentProjects = new ObservableCollection<DashboardProjectRow>(d.RecentProjects);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading dashboard data: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadNotificationsAsync()
    {
        try
        {
            string url = $"/api/notifications?unreadOnly={UnreadOnly}&limit=50";
            List<NotificationListItem> items = await ApiClient.GetListAsync<NotificationListItem>(url);
            Notifications = new ObservableCollection<NotificationListItem>(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading notifications: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        try
        {
            await ApiClient.PutAsync("/api/notifications/read-all", "{}");
            await LoadNotificationsAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante la marcatura delle notifiche come lette: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task MarkAsReadAsync(NotificationListItem? notif)
    {
        if (notif == null || notif.IsRead) return;
        try
        {
            await ApiClient.PutAsync($"/api/notifications/{notif.Id}/read", "{}");
            await LoadNotificationsAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante la lettura della notifica: {ex.Message}");
        }
    }

    partial void OnUnreadOnlyChanged(bool value)
    {
        _ = LoadNotificationsAsync();
    }
}
