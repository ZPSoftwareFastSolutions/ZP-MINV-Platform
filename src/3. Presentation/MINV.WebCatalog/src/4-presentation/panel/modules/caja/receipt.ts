// Módulo «Caja» · el comprobante que se imprime después de cobrar (funciones puras): con factura del SIN, la
// representación que arma el servidor (`GetFiscalPrintModelQuery`: emisor, número, CUF, comprador, líneas con series y
// garantía, importes, leyendas y la dirección de verificación); sin factura, el ticket de la venta (`CheckoutResult`,
// como el ticket en pantalla del escritorio). La página lo dibuja y el navegador lo imprime.

import { formatDate, formatDateTime, formatMoney } from '@/4-presentation/panel/lib';
import { formatFiscalTime } from './fiscal';
import type { PosStateData, PrintModelData, SaleResultData } from './types';

export interface ReceiptLineView {
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  amount: number;
  serials: string | null;
  warranty: string | null;
}

export interface ReceiptPair {
  label: string;
  value: string;
  strong?: boolean;
}

/** Comprobante listo para dibujar (en pantalla o en papel). */
export interface ReceiptView {
  /** Emisor: empresa, NIT, sucursal, dirección… */
  header: string[];
  title: string;
  subtitle: string | null;
  meta: ReceiptPair[];
  lines: ReceiptLineView[];
  totals: ReceiptPair[];
  notes: string[];
  /** Aviso destacado: «SIN VALOR LEGAL» en el ambiente de pruebas del SIN (regla F-13) o «ANULADA». */
  warning: string | null;
}

function present(values: readonly (string | null | undefined | false)[]): string[] {
  return values.filter((value): value is string => typeof value === 'string' && value.trim().length > 0);
}

function warrantyUntil(date: string | null): string | null {
  return date ? `Garantía hasta el ${formatDate(date)}` : null;
}

/** Comprobante de una factura del SIN (lo que arma el servidor para el PDF y el rollo). */
export function receiptFromModel(model: PrintModelData): ReceiptView {
  const meta: ReceiptPair[] = [
    { label: 'Factura N°', value: String(model.number) },
    { label: 'Código de autorización (CUF)', value: model.cuf },
    { label: 'Punto de venta', value: String(model.pointOfSaleCode) },
    { label: 'Fecha de emisión', value: formatFiscalTime(model.issuedAt) },
    { label: 'Nombre o razón social', value: model.buyerName },
    { label: 'NIT / CI / CEX', value: model.buyerDocument },
    { label: 'Código del cliente', value: model.customerCode },
  ];
  if (model.saleNumber) meta.push({ label: 'Venta', value: model.saleNumber });
  if (model.paymentMethod) meta.push({ label: 'Medio de pago', value: model.paymentMethod });
  if (model.cashier) meta.push({ label: 'Cajero', value: model.cashier });
  const totals: ReceiptPair[] = [
    { label: 'Subtotal', value: formatMoney(model.subtotal) },
    { label: 'Descuento', value: formatMoney(model.discount) },
    { label: 'Total', value: formatMoney(model.total) },
  ];
  if (model.giftCard > 0) totals.push({ label: 'Gift card', value: formatMoney(model.giftCard) });
  totals.push({ label: 'Monto a pagar', value: formatMoney(model.amountToPay), strong: true });
  totals.push({ label: 'Importe base para crédito fiscal', value: formatMoney(model.taxBase) });
  return {
    header: present([model.issuerName, `NIT ${model.issuerNit}`, model.branchLabel, model.address, model.phone && `Teléfono ${model.phone}`, model.municipality]),
    title: model.title,
    subtitle: model.subtitle.trim().length > 0 ? model.subtitle : null,
    meta,
    lines: model.lines.map((line) => ({
      description: `${line.productCode} · ${line.description}`,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      discount: line.discount,
      amount: line.subtotal,
      serials: line.serialsText,
      warranty: warrantyUntil(line.warrantyUntil),
    })),
    totals,
    notes: present([
      model.amountInWords && `Son: ${model.amountInWords}`,
      ...model.legends,
      model.qrUrl && `Verifique esta factura en el sitio del SIN: ${model.qrUrl}`,
    ]),
    warning: model.isTest ? 'SIN VALOR LEGAL · ambiente de pruebas del SIN' : model.isVoided ? 'ANULADA' : null,
  };
}

/** Ticket de una venta sin factura del SIN (la empresa no factura o la factura todavía no se puede leer). */
export function receiptFromSale(sale: SaleResultData, state: PosStateData | undefined, cashier: string | null): ReceiptView {
  const meta: ReceiptPair[] = [
    { label: 'Venta', value: sale.invoiceNumber },
    { label: 'Pedido', value: sale.orderNumber },
    { label: 'Fecha', value: formatDateTime(sale.issuedAt) },
    { label: 'Cliente', value: sale.customer },
  ];
  if (cashier) meta.push({ label: 'Cajero', value: cashier });
  if (sale.fiscalNumber != null) meta.push({ label: 'Factura del SIN N°', value: String(sale.fiscalNumber) });
  const totals: ReceiptPair[] = [
    { label: 'Total', value: formatMoney(sale.total), strong: true },
    { label: 'IVA incluido', value: formatMoney(sale.tax) },
    { label: 'Pago', value: sale.paymentMethod },
  ];
  if (sale.change > 0) totals.push({ label: 'Vuelto', value: formatMoney(sale.change) });
  return {
    header: present([state?.companyName, state?.taxId && `NIT ${state.taxId}`, state?.branchName]),
    title: 'Comprobante de venta',
    subtitle: null,
    meta,
    lines: sale.lines.map((line) => ({
      description: line.description,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      discount: 0,
      amount: line.amount,
      serials: line.serialsText,
      warranty: warrantyUntil(line.warrantyUntil),
    })),
    totals,
    notes: ['¡Gracias por su compra!'],
    warning: null,
  };
}
