using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.Models;

namespace ATEC.PM.Client.Views;

public partial class OfferViewPage : Page
{
    private int _offerId;
    private Offer _offer = new();

    public OfferViewPage(int offerId)
    {
        InitializeComponent();
        _offerId = offerId;
        Loaded += async (_, _) => await LoadOffer();
    }

    private async Task LoadOffer()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/offers/{_offerId}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _offer = JsonSerializer.Deserialize<Offer>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Header
            txtCode.Text = _offer.OfferCode;
            txtRevision.Text = $"Revisione {_offer.Revision}";
            txtTitle.Text = _offer.Title;
            txtCustomer.Text = _offer.CustomerName;
            txtCreatedBy.Text = $"Creata da: {_offer.CreatedByName}";
            txtStatus.Text = _offer.Status;

            // Colore status
            txtStatus.Foreground = _offer.Status switch
            {
                "BOZZA" => new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                "INVIATA" => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                "ACCETTATA" => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                "CONVERTITA" => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                "RIFIUTATA" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                "PERSA" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                "SUPERATA" => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                _ => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
            };

            // Link commessa convertita
            if (_offer.ConvertedProjectId.HasValue && !string.IsNullOrEmpty(_offer.ConvertedProjectCode))
            {
                txtConvertedLink.Text = $"→ Commessa {_offer.ConvertedProjectCode}";
                txtConvertedLink.Visibility = Visibility.Visible;
            }
            else
            {
                txtConvertedLink.Visibility = Visibility.Collapsed;
            }

            // Visibilità pulsanti in base allo stato
            UpdateButtonVisibility();

            // Carica costing
            bool readOnly = _offer.Status is "CONVERTITA" or "SUPERATA" or "PERSA";
            costingControl.LoadForOffer(_offerId, readOnly);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento offerta: {ex.Message}");
        }
    }

    private void UpdateButtonVisibility()
    {
        // BOZZA: può diventare INVIATA, RIFIUTATA, PERSA; può creare revisione
        // INVIATA: può diventare ACCETTATA, RIFIUTATA, PERSA; può creare revisione
        // ACCETTATA: può solo convertire in commessa
        // CONVERTITA/SUPERATA/PERSA/RIFIUTATA: nessuna azione stato

        bool isBozza = _offer.Status == "BOZZA";
        bool isInviata = _offer.Status == "INVIATA";
        bool isAccettata = _offer.Status == "ACCETTATA";
        bool isEditable = isBozza || isInviata;

        btnSetInviata.Visibility = isBozza ? Visibility.Visible : Visibility.Collapsed;
        btnSetAccettata.Visibility = isInviata ? Visibility.Visible : Visibility.Collapsed;
        btnSetRifiutata.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
        btnSetPersa.Visibility = isEditable ? Visibility.Visible : Visibility.Collapsed;
        btnRevision.Visibility = isEditable || _offer.Status == "RIFIUTATA" ? Visibility.Visible : Visibility.Collapsed;
        btnConvert.Visibility = isAccettata ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────── STATUS CHANGE ─────────────────────

    private async void BtnSetStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string newStatus = btn.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(newStatus)) return;

        var confirm = MessageBox.Show(
            $"Cambiare stato offerta a {newStatus}?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            string body = JsonSerializer.Serialize(new
            {
                Title = _offer.Title,
                Description = _offer.Description,
                Status = newStatus
            });
            var json = await ApiClient.PutAsync($"/api/offers/{_offerId}", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadOffer();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ───────────────────── REVISION ─────────────────────

    private async void BtnRevision_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Creare una nuova revisione?\nQuesta revisione verrà segnata come SUPERATA.",
            "Nuova Revisione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var json = await ApiClient.PostAsync($"/api/offers/{_offerId}/revision", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                int newOfferId = doc.RootElement.GetProperty("data").GetInt32();
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Revisione creata";
                MessageBox.Show(msg, "OK", MessageBoxButton.OK, MessageBoxImage.Information);

                // Naviga alla nuova revisione
                _offerId = newOfferId;
                await LoadOffer();
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ───────────────────── CONVERT ─────────────────────

    private async void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConvertOfferDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string body = JsonSerializer.Serialize(new { PmId = dlg.SelectedPmId });
            var json = await ApiClient.PostAsync($"/api/offers/{_offerId}/convert", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                string msg = doc.RootElement.GetProperty("message").GetString() ?? "Commessa creata";
                MessageBox.Show(msg, "Conversione completata", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadOffer(); // Ricarica — ora sarà CONVERTITA con link
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ───────────────────── BACK ─────────────────────

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.Navigate(new OffersPage());
    }
}
