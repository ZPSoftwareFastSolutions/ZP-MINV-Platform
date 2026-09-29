using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using MINV.Application.Common;
using MINV.Application.Iam;
using MINV.Application.Storefront;
using MINV.Domain.Iam;
using MINV.Domain.Sales;

namespace MINV.Application.Accounts;

// =====================================================================================================================
// V7 · Cuentas de cliente de la tienda web (reglas P-03 y P-04). Contratos: el registro (previo a la sesión, como el inicio
// de sesión: NO viaja por RPC) y los casos de uso de la cuenta, que operan SIEMPRE sobre el cliente ligado al usuario de la
// sesión (sales.customer_accounts). Ninguno recibe un identificador de cliente ni de usuario. Los manejadores viven en
// AccountRegistration.cs y AccountUseCases.cs.
// =====================================================================================================================

/// <summary>Reglas de entrada de las cuentas de cliente (forma de los datos; las reglas de negocio siguen en el dominio).</summary>
public static partial class AccountRules
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MaxNameLength = 120;

    public const string PasswordLengthMessage = "La contraseña debe tener de 8 a 128 caracteres.";
    public const string PasswordMixMessage = "La contraseña debe combinar letras y números.";
    public const string EmailMessage = "El correo no es válido.";
    public const string DocumentPairMessage = "Indique el tipo y el número de documento juntos (o ninguno).";
    public const string ComplementMessage = "El complemento solo se usa con la cédula de identidad (CI).";

    /// <summary>Código de error de un correo que ya tiene cuenta.</summary>
    public const string EmailTaken = "account.email_taken";

    /// <summary>Código de error de un usuario sin cuenta de cliente (el personal, por ejemplo).</summary>
    public const string Missing = "account.missing";

    /// <summary>Prefijo del código de los clientes que se registran en la web: WEB-000001.</summary>
    public const string CustomerCodePrefix = "WEB";

    /// <summary>¿Contraseña admitida? De 8 a 128 caracteres, con al menos una letra y un número.</summary>
    public static bool IsStrongPassword(string? password) =>
        password is { Length: >= MinPasswordLength and <= MaxPasswordLength } && password.Any(char.IsLetter) && password.Any(char.IsDigit);

    /// <summary>¿Correo con forma de correo? Una sola dirección, sin espacios, sin caracteres de control y sin los
    /// caracteres que en una cabecera de correo separan direcciones o abren un nombre visible.</summary>
    public static bool IsEmail(string? email)
    {
        var text = email?.Trim();
        return text is { Length: > 0 and <= 254 } && !Domain.Common.Guard.HasControlCharacters(text)
               && text.IndexOfAny(HeaderCharacters) < 0 && EmailPattern().IsMatch(text);
    }

    private static readonly char[] HeaderCharacters = ['<', '>', '"', ',', ';', ':', '(', ')', '[', ']', '\\'];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}

// --------------------------------------------------------------------------------------------------- registro
/// <summary>
/// Registro de una cuenta de cliente (regla P-03). Es previo a la sesión, como <see cref="LoginCommand"/>: fija la empresa
/// por su código (el servidor lo toma de <c>Minv:Web:TenantCode</c>, nunca del cuerpo de la petición) y crea, en UNA
/// transacción, el usuario, su credencial (PBKDF2), el rol <c>CLIENTE</c>, la asignación a la sucursal de la tienda, el
/// cliente <c>WEB-000001</c> (correlativo), la cuenta que los une y la sesión. SIEMPRE con el rol CLIENTE: el comando no
/// tiene dónde recibir un rol, una sucursal elegida por el cliente ni permisos. No viaja por RPC.
/// </summary>
/// <param name="BranchCode">Sucursal de las cuentas de cliente (de <c>Minv:Web:BranchCode</c>); sin valor, la del almacén principal.</param>
public sealed record RegisterCustomerAccountCommand(string TenantCode, string Name, string Email, string Phone, string Password,
    string? BranchCode = null, string MachineName = "web", string ClientVersion = "web")
    : IRequest<CustomerAccountSession>, IAuditableRequest
{
    // La contraseña NUNCA va a la auditoría; el teléfono, enmascarado como en las reservas (reglas P-03 y S-06)
    public object AuditDetails => new { TenantCode, Name, Email, Phone = CreateStorefrontReservationCommand.Mask(Phone?.Trim()), BranchCode, MachineName };

    // Un record imprime todos sus campos: este no muestra la contraseña ni el teléfono
    public override string ToString() => $"RegisterCustomerAccountCommand {{ TenantCode = {TenantCode}, Email = {Email} }}";
}

/// <summary>Cuenta recién creada con su sesión abierta (el servidor emite el token y lo pone en la cookie).</summary>
public sealed record CustomerAccountSession(LoginResult Login, string CustomerCode) : IAuditableResponse
{
    object IAuditableResponse.AuditResult => new { Login.UserId, Login.SessionId, Login.Roles, CustomerCode };
}

public sealed class RegisterCustomerAccountValidator : AbstractValidator<RegisterCustomerAccountCommand>
{
    public RegisterCustomerAccountValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty().WithMessage("La tienda no está configurada para registrar cuentas.");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique su nombre.")
            .Must(n => n.Trim().Length >= 2).WithMessage("El nombre debe tener al menos 2 caracteres.")
            .Must(n => n.Trim().Length <= AccountRules.MaxNameLength).WithMessage($"El nombre supera {AccountRules.MaxNameLength} caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El nombre " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique su correo.")
            .Must(AccountRules.IsEmail).WithMessage(AccountRules.EmailMessage);
        RuleFor(x => x.Phone).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique un teléfono o WhatsApp.")
            .MaximumLength(30).WithMessage("El teléfono supera 30 caracteres.");
        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique una contraseña.")
            .Must(p => p.Length is >= AccountRules.MinPasswordLength and <= AccountRules.MaxPasswordLength).WithMessage(AccountRules.PasswordLengthMessage)
            .Must(AccountRules.IsStrongPassword).WithMessage(AccountRules.PasswordMixMessage);
        RuleFor(x => x.MachineName).MaximumLength(100);
        RuleFor(x => x.ClientVersion).MaximumLength(30);
    }
}

// --------------------------------------------------------------------------------------------------- mi cuenta
/// <summary>Datos de la cuenta del cliente de la sesión: nombre, correo con el que ingresa, teléfono y documento para la
/// factura (tipo del SIN 1 CI, 2 CEX, 3 PAS, 4 OD, 5 NIT; número y complemento).</summary>
public sealed record MyAccountView(string Name, string Email, string? Phone, int? DocumentType, string? DocumentNumber, string? Complement,
    string CustomerCode) : IAuditableResponse
{
    // En la auditoría, el teléfono y el documento van enmascarados (regla S-06)
    object IAuditableResponse.AuditResult => new
    {
        Name, Email, Phone = Phone is null ? null : CreateStorefrontReservationCommand.Mask(Phone), DocumentType,
        DocumentNumber = DocumentNumber is null ? null : CreateStorefrontReservationCommand.Mask(DocumentNumber),
        Complement = Complement is null ? null : "***", CustomerCode,
    };

    public override string ToString() => $"MyAccountView {{ CustomerCode = {CustomerCode} }}";
}

/// <summary>Los datos de MI cuenta (la del usuario de la sesión, regla P-04).</summary>
[RequiresPermission(PermissionCodes.AccountManage)]
public sealed record GetMyAccountQuery : IRequest<MyAccountView>;

/// <summary>Actualiza MIS datos: nombre, teléfono (boliviano, con la normalización de las reservas) y documento para la
/// factura (opcional; mismas reglas del SIN que un cliente del escritorio). El correo no cambia desde aquí: es con el que se
/// ingresa.</summary>
[RequiresPermission(PermissionCodes.AccountManage)]
public sealed record UpdateMyAccountCommand(string Name, string Phone, int? DocumentType = null, string? DocumentNumber = null, string? Complement = null)
    : IRequest<MyAccountView>, IAuditableRequest
{
    public object AuditDetails => new
    {
        Name, Phone = CreateStorefrontReservationCommand.Mask(Phone?.Trim()), DocumentType,
        DocumentNumber = string.IsNullOrWhiteSpace(DocumentNumber) ? null : CreateStorefrontReservationCommand.Mask(DocumentNumber.Trim()),
        Complement = string.IsNullOrWhiteSpace(Complement) ? null : "***",
    };

    public override string ToString() => $"UpdateMyAccountCommand {{ DocumentType = {DocumentType} }}";
}

public sealed class UpdateMyAccountValidator : AbstractValidator<UpdateMyAccountCommand>
{
    public UpdateMyAccountValidator()
    {
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique su nombre.")
            .Must(n => n.Trim().Length >= 2).WithMessage("El nombre debe tener al menos 2 caracteres.")
            .Must(n => n.Trim().Length <= AccountRules.MaxNameLength).WithMessage($"El nombre supera {AccountRules.MaxNameLength} caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El nombre " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Phone).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indique un teléfono o WhatsApp.")
            .MaximumLength(30).WithMessage("El teléfono supera 30 caracteres.");
        RuleFor(x => x.DocumentType).InclusiveBetween(1, 5).WithMessage("El tipo de documento va de 1 (CI) a 5 (NIT).");
        RuleFor(x => x).Must(x => x.DocumentType is null == string.IsNullOrWhiteSpace(x.DocumentNumber)).WithMessage(AccountRules.DocumentPairMessage);
        RuleFor(x => x.DocumentNumber).MaximumLength(20).WithMessage("El número de documento supera 20 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El número de documento " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Complement).MaximumLength(5).WithMessage("El complemento tiene como máximo 5 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El complemento " + ReservationRules.ControlCharacters);
        RuleFor(x => x).Must(x => string.IsNullOrWhiteSpace(x.Complement) || x.DocumentType == 1).WithMessage(AccountRules.ComplementMessage);
    }
}

// --------------------------------------------------------------------------------------------------- mis reservas
/// <summary>MIS reservas (las del cliente de la sesión, por <c>CustomerId</c>), de la más nueva a la más antigua: armados y
/// carritos que alguna vez se reservaron, con su estado (Reservada, Vendida, Cancelada o Vencida).</summary>
[RequiresPermission(PermissionCodes.AccountManage)]
public sealed record GetMyReservationsQuery : IRequest<IReadOnlyList<StorefrontReservationView>>;

/// <summary>Libera MI reserva: solo si es del cliente de la sesión (la de otro cliente «no existe») y sigue reservada. El
/// stock vuelve a estar disponible.</summary>
[RequiresPermission(PermissionCodes.AccountManage)]
public sealed record CancelMyReservationCommand(string Number) : IRequest<StorefrontReservationView>, IAuditableRequest
{
    public object AuditDetails => new { Number };
}

public sealed class CancelMyReservationValidator : AbstractValidator<CancelMyReservationCommand>
{
    public CancelMyReservationValidator() =>
        RuleFor(x => x.Number).NotEmpty().WithMessage("Indique el número de la reserva.").MaximumLength(40);
}

/// <summary>
/// Reserva con los datos de MI cuenta (nombre, teléfono y correo del cliente de la sesión) y queda ligada a mi cliente
/// (<c>CustomerId</c>). Reutiliza la reserva de la tienda (reglas S-03 y P-05): armado (<c>Build</c>) o carrito
/// (<c>Cart</c>, por defecto), una reserva de stock por línea, todo o nada. La idempotencia la da el <c>requestId</c> del RPC.
/// </summary>
[RequiresPermission(PermissionCodes.AccountReserve)]
public sealed record CreateMyReservationCommand(IReadOnlyList<StorefrontReservationLineInput> Lines, PcBuildKind Kind = PcBuildKind.Cart,
    int? HoldDays = null, string? Notes = null, string? Name = null) : IRequest<StorefrontReservationView>, IAuditableRequest
{
    public object AuditDetails => new { Lines, Kind, HoldDays, Notes, Name };
}

public sealed class CreateMyReservationValidator : AbstractValidator<CreateMyReservationCommand>
{
    public CreateMyReservationValidator()
    {
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Agregue al menos un producto a la reserva.")
            .Must(l => l.Count <= PcBuild.MaxLines).WithMessage($"Una reserva admite como máximo {PcBuild.MaxLines} líneas.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Sku).NotEmpty().WithMessage("Cada producto necesita su SKU.").MaximumLength(60);
            l.RuleFor(x => x.Quantity).InclusiveBetween(1, PcBuild.MaxQuantity).WithMessage($"La cantidad de cada producto va de 1 a {PcBuild.MaxQuantity}.");
        });
        RuleFor(x => x.Kind).IsInEnum().WithMessage("El tipo de reserva no existe.");
        RuleFor(x => x.HoldDays).InclusiveBetween(1, StorefrontOptions.HoldDaysLimit).WithMessage(ReservationRules.HoldDaysMessage);
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Las notas superan 500 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("Las notas van en una sola línea: " + ReservationRules.ControlCharacters);
        RuleFor(x => x.Name).MaximumLength(150).WithMessage("El nombre de la reserva supera 150 caracteres.")
            .Must(ReservationRules.IsPlain).WithMessage("El nombre de la reserva " + ReservationRules.ControlCharacters);
    }
}
