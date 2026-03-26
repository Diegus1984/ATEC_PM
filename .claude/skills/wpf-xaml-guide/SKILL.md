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
