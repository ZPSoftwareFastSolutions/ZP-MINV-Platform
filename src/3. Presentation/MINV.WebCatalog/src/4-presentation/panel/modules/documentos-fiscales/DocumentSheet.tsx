// Módulo «Documentos fiscales» · la representación de un documento del SIN (`GetFiscalPrintModelQuery`): emisor, número,
// CUF, comprador, factura original (notas), líneas con series y garantía, totales, importe en letras, leyendas y la
// dirección de consulta en el SIN. Se ve en «Ver e imprimir» (colores del tema) y en el área de impresión (`paper`).

import clsx from 'clsx';
import { formatDate, formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { formatFiscalTime, type PrintModelData } from './fiscal';

export interface DocumentSheetProps {
  model: PrintModelData;
  /** Para imprimir: negro sobre blanco, sin los colores del tema. */
  paper?: boolean;
}

export function DocumentSheet({ model, paper = false }: DocumentSheetProps) {
  const muted = paper ? 'text-black/70' : 'text-text-muted';
  const rule = paper ? 'border-black/30' : 'border-border';
  const flags = [model.isTest && 'SIN VALOR LEGAL', model.isVoided && 'ANULADA', model.isOffline && 'Emitida fuera de línea'].filter((flag): flag is string => Boolean(flag));
  const totals: [string, number | null][] = [
    ['Subtotal', model.subtotal],
    ['Descuento', model.discount],
    ['Total', model.total],
    ['Gift card', model.giftCard > 0 ? model.giftCard : null],
    ['Monto a pagar', model.amountToPay],
    ['Importe base para el crédito fiscal', model.taxBase],
    ['Monto total devuelto', model.returnedTotal],
    ['Monto efectivo del débito o crédito', model.creditDebitAmount],
  ];

  return (
    <article className={clsx('space-y-4 text-sm', paper ? 'text-black' : 'text-text')} aria-label={`${model.title} N° ${model.number}`}>
      <header className={clsx('space-y-1 border-b pb-3', rule)}>
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <div>
            <p className="font-display text-lg font-semibold uppercase">{model.title}</p>
            {model.subtitle && <p className={muted}>{model.subtitle}</p>}
          </div>
          {flags.length > 0 && (
            <p className={clsx('font-semibold uppercase', paper ? 'text-black' : 'text-warning-text')} data-testid="documento-avisos">
              {flags.join(' · ')}
            </p>
          )}
        </div>
        <p className="font-semibold">{model.issuerName}</p>
        <p>
          NIT {model.issuerNit} · N° {model.number}
        </p>
        <p className={muted}>
          {model.branchLabel} · Punto de venta {model.pointOfSaleCode}
        </p>
        <p className={muted}>{[model.address, model.municipality, model.phone ? `Tel. ${model.phone}` : null].filter(Boolean).join(' · ')}</p>
        <p className="break-all">
          <span className={muted}>CUF: </span>
          {model.cuf}
        </p>
        <p>
          <span className={muted}>Fecha de emisión: </span>
          {formatFiscalTime(model.issuedAt)}
        </p>
      </header>

      <section aria-label="Comprador" className={clsx('space-y-0.5 border-b pb-3', rule)}>
        <p>
          <span className={muted}>Nombre o razón social: </span>
          {model.buyerName}
        </p>
        <p>
          <span className={muted}>NIT / CI / CEX: </span>
          {model.buyerDocument}
        </p>
        <p>
          <span className={muted}>Código del cliente: </span>
          {model.customerCode}
        </p>
      </section>

      {model.original && (
        <p className={clsx('border-b pb-3', rule)}>
          <span className={muted}>Factura original: </span>N° {model.original.number} · {formatFiscalTime(model.original.issuedAt)} ·{' '}
          <span className="break-all">CUF {model.original.cuf}</span>
        </p>
      )}

      <ul aria-label="Detalle" className={clsx('divide-y border-b pb-1', rule, paper ? 'divide-black/20' : 'divide-border')}>
        {model.lines.map((line, index) => (
          <li key={`${line.productCode}-${index}`} className="break-inside-avoid py-2">
            <div className="flex items-start justify-between gap-3">
              <p className="min-w-0">
                {formatQuantity(line.quantity)} {line.unit} × {line.description}
              </p>
              <p className="shrink-0 font-semibold tabular-nums">{formatMoney(line.subtotal)}</p>
            </div>
            <p className={clsx('text-xs', muted)}>
              Código {line.productCode} · precio unitario {formatMoney(line.unitPrice)}
              {line.discount > 0 ? ` · descuento ${formatMoney(line.discount)}` : ''}
            </p>
            {line.serialsText && <p className={clsx('text-xs break-words', muted)}>{line.serialsText}</p>}
            {line.warrantyUntil && <p className={clsx('text-xs', muted)}>Garantía hasta el {formatDate(line.warrantyUntil)}</p>}
          </li>
        ))}
      </ul>

      <dl className="ml-auto grid max-w-sm grid-cols-[1fr_auto] gap-x-4 gap-y-1">
        {totals
          .filter((entry): entry is [string, number] => entry[1] !== null)
          .map(([label, value]) => (
            <div key={label} className="contents">
              <dt className={muted}>{label}</dt>
              <dd className={clsx('text-right tabular-nums', label === 'Monto a pagar' && 'font-semibold')}>{formatMoney(value)}</dd>
            </div>
          ))}
      </dl>

      <p>
        <span className={muted}>Son: </span>
        {model.amountInWords}
      </p>
      <p className={muted}>
        {[model.saleNumber ? `Venta ${model.saleNumber}` : null, model.paymentMethod ? `Pago: ${model.paymentMethod}` : null, model.cashier ? `Atendió: ${model.cashier}` : null]
          .filter(Boolean)
          .join(' · ')}
      </p>
      {model.legends.length > 0 && (
        <div className={clsx('space-y-1 border-t pt-3 text-xs', rule)}>
          {model.legends.map((legend, index) => (
            <p key={index}>{legend}</p>
          ))}
        </div>
      )}
      {model.qrUrl && (
        <p className={clsx('text-xs break-all', muted)}>
          Consulte este documento en el SIN: <span className={paper ? 'text-black' : 'text-accent'}>{model.qrUrl}</span>
        </p>
      )}
    </article>
  );
}
