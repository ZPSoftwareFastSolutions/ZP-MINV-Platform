// Módulo «Documentos fiscales» dentro del panel real, con un registro que tiene SOLO este módulo (las pruebas no dependen de
// los demás módulos en construcción). El servidor de la facturación se simula en la prueba (el modo mock no atiende estas
// operaciones): lista con filtros en la dirección (servidor y página), detalle con la bitácora del SIN, `?documento=`,
// `?venta=`, `&anular=1` y `?periodo=hoy`, anular (motivo del catálogo, «anular y devolver», confirmación y la forma
// exacta del contrato), revertir, re-emitir con el comprador corregido y envío inmediato, correo, verificar, ver e
// imprimir / PDF con la entrega registrada, la guarda del menú «⋯», errores del servidor, permisos, la estadística y el
// tablero. Ninguna prueba toca la red. La hora queda fija: 29/09/2026 10:00 en La Paz.

import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { WebApiError } from '@/1-domain/auth/errors';
import { renderPanel, signedInAs, type MockWeb, type StaffRole } from '@/test-utils';
import { buildRegistry, dashboardActions, dashboardStats } from '../../registry';
import { PanelApp } from '../../shell/PanelApp';
import type { CatalogItemData, DocumentDetailData, DocumentRecord, PrintModelData } from './fiscal';
import documentosFiscales from './module';
import { TodayDocumentsStat } from './TodayDocumentsStat';

const REGISTRY = buildRegistry([{ source: '../modules/documentos-fiscales/module.tsx', definition: documentosFiscales }]);

/** 10:00 de La Paz del 29/09/2026. */
const NOW = new Date('2026-09-29T14:00:00Z');

beforeAll(async () => {
  await import('../../shell/PanelLayout');
  await import('./FiscalDocumentsPage');
}, 30_000);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------------------------------- servidor simulado

function doc(overrides: Partial<DocumentRecord>): DocumentRecord {
  return {
    id: 'doc-x',
    kind: 'Invoice',
    number: 1,
    cuf: '4A1F2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A',
    issuedAt: '2026-09-29T09:15:00.000',
    branchId: 'b-cm',
    branchCode: 'CM',
    pointOfSaleCode: 1,
    buyerName: 'Juan Pérez',
    buyerDocument: '4455667',
    total: 100,
    status: 'Valid',
    isReverted: false,
    emissionType: 1,
    saleNumber: null,
    lastSiatCode: 908,
    canVoid: false,
    canRevert: false,
    canCreditNote: false,
    voidDeadline: '2026-10-09T23:59:59',
    ...overrides,
  };
}

const VOID_REASONS: CatalogItemData[] = [
  { catalog: 'MOTIVOS_ANULACION', code: 1, description: 'FACTURA MAL EMITIDA', isCurrent: true },
  { catalog: 'MOTIVOS_ANULACION', code: 2, description: 'NOTA DE CREDITO-DEBITO MAL EMITIDA', isCurrent: true },
  { catalog: 'MOTIVOS_ANULACION', code: 3, description: 'DATOS DE EMISION INCORRECTOS', isCurrent: true },
];

const DOCUMENT_TYPES: CatalogItemData[] = [1, 2, 3, 4, 5].map((code) => ({ catalog: 'TIPO_DOCUMENTO_IDENTIDAD', code, description: `TIPO ${code}`, isCurrent: true }));

function printModel(row: DocumentRecord): PrintModelData {
  return {
    address: 'Av. Arce 100',
    amountInWords: 'MIL OCHOCIENTOS CINCUENTA 00/100 BOLIVIANOS',
    amountToPay: row.total,
    branchLabel: 'Casa matriz',
    buyerDocument: row.buyerDocument,
    buyerName: row.buyerName,
    cashier: 'Diego Flores',
    creditDebitAmount: null,
    cuf: row.cuf,
    customerCode: 'CLI-0001',
    discount: 0,
    giftCard: 0,
    isOffline: false,
    isTest: true,
    isVoided: row.status === 'Voided',
    issuedAt: row.issuedAt,
    issuerName: 'Tech Zone Gaming S.R.L.',
    issuerNit: 1020304050,
    legends: ['Ley N° 453: el proveedor debe exhibir sus certificaciones.'],
    lines: [{ description: 'Mouse gamer', discount: 0, productCode: 'MOU-01', quantity: 1, serialsText: null, subtotal: row.total, transactionCode: null, unit: 'UNIDAD', unitPrice: row.total, warrantyUntil: null }],
    municipality: 'La Paz',
    number: row.number,
    original: null,
    paymentMethod: 'Efectivo',
    phone: null,
    pointOfSaleCode: row.pointOfSaleCode,
    qrUrl: 'consulta.siat.invalid/QR?nit=1020304050',
    returnedTotal: null,
    saleNumber: row.saleNumber,
    subtitle: '(Con derecho a crédito fiscal)',
    subtotal: row.total,
    taxBase: row.total,
    title: 'FACTURA',
    total: row.total,
  };
}

type Payload = Record<string, unknown>;

/** Servidor de la facturación en memoria: responde las operaciones del módulo y anota lo que se pidió. */
class FiscalServer {
  rows: DocumentRecord[] = [
    doc({ id: 'nota-5', kind: 'CreditDebitNote', number: 5, issuedAt: '2026-09-29T11:00:00.000', total: 350, saleNumber: 'F-CM-000101', canVoid: true }),
    doc({
      id: 'doc-101',
      number: 101,
      issuedAt: '2026-09-29T09:15:00.000',
      total: 1850,
      saleNumber: 'F-CM-000101',
      canVoid: true,
      canCreditNote: true,
      buyerDocument: '5115889-1A',
    }),
    doc({ id: 'doc-100', number: 100, status: 'Discarded', issuedAt: '2026-09-29T09:00:00.000', total: 1850, saleNumber: 'F-CM-000101', lastSiatCode: null }),
    doc({
      id: 'doc-50',
      number: 50,
      branchId: 'b-cb',
      branchCode: 'CB',
      pointOfSaleCode: 2,
      status: 'Rejected',
      issuedAt: '2026-09-28T16:40:00.000',
      buyerName: 'Ana Rojas',
      buyerDocument: '1020304',
      total: 700,
      saleNumber: 'F-CB-000050',
      lastSiatCode: 902,
    }),
    doc({ id: 'doc-90', number: 90, status: 'Voided', issuedAt: '2026-09-20T12:00:00.000', buyerName: 'Luis Mamani', total: 420, saleNumber: 'F-CM-000090', canRevert: true }),
    doc({
      id: 'doc-10',
      number: 10,
      branchId: 'b-sc',
      branchCode: 'SC',
      status: 'Offline',
      emissionType: 2,
      issuedAt: '2026-09-27T08:00:00.000',
      buyerName: 'Eva Luna',
      total: 90,
      saleNumber: 'F-SC-000010',
      lastSiatCode: null,
    }),
    doc({ id: 'doc-7', number: 7, issuedAt: '2026-07-10T10:00:00.000', buyerName: 'Cliente antiguo', total: 55, saleNumber: 'F-CM-000007' }),
  ];
  calls: { operation: string; payload: Payload }[] = [];
  failures = new Map<string, WebApiError>();

  payloads(operation: string): Payload[] {
    return this.calls.filter((call) => call.operation === operation).map((call) => call.payload);
  }

  private find(id: unknown): DocumentRecord {
    const found = this.rows.find((row) => row.id === id);
    if (!found) throw new WebApiError({ kind: 'not_found', status: 404, message: 'El documento fiscal no existe (o es de una sucursal que no es suya).' });
    return found;
  }

  private replace(next: DocumentRecord): DocumentRecord {
    this.rows = this.rows.map((row) => (row.id === next.id ? next : row));
    return next;
  }

  detail(row: DocumentRecord): DocumentDetailData {
    const rejected = row.status === 'Rejected';
    return {
      buyerEmail: row.id === 'doc-101' ? 'juan@correo.example' : null,
      cafc: null,
      cardNumberMasked: null,
      deliveries: [{ channel: 'Email', error: null, occurredAt: '2026-09-29T13:20:00Z', recipient: 'juan@correo.example', succeeded: true }],
      events: [
        { action: 'Issued', description: 'Emitida al cobrar', messages: null, occurredAt: '2026-09-29T13:15:00Z', receptionCode: null, siatCode: null, user: 'Diego Flores' },
        rejected
          ? { action: 'Rejected', description: null, messages: 'NIT inexistente en el Padrón', occurredAt: '2026-09-29T13:16:00Z', receptionCode: null, siatCode: 902, user: null }
          : { action: 'Accepted', description: null, messages: null, occurredAt: '2026-09-29T13:16:00Z', receptionCode: 'REC-1', siatCode: 908, user: null },
      ],
      exceptionCode: 0,
      legend: 'Ley N° 453: el proveedor debe exhibir sus certificaciones.',
      lines: [
        { activityCode: '461000', description: 'Mouse gamer', discount: 0, lineNumber: 1, productCode: 'MOU-01', quantity: 1, serialsText: 'S/N: ABC123', sinProductCode: 83141, sinUnitCode: 58, subtotal: row.total, transactionCode: null, unit: 'UNIDAD', unitPrice: row.total },
      ],
      original: null,
      paymentMethod: 'Efectivo',
      receptionCode: rejected ? null : 'REC-1',
      replacedByDocumentId: row.id === 'doc-100' ? 'doc-101' : null,
      replacesDocumentId: row.id === 'doc-101' ? 'doc-100' : null,
      row,
      taxAmount: Math.round(row.total * 13) / 100,
      voidReason: row.status === 'Voided' ? 'FACTURA MAL EMITIDA' : null,
      xml: '<facturaComputarizadaCompraVenta><cabecera><nitEmisor>1020304050</nitEmisor><codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad></cabecera></facturaComputarizadaCompraVenta>',
    };
  }

  handle(operation: string, payload: Payload): { handled: true; result: unknown } | { handled: false } {
    const known = [
      'GetFiscalDocumentsQuery',
      'GetFiscalDocumentQuery',
      'GetSiatCatalogQuery',
      'VoidFiscalDocumentCommand',
      'RevertFiscalVoidCommand',
      'ReissueFiscalDocumentCommand',
      'DispatchFiscalDocumentsCommand',
      'SendFiscalDocumentEmailCommand',
      'CheckFiscalDocumentStatusCommand',
      'GetFiscalPrintModelQuery',
      'RenderFiscalDocumentQuery',
      'RecordFiscalDeliveryCommand',
    ];
    if (!known.includes(operation)) return { handled: false };
    this.calls.push({ operation, payload });
    const failure = this.failures.get(operation);
    if (failure) {
      this.failures.delete(operation);
      throw failure;
    }
    return { handled: true, result: this.execute(operation, payload) };
  }

  private execute(operation: string, payload: Payload): unknown {
    switch (operation) {
      case 'GetFiscalDocumentsQuery': {
        const search = typeof payload.search === 'string' ? payload.search.toUpperCase() : null;
        return this.rows.filter((row) => {
          const day = row.issuedAt.slice(0, 10);
          return (
            day >= String(payload.from) &&
            day <= String(payload.to) &&
            (payload.status == null || row.status === payload.status) &&
            (payload.kind == null || row.kind === payload.kind) &&
            (search === null || String(row.number) === search || row.cuf.startsWith(search) || row.buyerDocument.includes(search) || row.buyerName.toUpperCase().includes(search))
          );
        });
      }
      case 'GetFiscalDocumentQuery':
        return this.detail(this.find(payload.documentId));
      case 'GetSiatCatalogQuery':
        return payload.catalog === 'MOTIVOS_ANULACION' ? VOID_REASONS : payload.catalog === 'TIPO_DOCUMENTO_IDENTIDAD' ? DOCUMENT_TYPES : [];
      case 'VoidFiscalDocumentCommand': {
        const row = this.find(payload.documentId);
        this.replace({ ...row, status: 'Voided', canVoid: false, canRevert: true, lastSiatCode: 905 });
        return `✔ Documento N° ${row.number} anulado en el SIN (código 905).`;
      }
      case 'RevertFiscalVoidCommand': {
        const row = this.find(payload.documentId);
        this.replace({ ...row, status: 'Valid', isReverted: true, canRevert: false, lastSiatCode: 907 });
        return '✔ Anulación revertida en el SIN (código 907).';
      }
      case 'ReissueFiscalDocumentCommand': {
        const row = this.find(payload.documentId);
        const created = doc({ ...row, id: 'doc-nuevo', number: 51, status: 'Pending', issuedAt: '2026-09-29T10:00:00.000', buyerName: 'Ana Rojas Vda.', lastSiatCode: null });
        this.rows = [created, ...this.rows];
        return created;
      }
      case 'DispatchFiscalDocumentsCommand': {
        const row = this.replace({ ...this.find(payload.documentId), status: 'Valid', lastSiatCode: 908 });
        return { sent: 1, valid: 1, rejected: 0, wentOffline: 0, documents: [row], messages: [] };
      }
      case 'SendFiscalDocumentEmailCommand':
        return `✔ Enviado a ${String(payload.email)}.`;
      case 'CheckFiscalDocumentStatusCommand':
        return '✔ El SIN confirma: VALIDADA (908).';
      case 'GetFiscalPrintModelQuery':
        return printModel(this.find(payload.documentId));
      case 'RenderFiscalDocumentQuery':
        return { fileName: 'Factura-101-CM.pdf', contentType: 'application/pdf', content: btoa('%PDF-prueba') };
      case 'RecordFiscalDeliveryCommand':
        return '✔ Entregado en PDF: factura N° 101 (queda la constancia de la entrega).';
      default:
        throw new Error(`Operación no simulada: ${operation}`);
    }
  }
}

function serve(web: MockWeb, server: FiscalServer) {
  const real = web.backend.rpc.call.bind(web.backend.rpc);
  vi.spyOn(web.backend.rpc, 'call').mockImplementation(async (operation, payload, options) => {
    const answer = server.handle(operation, payload as Payload);
    if (answer.handled) return { result: answer.result as never, replayed: false, requestId: options?.requestId ?? 'prueba' };
    return real(operation, payload, options);
  });
}

async function openDocuments(role: StaffRole = 'ADMIN', query = '', server = new FiscalServer()) {
  const web = await signedInAs(role);
  serve(web, server);
  const view = await renderPanel(<PanelApp registry={REGISTRY} />, { web: web.services, route: `/panel/documentos-fiscales${query}`, path: '/panel/*' });
  return { web, server, location: view.location };
}

async function table(): Promise<HTMLElement> {
  const found = await screen.findByTestId('tabla', undefined, { timeout: 5000 });
  await waitFor(() => expect(within(found).queryAllByRole('row').length).toBeGreaterThan(1));
  return found;
}

function dataRows(tableElement: HTMLElement): HTMLElement[] {
  return within(tableElement)
    .getAllByRole('row')
    .filter((row) => row.hasAttribute('data-row-key'));
}

function rowOf(tableElement: HTMLElement, id: string): HTMLElement {
  const found = dataRows(tableElement).find((row) => row.getAttribute('data-row-key') === id);
  if (!found) throw new Error(`No está la fila ${id}`);
  return found;
}

async function rowAction(tableElement: HTMLElement, id: string, action: string) {
  fireEvent.click(within(rowOf(tableElement, id)).getByRole('button', { name: /^Acciones de / }));
  fireEvent.click(await screen.findByRole('menuitem', { name: action }));
}

function menuLabels(): string[] {
  return within(screen.getByTestId('menu-acciones'))
    .getAllByRole('menuitem')
    .map((item) => item.textContent ?? '');
}

async function detailPanel(): Promise<HTMLElement> {
  return screen.findByTestId('detalle-documento', undefined, { timeout: 5000 });
}

// ---------------------------------------------------------------------------------------------------- lista

describe('Documentos fiscales · lista', () => {
  it('pide los últimos 30 días al servidor y muestra los documentos, lo más reciente primero', async () => {
    const { server } = await openDocuments();
    expect(await screen.findByRole('heading', { level: 1, name: 'Documentos fiscales' }, { timeout: 5000 })).toBeInTheDocument();
    const grid = await table();
    expect(server.payloads('GetFiscalDocumentsQuery')).toEqual([{ from: '2026-08-31', to: '2026-09-29', status: null, kind: null, search: null }]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['nota-5', 'doc-101', 'doc-100', 'doc-50', 'doc-10', 'doc-90']);
    expect(rowOf(grid, 'doc-101')).toHaveTextContent('Factura N° 101');
    expect(rowOf(grid, 'doc-101')).toHaveTextContent('29/09/2026 09:15:00');
    expect(rowOf(grid, 'doc-50')).toHaveTextContent('Rechazada');
    expect(rowOf(grid, 'doc-10')).toHaveTextContent('Fuera de línea');
    expect(screen.getByTestId('documentos-resumen')).toHaveTextContent('6 de 6 documentos · 5 facturas · 1 notas');
    expect(document.title).toBe('Documentos fiscales · Panel · Tech Zone Gaming');
    // El resumen del período está plegado.
    expect(screen.getByRole('button', { name: /Ver resumen del período/ })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('resumen-periodo')).not.toBeInTheDocument();
  });

  it('estado, tipo y búsqueda van al servidor; sucursal y tipo de emisión se filtran en la página (todo en la dirección)', async () => {
    const { server, location } = await openDocuments();
    const grid = await table();
    fireEvent.change(screen.getByRole('combobox', { name: 'Estado' }), { target: { value: 'Rejected' } });
    await waitFor(() => expect(location()).toBe('/panel/documentos-fiscales?estado=Rejected'));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['doc-50']));
    expect(server.payloads('GetFiscalDocumentsQuery').at(-1)).toEqual({ from: '2026-08-31', to: '2026-09-29', status: 'Rejected', kind: null, search: null });

    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await waitFor(() => expect(dataRows(grid)).toHaveLength(6));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: 'CM' } });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['nota-5', 'doc-101', 'doc-100', 'doc-90']));
    fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal' }), { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de emisión' }), { target: { value: '2' } });
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['doc-10']));
    expect(location()).toBe('/panel/documentos-fiscales?emision=2');

    fireEvent.change(screen.getByRole('combobox', { name: 'Tipo de emisión' }), { target: { value: '' } });
    const search = screen.getByRole('searchbox', { name: 'Buscar' });
    fireEvent.change(search, { target: { value: 'ana rojas' } });
    fireEvent.keyDown(search, { key: 'Enter' });
    await waitFor(() => expect(server.payloads('GetFiscalDocumentsQuery').at(-1)).toMatchObject({ search: 'ana rojas' }));
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['doc-50']));
  });

  it('«Todas» las fechas pide desde el principio (así aparece un documento de julio)', async () => {
    const { server } = await openDocuments('ADMIN', '?desde=&hasta=');
    const grid = await table();
    expect(server.payloads('GetFiscalDocumentsQuery')).toEqual([{ from: '2000-01-01', to: '2026-09-29', status: null, kind: null, search: null }]);
    expect(rowOf(grid, 'doc-7')).toHaveTextContent('Cliente antiguo');
  });

  it('el botón del tablero (?periodo=hoy) muestra el día de hoy y deja las fechas en la dirección', async () => {
    const { server, location } = await openDocuments('ADMIN', '?periodo=hoy');
    await waitFor(() => expect(location()).toBe('/panel/documentos-fiscales?desde=2026-09-29&hasta=2026-09-29'));
    const grid = await table();
    await waitFor(() => expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['nota-5', 'doc-101', 'doc-100']));
    expect(server.payloads('GetFiscalDocumentsQuery')).toEqual([{ from: '2026-09-29', to: '2026-09-29', status: null, kind: null, search: null }]);
  });

  it('exporta a CSV lo filtrado y el resumen del período se abre a pedido', async () => {
    const created: Blob[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
      created.push(blob as Blob);
      return 'blob:prueba';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await openDocuments('ADMIN', '?sucursal=CB');
    await table();
    fireEvent.click(screen.getByRole('button', { name: 'Exportar CSV' }));
    expect(await screen.findByText('Exportación lista')).toBeInTheDocument();
    const lines = (await created[0].text()).replace(/^﻿/, '').trim().split('\r\n');
    expect(lines[0]).toBe(
      '"Tipo";"Número";"Fecha y hora fiscal";"Sucursal";"Punto de venta";"Comprador";"Documento del comprador";"Venta";"Estado";"Emisión";"Total";"CUF";"Plazo de anulación"',
    );
    expect(lines.slice(1)).toEqual([
      '"Factura";50;"28/09/2026 16:40:00";"CB";2;"Ana Rojas";"1020304";"F-CB-000050";"Rechazada";"En línea";700;"4A1F2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A";"09/10/2026"',
    ]);

    fireEvent.click(screen.getByRole('button', { name: /Ver resumen del período/ }));
    expect(await screen.findByTestId('resumen-periodo')).toHaveTextContent('Anulados y rechazados1');
  });

  it('si la consulta falla muestra el error con «Reintentar» y se recupera', async () => {
    const server = new FiscalServer();
    server.failures.set('GetFiscalDocumentsQuery', new WebApiError({ kind: 'network', message: 'Sin conexión' }));
    await openDocuments('ADMIN', '', server);
    const error = await screen.findByTestId('estado-error', undefined, { timeout: 5000 });
    expect(error).toHaveTextContent('No se pudo cargar la información');
    fireEvent.click(within(error).getByRole('button', { name: 'Reintentar' }));
    await table();
  });
});

// ---------------------------------------------------------------------------------------------------- detalle y dirección

describe('Documentos fiscales · detalle y parámetros de la dirección', () => {
  it('abre el detalle desde la fila (queda en la dirección) con la bitácora del SIN, líneas, entregas y el XML', async () => {
    const { location } = await openDocuments();
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'doc-50')).getAllByRole('button')[0]);
    const panel = await detailPanel();
    expect(screen.getByRole('dialog', { name: 'Factura N° 50' })).toBeInTheDocument();
    expect(location()).toBe('/panel/documentos-fiscales');
    expect(panel).toHaveTextContent('El SIN la rechazó');
    expect(panel).toHaveTextContent('Mensajes del SIN: NIT inexistente en el Padrón');
    expect(within(screen.getByTestId('bitacora-sin')).getByText('Rechazado por el SIN · código 902')).toBeInTheDocument();
    expect(panel).toHaveTextContent('S/N: ABC123');
    expect(panel).toHaveTextContent('Correo · juan@correo.example');
    const actions = screen.getByTestId('acciones-documento');
    expect(within(actions).getByRole('button', { name: 'Re-emitir' })).toBeInTheDocument();
    expect(within(actions).queryByRole('button', { name: 'Anular ante el SIN' })).not.toBeInTheDocument();
    expect(within(actions).queryByRole('button', { name: 'Enviar por correo' })).not.toBeInTheDocument();

    fireEvent.click(within(panel).getByRole('button', { name: 'Ver el XML enviado al SIN' }));
    const xml = await screen.findByTestId('xml-documento');
    expect(xml.textContent).toContain('  <cabecera>');
    expect(xml.textContent).toContain('<codigoTipoDocumentoIdentidad>1</codigoTipoDocumentoIdentidad>');
  });

  it('?documento=<id> abre ese documento aunque no esté en la lista; «Ver el documento que lo reemplaza» lleva al nuevo', async () => {
    const { server, location } = await openDocuments('ADMIN', '?documento=doc-100&estado=Valid');
    const panel = await detailPanel();
    expect(server.payloads('GetFiscalDocumentQuery')).toEqual([{ documentId: 'doc-100' }]);
    expect(screen.getByRole('dialog', { name: 'Factura N° 100' })).toBeInTheDocument();
    expect(panel).toHaveTextContent('Nunca llegó al SIN y se reemplazó por otra');
    // El parámetro se usa al llegar: la dirección queda con los filtros (así filtrar después no choca con el panel).
    expect(location()).toBe('/panel/documentos-fiscales?estado=Valid');
    fireEvent.click(within(panel).getByRole('button', { name: 'Ver el documento que lo reemplaza' }));
    expect(await screen.findByRole('dialog', { name: 'Factura N° 101' })).toBeInTheDocument();
  });

  it('?venta=<número> muestra los documentos de esa venta (todas las fechas) y abre el vigente', async () => {
    const { server, location } = await openDocuments('ADMIN', '?venta=F-CM-000101');
    await waitFor(() => expect(location()).toBe('/panel/documentos-fiscales?venta=F-CM-000101&desde=&hasta='));
    const grid = await table();
    expect(server.payloads('GetFiscalDocumentsQuery')).toEqual([{ from: '2000-01-01', to: '2026-09-29', status: null, kind: null, search: null }]);
    expect(dataRows(grid).map((row) => row.getAttribute('data-row-key'))).toEqual(['nota-5', 'doc-101', 'doc-100']);
    expect(screen.getByText('Documentos de la venta F-CM-000101')).toBeInTheDocument();
    expect(await screen.findByRole('dialog', { name: 'Factura N° 101' })).toBeInTheDocument();
  });

  it('?documento=<id>&anular=1 abre la anulación; si el documento ya está anulado lo explica', async () => {
    const first = await openDocuments('ADMIN', '?documento=doc-101&anular=1');
    expect(await screen.findByRole('alertdialog', { name: 'Anular la factura N° 101' }, { timeout: 5000 })).toBeInTheDocument();
    expect(first.location()).toBe('/panel/documentos-fiscales');
    fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    // El detalle del documento sigue abierto.
    expect(screen.getByRole('dialog', { name: 'Factura N° 101' })).toBeInTheDocument();
  });

  it('&anular=1 sobre un documento anulado avisa por qué no se puede', async () => {
    const { location } = await openDocuments('ADMIN', '?documento=doc-90&anular=1');
    expect(await screen.findByText('No se puede anular', undefined, { timeout: 5000 })).toBeInTheDocument();
    expect(screen.getByText('El documento ya está anulado.')).toBeInTheDocument();
    expect(location()).toBe('/panel/documentos-fiscales');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('un documento que no existe (u otra sucursal) muestra el error del servidor en el panel', async () => {
    await openDocuments('ADMIN', '?documento=no-existe');
    expect(await screen.findByText('El documento fiscal no existe (o es de una sucursal que no es suya).', undefined, { timeout: 5000 })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------------------------------- comandos

describe('Documentos fiscales · comandos', () => {
  it('anular desde el menú «⋯»: no abre el detalle, propone el motivo, exige confirmar y envía la forma exacta del contrato', async () => {
    const { server, location } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-101', 'Anular ante el SIN');
    const dialog = await screen.findByRole('alertdialog', { name: 'Anular la factura N° 101' });
    // La guarda del menú: elegir la acción no abre el detalle de la fila.
    expect(location()).toBe('/panel/documentos-fiscales');
    expect(screen.queryByTestId('detalle-documento')).not.toBeInTheDocument();
    expect(dialog).toHaveTextContent('Se puede anular hasta el 09/10/2026 a las 23:59');
    const reason = within(dialog).getByRole('combobox', { name: /^Motivo/ });
    await waitFor(() => expect(reason).toHaveValue('1'));

    fireEvent.click(within(dialog).getByRole('button', { name: 'Anular ante el SIN' }));
    expect(await within(dialog).findByText('Marque la confirmación para anular.')).toBeInTheDocument();
    expect(server.payloads('VoidFiscalDocumentCommand')).toEqual([]);

    fireEvent.change(reason, { target: { value: '3' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Anular y devolver la mercadería' }));
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Confirmo que anulo el documento N° 101 ante el SIN.' }));
    fireEvent.change(within(dialog).getByLabelText(/^Nota/), { target: { value: 'El cliente devolvió todo' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Anular ante el SIN' }));
    });
    expect(server.payloads('VoidFiscalDocumentCommand')).toEqual([{ documentId: 'doc-101', reasonCode: 3, returnGoods: true, note: 'El cliente devolvió todo' }]);
    expect(await screen.findByText('Documento anulado')).toBeInTheDocument();
    expect(screen.getByText('Documento N° 101 anulado en el SIN (código 905).')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'doc-101')).toHaveTextContent('Anulada'));
  });

  it('el error del servidor al anular se ve dentro del diálogo (sigue abierto para reintentar)', async () => {
    const server = new FiscalServer();
    server.failures.set(
      'VoidFiscalDocumentCommand',
      new WebApiError({ kind: 'domain', status: 422, code: 'billing.void_deadline', message: 'El plazo para anular venció el 09/10/2026 (día 9 del mes siguiente).' }),
    );
    await openDocuments('ADMIN', '', server);
    const grid = await table();
    await rowAction(grid, 'nota-5', 'Anular ante el SIN');
    const dialog = await screen.findByRole('alertdialog', { name: 'Anular la nota N° 5' });
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: /^Motivo/ })).toHaveValue('2'));
    expect(within(dialog).queryByRole('checkbox', { name: 'Anular y devolver la mercadería' })).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Confirmo que anulo el documento N° 5 ante el SIN.' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Anular ante el SIN' }));
    });
    expect(await within(dialog).findByText('El plazo para anular venció el 09/10/2026 (día 9 del mes siguiente).')).toBeInTheDocument();
    expect(server.payloads('VoidFiscalDocumentCommand')).toEqual([{ documentId: 'nota-5', reasonCode: 2, returnGoods: false, note: null }]);
  });

  it('revertir la anulación pide confirmación y envía el documento', async () => {
    const { server } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-90', 'Revertir la anulación');
    const dialog = await screen.findByRole('alertdialog', { name: '¿Revertir la anulación de N° 90?' });
    expect(dialog).toHaveTextContent('La reversión se hace UNA sola vez');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Revertir anulación' }));
    });
    expect(server.payloads('RevertFiscalVoidCommand')).toEqual([{ documentId: 'doc-90' }]);
    expect(await screen.findByText('Anulación revertida en el SIN (código 907).')).toBeInTheDocument();
    await waitFor(() => expect(rowOf(grid, 'doc-90')).toHaveTextContent('Anulación revertida'));
  });

  it('re-emitir corrige el comprador (datos del documento a la vista), lo envía ya al SIN y abre el documento nuevo', async () => {
    const { server, location } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-50', 'Re-emitir');
    const dialog = await screen.findByRole('dialog', { name: 'Re-emitir la factura N° 50' });
    const number = await within(dialog).findByLabelText(/^Número de documento/);
    expect(number).toHaveValue('1020304');
    expect(within(dialog).getByRole('combobox', { name: /^Tipo de documento/ })).toHaveValue('1');

    fireEvent.change(number, { target: { value: '10.20' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Emitir documento nuevo' }));
    expect(await within(dialog).findByText('El CI lleva solo números (sin puntos, guiones ni espacios).')).toBeInTheDocument();

    fireEvent.change(within(dialog).getByRole('combobox', { name: /^Tipo de documento/ }), { target: { value: '5' } });
    fireEvent.change(number, { target: { value: '1020304019' } });
    fireEvent.change(within(dialog).getByLabelText(/^Nombre o razón social/), { target: { value: 'Ana Rojas Vda.' } });
    fireEvent.click(within(dialog).getByRole('checkbox', { name: 'Facturar aunque el NIT no sea válido' }));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Emitir documento nuevo' }));
    });
    expect(server.payloads('ReissueFiscalDocumentCommand')).toEqual([
      { documentId: 'doc-50', buyer: { documentType: 5, documentNumber: '1020304019', complement: null, name: 'Ana Rojas Vda.', email: null, exceptionRequested: true } },
    ]);
    expect(server.payloads('DispatchFiscalDocumentsCommand')).toEqual([{ documentId: 'doc-nuevo', max: 1 }]);
    expect(await screen.findByText('Documento N° 51 emitido')).toBeInTheDocument();
    expect(screen.getByText('Documento N° 51: válida.')).toBeInTheDocument();
    expect(await screen.findByRole('dialog', { name: 'Factura N° 51' })).toBeInTheDocument();
    expect(location()).toBe('/panel/documentos-fiscales');
  });

  it('enviar por correo valida el correo y envía el documento con el correo escrito', async () => {
    const { server } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-101', 'Enviar por correo');
    const dialog = await screen.findByRole('dialog', { name: 'Enviar por correo' });
    const email = within(dialog).getByLabelText(/^Correo del comprador/);
    await waitFor(() => expect(email).toHaveValue('juan@correo.example'));
    fireEvent.change(email, { target: { value: 'juan@' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));
    expect(await within(dialog).findByText('Escriba un correo válido, por ejemplo nombre@correo.com.')).toBeInTheDocument();
    fireEvent.change(email, { target: { value: 'contador@empresa.example' } });
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Enviar' }));
    });
    expect(server.payloads('SendFiscalDocumentEmailCommand')).toEqual([{ documentId: 'doc-101', email: 'contador@empresa.example' }]);
    expect(await screen.findByText('Correo enviado')).toBeInTheDocument();
  });

  it('verificar en el SIN envía el documento y muestra la respuesta', async () => {
    const { server } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-10', 'Verificar en el SIN');
    await waitFor(() => expect(server.payloads('CheckFiscalDocumentStatusCommand')).toEqual([{ documentId: 'doc-10' }]));
    expect(await screen.findByText('El SIN confirma: VALIDADA (908).')).toBeInTheDocument();
  });

  it('ver e imprimir: la representación del documento, «Descargar PDF» y la entrega queda registrada', async () => {
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => 'blob:prueba');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const { server } = await openDocuments();
    const grid = await table();
    await rowAction(grid, 'doc-101', 'Ver e imprimir');
    const dialog = await screen.findByRole('dialog', { name: 'Ver e imprimir · Factura N° 101' });
    expect(await within(dialog).findByText('Tech Zone Gaming S.R.L.')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('SIN VALOR LEGAL');
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Descargar PDF' }));
    });
    expect(server.payloads('RenderFiscalDocumentQuery')).toEqual([{ documentId: 'doc-101', format: 'Pdf', columns: 48 }]);
    expect(await screen.findByText('Se descargó Factura-101-CM.pdf.')).toBeInTheDocument();
    await waitFor(() => expect(server.payloads('RecordFiscalDeliveryCommand')).toEqual([{ documentId: 'doc-101', channel: 'Pdf', recipient: null }]));
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'Imprimir' }));
    });
    expect(print).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(server.payloads('RecordFiscalDeliveryCommand')).toHaveLength(2));
    expect(server.payloads('RecordFiscalDeliveryCommand')[1]).toEqual({ documentId: 'doc-101', channel: 'Print', recipient: null });
  });
});

// ---------------------------------------------------------------------------------------------------- permisos y tablero

describe('Documentos fiscales · permisos, estadística y tablero', () => {
  it('consulta (solo ver la facturación): ve, imprime y verifica, pero no anula, re-emite ni envía', async () => {
    await openDocuments('CONSULTA');
    const grid = await table();
    fireEvent.click(within(rowOf(grid, 'doc-101')).getByRole('button', { name: /^Acciones de / }));
    await screen.findByTestId('menu-acciones');
    expect(menuLabels()).toEqual(['Ver detalle', 'Ver e imprimir', 'Verificar en el SIN', 'Copiar CUF']);
  });

  it('caja (emite pero no anula): envía y re-emite; no anula ni revierte; tampoco devuelve', async () => {
    await openDocuments('CAJERO', '?documento=doc-101');
    await detailPanel();
    const actions = screen.getByTestId('acciones-documento');
    expect(within(actions).getByRole('button', { name: 'Enviar por correo' })).toBeInTheDocument();
    expect(within(actions).queryByRole('button', { name: 'Anular ante el SIN' })).not.toBeInTheDocument();
    expect(within(actions).queryByRole('link', { name: 'Devolver productos (nota)' })).not.toBeInTheDocument();
  });

  it('el administrador ve «Devolver productos (nota)» que lleva a «Ventas» con la venta', async () => {
    await openDocuments('ADMIN', '?documento=doc-101');
    await detailPanel();
    expect(within(screen.getByTestId('acciones-documento')).getByRole('link', { name: 'Devolver productos (nota)' })).toHaveAttribute('href', '/panel/ventas?devolver=F-CM-000101');
    expect(within(await detailPanel()).getByRole('link', { name: 'F-CM-000101' })).toHaveAttribute('href', '/panel/ventas?q=F-CM-000101&desde=2026-09-29&hasta=2026-09-29');
  });

  it('un rol sin `billing.view` (bodega) ve «No tiene acceso a esta pantalla»', async () => {
    await openDocuments('BODEGA');
    expect(await screen.findByTestId('sin-acceso', undefined, { timeout: 5000 })).toHaveTextContent('Falta el permiso');
    expect(screen.queryByTestId('tabla')).not.toBeInTheDocument();
  });

  it('la estadística «Facturación de hoy» pide el día y enlaza a la lista del día', async () => {
    const web = await signedInAs('GERENCIA');
    const server = new FiscalServer();
    serve(web, server);
    await renderPanel(<TodayDocumentsStat />, { web: web.services });
    const stat = await screen.findByTestId('facturacion-de-hoy');
    await waitFor(() => expect(within(stat).getByRole('link', { name: 'Ver los documentos de hoy' })).toHaveAttribute('href', '/panel/documentos-fiscales?periodo=hoy'));
    expect(server.payloads('GetFiscalDocumentsQuery')).toEqual([{ from: '2026-09-29', to: '2026-09-29', status: null, kind: null, search: null }]);
    expect(stat).toHaveTextContent('Documentos3');
    expect(stat).toHaveTextContent('Anulados y rechazados0');
  });

  it('el módulo es válido y ofrece al tablero «Facturas de hoy» y «Revisar rechazadas» (y su estadística)', () => {
    expect(REGISTRY.problems).toEqual([]);
    const actions = dashboardActions(REGISTRY, ['billing.view']).flatMap((group) => group.actions.map((item) => [item.action.label, item.to]));
    expect(actions).toEqual([
      ['Facturas de hoy', '/panel/documentos-fiscales?periodo=hoy'],
      ['Revisar rechazadas', '/panel/documentos-fiscales?estado=Rejected'],
    ]);
    expect(dashboardStats(REGISTRY, ['billing.view']).map((item) => item.stat.title)).toEqual(['Facturación de hoy']);
    expect(dashboardActions(REGISTRY, ['inventory.stock.view'])).toEqual([]);
  });
});
