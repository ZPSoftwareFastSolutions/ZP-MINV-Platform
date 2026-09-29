using MINV.Domain.Common;

namespace MINV.Domain.Sales;

/// <summary>
/// V7 · Cuenta de cliente de la tienda web (regla P-04): une UN usuario (<c>iam.users</c>, con el que ingresa) con UN cliente
/// (<c>sales.customers</c>, a cuyo nombre quedan sus reservas y sus facturas). Es 1 a 1 en los dos sentidos: un usuario tiene
/// a lo sumo una cuenta y un cliente pertenece a lo sumo a un usuario. Los casos de uso <c>account.*</c> llegan al cliente
/// SOLO a través de esta fila y del usuario de la sesión: nunca reciben un identificador de cliente.
/// <para>No guarda nada derivable: el nombre, el correo, el teléfono y el documento viven en el usuario y en el cliente. El
/// vínculo no cambia después de crearlo (una cuenta no se «pasa» a otro usuario ni a otro cliente).</para>
/// </summary>
public sealed class CustomerAccount : Entity
{
    private CustomerAccount()
    {
    }

    public CustomerAccount(Guid tenantId, Guid userId, Guid customerId)
        : base(tenantId)
    {
        UserId = Guard.NotEmpty(userId, nameof(userId));
        CustomerId = Guard.NotEmpty(customerId, nameof(customerId));
    }

    /// <summary>Usuario que ingresa a la tienda web (rol CLIENTE).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Cliente al que pertenecen sus reservas y sus compras.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>¿La cuenta es de este usuario? (la única pregunta que hacen los casos de uso de la cuenta).</summary>
    public bool BelongsTo(Guid userId) => userId != Guid.Empty && UserId == userId;
}
