using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ATEC.PM.Client.Views.Costing.ViewModels;

namespace ATEC.PM.Client.Views.Costing;

public partial class CostResourceRowControl : UserControl
{
    public event Action<CostResourceVM>? DeleteRequested;
    public event Action<CostResourceVM>? ResourceChanged;

    public CostResourceRowControl()
    {
        InitializeComponent();
    }

    public void Bind(CostResourceVM vm, string departmentCode = "", bool showTravel = false)
    {
        DataContext = vm;
        txtRep.Text = departmentCode;
        vm.PropertyChanged += (s, e) => ResourceChanged?.Invoke(vm);

        // Mostra/nascondi campi trasferta
        Visibility travelVis = showTravel ? Visibility.Visible : Visibility.Collapsed;
        brdSep.Visibility = travelVis;
        lblViaggi.Visibility = travelVis;
        txtViaggi.Visibility = travelVis;
        lblKm.Visibility = travelVis;
        txtKmTrip.Visibility = travelVis;
        lblCostKm.Visibility = travelVis;
        pnlCostKm.Visibility = travelVis;
        lblFood.Visibility = travelVis;
        pnlFood.Visibility = travelVis;
        lblHotel.Visibility = travelVis;
        pnlHotel.Visibility = travelVis;
        lblAllowDays.Visibility = travelVis;
        txtAllowDays.Visibility = travelVis;
        lblAllow.Visibility = travelVis;
        pnlAllow.Visibility = travelVis;
    }

    // ── DROPDOWN ────────────────────────────────────────────────────

    private void BtnDropDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string field) return;
        Popup? popup = GetPopup(field);
        if (popup != null) popup.IsOpen = !popup.IsOpen;
    }

    private void LstTariff_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListBox lst || lst.Tag is not string field) return;
        if (lst.SelectedItem is not decimal value) return;

        TextBox? txt = GetTextBox(field);
        if (txt != null)
        {
            txt.Text = field == "CostPerKm"
                ? value.ToString("N2", CultureInfo.InvariantCulture)
                : value.ToString("N0", CultureInfo.InvariantCulture);
            var binding = txt.GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource();
        }

        Popup? popup = GetPopup(field);
        if (popup != null) popup.IsOpen = false;
    }

    private Popup? GetPopup(string field) => field switch
    {
        "CostPerKm" => popCostKm,
        "DailyFood" => popFood,
        "DailyHotel" => popHotel,
        "DailyAllowance" => popAllow,
        _ => null
    };

    private TextBox? GetTextBox(string field) => field switch
    {
        "CostPerKm" => txtCostKm,
        "DailyFood" => txtFood,
        "DailyHotel" => txtHotel,
        "DailyAllowance" => txtAllow,
        _ => null
    };

    // ── DELETE ───────────────────────────────────────────────────────

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CostResourceVM vm)
            DeleteRequested?.Invoke(vm);
    }
}
