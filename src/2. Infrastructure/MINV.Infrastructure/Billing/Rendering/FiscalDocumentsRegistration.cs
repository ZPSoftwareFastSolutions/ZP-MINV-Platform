using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MINV.Application.Abstractions;
using MINV.Infrastructure.Billing.Xml;

namespace MINV.Infrastructure.Billing.Rendering;

/// <summary>V4.1 · Registro de los documentos fiscales: XML validado contra el XSD, GZIP/TAR/SHA-256 y PDF.</summary>
public static class FiscalDocumentsRegistration
{
    /// <summary>Serializador XML del SIN (<see cref="IFiscalDocumentSerializer"/>) y representación gráfica en PDF
    /// (<see cref="IFiscalDocumentRenderer"/>). Ambos sin estado: una instancia por proceso.</summary>
    public static IServiceCollection AddMinvFiscalDocuments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFiscalDocumentSerializer, SiatXmlSerializer>();
        services.TryAddSingleton<IFiscalDocumentRenderer, FiscalPdfRenderer>();
        return services;
    }
}
