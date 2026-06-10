using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Views.Commerciale.QuoteCatalog;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.GammaRobot;

public partial class GammaRobotPage
{
    private async Task OpenProductEditorAsync(int productId, string code, string name)
    {
        if (productId <= 0)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Componente senza anagrafica catalogo collegata.", "Gamma Robot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        QuoteProductDialog dlg = new(productId)
        {
            Owner = Window.GetWindow(this),
            Title = string.IsNullOrWhiteSpace(code) ? "Scheda prodotto" : $"{code} — {name}"
        };

        if (dlg.ShowDialog() == true)
            await RefreshAfterProductEditAsync();
    }

    private async Task RefreshAfterProductEditAsync()
    {
        if (viewRobot.Visibility == Visibility.Visible
            && treeRobot.SelectedItem is TreeViewItem { Tag: GammaQuadroDto quadro })
        {
            await LoadDistinta(quadro);
        }

        if (viewMagazzino.Visibility == Visibility.Visible && _componentsLoaded)
        {
            GammaComponentDto? selected = dgComponents.SelectedItem as GammaComponentDto;
            await LoadComponents();
            if (selected != null)
            {
                dgComponents.SelectedItem = _components.FirstOrDefault(c => c.ProductId == selected.ProductId);
            }
        }

        if (viewComposizione.Visibility == Visibility.Visible)
        {
            await ReloadDistintaEditor();
            await LoadEditorComponents();
        }
    }

    private void DistintaAltRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is FrameworkElement fe && fe.DataContext is GammaDistintaItemDto item)
        {
            e.Handled = true;
            OpenProductFromDistintaRow(item);
        }
    }

    private void DgDistinta_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetDistintaItemFromClick(e.OriginalSource as DependencyObject, out GammaDistintaItemDto? item))
        {
            e.Handled = true;
            OpenProductFromDistintaRow(item!);
        }
    }

    private void DgComponents_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgComponents.SelectedItem is not GammaComponentDto comp)
            return;

        e.Handled = true;
        _ = OpenProductEditorAsync(comp.ProductId, comp.Code, comp.Name);
    }

    internal void OpenDescriptionFromDistintaRow(GammaDistintaItemDto row)
    {
        OpenProductFromDistintaRow(row);
    }

    private void OpenProductFromDistintaRow(GammaDistintaItemDto row)
    {
        _ = OpenProductEditorAsync(
            row.ProductId ?? 0,
            row.ProductCode ?? row.CodeRaw ?? "?",
            row.ProductName ?? row.CodeRaw ?? "");
    }

    private static bool TryGetDistintaItemFromClick(DependencyObject? source, out GammaDistintaItemDto? item)
    {
        item = null;
        DependencyObject? current = source;
        while (current != null)
        {
            if (current is FrameworkElement fe)
            {
                if (fe.DataContext is GammaDistintaItemDto dist)
                {
                    item = dist;
                    return true;
                }

                if (fe.DataContext is GammaSlotRow slot && slot.ProductId > 0)
                {
                    item = new GammaDistintaItemDto
                    {
                        ProductId = slot.ProductId,
                        ProductCode = slot.ProductCode,
                        ProductName = slot.ProductName
                    };
                    return true;
                }
            }

            if (current is DataGridRow)
                break;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
