using Microsoft.EntityFrameworkCore;
using MINV.Application.Abstractions;
using MINV.Application.Integration;
using MINV.Infrastructure.Persistence;

namespace MINV.Infrastructure.Integration;

/// <summary>V4 · Emite el token de una sesión recién abierta por el servidor en la nube (solo se guarda su hash).</summary>
public static class CloudSessions
{
    public static async Task<string> IssueTokenAsync(MinvWriteDbContext db, Guid sessionId, IClock clock, CancellationToken ct)
    {
        var token = ApiKeyTokens.NewSessionToken();
        var session = await db.Sessions.FirstAsync(s => s.Id == sessionId, ct);
        session.IssueToken(ApiKeyTokens.Hash(token), clock.UtcNow + CloudSessionAuthenticator.SlidingExpiration);
        await db.SaveChangesAsync(ct);
        return token;
    }
}
