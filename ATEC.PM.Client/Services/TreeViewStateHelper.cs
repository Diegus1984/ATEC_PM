using System.Windows.Controls;

namespace ATEC.PM.Client.Services;

/// <summary>
/// Utility per preservare lo stato visivo (espansione e selezione) di una TreeView
/// quando l'albero viene ricaricato da zero (es. dopo una modifica server-side).
///
/// Funziona con qualunque tipo di nodo: l'accesso a Id / IsExpanded / Children
/// avviene via delegati passati dal chiamante.
///
/// Esempio d'uso:
/// <code>
/// // PRIMA del reload
/// HashSet&lt;int&gt; expanded = TreeViewStateHelper.CollectExpandedIds(
///     tv.ItemsSource as IEnumerable&lt;MyNode&gt;,
///     n => n.IsExpanded,
///     n => n.Id,
///     n => n.Children);
///
/// // ... ricarico l'albero ...
///
/// // DOPO il reload
/// TreeViewStateHelper.ApplyExpandedState(newRoots, expanded,
///     n => n.Id,
///     (n, v) => n.IsExpanded = v,
///     n => n.Children);
/// </code>
/// </summary>
public static class TreeViewStateHelper
{
    /// <summary>
    /// Raccoglie ricorsivamente gli ID dei nodi attualmente espansi.
    /// </summary>
    /// <param name="roots">Radici dell'albero corrente (può essere null).</param>
    /// <param name="isExpanded">Predicato: ritorna true se il nodo è espanso.</param>
    /// <param name="idOf">Funzione che estrae l'ID univoco del nodo.</param>
    /// <param name="childrenOf">Funzione che ritorna i figli del nodo.</param>
    public static HashSet<int> CollectExpandedIds<T>(
        IEnumerable<T>? roots,
        Func<T, bool> isExpanded,
        Func<T, int> idOf,
        Func<T, IEnumerable<T>> childrenOf)
    {
        HashSet<int> set = new();
        if (roots == null) return set;
        foreach (T n in roots) Walk(n);
        return set;

        void Walk(T n)
        {
            if (isExpanded(n)) set.Add(idOf(n));
            foreach (T c in childrenOf(n)) Walk(c);
        }
    }

    /// <summary>
    /// Applica al nuovo albero lo stato di espansione precedentemente raccolto.
    /// </summary>
    public static void ApplyExpandedState<T>(
        IEnumerable<T> roots,
        HashSet<int> expandedIds,
        Func<T, int> idOf,
        Action<T, bool> setExpanded,
        Func<T, IEnumerable<T>> childrenOf)
    {
        foreach (T n in roots) Walk(n);

        void Walk(T n)
        {
            if (expandedIds.Contains(idOf(n)))
                setExpanded(n, true);
            foreach (T c in childrenOf(n)) Walk(c);
        }
    }

    /// <summary>
    /// Cerca ricorsivamente il primo nodo che soddisfa <paramref name="predicate"/>.
    /// Utile per riallineare un riferimento (es. nodo selezionato) dopo il reload.
    /// </summary>
    public static T? FindNode<T>(
        IEnumerable<T>? roots,
        Func<T, bool> predicate,
        Func<T, IEnumerable<T>> childrenOf) where T : class
    {
        if (roots == null) return null;
        foreach (T n in roots)
        {
            T? f = Walk(n);
            if (f != null) return f;
        }
        return null;

        T? Walk(T n)
        {
            if (predicate(n)) return n;
            foreach (T c in childrenOf(n))
            {
                T? f = Walk(c);
                if (f != null) return f;
            }
            return null;
        }
    }

    /// <summary>
    /// Verifica se <paramref name="candidate"/> è un discendente di <paramref name="folder"/>
    /// (utile per validare paste/move ed evitare cicli).
    /// </summary>
    public static bool IsDescendant<T>(
        T folder,
        int candidateId,
        Func<T, int> idOf,
        Func<T, IEnumerable<T>> childrenOf)
    {
        foreach (T child in childrenOf(folder))
        {
            if (idOf(child) == candidateId) return true;
            if (IsDescendant(child, candidateId, idOf, childrenOf)) return true;
        }
        return false;
    }

    // ══════════════════════════════════════════════════════════════
    //  API per TreeView WPF NATIVE (TreeViewItem creati a mano)
    //  Usare questa variante quando NON c'è DataTemplate + binding,
    //  ma la pagina costruisce TreeViewItem direttamente in code-behind.
    //  La chiave del nodo si estrae via delegato (tipicamente da .Tag).
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Raccoglie ricorsivamente le chiavi dei <see cref="TreeViewItem"/> attualmente espansi.
    /// </summary>
    /// <param name="items">Items collection radice (es. <c>tv.Items</c>).</param>
    /// <param name="keyOf">Estrae la chiave del nodo (tipicamente il Tag o sua trasformazione).
    /// Ritorna null per nodi senza chiave significativa (verranno ignorati).</param>
    public static HashSet<object> CollectExpandedKeys(
        ItemCollection items,
        Func<TreeViewItem, object?> keyOf)
    {
        HashSet<object> set = new();
        Walk(items);
        return set;

        void Walk(ItemCollection coll)
        {
            foreach (object? item in coll)
            {
                if (item is not TreeViewItem tvi) continue;
                if (tvi.IsExpanded)
                {
                    object? k = keyOf(tvi);
                    if (k != null) set.Add(k);
                }
                Walk(tvi.Items);
            }
        }
    }

    /// <summary>
    /// Applica al nuovo albero lo stato di espansione precedentemente raccolto.
    /// </summary>
    public static void ApplyExpandedKeys(
        ItemCollection items,
        HashSet<object> expandedKeys,
        Func<TreeViewItem, object?> keyOf)
    {
        if (expandedKeys.Count == 0) return;
        Walk(items);

        void Walk(ItemCollection coll)
        {
            foreach (object? item in coll)
            {
                if (item is not TreeViewItem tvi) continue;
                object? k = keyOf(tvi);
                if (k != null && expandedKeys.Contains(k))
                    tvi.IsExpanded = true;
                Walk(tvi.Items);
            }
        }
    }

    /// <summary>
    /// Cerca ricorsivamente il primo <see cref="TreeViewItem"/> che soddisfa il predicato.
    /// Utile per riselezionare un nodo dopo il reload (<c>found.IsSelected = true</c>).
    /// </summary>
    public static TreeViewItem? FindItem(
        ItemCollection items,
        Func<TreeViewItem, bool> predicate)
    {
        foreach (object? item in items)
        {
            if (item is not TreeViewItem tvi) continue;
            if (predicate(tvi)) return tvi;
            TreeViewItem? f = FindItem(tvi.Items, predicate);
            if (f != null) return f;
        }
        return null;
    }
}
