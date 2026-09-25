using MINV.Application.Corporate;
using MINV.Application.Integration;
using MINV.DesktopClient.Controls;
using MINV.DesktopClient.Mvvm;
using MINV.DesktopClient.Services;
using MINV.Domain.Iam;

namespace MINV.DesktopClient.ViewModels;

/// <summary>Fila de una API Key.</summary>
public sealed record ApiKeyItem(ApiKeyRow Row)
{
    public string PrefixText => $"minv_{Row.Prefix}_••••";

    public string ScopesText => string.Join(" · ", Row.Scopes);

    public string BranchText => Row.Branch ?? "Todas sus sucursales";

    public string CreatedText => Fmt.DateTime(Row.CreatedAt);

    public string LastUsedText => Row.LastUsedAt is { } at ? Fmt.DateTime(at) : "Nunca";

    public string StatusText => Row.RevokedAt is not null ? "Revocada" : Row.IsUsable ? "Activa" : "Vencida";

    public string StatusBrush => Row.IsUsable ? "Success" : "StatusInactive";

    public string StatusSoftBrush => StatusBrush + "Soft";
}

/// <summary>Fila de un webhook.</summary>
public sealed record WebhookItem(WebhookRow Row)
{
    public string EventsText => string.Join(" · ", Row.Events);

    public string DeliveriesText => $"{Row.Delivered} entregas correctas · {Row.Failed} fallidas";

    public string StatusText => Row.IsActive ? "Activo" : "Desactivado";

    public string StatusBrush => Row.IsActive ? "Success" : "StatusInactive";

    public string StatusSoftBrush => StatusBrush + "Soft";

    public string LastText => Row.LastAttemptAt is { } at ? $"Último intento {Fmt.DateTime(at)}" : "Sin entregas todavía";
}

/// <summary>Fila de un intento de entrega.</summary>
public sealed record DeliveryItem(WebhookDeliveryRow Row)
{
    public string AtText => Fmt.DateTime(Row.AttemptedAt);

    public string ResultText => Row.Succeeded ? $"✔ {Row.StatusCode}" : $"✖ {Row.StatusCode?.ToString() ?? "sin respuesta"}";

    public string ResultBrush => Row.Succeeded ? "Success" : "Danger";

    public string DurationText => $"{Row.DurationMs} ms";
}

/// <summary>Casilla de un alcance o evento.</summary>
public sealed class OptionCheck(string code, string description, bool isChecked = false) : ObservableObject
{
    private bool _checked = isChecked;

    public string Code { get; } = code;

    public string Description { get; } = description;

    public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
}

/// <summary>
/// V4 · Integraciones B2B (API Gateway): API Keys para el e-commerce o el ERP (el token se ve UNA sola vez), webhooks
/// firmados (el secreto también) y el historial de entregas para diagnosticar una integración.
/// </summary>
public sealed class IntegrationsViewModel : PageViewModel
{
    private int _tab;
    private ApiKeyItem? _selectedKey;
    private WebhookItem? _selectedHook;
    private ApiKeyEditor? _keyEditor;
    private WebhookEditor? _hookEditor;
    private IntegrationCatalog? _catalog;

    public IntegrationsViewModel(AppServices app)
        : base(app, "integraciones", "Integraciones", "API Keys, webhooks y entregas para e-commerce y ERP", Glyphs.Link)
    {
        NewKey = new AsyncRelayCommand(OpenKeyEditorAsync);
        Revoke = new AsyncRelayCommand(RevokeAsync, () => _selectedKey?.Row.IsUsable == true);
        NewHook = new AsyncRelayCommand(OpenHookEditorAsync);
        Rotate = new AsyncRelayCommand(RotateAsync, () => _selectedHook?.Row.IsActive == true);
        Disable = new AsyncRelayCommand(DisableAsync, () => _selectedHook?.Row.IsActive == true);
    }

    /// <summary>0 = API Keys, 1 = Webhooks, 2 = Entregas.</summary>
    public int Tab
    {
        get => _tab;
        set
        {
            if (Set(ref _tab, value))
            {
                OnPropertiesChanged(nameof(IsKeysTab), nameof(IsHooksTab), nameof(IsDeliveriesTab));
            }
        }
    }

    public bool IsKeysTab { get => _tab == 0; set { if (value) { Tab = 0; } } }

    public bool IsHooksTab { get => _tab == 1; set { if (value) { Tab = 1; } } }

    public bool IsDeliveriesTab { get => _tab == 2; set { if (value) { Tab = 2; } } }

    public KpiCard KeysKpi { get; } = new("API Keys activas", Glyphs.Key);

    public KpiCard HooksKpi { get; } = new("Webhooks activos", Glyphs.Link, "Info", "InfoSoft");

    public KpiCard DeliveredKpi { get; } = new("Entregas correctas", Glyphs.CheckCircle, "Success", "SuccessSoft");

    public KpiCard FailedKpi { get; } = new("Entregas fallidas", Glyphs.Warning, "Danger", "DangerSoft");

    public BulkObservableCollection<ApiKeyItem> Keys { get; } = [];

    public BulkObservableCollection<WebhookItem> Hooks { get; } = [];

    public BulkObservableCollection<DeliveryItem> Deliveries { get; } = [];

    public ApiKeyItem? SelectedKey { get => _selectedKey; set => Set(ref _selectedKey, value); }

    public WebhookItem? SelectedHook { get => _selectedHook; set => Set(ref _selectedHook, value); }

    public ApiKeyEditor? KeyEditor
    {
        get => _keyEditor;
        private set
        {
            if (Set(ref _keyEditor, value))
            {
                OnPropertiesChanged(nameof(IsEditing), nameof(IsEditingKey));
            }
        }
    }

    public WebhookEditor? HookEditor
    {
        get => _hookEditor;
        private set
        {
            if (Set(ref _hookEditor, value))
            {
                OnPropertiesChanged(nameof(IsEditing), nameof(IsEditingHook));
            }
        }
    }

    public bool IsEditing => _keyEditor is not null || _hookEditor is not null;

    public bool IsEditingKey => _keyEditor is not null;

    public bool IsEditingHook => _hookEditor is not null;

    public string GatewayHint => App.Session.IsCloud
        ? $"Las integraciones llaman al API Gateway de su servidor (p. ej. https://api.{App.Session.Connection.Server}/v1) con la llave en «Authorization: Bearer»."
        : "Las integraciones llaman al API Gateway (por defecto http://localhost:5090/v1 en este equipo) con la llave en «Authorization: Bearer».";

    public AsyncRelayCommand NewKey { get; }

    public AsyncRelayCommand Revoke { get; }

    public AsyncRelayCommand NewHook { get; }

    public AsyncRelayCommand Rotate { get; }

    public AsyncRelayCommand Disable { get; }

    protected override async Task LoadCoreAsync(bool force)
    {
        _catalog ??= await App.SendAsync(new GetIntegrationCatalogQuery());
        var keys = await App.SendAsync(new GetApiKeysQuery());
        var hooks = await App.SendAsync(new GetWebhooksQuery());
        var deliveries = await App.SendAsync(new GetWebhookDeliveriesQuery(null, 300));
        Keys.ReplaceAll(keys.Select(k => new ApiKeyItem(k)));
        Hooks.ReplaceAll(hooks.Select(h => new WebhookItem(h)));
        Deliveries.ReplaceAll(deliveries.Select(d => new DeliveryItem(d)));
        KeysKpi.Value = keys.Count(k => k.IsUsable).ToString("N0", Fmt.Culture);
        KeysKpi.Detail = keys.Count(k => k.LastUsedAt is not null) is var used and > 0 ? $"{used} en uso" : "Ninguna usada todavía";
        HooksKpi.Value = hooks.Count(h => h.IsActive).ToString("N0", Fmt.Culture);
        HooksKpi.Detail = $"{hooks.Sum(h => h.Events.Count)} suscripciones a eventos";
        DeliveredKpi.Value = hooks.Sum(h => h.Delivered).ToString("N0", Fmt.Culture);
        DeliveredKpi.Detail = "Firmadas con HMAC-SHA256";
        FailedKpi.Value = hooks.Sum(h => h.Failed).ToString("N0", Fmt.Culture);
        FailedKpi.Detail = "Se reintentan hasta 8 veces";
    }

    internal void CloseEditors()
    {
        KeyEditor = null;
        HookEditor = null;
    }

    internal async Task AfterChangeAsync()
    {
        CloseEditors();
        await LoadAsync(force: true);
    }

    private async Task<IReadOnlyList<BranchRow>> VisibleBranchesAsync() =>
        (await App.SendAsync(new GetBranchesQuery())).Where(b => b.IsVisible && b.IsActive).ToList();

    private async Task OpenKeyEditorAsync()
    {
        try
        {
            _catalog ??= await App.SendAsync(new GetIntegrationCatalogQuery());
            HookEditor = null;
            KeyEditor = new ApiKeyEditor(this, App, _catalog, await VisibleBranchesAsync());
            Tab = 0;
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir la llave nueva", AppServices.Describe(ex));
        }
    }

    private async Task OpenHookEditorAsync()
    {
        try
        {
            _catalog ??= await App.SendAsync(new GetIntegrationCatalogQuery());
            KeyEditor = null;
            HookEditor = new WebhookEditor(this, App, _catalog, await VisibleBranchesAsync());
            Tab = 1;
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo abrir el webhook nuevo", AppServices.Describe(ex));
        }
    }

    private async Task RevokeAsync()
    {
        var key = _selectedKey!;
        if (!await App.Dialogs.ConfirmAsync($"Revocar «{key.Row.Name}»", $"La llave {key.PrefixText} deja de funcionar de inmediato en el API " +
                "Gateway. No se borra: queda en la auditoría.", "Revocar", isDanger: true, glyph: Glyphs.Key))
        {
            return;
        }
        if (await RunAsync(() => App.SendAsync(new RevokeApiKeyCommand(key.Row.Id)), "No se pudo revocar"))
        {
            App.Notify.Success("Llave revocada", key.Row.Name);
            await LoadAsync(force: true);
        }
    }

    private async Task RotateAsync()
    {
        var hook = _selectedHook!;
        if (!await App.Dialogs.ConfirmAsync("Rotar el secreto", $"Se genera un secreto nuevo para {hook.Row.Url}. Durante 24 horas cada entrega " +
                "lleva las dos firmas (la anterior y la nueva) para que su sistema cambie sin cortes.", "Rotar secreto", glyph: Glyphs.Key))
        {
            return;
        }
        try
        {
            var result = await App.SendAsync(new RotateWebhookSecretCommand(hook.Row.Id));
            await App.Dialogs.ShowSecretAsync("Secreto nuevo del webhook", result.Message, result.Secret,
                ["Se muestra UNA sola vez: cópielo ahora en la configuración de su sistema."]);
            await LoadAsync(force: true);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            App.Notify.Error("No se pudo rotar el secreto", AppServices.Describe(ex));
        }
    }

    private async Task DisableAsync()
    {
        var hook = _selectedHook!;
        if (!await App.Dialogs.ConfirmAsync("Desactivar webhook", $"{hook.Row.Url} deja de recibir eventos. Su historial de entregas se conserva.",
                "Desactivar", isDanger: true))
        {
            return;
        }
        if (await RunAsync(() => App.SendAsync(new DisableWebhookCommand(hook.Row.Id)), "No se pudo desactivar"))
        {
            App.Notify.Success("Webhook desactivado", hook.Row.Url);
            await LoadAsync(force: true);
        }
    }
}

/// <summary>Opción de sucursal (null = todas las del alcance).</summary>
public sealed record BranchChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Llave nueva: nombre, alcances, sucursal y vencimiento. El token se muestra una sola vez.</summary>
public sealed class ApiKeyEditor : ObservableObject
{
    private readonly IntegrationsViewModel _owner;
    private readonly AppServices _app;
    private string _name = "Tienda en línea";
    private BranchChoice _branch;
    private Choice<int?> _expiry;
    private string? _error;

    public ApiKeyEditor(IntegrationsViewModel owner, AppServices app, IntegrationCatalog catalog, IReadOnlyList<BranchRow> branches)
    {
        _owner = owner;
        _app = app;
        Scopes = catalog.Scopes.Select(s => new OptionCheck(s.Code, s.Description, s.Code is ApiScopesText.CatalogRead or ApiScopesText.StockRead)).ToList();
        Branches = [new BranchChoice(null, "Todas mis sucursales"), .. branches.Select(b => new BranchChoice(b.Code, $"{b.Code} · {b.Name}"))];
        _branch = Branches[0];
        Expiries = [new("Sin vencimiento", null), new("30 días", 30), new("90 días", 90), new("1 año", 365)];
        _expiry = Expiries[2];
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditors);
    }

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty); }

    public IReadOnlyList<OptionCheck> Scopes { get; }

    public IReadOnlyList<BranchChoice> Branches { get; }

    public BranchChoice Branch { get => _branch; set => Set(ref _branch, value ?? Branches[0]); }

    public IReadOnlyList<Choice<int?>> Expiries { get; }

    public Choice<int?> Expiry { get => _expiry; set => Set(ref _expiry, value ?? Expiries[0]); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            var created = await _app.SendAsync(new CreateApiKeyCommand(_name.Trim(), Scopes.Where(s => s.IsChecked).Select(s => s.Code).ToList(), _branch.Code,
                _expiry.Value));
            await _owner.AfterChangeAsync();
            await _app.Dialogs.ShowSecretAsync($"API Key «{created.Name}»", "Copie el token y guárdelo en su sistema: se muestra UNA sola vez " +
                "(en M-INV solo queda su huella SHA-256).", created.Token,
                [$"Permisos efectivos: {string.Join(", ", created.EffectivePermissions)}", "Uso: Authorization: Bearer <token>"]);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}

/// <summary>Webhook nuevo: URL https, eventos y sucursal. El secreto de la firma se muestra una sola vez.</summary>
public sealed class WebhookEditor : ObservableObject
{
    private readonly IntegrationsViewModel _owner;
    private readonly AppServices _app;
    private string _url = "https://";
    private string _description = string.Empty;
    private BranchChoice _branch;
    private string? _error;

    public WebhookEditor(IntegrationsViewModel owner, AppServices app, IntegrationCatalog catalog, IReadOnlyList<BranchRow> branches)
    {
        _owner = owner;
        _app = app;
        Events = catalog.Events.Select(e => new OptionCheck(e.Code, e.Description, e.Code == "sale.completed")).ToList();
        Branches = [new BranchChoice(null, "Todas mis sucursales"), .. branches.Select(b => new BranchChoice(b.Code, $"{b.Code} · {b.Name}"))];
        _branch = Branches[0];
        Save = new AsyncRelayCommand(SaveAsync);
        Cancel = new RelayCommand(owner.CloseEditors);
    }

    public string Url { get => _url; set => Set(ref _url, value ?? string.Empty); }

    public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }

    public IReadOnlyList<OptionCheck> Events { get; }

    public IReadOnlyList<BranchChoice> Branches { get; }

    public BranchChoice Branch { get => _branch; set => Set(ref _branch, value ?? Branches[0]); }

    public string? Error { get => _error; private set => Set(ref _error, value); }

    public AsyncRelayCommand Save { get; }

    public RelayCommand Cancel { get; }

    private async Task SaveAsync()
    {
        Error = null;
        try
        {
            var created = await _app.SendAsync(new CreateWebhookCommand(_url.Trim(), Events.Where(e => e.IsChecked).Select(e => e.Code).ToList(),
                string.IsNullOrWhiteSpace(_description) ? null : _description.Trim(), _branch.Code));
            await _owner.AfterChangeAsync();
            await _app.Dialogs.ShowSecretAsync("Secreto del webhook", created.Message, created.Secret,
                ["Verifique X-MINV-Signature: t=<unix>,v1=<HMAC-SHA256 hex de \"t.cuerpo\"> con este secreto."]);
        }
        catch (Exception ex) when (AppServices.IsExpected(ex))
        {
            Error = AppServices.Describe(ex);
        }
    }
}

/// <summary>Códigos de alcance que se marcan por defecto en una llave nueva.</summary>
internal static class ApiScopesText
{
    public const string CatalogRead = "catalog:read";
    public const string StockRead = "stock:read";
}
