using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Wpf;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Crea e rilascia PlotView WPF on-demand (evita OxyPlot finché il grafico non serve).
/// </summary>
public sealed class OxyPlotChartHost
{
    private readonly Panel _host;
    private PlotView? _plotView;

    public OxyPlotChartHost(Panel host) => _host = host;

    public bool IsCreated => _plotView != null;

    public PlotView EnsurePlotView(PlotModel model, double? height = null)
    {
        if (_plotView != null)
        {
            _plotView.Model = model;
            return _plotView;
        }

        PlotView view = new PlotView { Model = model };
        if (height.HasValue)
            view.Height = height.Value;

        _host.Children.Add(view);
        _plotView = view;
        return view;
    }

    public void UpdateModel(PlotModel model)
    {
        if (_plotView != null)
            _plotView.Model = model;
    }

    public void Teardown()
    {
        if (_plotView == null)
            return;

        _host.Children.Remove(_plotView);
        _plotView = null;
    }
}
