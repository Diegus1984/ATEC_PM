using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ATEC.PM.Client.Controls;

/// <summary>
/// Chrome riusabile per le pagine "tabella": toolbar (sinistra/destra), area centrale
/// per la DataGrid (Body, content property), barra di stato/paginazione in basso.
/// La pagina fornisce le colonne della DataGrid; lo shell standardizza layout e stile Shadcn.
/// </summary>
[ContentProperty(nameof(Body))]
public partial class ShadcnDataTableShell : UserControl
{
    public ShadcnDataTableShell() => InitializeComponent();

    /// <summary>Contenuto centrale (tipicamente la DataGrid). È la content property del controllo.</summary>
    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(object), typeof(ShadcnDataTableShell));
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    /// <summary>Contenuto toolbar a sinistra (azioni principali).</summary>
    public static readonly DependencyProperty ToolbarLeftProperty =
        DependencyProperty.Register(nameof(ToolbarLeft), typeof(object), typeof(ShadcnDataTableShell));
    public object? ToolbarLeft { get => GetValue(ToolbarLeftProperty); set => SetValue(ToolbarLeftProperty, value); }

    /// <summary>Contenuto toolbar a destra (filtri, ricerca).</summary>
    public static readonly DependencyProperty ToolbarRightProperty =
        DependencyProperty.Register(nameof(ToolbarRight), typeof(object), typeof(ShadcnDataTableShell));
    public object? ToolbarRight { get => GetValue(ToolbarRightProperty); set => SetValue(ToolbarRightProperty, value); }

    /// <summary>Contenuto opzionale a destra della barra di stato (es. controlli di paginazione).</summary>
    public static readonly DependencyProperty PaginationProperty =
        DependencyProperty.Register(nameof(Pagination), typeof(object), typeof(ShadcnDataTableShell));
    public object? Pagination { get => GetValue(PaginationProperty); set => SetValue(PaginationProperty, value); }

    /// <summary>Testo di stato a sinistra della barra inferiore (es. "42 risultati").</summary>
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(ShadcnDataTableShell), new PropertyMetadata(""));
    public string StatusText { get => (string)GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    /// <summary>Testo hint a destra della barra inferiore (mostrato solo se Pagination non è impostato).</summary>
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ShadcnDataTableShell), new PropertyMetadata(""));
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
}
