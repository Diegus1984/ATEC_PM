using System.Windows;
using System.Windows.Controls;

namespace ATEC.PM.Client.Helpers;

/// <summary>
/// Applica lo stile scuro Shadcn (lo stesso del menu profilo/logout) a un ContextMenu
/// creato da codice, così tutti i menu col tasto destro hanno lo stesso aspetto.
/// Gli stili sono risorse globali (ShadcnTheme mergeato in App.xaml).
/// </summary>
public static class ShadcnMenuHelper
{
    /// <summary>Stila il ContextMenu e tutti i suoi MenuItem/Separator col tema dark Shadcn. Chiamare DOPO aver aggiunto gli item.</summary>
    public static ContextMenu ApplyDark(ContextMenu menu)
    {
        Application? app = Application.Current;
        if (app == null) return menu;

        if (app.TryFindResource("ShadcnContextMenuDark") is Style ctxStyle)
            menu.Style = ctxStyle;

        Style? itemStyle = app.TryFindResource("ShadcnMenuItemDark") as Style;
        Style? sepStyle = app.TryFindResource("ShadcnMenuSeparatorDark") as Style;

        foreach (object item in menu.Items)
        {
            if (item is MenuItem mi && itemStyle != null) mi.Style = itemStyle;
            else if (item is Separator sep && sepStyle != null) sep.Style = sepStyle;
        }
        return menu;
    }
}
