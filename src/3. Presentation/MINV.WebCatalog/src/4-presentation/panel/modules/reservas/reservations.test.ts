// Módulo «Reservas» · funciones puras: qué filas son reservas y en qué estado están, el plazo (resaltado si vence en menos
// de 6 horas), el estado del correo, lo que se pide al servidor y lo que se filtra en la página (tipo, canal, estado, vence
// hoy/mañana en días de La Paz, sucursal, fechas y búsqueda por teléfono), el CSV, el resumen de la estadística, el
// teléfono y WhatsApp, y el formulario de la reserva en mostrador (validación y el pedido exacto de `ReserveCartCommand`).

import { describe, expect, it } from 'vitest';
import { buildCsv } from '@/4-presentation/panel/lib';
import {
  CSV_COLUMNS,
  EMPTY_CART,
  OTHER_REASON,
  RESERVATION_FILTERS,
  activeReservationsSummary,
  addCartLine,
  availabilityText,
  buildsRequest,
  canBeSold,
  cartProblems,
  cartTotal,
  customerOptions,
  filterReservations,
  formatPhone,
  holdInfo,
  isBolivianPhone,
  latestMails,
  listSummary,
  localPhoneDigits,
  releaseReason,
  remainingText,
  reservationState,
  sellPath,
  toReservationItems,
  toReserveCartPayload,
  whatsappUrl,
  type BuildRecord,
  type CartForm,
  type CustomerRecord,
  type MailRecord,
  type ProductRecord,
} from './reservations';

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');
const hours = (value: number) => new Date(NOW.getTime() + value * 3_600_000).toISOString();

function build(overrides: Partial<BuildRecord>): BuildRecord {
  return {
    id: 'b-1',
    number: 'RES-WEB-000001',
    name: 'Reserva de Juan Pérez',
    branchCode: 'CM',
    customer: null,
    status: 'Reserved',
    validUntil: '2026-10-06',
    isExpired: false,
    total: 1500,
    items: 2,
    isCompatible: true,
    createdAt: hours(-20),
    invoiceNumber: null,
    quotedWithErrors: false,
    channel: 'Web',
    contactName: 'Juan Pérez',
    contactPhone: '+59171234567',
    contactEmail: 'juan@correo.example',
    reservedUntil: hours(28),
    publishedToWeb: false,
    cancelReason: null,
    notes: null,
    reserved: 2,
    kind: 'Cart',
    buyerDocumentType: null,
    buyerDocumentNumber: null,
    buyerComplement: null,
    buyerName: null,
    ...overrides,
  };
}

function mail(overrides: Partial<MailRecord>): MailRecord {
  return {
    id: 'm-1',
    reservation: 'RES-WEB-000001',
    reservationKind: 'Cart',
    branchCode: 'CM',
    kind: 'ReservationConfirmed',
    kindText: 'Confirmación de reserva',
    recipient: 'juan@correo.example',
    status: 'Sent',
    statusText: 'Enviado',
    attempts: 1,
    maxAttempts: 5,
    lastError: null,
    requestedAt: hours(-20),
    nextAttemptAt: null,
    lastAttemptAt: hours(-20),
    completedAt: hours(-20),
    ...overrides,
  };
}

describe('reservas · estado, plazo y correo', () => {
  it('el estado sale del armado, de su plazo y del motivo del cierre', () => {
    expect(reservationState(build({}), NOW)).toBe('reservada');
    expect(reservationState(build({ reservedUntil: hours(-1) }), NOW)).toBe('vencida');
    expect(reservationState(build({ status: 'Cancelled', cancelReason: 'Vencida' }), NOW)).toBe('vencida');
    expect(reservationState(build({ status: 'Cancelled', cancelReason: 'El cliente desistió' }), NOW)).toBe('liberada');
    expect(reservationState(build({ status: 'Sold', invoiceNumber: 'F-CM-000010' }), NOW)).toBe('vendida');
  });

  it('el plazo se resalta si vence en menos de 6 horas y avisa si ya venció', () => {
    expect(holdInfo(build({ reservedUntil: hours(28) }), NOW)).toEqual({ text: '30/09/2026 14:00', tone: 'normal' });
    expect(holdInfo(build({ reservedUntil: hours(2.5) }), NOW)).toEqual({ text: '29/09/2026 12:30 · vence en 2 h 30 min', tone: 'soon' });
    expect(holdInfo(build({ reservedUntil: hours(-1) }), NOW)).toEqual({ text: 'Venció el 29/09/2026 09:00', tone: 'expired' });
    // Una reserva cerrada muestra su plazo sin resaltar.
    expect(holdInfo(build({ status: 'Sold', reservedUntil: hours(-1) }), NOW)).toEqual({ text: '29/09/2026 09:00', tone: 'none' });
    expect(holdInfo(build({ reservedUntil: null }), NOW)).toEqual({ text: '—', tone: 'none' });
    expect(remainingText(30_000)).toBe('menos de 1 min');
    expect(remainingText(45 * 60_000)).toBe('45 min');
    expect(remainingText(3 * 3_600_000)).toBe('3 h');
  });

  it('se vende en caja solo reservada y con los precios vigentes', () => {
    expect(canBeSold(build({}))).toBe(true);
    expect(canBeSold(build({ isExpired: true }))).toBe(false);
    expect(canBeSold(build({ status: 'Cancelled' }))).toBe(false);
    expect(sellPath('RES-WEB-000001')).toBe('caja?reserva=RES-WEB-000001');
  });

  it('el estado del correo es el del último correo de la reserva; sin correo, si tenía o no uno de contacto', () => {
    const mails = [mail({ id: 'm-1', status: 'Exhausted', requestedAt: hours(-20) }), mail({ id: 'm-2', status: 'Pending', requestedAt: hours(-1) })];
    expect(latestMails(mails).get('RES-WEB-000001')?.id).toBe('m-2');
    const [withMail, withoutMail, noEmail] = toReservationItems(
      [build({}), build({ number: 'RES-CM-000002', channel: 'Desktop' }), build({ number: 'RES-CM-000003', contactEmail: null })],
      mails,
      NOW,
    );
    expect(withMail.mailState).toBe('Pending');
    expect(withoutMail.mailState).toBe('sinEnvio');
    expect(noEmail.mailState).toBe('sinCorreo');
    // Si la cola no se pudo leer, queda sin datos.
    expect(toReservationItems([build({})], null, NOW)[0].mailState).toBe('sinDatos');
  });
});

describe('reservas · lista y filtros', () => {
  const rows = [
    build({ number: 'RES-WEB-000001', reservedUntil: hours(2) }),
    build({ number: 'RES-CM-000002', channel: 'Desktop', kind: 'Cart', contactName: 'Ana Rojas', contactPhone: '76543210', reservedUntil: hours(20), branchCode: 'CB' }),
    build({ number: 'ARM-WEB-000003', kind: 'Build', name: 'PC gamer', reservedUntil: hours(-3) }),
    build({ number: 'ARM-CM-000004', kind: 'Build', channel: 'Desktop', status: 'Sold', invoiceNumber: 'F-CM-000100', reservedUntil: hours(-30), createdAt: '2026-09-20T15:00:00Z' }),
    build({ number: 'RES-WEB-000005', status: 'Cancelled', cancelReason: 'El cliente desistió', reservedUntil: hours(-10) }),
    // Una cotización que nunca se reservó no es una reserva.
    build({ number: 'ARM-CM-000006', kind: 'Build', channel: 'Desktop', status: 'Quoted', reservedUntil: null }),
  ];
  const items = toReservationItems(rows, [], NOW);
  const numbers = (filters: Partial<typeof RESERVATION_FILTERS>) => filterReservations(items, { ...RESERVATION_FILTERS, ...filters }, NOW).map((item) => item.row.number);

  it('solo las que se reservaron alguna vez, con cliente y teléfono a la vista', () => {
    expect(items.map((item) => item.row.number)).toEqual(['RES-WEB-000001', 'RES-CM-000002', 'ARM-WEB-000003', 'ARM-CM-000004', 'RES-WEB-000005']);
    expect(items[1]).toMatchObject({ client: 'Ana Rojas', phone: '+591 76543210', whatsapp: 'https://wa.me/59176543210' });
  });

  it('pide al servidor el tipo, el canal y el estado cuando es uno solo', () => {
    expect(buildsRequest({ tipo: '', canal: '', estado: '', vence: '' })).toEqual({ status: null, channel: null, kind: null });
    expect(buildsRequest({ tipo: 'Cart', canal: 'Web', estado: 'reservada', vence: '' })).toEqual({ status: 'Reserved', channel: 'Web', kind: 'Cart' });
    expect(buildsRequest({ tipo: 'Build', canal: 'Desktop', estado: 'liberada', vence: '' })).toEqual({ status: 'Cancelled', channel: 'Desktop', kind: 'Build' });
    expect(buildsRequest({ tipo: '', canal: '', estado: 'vendida', vence: '' })).toEqual({ status: 'Sold', channel: null, kind: null });
    // «Vencida» junta reservadas con el plazo cumplido y cerradas por vencimiento: se pide todo.
    expect(buildsRequest({ tipo: '', canal: '', estado: 'vencida', vence: '' })).toEqual({ status: null, channel: null, kind: null });
    // «Vence hoy o mañana» solo puede ser una reservada.
    expect(buildsRequest({ tipo: '', canal: '', estado: '', vence: 'hoy' })).toEqual({ status: 'Reserved', channel: null, kind: null });
    expect(buildsRequest({ tipo: 'raro', canal: 'otro', estado: '', vence: 'vencidas' })).toEqual({ status: null, channel: null, kind: null });
  });

  it('filtra por tipo, canal, estado, sucursal y fechas', () => {
    expect(numbers({ tipo: 'Build' })).toEqual(['ARM-WEB-000003', 'ARM-CM-000004']);
    expect(numbers({ canal: 'Desktop' })).toEqual(['RES-CM-000002', 'ARM-CM-000004']);
    expect(numbers({ estado: 'reservada' })).toEqual(['RES-WEB-000001', 'RES-CM-000002']);
    expect(numbers({ estado: 'vencida' })).toEqual(['ARM-WEB-000003']);
    expect(numbers({ estado: 'vendida' })).toEqual(['ARM-CM-000004']);
    expect(numbers({ estado: 'liberada' })).toEqual(['RES-WEB-000005']);
    expect(numbers({ sucursal: 'CB' })).toEqual(['RES-CM-000002']);
    expect(numbers({ desde: '2026-09-20', hasta: '2026-09-20' })).toEqual(['ARM-CM-000004']);
  });

  it('«Vence» hoy o mañana (días de La Paz) entre las vigentes, o las ya vencidas', () => {
    // 29/09 10:00 en La Paz: +2 h es hoy; +20 h es el 30/09 06:00 (mañana).
    expect(numbers({ vence: 'hoy' })).toEqual(['RES-WEB-000001']);
    expect(numbers({ vence: 'manana' })).toEqual(['RES-CM-000002']);
    expect(numbers({ vence: 'vencidas' })).toEqual(['ARM-WEB-000003']);
  });

  it('busca por número, nombre o teléfono (con o sin +591, con espacios)', () => {
    expect(numbers({ q: 'arm-web' })).toEqual(['ARM-WEB-000003']);
    expect(numbers({ q: 'ana rojas' })).toEqual(['RES-CM-000002']);
    expect(numbers({ q: '7654 3210' })).toEqual(['RES-CM-000002']);
    expect(numbers({ q: '+591 76543210' })).toEqual(['RES-CM-000002']);
    expect(numbers({ q: 'pc gamer' })).toEqual(['ARM-WEB-000003']);
  });

  it('resume la lista: cantidad, total, las que vencen pronto y las vencidas que retienen stock', () => {
    expect(listSummary(items)).toEqual({ count: 5, total: 7500, soon: 1, expired: 1 });
  });

  it('el CSV lleva los datos de cada reserva en palabras', () => {
    const csv = buildCsv(CSV_COLUMNS, items.slice(1, 2), { bom: false });
    const [header, first] = csv.split('\r\n');
    expect(header).toBe(
      '"Número";"Nombre";"Tipo";"Canal";"Sucursal";"Cliente";"Teléfono";"Correo de contacto";"Creada";"Reservado hasta";"Estado";"Correo";"Total";"Unidades reservadas";"Venta";"Motivo del cierre"',
    );
    expect(first).toBe(
      '"RES-CM-000002";"Reserva de Juan Pérez";"Compra";"Mostrador";"CB";"Ana Rojas";"76543210";"juan@correo.example";"28/09/2026 14:00";"30/09/2026 06:00";"Reservada";"No se envió";1500;2;;',
    );
  });
});

describe('reservas · estadística y disponibilidad', () => {
  it('resume las reservas vigentes: cantidad, valor, las que vencen pronto, las vencidas y por tipo y canal', () => {
    const summary = activeReservationsSummary(
      [
        build({ number: 'A', total: 1000, reservedUntil: hours(2) }),
        build({ number: 'B', total: 500, channel: 'Desktop', reservedUntil: hours(30) }),
        build({ number: 'C', total: 2000, kind: 'Build', reservedUntil: hours(30) }),
        build({ number: 'D', total: 800, reservedUntil: hours(-2) }),
        build({ number: 'E', status: 'Sold', total: 999 }),
      ],
      NOW,
    );
    expect(summary).toMatchObject({ active: 3, value: 3500, soon: 1, expired: 1 });
    expect(summary.groups).toEqual([
      { label: 'Compras de la tienda web', value: 1, hint: 'Bs 1.000,00' },
      { label: 'Compras de mostrador', value: 1, hint: 'Bs 500,00' },
      { label: 'Armados de la tienda web', value: 1, hint: 'Bs 2.000,00' },
    ]);
  });

  it('la disponibilidad de cada línea: lo que hay además de lo reservado, o si alcanza', () => {
    expect(availabilityText({ stock: 3, quantity: 1 }, true)).toEqual({ text: 'Reservada · 3 más disponibles', tone: 'accent' });
    expect(availabilityText({ stock: 0, quantity: 1 }, true)).toEqual({ text: 'Reservada · sin más unidades', tone: 'accent' });
    expect(availabilityText({ stock: 5, quantity: 2 }, false)).toEqual({ text: 'Disponible 5', tone: 'success' });
    expect(availabilityText({ stock: 1, quantity: 2 }, false)).toEqual({ text: 'Solo 1 disponible', tone: 'danger' });
    expect(availabilityText({ stock: 0, quantity: 2 }, false)).toEqual({ text: 'Sin stock', tone: 'danger' });
  });
});

describe('reservas · teléfono, WhatsApp y motivo', () => {
  it('WhatsApp se arma SOLO con los dígitos del teléfono boliviano', () => {
    expect(localPhoneDigits('+591 7123-4567')).toBe('71234567');
    expect(localPhoneDigits('(591) 2 211 234')).toBe('2211234');
    expect(localPhoneDigits('71234567')).toBe('71234567');
    expect(localPhoneDigits('123')).toBeNull();
    expect(whatsappUrl('+59171234567')).toBe('https://wa.me/59171234567');
    expect(whatsappUrl('7123 4567 <script>')).toBe('https://wa.me/59171234567');
    expect(whatsappUrl(null)).toBeNull();
    expect(formatPhone('71234567')).toBe('+591 71234567');
    expect(formatPhone('texto')).toBe('texto');
  });

  it('el motivo de la liberación: uno de la lista o el escrito a mano', () => {
    expect(releaseReason('El cliente desistió', 'ignorado')).toBe('El cliente desistió');
    expect(releaseReason(OTHER_REASON, '  Pidió otro modelo  ')).toBe('Pidió otro modelo');
    expect(releaseReason('', '')).toBe('');
  });
});

describe('reservas · reserva en mostrador', () => {
  const product = (overrides: Partial<ProductRecord>): ProductRecord => ({
    variantId: 'v-1',
    sku: 'MOU-LOG-G502',
    name: 'Mouse Logitech G502',
    categoryCode: 'PER',
    category: 'Periféricos',
    unit: 'UND',
    allowsDecimals: false,
    price: 350,
    available: 4,
    barcodes: [],
    ...overrides,
  });
  const filled = (overrides: Partial<CartForm> = {}): CartForm => ({
    ...EMPTY_CART,
    lines: addCartLine([], product({})),
    contactName: 'Ana Rojas',
    contactPhone: '+591 7654-3210',
    ...overrides,
  });

  it('agregar el mismo producto suma una unidad (hasta 16) y el total sale de las líneas', () => {
    let lines = addCartLine([], product({}));
    lines = addCartLine(lines, product({}));
    lines = addCartLine(lines, product({ sku: 'TEC-RZ-01', name: 'Teclado', price: 500 }));
    expect(lines.map((line) => [line.sku, line.quantity])).toEqual([
      ['MOU-LOG-G502', 2],
      ['TEC-RZ-01', 1],
    ]);
    expect(cartTotal(lines)).toBe(1200);
    const many = addCartLine([{ ...lines[0], quantity: 16 }], product({}));
    expect(many[0].quantity).toBe(16);
  });

  it('valida lo mismo que el servidor: productos, nombre, teléfono boliviano, correo y cantidades', () => {
    expect(cartProblems(EMPTY_CART)).toEqual({
      lines: 'Agregue al menos un producto a la reserva.',
      contactName: 'Indique el nombre de quien recoge la reserva.',
      contactPhone: 'Indique un teléfono o WhatsApp para avisar al cliente.',
    });
    expect(cartProblems(filled())).toEqual({});
    expect(cartProblems(filled({ contactPhone: '12345' })).contactPhone).toBe('El teléfono debe tener 7 u 8 dígitos (Bolivia), con o sin +591.');
    expect(cartProblems(filled({ contactEmail: 'no-es-correo' })).contactEmail).toMatch(/correo válido/);
    expect(cartProblems(filled({ lines: [{ ...filled().lines[0], quantity: 17 }] })).lines).toBe('La cantidad de cada producto va de 1 a 16.');
    expect(cartProblems(filled({ lines: [{ ...filled().lines[0], quantity: null }] })).lines).toBe('La cantidad de cada producto va de 1 a 16.');
    expect(cartProblems(filled({ notes: 'una\nlínea' })).notes).toBe('Las notas van en una sola línea.');
    expect(isBolivianPhone('591 71234567')).toBe(true);
    expect(isBolivianPhone('+54 11 1234 5678')).toBe(false);
  });

  it('los datos para la factura siguen las reglas del SIN: CI y NIT solo dígitos, complemento solo con CI', () => {
    expect(cartProblems(filled({ documentNumber: '123' })).documentType).toBe('Elija el tipo de documento para guardar los datos de la factura.');
    expect(cartProblems(filled({ documentType: '1' })).documentNumber).toBe('Indique el número de documento.');
    expect(cartProblems(filled({ documentType: '5', documentNumber: '12A' })).documentNumber).toBe('Con NIT el número solo admite dígitos.');
    expect(cartProblems(filled({ documentType: '5', documentNumber: '1020304050', complement: '1A' })).complement).toBe('El complemento solo se usa con la cédula de identidad.');
    expect(cartProblems(filled({ documentType: '1', documentNumber: '4455667', complement: '1A' }))).toEqual({});
    expect(cartProblems(filled({ documentType: '3', documentNumber: 'AB-123456' }))).toEqual({});
  });

  it('arma el pedido de ReserveCartCommand con TODOS sus parámetros (lo vacío como null)', () => {
    expect(toReserveCartPayload(filled())).toEqual({
      items: [{ sku: 'MOU-LOG-G502', quantity: 1 }],
      contactName: 'Ana Rojas',
      contactPhone: '+591 7654-3210',
      contactEmail: null,
      notes: null,
      holdDays: 2,
      buyer: null,
      customerCode: null,
      name: null,
    });
    expect(
      toReserveCartPayload(
        filled({
          customerCode: 'CLI-0042',
          contactEmail: ' ana@correo.example ',
          holdDays: '3',
          notes: ' Pasa a las 18:00 ',
          name: 'Periféricos de Ana',
          documentType: '1',
          documentNumber: '4455667',
          complement: '1a',
          legalName: 'Ana Rojas',
        }),
      ),
    ).toEqual({
      items: [{ sku: 'MOU-LOG-G502', quantity: 1 }],
      contactName: 'Ana Rojas',
      contactPhone: '+591 7654-3210',
      contactEmail: 'ana@correo.example',
      notes: 'Pasa a las 18:00',
      holdDays: 3,
      buyer: { documentType: 1, documentNumber: '4455667', complement: '1A', name: 'Ana Rojas' },
      customerCode: 'CLI-0042',
      name: 'Periféricos de Ana',
    });
  });

  it('los clientes registrados se ofrecen activos, sin el consumidor final y por nombre', () => {
    const customer = (overrides: Partial<CustomerRecord>): CustomerRecord => ({
      code: 'CLI-0001',
      name: 'Zoe',
      category: 'General',
      categoryCode: 'GEN',
      email: null,
      phone: null,
      taxId: null,
      isActive: true,
      lastPurchase: null,
      purchases: 0,
      total: 0,
      ...overrides,
    });
    const options = customerOptions([
      customer({ code: 'CF', name: 'Consumidor final' }),
      customer({ code: 'CLI-0002', name: 'Ana Rojas', taxId: '4455667', phone: '76543210' }),
      customer({ code: 'CLI-0003', name: 'Inactivo', isActive: false }),
      customer({}),
    ]);
    expect(options.map((option) => [option.value, option.label, option.description])).toEqual([
      ['CLI-0002', 'Ana Rojas', 'CLI-0002 · NIT/CI 4455667 · 76543210'],
      ['CLI-0001', 'Zoe', 'CLI-0001'],
    ]);
  });
});
