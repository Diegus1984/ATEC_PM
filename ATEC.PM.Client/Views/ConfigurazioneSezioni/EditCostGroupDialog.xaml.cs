using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class EditCostGroupDialog : Window
{
    private static readonly string[] ColorPalette =
    {
        "#2563EB", "#3B82F6", "#1D4ED8",
        "#059669", "#10B981", "#047857",
        "#D97706", "#F59E0B", "#B45309",
        "#DC2626", "#EF4444", "#B91C1C",
        "#7C3AED", "#8B5CF6", "#6D28D9",
        "#0891B2", "#06B6D4", "#0E7490",
        "#BE185D", "#EC4899", "#9D174D",
        "#374151", "#6B7280", "#1F2937",
    };

    private string _selectedColor;
    private Button? _selectedBtn;

    public string GroupName => txtName.Text.Trim();
    public string BgColor => _selectedColor;
    public int SortOrder => int.TryParse(txtSortOrder.Text, out int n) ? n : 0;

    public EditCostGroupDialog(CostSectionGroupDto group)
    {
        InitializeComponent();
        txtName.Text = group.Name;
        txtSortOrder.Text = group.SortOrder.ToString();
        _selectedColor = group.BgColor ?? "#3B82F6";
        BuildColorPalette();
    }

    private void BuildColorPalette()
    {
        wpColors.Children.Clear();
        Brush ring = (Brush)FindResource("ShadcnForegroundBrush");
        foreach (string hex in ColorPalette)
        {
            string captured = hex;
            Button btn = new()
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == _selectedColor ? ring : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            if (hex == _selectedColor) _selectedBtn = btn;
            btn.Click += (_, _) =>
            {
                if (_selectedBtn != null)
                    _selectedBtn.BorderBrush = Brushes.Transparent;
                btn.BorderBrush = ring;
                _selectedBtn = btn;
                _selectedColor = captured;
            };
            wpColors.Children.Add(btn);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
            return;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
