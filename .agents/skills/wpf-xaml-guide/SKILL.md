---
name: wpf-xaml-guide
description: WPF XAML patterns and templates for ATEC PM. Use when creating new UserControls, pages, ResourceDictionaries, or refactoring existing XAML. Provides ready-to-use templates that follow the ATEC design system.
---

# WPF XAML Patterns — ATEC PM

## New UserControl Template
```xml
<UserControl x:Class="ATEC_PM.Controls.{ControlName}"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Gray50Brush}">
    <Grid Margin="16">
        <!-- Content here -->
    </Grid>
</UserControl>
```

## New ResourceDictionary Template
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Styles scoped to a specific control -->
</ResourceDictionary>
```
Merge in the control:
```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="/Styles/{ControlName}Styles.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</UserControl.Resources>
```

## Standard Grid Layout (form with sidebar)
```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="220"/>   <!-- Sidebar -->
        <ColumnDefinition Width="*"/>     <!-- Main content -->
    </Grid.ColumnDefinitions>
    <!-- Sidebar in Column 0, Content in Column 1 -->
</Grid>
```

## Form Layout Pattern
```xml
<StackPanel Margin="16" MaxWidth="600">
    <!-- Field group -->
    <TextBlock Text="Nome Progetto" Style="{StaticResource LabelStyle}"/>
    <TextBox Style="{StaticResource AtecTextBox}" Margin="0,4,0,8"/>

    <TextBlock Text="Cliente" Style="{StaticResource LabelStyle}"/>
    <ComboBox Style="{StaticResource AtecComboBox}" Margin="0,4,0,8"/>

    <!-- Actions -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
        <Button Content="Annulla" Style="{StaticResource SecondaryButton}" Margin="0,0,8,0"/>
        <Button Content="Salva" Style="{StaticResource PrimaryButton}"/>
    </StackPanel>
</StackPanel>
```

## DataGrid with standard config
```xml
<DataGrid ItemsSource="{Binding Items}"
          Style="{StaticResource AtecDataGrid}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          SelectionMode="Single"
          CanUserAddRows="False"
          CanUserDeleteRows="False"
          CanUserReorderColumns="False"
          CanUserResizeRows="False">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Codice" Binding="{Binding Code}" Width="100"/>
        <DataGridTextColumn Header="Descrizione" Binding="{Binding Description}" Width="*"/>
        <DataGridTextColumn Header="Stato" Binding="{Binding Status}" Width="100"/>
    </DataGrid.Columns>
</DataGrid>
```

## KPI Dashboard Row
```xml
<UniformGrid Columns="4" Margin="0,0,0,8">
    <!-- Repeat KPI card pattern from design system -->
</UniformGrid>
```

## Conventions
- All UserControls in `/Controls/` folder
- Per-control styles in `/Styles/{ControlName}Styles.xaml`
- Event handlers: `{Action}_{Event}` pattern (e.g., `BtnSave_Click`)
- Loaded event: `{ControlName}_Loaded`
- Always set `SnapsToDevicePixels="True"` on Border elements with 1px borders

## TreeView — preservare espansione e selezione tra reload

**Problema ricorrente**: ogni reload di una TreeView gerarchica chiude tutti i rami aperti (UX rotta dopo create/rename/delete/move).

**Soluzione standard ATEC**: helper condiviso `Services/TreeViewStateHelper.cs`. Generico via delegati — funziona con qualunque modello di nodo. Già in uso in `Views/Templates/ProjectTemplatePage`.

### Requisiti sul modello del nodo
- `Id` (int) univoco
- `IsExpanded` (bool) settabile
- `Children` come `IEnumerable<T>` (tipicamente `ObservableCollection<T>`)

### Binding TwoWay sul TreeViewItem.IsExpanded
```xml
<TreeView.ItemContainerStyle>
    <Style TargetType="TreeViewItem">
        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}" />
    </Style>
</TreeView.ItemContainerStyle>
```
`Mode=TwoWay` propaga click utente → modello in automatico.

### Save/restore nel reload
```csharp
private async Task LoadTreeAsync()
{
    // PRIMA del reload: snapshot
    HashSet<int> expandedIds = TreeViewStateHelper.CollectExpandedIds(
        tv.ItemsSource as IEnumerable<MyNode>,
        isExpanded: n => n.IsExpanded,
        idOf:       n => n.Id,
        childrenOf: n => n.Children);

    int? selKey = _selected?.Id;

    // ... fetch e build del nuovo albero ...
    ObservableCollection<MyNode> tree = ...;

    // DOPO il reload: ripristino
    TreeViewStateHelper.ApplyExpandedState(tree, expandedIds,
        idOf:        n => n.Id,
        setExpanded: (n, v) => n.IsExpanded = v,
        childrenOf:  n => n.Children);

    tv.ItemsSource = tree;

    // Riallinea il riferimento al nodo selezionato
    if (selKey is int id)
        _selected = TreeViewStateHelper.FindNode(tree,
            n => n.Id == id,
            n => n.Children);
}
```

### Helper extra
- `TreeViewStateHelper.FindNode<T>(...)` — primo nodo che soddisfa un predicato (riallinea selezione).
- `TreeViewStateHelper.IsDescendant<T>(...)` — anti-cicli per paste/move di cartelle.

### Pattern UX dopo mutazioni
Dopo create/upload/paste, **espandi il parent** della destinazione *prima* del reload — l'helper lo salva nella snapshot automaticamente:
```csharp
if (parent is not null) parent.IsExpanded = true;
await LoadTreeAsync();
```
Così il nuovo nodo è subito visibile.

### Nodi eterogenei (folder/file, gruppo/commessa, …)
Se l'albero mescola tipi che possono avere ID collidenti tra tabelle, restringi il predicato a una sola categoria (es. solo folder) o componi una chiave univoca (`n => n.IsFolder ? n.Id : -n.Id`).

## TreeView — API alternativa per `TreeViewItem` creati a mano

Le pagine che costruiscono `TreeViewItem` direttamente in code-behind (senza DataTemplate) usano una variante dell'helper basata su **chiave estratta da Tag**:

```csharp
private void BuildTree(...)
{
    // Snapshot PRIMA del rebuild
    HashSet<object> expandedKeys = TreeViewStateHelper.CollectExpandedKeys(
        tv.Items, tvi => tvi.Tag);
    object? selectedKey = (tv.SelectedItem as TreeViewItem)?.Tag;

    tv.Items.Clear();
    // ... ricostruzione manuale ...

    // Restore DOPO il rebuild
    TreeViewStateHelper.ApplyExpandedKeys(tv.Items, expandedKeys, tvi => tvi.Tag);
    if (selectedKey != null)
    {
        TreeViewItem? toSel = TreeViewStateHelper.FindItem(tv.Items,
            tvi => Equals(tvi.Tag, selectedKey));
        if (toSel != null) toSel.IsSelected = true;
    }
}
```

Funziona con qualunque Tag univoco (stringhe tipo `"project|123"`, DTO con Equals corretti, tuple, ecc.).

## Quando NON usare l'helper

L'helper assume **multi-expand** (più nodi possono essere aperti contemporaneamente). Alcune pagine implementano invece **single-expand by design**: aprendo un nodo, gli altri si chiudono. In quel caso una "memoria di un solo nodo aperto" (es. `string? _lastExpandedTreeGroup`) è semanticamente corretta e l'helper la peggiorerebbe.

**Esempio concreto**: `Views/ConfigurazioneSezioni/CostSectionsTreePage` — solo un gruppo alla volta è aperto, per scelta UX. Lascia il pattern esistente.

## Pagine del progetto che usano questo pattern
- ✅ `Views/Templates/ProjectTemplatePage` — modello+binding (API generica `<T>`)
- ✅ `Views/Commesse/ProjectsPage` — `TreeViewItem` manuali (API `ItemCollection` + Tag)
- ⛔ `Views/ConfigurazioneSezioni/CostSectionsTreePage` — single-expand by design, NON usare l'helper
