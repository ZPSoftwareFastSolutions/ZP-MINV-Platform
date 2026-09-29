using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Domain.Billing;
using MINV.Domain.Common;

namespace MINV.Application.Integration;

/// <summary>
/// V7 · Servidor SMTP de una empresa: la configuración de <c>billing.mail_settings</c> (la que se carga en el escritorio con
/// <c>SaveMailSettingsCommand</c>, contraseña cifrada con <see cref="ISecretProtector"/>, regla F-12). La usan el correo al
/// comprador de la facturación (V4.1) y el despachador de la cola de correos de las reservas (V7), que si la empresa no tiene
/// una activa usa la del servidor (<c>Minv:Mail</c>).
/// </summary>
public static class MailServers
{
    /// <summary>Servidor SMTP ACTIVO de la empresa fijada en <paramref name="db"/>, con la contraseña descifrada SOLO en memoria,
    /// o null si no tiene uno o está desactivado. Sin <paramref name="protector"/> y con contraseña guardada lanza
    /// <c>mail.no_keys</c>; con una clave maestra ausente o rotada, lo que lance el protector.</summary>
    public static async Task<MailServer?> CompanyAsync(IMinvDbContext db, ISecretProtector? protector, CancellationToken ct)
    {
        var settings = await db.Set<MailSettings>().FirstOrDefaultAsync(ct);
        if (settings is not { IsEnabled: true })
        {
            return null;
        }
        string? password = null;
        if (settings.PasswordCiphertext is { } cipher && settings.PasswordKeyId is { } keyId)
        {
            if (protector is null)
            {
                throw new DomainException("mail.no_keys",
                    "Este equipo no tiene la clave maestra para leer la contraseña del correo: el envío lo hace el servidor.");
            }
            password = protector.Unprotect(cipher, keyId);
        }
        return new MailServer(settings.Host, settings.Port, settings.UseSsl, settings.UserName, password, settings.FromAddress, settings.FromName);
    }
}
