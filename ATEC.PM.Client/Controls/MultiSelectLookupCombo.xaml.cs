using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Controls;

// Multi-select grezzo: pulsante + pannello inline con ricerca e checkbox plain.
// Niente Popup, niente stili custom — pensato per funzionare dentro il TreeView MoM.
public partial class MultiSelectLookupCombo : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<LookupItem>),
            typeof(MultiSelectLookupCombo),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedIdsProperty =
        DependencyProperty.Register(
            nameof(SelectedIds),
            typeof(string),
            typeof(MultiSelectLookupCombo),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIdsChanged));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(MultiSelectLookupCombo), new PropertyMetadata("Seleziona..."));

    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MultiSelectLookupCombo));

    private readonly List<int> _selectionOrder = new();
    private readonly List<CheckBox> _checkboxes = new();
    private bool _internalUpdate;

    public MultiSelectLookupCombo()
    {
        InitializeComponent();
        btnHeader.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
                AdjustHeaderHeight();
        };
        UpdateHeaderText();
    }

    public IEnumerable<LookupItem>? ItemsSource
    {
        get => (IEnumerable<LookupItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string SelectedIds
    {
        get => (string)GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public event RoutedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MultiSelectLookupCombo c)
        {
            c.SyncOrderFromIds();
            c.RebuildCheckboxes();
            c.UpdateHeaderText();
        }
    }

    private static void OnSelectedIdsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MultiSelectLookupCombo c)
        {
            if (c._internalUpdate) return;
            c.SyncOrderFromIds();
            c.SyncCheckboxStates();
            c.UpdateHeaderText();
        }
    }

    private void BtnHeader_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        bool open = !popOpen.IsOpen;
        if (open)
        {
            SyncOrderFromIds();
            RebuildCheckboxes();
            txtSearch.Clear();
            ApplySearchFilter();
        }
        popOpen.IsOpen = open;
        if (open)
            Dispatcher.BeginInvoke(new System.Action(() => txtSearch.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void TxtSearch_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        txtSearch.Focus();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();

    private void SyncOrderFromIds()
    {
        _selectionOrder.Clear();
        foreach (int id in ParseIds(SelectedIds))
            _selectionOrder.Add(id);
    }

    private void RebuildCheckboxes()
    {
        wpItems.Children.Clear();
        _checkboxes.Clear();

        if (ItemsSource == null) return;

        Style? cbStyle = TryFindResource("ShadcnCheckBox") as Style;

        foreach (LookupItem item in ItemsSource)
        {
            CheckBox cb = new CheckBox
            {
                Content = item.Name,
                Tag = item.Id,
                Margin = new Thickness(0, 2, 0, 2),
                IsChecked = _selectionOrder.Contains(item.Id)
            };
            if (cbStyle != null) cb.Style = cbStyle;
            cb.PreviewMouseLeftButtonDown += ItemCheckBox_PreviewMouseLeftButtonDown;
            _checkboxes.Add(cb);
            wpItems.Children.Add(cb);
        }

        ApplySearchFilter();
    }

    private void SyncCheckboxStates()
    {
        foreach (CheckBox cb in _checkboxes)
        {
            if (cb.Tag is not int id) continue;
            bool on = _selectionOrder.Contains(id);
            if ((cb.IsChecked == true) != on)
                cb.IsChecked = on;
        }
    }

    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim().ToLower();
        foreach (CheckBox cb in _checkboxes)
        {
            string name = cb.Content?.ToString() ?? string.Empty;
            cb.Visibility = string.IsNullOrEmpty(q) || name.ToLower().Contains(q)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ItemCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not CheckBox cb || cb.Tag is not int id) return;

        bool newChecked = cb.IsChecked != true;
        cb.IsChecked = newChecked;

        if (newChecked)
        {
            if (!_selectionOrder.Contains(id))
                _selectionOrder.Add(id);
        }
        else
        {
            _selectionOrder.Remove(id);
        }

        CommitSelection();
    }

    private void CommitSelection()
    {
        string joined = _selectionOrder.Count == 0 ? string.Empty : string.Join(",", _selectionOrder);

        _internalUpdate = true;
        SelectedIds = joined;
        _internalUpdate = false;

        UpdateHeaderText();
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent, this));
    }

    private void UpdateHeaderText()
    {
        if (_selectionOrder.Count == 0)
        {
            btnHeader.Content = CreatePlaceholderBlock();
            RefreshHeaderContentWidth();
            return;
        }

        Dictionary<int, string> map = new Dictionary<int, string>();
        if (ItemsSource != null)
        {
            foreach (LookupItem item in ItemsSource)
                map[item.Id] = item.Name;
        }

        List<string> names = new List<string>();
        foreach (int id in _selectionOrder)
        {
            if (map.TryGetValue(id, out string? n))
                names.Add(n);
        }

        if (names.Count == 0)
        {
            btnHeader.Content = CreatePlaceholderBlock();
        }
        else
        {
            TextBlock header = new TextBlock
            {
                Text = string.Join("\n", names),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            btnHeader.Content = header;
        }

        RefreshHeaderContentWidth();
        Dispatcher.BeginInvoke(new Action(AdjustHeaderHeight), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private TextBlock CreatePlaceholderBlock() =>
        new TextBlock
        {
            Text = Placeholder,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private void RefreshHeaderContentWidth()
    {
        if (btnHeader.Content is not FrameworkElement content || btnHeader.ActualWidth <= 0)
            return;

        double innerWidth = Math.Max(0, btnHeader.ActualWidth - btnHeader.Padding.Left - btnHeader.Padding.Right - 4);
        content.MaxWidth = innerWidth;
    }

    private void AdjustHeaderHeight()
    {
        if (btnHeader.ActualWidth <= 0)
            return;

        RefreshHeaderContentWidth();
        if (btnHeader.Content is not FrameworkElement content)
            return;

        double innerWidth = Math.Max(0, btnHeader.ActualWidth - btnHeader.Padding.Left - btnHeader.Padding.Right);
        content.Measure(new Size(innerWidth, double.PositiveInfinity));
        double targetHeight = Math.Max(30, content.DesiredSize.Height + btnHeader.Padding.Top + btnHeader.Padding.Bottom + 2);
        if (double.IsNaN(btnHeader.Height) || Math.Abs(btnHeader.Height - targetHeight) > 0.5)
            btnHeader.Height = targetHeight;
    }

    private static List<int> ParseIds(string? raw)
    {
        List<int> ids = new List<int>();
        if (string.IsNullOrWhiteSpace(raw)) return ids;
        foreach (string part in raw.Split(','))
            if (int.TryParse(part.Trim(), out int id) && !ids.Contains(id))
                ids.Add(id);
        return ids;
    }
}
