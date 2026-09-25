using System.Collections.ObjectModel;
using System.Globalization;
using MINV.Application.Inventory.Queries;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;

namespace MINV.DesktopClient.ViewModels;

/// <summary>
/// Buscador de productos con autocompletado: por SKU, nombre (sin importar tildes ni mayúsculas) o código de barras.
/// El operador elige de la lista, nunca escribe un ID (regla R-05 de la V1 llevada a la V3).
/// </summary>
public sealed class ProductPickerViewModel : ObservableObject
{
    private static readonly CompareInfo Compare = Fmt.Culture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private readonly DataCache _data;
    private IReadOnlyList<ProductLookupItem> _all = [];
    private string _query = string.Empty;
    private bool _open;
    private ProductLookupItem? _highlighted;
    private ProductLookupItem? _selected;
    private bool _suppress;

    public ProductPickerViewModel(DataCache data, int maxSuggestions = 8)
    {
        _data = data;
        MaxSuggestions = maxSuggestions;
        Pick = new RelayCommand<ProductLookupItem>(Select);
        ClearCommand = new RelayCommand(Clear);
    }

    public int MaxSuggestions { get; }

    public ObservableCollection<ProductLookupItem> Suggestions { get; } = [];

    /// <summary>Se eligió un producto (clic, Enter o lectura del escáner).</summary>
    public event EventHandler<ProductLookupItem>? Picked;

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value ?? string.Empty) && !_suppress)
            {
                if (_selected is not null && !string.Equals(_query, Label(_selected), StringComparison.Ordinal))
                {
                    Selected = null;
                }
                Filter();
            }
        }
    }

    public bool IsOpen
    {
        get => _open;
        set => Set(ref _open, value);
    }

    public ProductLookupItem? Highlighted
    {
        get => _highlighted;
        set => Set(ref _highlighted, value);
    }

    public ProductLookupItem? Selected
    {
        get => _selected;
        private set
        {
            if (Set(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selected is not null;

    public bool HasNoResults => IsOpen && Suggestions.Count == 0;

    public RelayCommand<ProductLookupItem> Pick { get; }

    public RelayCommand ClearCommand { get; }

    public int CatalogSize => _all.Count;

    public async Task EnsureLoadedAsync(bool force = false)
    {
        _all = await _data.LookupAsync(force);
        OnPropertyChanged(nameof(CatalogSize));
    }

    public static string Label(ProductLookupItem item) => $"{item.Sku} · {item.Name}";

    public void Select(ProductLookupItem item)
    {
        _suppress = true;
        Query = Label(item);
        _suppress = false;
        Selected = item;
        IsOpen = false;
        Suggestions.Clear();
        Picked?.Invoke(this, item);
    }

    /// <summary>Código exacto (SKU o código de barras), p. ej. una lectura del escáner.</summary>
    public bool TrySelectCode(string code)
    {
        var c = code.Trim();
        var match = _all.FirstOrDefault(p => string.Equals(p.Sku, c, StringComparison.OrdinalIgnoreCase))
                    ?? _all.FirstOrDefault(p => p.Barcodes.Contains(c, StringComparer.Ordinal));
        if (match is null)
        {
            return false;
        }
        Select(match);
        return true;
    }

    /// <summary>Enter: el resaltado, o el único resultado, o un código exacto.</summary>
    public bool Commit()
    {
        if (IsOpen && Highlighted is { } h)
        {
            Select(h);
            return true;
        }
        if (Suggestions.Count == 1)
        {
            Select(Suggestions[0]);
            return true;
        }
        return TrySelectCode(Query);
    }

    public void MoveHighlight(int delta)
    {
        if (Suggestions.Count == 0)
        {
            return;
        }
        IsOpen = true;
        var index = Highlighted is null ? (delta > 0 ? 0 : Suggestions.Count - 1) : Suggestions.IndexOf(Highlighted) + delta;
        Highlighted = Suggestions[Math.Clamp(index, 0, Suggestions.Count - 1)];
    }

    public void Clear()
    {
        _suppress = true;
        Query = string.Empty;
        _suppress = false;
        Selected = null;
        Suggestions.Clear();
        IsOpen = false;
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void Filter()
    {
        Suggestions.Clear();
        var q = _query.Trim();
        if (q.Length == 0 || _selected is not null)
        {
            IsOpen = false;
            OnPropertyChanged(nameof(HasNoResults));
            return;
        }
        var ranked = _all
            .Select(p => (Item: p, Score: Score(p, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Sku, StringComparer.Ordinal)
            .Take(MaxSuggestions);
        foreach (var (item, _) in ranked)
        {
            Suggestions.Add(item);
        }
        Highlighted = Suggestions.FirstOrDefault();
        IsOpen = true;
        OnPropertyChanged(nameof(HasNoResults));
    }

    private static int Score(ProductLookupItem p, string q)
    {
        if (string.Equals(p.Sku, q, StringComparison.OrdinalIgnoreCase) || p.Barcodes.Contains(q, StringComparer.Ordinal))
        {
            return 100;
        }
        if (p.Sku.StartsWith(q, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }
        if (Compare.IsPrefix(p.Name, q, Loose))
        {
            return 60;
        }
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(w => Compare.IndexOf(p.Name, w, Loose) >= 0 || Compare.IndexOf(p.Category, w, Loose) >= 0))
        {
            return 40;
        }
        return p.Sku.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Barcodes.Any(b => b.Contains(q, StringComparison.Ordinal)) ? 20 : 0;
    }
}
