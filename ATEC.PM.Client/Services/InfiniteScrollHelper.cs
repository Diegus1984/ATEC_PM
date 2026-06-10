using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Carica altre pagine quando lo scroll è vicino al fondo (o il contenuto non riempie il viewport).
/// Gestisce anche il trascinamento della thumb della scrollbar (DataGrid virtualizzato).
/// </summary>
public sealed class InfiniteScrollHelper
{
    private const double BottomThresholdPx = 80;
    private const double BottomThresholdItems = 5;
    private const int MinLoadIntervalMs = 400;
    private const int CheckDebounceMs = 80;

    private readonly Func<bool> _canLoadMore;
    private readonly Func<Task> _loadMoreAsync;
    private readonly ScrollEventHandler _scrollBarScrollHandler;
    private ScrollViewer? _scrollViewer;
    private ScrollBar? _verticalScrollBar;
    private FrameworkElement? _host;
    private DispatcherTimer? _checkDebounceTimer;
    private bool _loadInProgress;
    private DateTime _lastLoadUtc = DateTime.MinValue;
    private bool _attached;

    public InfiniteScrollHelper(Func<bool> canLoadMore, Func<Task> loadMoreAsync)
    {
        _canLoadMore = canLoadMore;
        _loadMoreAsync = loadMoreAsync;
        _scrollBarScrollHandler = OnScrollBarScroll;
    }

    public void Attach(FrameworkElement host)
    {
        if (_attached) return;
        _attached = true;
        _host = host;
        host.Loaded += OnHostLoaded;
        host.Unloaded += OnHostUnloaded;
        host.LayoutUpdated += OnHostLayoutUpdated;
        if (host.IsLoaded)
            RegisterScrollViewer(host);
    }

    /// <summary>Chiamare dopo ogni caricamento dati (anche prima pagina).</summary>
    public void NotifyContentUpdated(FrameworkElement host)
    {
        _host = host;
        if (_scrollViewer == null)
            RegisterScrollViewer(host);
        ScheduleCheckScrollPosition();
    }

    private void OnHostLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host)
            RegisterScrollViewer(host);
    }

    private void OnHostUnloaded(object sender, RoutedEventArgs e)
    {
        DetachScrollHandlers();
        if (_host != null)
            _host.LayoutUpdated -= OnHostLayoutUpdated;
        _checkDebounceTimer?.Stop();
        _checkDebounceTimer = null;
        _host = null;
    }

    private void OnHostLayoutUpdated(object? sender, EventArgs e)
    {
        // Dopo virtualizzazione / drag thumb l'extent può cambiare senza ScrollChanged utile
        ScheduleCheckScrollPosition();
    }

    private void DetachScrollHandlers()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        if (_verticalScrollBar != null)
        {
            _verticalScrollBar.ValueChanged -= OnScrollBarValueChanged;
            _verticalScrollBar.RemoveHandler(ScrollBar.ScrollEvent, _scrollBarScrollHandler);
        }
        _scrollViewer = null;
        _verticalScrollBar = null;
    }

    private void RegisterScrollViewer(FrameworkElement host)
    {
        _host = host;
        host.Dispatcher.BeginInvoke(() =>
        {
            ScrollViewer? sv = FindScrollViewer(host);
            if (sv == null) return;
            if (_scrollViewer == sv && _verticalScrollBar != null) return;

            DetachScrollHandlers();
            _scrollViewer = sv;
            _scrollViewer.ScrollChanged += OnScrollChanged;

            _verticalScrollBar = FindVerticalScrollBar(sv) ?? FindVerticalScrollBar(host);
            if (_verticalScrollBar != null)
            {
                _verticalScrollBar.ValueChanged += OnScrollBarValueChanged;
                _verticalScrollBar.AddHandler(ScrollBar.ScrollEvent, _scrollBarScrollHandler, true);
            }

            CheckScrollPosition();
        }, DispatcherPriority.Loaded);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScheduleCheckScrollPosition();
    }

    private void OnScrollBarValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ScheduleCheckScrollPosition();
    }

    private void OnScrollBarScroll(object sender, ScrollEventArgs e)
    {
        ScheduleCheckScrollPosition();
    }

    private void ScheduleCheckScrollPosition()
    {
        if (_host == null) return;
        if (_checkDebounceTimer == null)
        {
            _checkDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CheckDebounceMs)
            };
            _checkDebounceTimer.Tick += (_, _) =>
            {
                _checkDebounceTimer!.Stop();
                CheckScrollPosition();
            };
        }
        _checkDebounceTimer.Stop();
        _checkDebounceTimer.Start();
    }

    private void CheckScrollPosition()
    {
        if (_scrollViewer == null || _loadInProgress) return;
        if (!_canLoadMore()) return;

        double threshold = _scrollViewer.CanContentScroll ? BottomThresholdItems : BottomThresholdPx;
        bool nearBottom = _scrollViewer.VerticalOffset + _scrollViewer.ViewportHeight
            >= _scrollViewer.ExtentHeight - threshold;
        bool notScrollable = _scrollViewer.ExtentHeight <= _scrollViewer.ViewportHeight + 1;

        if (nearBottom || notScrollable)
            _ = TriggerLoadAsync();
    }

    private async Task TriggerLoadAsync()
    {
        if (_loadInProgress || !_canLoadMore()) return;
        if ((DateTime.UtcNow - _lastLoadUtc).TotalMilliseconds < MinLoadIntervalMs) return;

        _loadInProgress = true;
        _lastLoadUtc = DateTime.UtcNow;
        try
        {
            await _loadMoreAsync();
        }
        finally
        {
            _loadInProgress = false;
            ScheduleCheckScrollPosition();
        }
    }

    public static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }
        return null;
    }

    public static ScrollBar? FindVerticalScrollBar(DependencyObject root)
    {
        if (root is ScrollBar bar && bar.Orientation == Orientation.Vertical)
            return bar;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            ScrollBar? found = FindVerticalScrollBar(VisualTreeHelper.GetChild(root, i));
            if (found != null)
                return found;
        }
        return null;
    }
}
