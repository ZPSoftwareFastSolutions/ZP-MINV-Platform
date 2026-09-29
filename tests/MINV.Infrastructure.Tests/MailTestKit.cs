using MINV.Application.Abstractions;

namespace MINV.Infrastructure.Tests;

/// <summary>
/// V7 · Emisor de correo falso: guarda lo que se le pide enviar (así se prueba que NINGÚN caso de uso de reserva envía nada: el
/// correo sale del despachador después del COMMIT, regla B-08). Puede fallar a propósito para un destinatario (<see cref="FailFor"/>)
/// y cuenta cada envío pedido, logrado o no (<see cref="Attempted"/>). Lo comparten las pruebas de infraestructura y las de
/// integración (enlazado en <c>MINV.Integration.Tests</c>).
/// </summary>
public sealed class RecordingMailSender : IMailSender
{
    private readonly List<MailMessageSpec> _sent = [];
    private readonly List<MailMessageSpec> _attempted = [];
    private readonly Dictionary<string, Queue<Exception>> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Mensajes enviados (los que salieron bien).</summary>
    public IReadOnlyList<MailMessageSpec> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    /// <summary>Los enviados a <paramref name="recipient"/>.</summary>
    public IReadOnlyList<MailMessageSpec> SentTo(string recipient) =>
        Sent.Where(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Envíos pedidos a <paramref name="recipient"/>, logrados o fallidos.</summary>
    public int Attempted(string recipient)
    {
        lock (_sent)
        {
            return _attempted.Count(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Los próximos envíos a <paramref name="recipient"/> fallan con estas excepciones, en orden; después salen bien.</summary>
    public void FailFor(string recipient, params Exception[] failures)
    {
        lock (_sent)
        {
            if (!_failures.TryGetValue(recipient, out var queue))
            {
                _failures[recipient] = queue = new Queue<Exception>();
            }
            foreach (var failure in failures)
            {
                queue.Enqueue(failure);
            }
        }
    }

    public Task SendAsync(MailMessageSpec message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_sent)
        {
            _attempted.Add(message);
            if (_failures.TryGetValue(message.To, out var queue) && queue.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }
            _sent.Add(message);
        }
        return Task.CompletedTask;
    }
}
