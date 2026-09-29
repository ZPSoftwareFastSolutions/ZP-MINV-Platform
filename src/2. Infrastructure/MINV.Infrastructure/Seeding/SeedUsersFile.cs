using System.Text;
using MINV.Domain.Iam;

namespace MINV.Infrastructure.Seeding;

/// <summary>
/// Texto del archivo de usuarios de prueba (<c>%LOCALAPPDATA%\M-INV\usuarios-prueba.txt</c>) que escribe
/// <c>minv datos-prueba --credenciales</c> (lo llama <c>tools\bd_local.ps1</c>): empresa, resumen de la carga y una fila por
/// usuario del personal con su rol, nombre, correo, contraseña y sucursales. V7: las cuentas de cliente de la tienda web van en
/// su propia sección (<see cref="CustomersTitle"/>) con las mismas columnas, salvo las sucursales.
/// <para>Lectores: el escritorio, para las capturas con la base local (<c>ScreenshotRunner.LocalUsers</c>, variable
/// <c>MINV_CAPTURAS_USUARIOS</c>), busca «código de empresa: X» y la primera fila que EMPIEZA con «Administrador» o «Cajero» (el
/// correo es la primera palabra con @ y la contraseña, la siguiente). Las filas de clientes empiezan con «Cliente» y la sección
/// va después del personal: el lector no cambia. Las contraseñas son de PRUEBA, aleatorias en cada carga (regla A-13).</para>
/// </summary>
public static class SeedUsersFile
{
    /// <summary>V7 · Título de la sección de las cuentas de cliente de la tienda web (regla P-13).</summary>
    public const string CustomersTitle = "Clientes de la tienda web";

    public static string Text(SeedResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var sb = new StringBuilder();
        sb.AppendLine("M-INV · base de datos de PRUEBA · usuarios");
        sb.AppendLine($"Empresa: {r.CompanyName} · código de empresa: {r.TenantCode}");
        sb.AppendLine($"Sucursales: {string.Join(", ", r.Branches)} (CM = casa matriz La Paz, CB = Cochabamba, SC = Santa Cruz)");
        sb.AppendLine($"Datos: {r.Products} productos con imagen, {r.Suppliers} proveedores, {r.Customers} clientes, {r.Tickets} ventas en caja, " +
                      $"{r.ExternalOrders} pedidos web, {r.Transfers} transferencias, {r.PurchaseOrders} órdenes de compra, {r.Movements} movimientos y " +
                      $"{r.JournalEntries} asientos ({r.From:dd/MM/yyyy} a {r.To:dd/MM/yyyy}).");
        if (r.Tech is { } t)
        {
            // V4.2 · Edición Tecnología: fichas técnicas, series e IMEI, garantías (RMA) y armados de PC
            sb.AppendLine($"Tecnología: {t.Categories} categorías, {t.SpecDefinitions} especificaciones ({t.SpecValues} valores), {t.Brands} marcas, " +
                          $"{t.SerializedProducts} productos con serie o IMEI ({t.Serials} series, {t.SerialsInStock} en stock), {t.WarrantyClaims} casos RMA, " +
                          $"{t.PcBuilds} armados de PC ({t.PcBuildsSold} vendidos, {t.PcBuildsIncompatible} incompatibles marcados) y {t.Returns} devoluciones.");
        }
        if (r.Billing is { } b)
        {
            // V4.1 · Facturación SIAT (el token de simulación NO va aquí: está en el archivo de claves de integración)
            sb.AppendLine($"Facturación SIAT (pruebas, simulador del SIN {b.SimulatorUrl}): NIT {b.Nit} · {b.BusinessName} · desde el {b.From:dd/MM/yyyy}: " +
                          $"{b.Documents} documentos ({b.ValidInvoices} facturas válidas, {b.OfflineRecovered} fuera de línea recuperadas, " +
                          $"{b.CafcInvoices} CAFC, {b.CreditNotes} notas crédito-débito, {b.Voided} anuladas, {b.Reverted} revertida) · " +
                          $"{b.SupplierInvoices} facturas de proveedores · {b.PointsOfSale} puntos de venta.");
        }
        sb.AppendLine();
        sb.AppendLine($"{"Rol",-15} {"Nombre",-26} {"Correo",-44} {"Contraseña",-16} Sucursales");
        sb.AppendLine(new string('-', 128));
        foreach (var u in r.Users.Where(u => u.RoleCode != RoleCodes.Customer))
        {
            sb.AppendLine($"{u.RoleName,-15} {u.Name,-26} {u.Email,-44} {u.Password,-16} {u.Branches}");
        }
        var customers = r.Users.Where(u => u.RoleCode == RoleCodes.Customer).ToList();
        if (customers.Count > 0)
        {
            // V7 · Cuentas de cliente (regla P-13): mismas columnas que el personal, sin sucursales (compran en la de la tienda)
            sb.AppendLine();
            sb.AppendLine($"{CustomersTitle}: ingresan en la web con «Ingresar» (Mi cuenta: sus datos y sus reservas); no sirven para el escritorio.");
            sb.AppendLine($"{"Rol",-15} {"Nombre",-26} {"Correo",-44} Contraseña");
            sb.AppendLine(new string('-', 104));
            foreach (var u in customers)
            {
                sb.AppendLine($"{u.RoleName,-15} {u.Name,-26} {u.Email,-44} {u.Password}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Son contraseñas de PRUEBA generadas al azar para esta base local: no las use en producción.");
        if (r.StorefrontUser is { } storefront)
        {
            // V6 · Usuario técnico de la tienda web: sin contraseña utilizable (el API Gateway lo autentica por configuración)
            sb.AppendLine();
            sb.AppendLine($"Tienda web: usuario técnico {storefront} (rol Tienda web) SIN contraseña utilizable: el API Gateway lo usa por " +
                          $"configuración (Minv:Storefront:TenantCode={r.TenantCode}) para /storefront/v1; no sirve para iniciar sesión en el escritorio.");
            if (r.Tech is { } tech)
            {
                sb.AppendLine($"Tienda web: {tech.PcBuildsPublished} armados publicados, {tech.WebReservationsActive} reserva(s) web activa(s) y " +
                              $"{tech.WebReservationsExpired} vencida(s) (ver Armador de PC › Cotizaciones, canal Web).");
            }
        }
        if (r.Web is { } web)
        {
            // V7 · Plataforma web: cuentas de cliente, carritos y la cola de correos (salen con el API Gateway y Minv:Mail)
            sb.AppendLine($"Tienda web (V7): {web.Accounts} cuentas de cliente con {web.AccountReservations} reservas propias, {web.ActiveCarts} carrito vigente " +
                          $"de un solo monitor, {web.ExpiredCarts} vencido y {web.CounterCarts} de mostrador; {web.QueuedMails} correos de confirmación en la " +
                          "cola (los envía el API Gateway con Minv:Mail).");
        }
        return sb.ToString();
    }
}
