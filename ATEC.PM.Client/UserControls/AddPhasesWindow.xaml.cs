using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.UserControls;

public partial class AddPhasesWindow : Window
{
    private readonly List<PhaseTemplateDto> _available;
    private readonly List<CheckBox> _checkBoxes = new();
    public List<PhaseTemplateDto> SelectedTemplates { get; } = new();

    public AddPhasesWindow(List<PhaseTemplateDto> available)
    {
        InitializeComponent();
        _available = available;
        BuildList();
    }

    private void BuildList()
    {
        string lastCategory = "";
        foreach (PhaseTemplateDto t in _available.OrderBy(t => t.SortOrder))
        {
            string cat = string.IsNullOrEmpty(t.DepartmentCode) ? "TRASVERSALE" : t.DepartmentCode;
            if (cat != lastCategory)
            {
                pnlTemplates.Children.Add(new TextBlock
                {
                    Text = $"── {cat} ──",
                    FontSize = 12, FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                lastCategory = cat;
            }

            CheckBox cb = new()
            {
                Content = t.Name,
                Tag = t,
                FontSize = 13,
                Margin = new Thickness(4, 2, 0, 2)
            };
            cb.Checked   += (_, _) => UpdateCount();
            cb.Unchecked += (_, _) => UpdateCount();
            _checkBoxes.Add(cb);
            pnlTemplates.Children.Add(cb);
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        int count = _checkBoxes.Count(c => c.IsChecked == true);
        txtCount.Text = $"{count} selezionate";
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox cb in _checkBoxes)
        {
            if (cb.IsChecked == true && cb.Tag is PhaseTemplateDto t)
                SelectedTemplates.Add(t);
        }
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
