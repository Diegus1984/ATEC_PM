using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class BackupPage : Page
{
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    public BackupPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadBackups();
    }

    // ── CARICA LISTA BACKUP ─────────────────────────────────────

    private async System.Threading.Tasks.Task LoadBackups()
    {
        txtFooter.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/backup/list");
            var response = JsonSerializer.Deserialize<ApiResponse<List<BackupFileInfo>>>(json, _jsonOpt);

            if (response?.Success == true && response.Data != null)
            {
                dgBackups.ItemsSource = response.Data;
                txtFooter.Text = $"{response.Data.Count} backup disponibili";
            }
            else
            {
                txtFooter.Text = "Nessun backup trovato";
            }
        }
        catch (Exception ex)
        {
            txtFooter.Text = $"Errore: {ex.Message}";
        }
    }

    // ── ESEGUI BACKUP ───────────────────────────────────────────

    private async void BtnBackupNow_Click(object sender, RoutedEventArgs e)
    {
        btnBackupNow.IsEnabled = false;
        txtStatus.Text = "Backup in corso...";
        txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F79009"));

        try
        {
            string json = await ApiClient.PostAsync("/api/backup/now", "{}");
            var response = JsonSerializer.Deserialize<ApiResponse<string>>(json, _jsonOpt);

            if (response?.Success == true)
            {
                txtStatus.Text = $"✓ {response.Message}";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
                await LoadBackups();
            }
            else
            {
                txtStatus.Text = $"✗ {response?.Message ?? "Errore"}";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"✗ Errore: {ex.Message}";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));
        }
        finally
        {
            btnBackupNow.IsEnabled = true;
        }
    }

    // ── RIPRISTINA ──────────────────────────────────────────────

    private async void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fileName) return;

        var result = MessageBox.Show(
            $"Sei sicuro di voler ripristinare il backup:\n\n{fileName}\n\n" +
            "ATTENZIONE: tutti i dati attuali verranno sovrascritti!\n" +
            "Un backup di sicurezza verrà creato automaticamente prima del ripristino.",
            "Conferma Ripristino",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // Seconda conferma
        var result2 = MessageBox.Show(
            "Confermi DEFINITIVAMENTE il ripristino?\n\nQuesta operazione non può essere annullata.",
            "Ultima Conferma",
            MessageBoxButton.YesNo,
            MessageBoxImage.Exclamation);

        if (result2 != MessageBoxResult.Yes) return;

        txtStatus.Text = "Ripristino in corso...";
        txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F79009"));
        btnBackupNow.IsEnabled = false;

        try
        {
            string json = await ApiClient.PostAsync($"/api/backup/restore/{fileName}", "{}");
            var response = JsonSerializer.Deserialize<ApiResponse<string>>(json, _jsonOpt);

            if (response?.Success == true)
            {
                txtStatus.Text = $"✓ {response.Message}";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));

                MessageBox.Show(
                    $"{response.Message}\n\nBackup di sicurezza salvato in:\n{response.Data}",
                    "Ripristino Completato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await LoadBackups();
            }
            else
            {
                txtStatus.Text = $"✗ {response?.Message ?? "Errore"}";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));

                MessageBox.Show(
                    response?.Message ?? "Errore durante il ripristino",
                    "Errore",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"✗ Errore: {ex.Message}";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));

            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnBackupNow.IsEnabled = true;
        }
    }

    // ── SCARICA ─────────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fileName) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = fileName,
            DefaultExt = ".sql",
            Filter = "File SQL (*.sql)|*.sql|Tutti i file (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            txtStatus.Text = "Download in corso...";
            byte[]? data = await ApiClient.DownloadAsync($"/api/backup/download/{fileName}");
            if (data == null) throw new Exception("Download fallito");
            File.WriteAllBytes(dlg.FileName, data);

            txtStatus.Text = $"✓ Salvato in {dlg.FileName}";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"✗ Errore: {ex.Message}";
            txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));
        }
    }

    // ── ELIMINA ─────────────────────────────────────────────────

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string fileName) return;

        var result = MessageBox.Show(
            $"Eliminare il backup {fileName}?",
            "Conferma Eliminazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            string json = await ApiClient.DeleteAsync($"/api/backup/{fileName}");
            var response = JsonSerializer.Deserialize<ApiResponse<string>>(json, _jsonOpt);

            if (response?.Success == true)
            {
                txtStatus.Text = $"✓ Backup eliminato";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
                await LoadBackups();
            }
            else
            {
                txtStatus.Text = $"✗ {response?.Message ?? "Errore"}";
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"✗ Errore: {ex.Message}";
        }
    }

    // ── AGGIORNA ────────────────────────────────────────────────

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadBackups();
    }
}

// ── DTO per la lista backup ─────────────────────────────────────
public class BackupFileInfo
{
    public string FileName { get; set; } = "";
    public double SizeMB { get; set; }
    public string Date { get; set; } = "";
}
