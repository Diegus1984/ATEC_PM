using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

// Modifica di uno stato DDP: etichetta + colori (sfondo/testo) + ordine. La chiave è fissa.
public partial class DdpStatusDialog : Window
{
    private readonly DdpStatusItem? _item;   // null = creazione di una nuova causale

    private static readonly string[] BgPresets =
    {
        "#FF0000", "#FFC000", "#FFFF00", "#00B050", "#006400", "#00B0F0", "#2563EB",
        "#7030A0", "#8B008B", "#FFB6C1", "#B4B4B4", "#ADD8E6", "#000000", "#FFFFFF"
    };

    // Modifica di una causale esistente.
    public DdpStatusDialog(DdpStatusItem item)
    {
        InitializeComponent();
        _item = item;
        Title = "Modifica Causale";

        txtKey.Text = item.StatusKey;
        txtLabel.Text = item.Label;
        txtBg.Text = item.ColorBg;
        txtFg.Text = item.ColorFg;
        txtOrder.Text = item.SortOrder.ToString();
        chkActive.IsChecked = item.IsActive;

        BuildPresets();
        UpdatePreview();
    }

    // Creazione di una nuova causale (la chiave la genera il server dall'etichetta).
    public DdpStatusDialog()
    {
        InitializeComponent();
        _item = null;
        Title = "Nuova Causale";

        txtKey.Text = "(generata automaticamente dall'etichetta)";
        txtBg.Text = "#CCCCCC";
        txtFg.Text = "#000000";
        txtOrder.Text = "0";

        BuildPresets();
        UpdatePreview();
    }

    private void BuildPresets()
    {
        foreach (string hex in BgPresets)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 4),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#D0D5DD")!,
                BorderThickness = new Thickness(1),
                Background = TryBrush(hex, Brushes.Transparent),
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = hex
            };
            swatch.MouseLeftButtonUp += (s, _) =>
            {
                if (s is Border b && b.Tag is string h) txtBg.Text = h;
            };
            wpBgPresets.Children.Add(swatch);
        }
    }

    // Aggiorna swatch e anteprima a ogni modifica dei campi.
    private void Field_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        Brush bg = TryBrush(txtBg.Text, Brushes.LightGray);
        Brush fg = TryBrush(txtFg.Text, Brushes.Black);

        swBg.Background = bg;
        swFg.Background = fg;
        prevBadge.Background = bg;
        prevText.Foreground = fg;
        prevText.Text = string.IsNullOrWhiteSpace(txtLabel.Text) ? "(etichetta)" : txtLabel.Text;
    }

    private void BtnFgWhite_Click(object sender, RoutedEventArgs e) => txtFg.Text = "#FFFFFF";
    private void BtnFgBlack_Click(object sender, RoutedEventArgs e) => txtFg.Text = "#000000";

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtLabel.Text))
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("L'etichetta è obbligatoria.", "Stato DDP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!IsValidColor(txtBg.Text) || !IsValidColor(txtFg.Text))
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Colore non valido. Usa il formato #RRGGBB (es. #FF0000).",
                "Stato DDP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int.TryParse(txtOrder.Text.Trim(), out int order);

        var req = new DdpStatusSaveRequest
        {
            Id = _item?.Id ?? 0,
            Label = txtLabel.Text.Trim(),
            ColorBg = Normalize(txtBg.Text),
            ColorFg = Normalize(txtFg.Text),
            SortOrder = order,
            IsActive = chkActive.IsChecked == true
        };

        try
        {
            string body = JsonSerializer.Serialize(req);
            string resp = _item == null
                ? await ApiClient.PostAsync("/api/ddp-statuses", body)
                : await ApiClient.PutAsync($"/api/ddp-statuses/{_item.Id}", body);

            if (ApiClient.IsApiSuccess(resp, out string msg))
            {
                DialogResult = true;
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                    string.IsNullOrWhiteSpace(msg) ? "Salvataggio non riuscito." : msg, "Causale DDP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Causale DDP");
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Helper colori ──
    private static string Normalize(string hex)
    {
        hex = (hex ?? "").Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return hex.ToUpperInvariant();
    }

    private static bool IsValidColor(string hex)
    {
        try { ColorConverter.ConvertFromString(Normalize(hex)); return true; }
        catch { return false; }
    }

    private static Brush TryBrush(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Brush)new BrushConverter().ConvertFromString(Normalize(hex))!; }
        catch { return fallback; }
    }
}
