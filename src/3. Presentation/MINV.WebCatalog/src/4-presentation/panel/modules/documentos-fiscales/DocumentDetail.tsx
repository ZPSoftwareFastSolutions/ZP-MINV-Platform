// Módulo «Documentos fiscales» · el contenido del detalle lateral de un documento (`GetFiscalDocumentQuery`), como el
// panel derecho del escritorio: qué significa su estado (y los mensajes del SIN si lo rechazó), el plazo de anulación,
// comprador, pago, CUF (con «Copiar»), factura original (notas), documento reemplazado o que lo reemplaza, líneas con sus
// series, totales, la bitácora del SIN (línea de tiempo) y las entregas al comprador.

import { Copy, ExternalLink, FileCode } from 'lucide-react';
import { useId } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Alert, Button, DetailList, StatusBadge } from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney } from '@/4-presentation/panel/lib';
import {
  EVENT_ACTIONS,
  channelLabel,
  deadlineText,
  emissionText,
  eventTitle,
  eventsNewestFirst,
  formatFiscalTime,
  lineDetailText,
  lineTransactionText,
  linesTotals,
  rejectionText,
  saleLink,
  sentenceCase,
  statusAlertTone,
  statusHelp,
  statusLabel,
  type DocumentDetailData,
} from './fiscal';

export interface DocumentDetailProps {
  detail: DocumentDetailData;
  /** ¿Puede abrir la venta en «Ventas»? */
  canSeeSale: boolean;
  onOpenDocument: (documentId: string) => void;
  onCopyCuf: () => void;
  onShowXml: () => void;
}

export function DocumentDetail({ detail, canSeeSale, onOpenDocument, onCopyCuf, onShowXml }: DocumentDetailProps) {
  const linesId = useId();
  const eventsId = useId();
  const deliveriesId = useId();
  const { row } = detail;
  const isNote = row.kind === 'CreditDebitNote';
  const rejection = rejectionText(detail);
  const deadline = deadlineText(row);
  const totals = linesTotals(detail);
  const sale = saleLink(row);
  const buyer = [
    row.buyerName,
    row.buyerDocument,
    detail.exceptionCode === 1 ? 'NIT sin validar (excepción)' : null,
    detail.buyerEmail,
  ].filter(Boolean);

  return (
    <div className="space-y-5" data-testid="detalle-documento">
      <Alert tone={statusAlertTone(row.status)} title={statusLabel(row)}>
        <p>{statusHelp(row.status)}</p>
        {rejection && <p className="mt-1 whitespace-pre-line break-words">Mensajes del SIN: {rejection}</p>}
        {deadline && <p className="mt-1">{deadline}</p>}
        {detail.voidReason && <p className="mt-1">Motivo de la anulación: {sentenceCase(detail.voidReason)}.</p>}
      </Alert>

      <DetailList
        items={[
          { label: 'Fecha y hora fiscal', value: formatFiscalTime(row.issuedAt) },
          { label: 'Sucursal y punto de venta', value: `${row.branchCode} · punto de venta ${row.pointOfSaleCode}` },
          { label: 'Comprador', value: buyer.join(' · '), wide: true },
          {
            label: 'Pago',
            value: [detail.paymentMethod, detail.cardNumberMasked ? `tarjeta ${detail.cardNumberMasked}` : null].filter(Boolean).join(' · '),
          },
          {
            label: 'Venta',
            value:
              row.saleNumber && sale && canSeeSale ? (
                <Button variant="ghost" to={ROUTES.panelModule(sale)} rightIcon={<ExternalLink />} className="-ml-3">
                  {row.saleNumber}
                </Button>
              ) : (
                row.saleNumber
              ),
          },
          { label: 'Emisión', value: emissionText(row.emissionType, detail.cafc), wide: true },
          { label: 'Código de recepción del SIN', value: detail.receptionCode },
          { label: 'Último código del SIN', value: row.lastSiatCode != null ? String(row.lastSiatCode) : null },
          {
            label: 'CUF',
            value: (
              <span className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-xs break-all" data-testid="cuf-completo">
                  {row.cuf}
                </span>
                <Button variant="subtle" leftIcon={<Copy />} onClick={onCopyCuf}>
                  Copiar CUF
                </Button>
              </span>
            ),
            wide: true,
          },
          { label: 'Leyenda', value: detail.legend, wide: true },
        ]}
      />

      {(detail.original || detail.replacesDocumentId || detail.replacedByDocumentId) && (
        <div className="space-y-2">
          {detail.original && (
            <p className="text-sm">
              Factura original N° {detail.original.number} del {formatFiscalTime(detail.original.issuedAt, false)} ·{' '}
              <span className="font-mono text-xs break-all">CUF {detail.original.cuf}</span>
            </p>
          )}
          <div className="flex flex-wrap gap-2">
            {detail.replacesDocumentId && (
              <Button variant="outline" onClick={() => onOpenDocument(detail.replacesDocumentId as string)}>
                Ver el documento que reemplaza
              </Button>
            )}
            {detail.replacedByDocumentId && (
              <Button variant="outline" onClick={() => onOpenDocument(detail.replacedByDocumentId as string)}>
                Ver el documento que lo reemplaza
              </Button>
            )}
          </div>
        </div>
      )}

      <section aria-labelledby={linesId} className="space-y-2">
        <h3 id={linesId} className="text-sm font-semibold text-text">
          {isNote ? 'Líneas de la nota' : 'Líneas'}
        </h3>
        <ul className="divide-y divide-border rounded-xl border border-border">
          {detail.lines.map((line) => {
            const transaction = lineTransactionText(line.transactionCode);
            return (
              <li key={line.lineNumber} className="space-y-0.5 p-3 text-sm">
                <div className="flex items-start justify-between gap-3">
                  <p className="min-w-0 font-medium">
                    {line.productCode} · {line.description}
                  </p>
                  <p className="shrink-0 font-semibold tabular-nums">{formatMoney(line.subtotal)}</p>
                </div>
                <p className="text-xs text-text-muted">
                  {lineDetailText(line)}
                  {transaction ? ` · ${transaction}` : ''}
                </p>
                {line.serialsText && <p className="text-xs break-words text-text-muted">{line.serialsText}</p>}
              </li>
            );
          })}
        </ul>
        <dl className="ml-auto grid max-w-xs grid-cols-[1fr_auto] gap-x-4 gap-y-1 text-sm">
          <dt className="text-text-muted">Subtotal</dt>
          <dd className="text-right tabular-nums">{formatMoney(totals.subtotal)}</dd>
          {totals.discount > 0 && (
            <>
              <dt className="text-text-muted">Descuento</dt>
              <dd className="text-right tabular-nums">− {formatMoney(totals.discount)}</dd>
            </>
          )}
          <dt className="text-text-muted">IVA incluido</dt>
          <dd className="text-right tabular-nums">{formatMoney(detail.taxAmount)}</dd>
          <dt className="font-semibold">{isNote ? 'Monto devuelto' : 'Total'}</dt>
          <dd className="text-right font-semibold tabular-nums" data-testid="total-documento">
            {formatMoney(row.total)}
          </dd>
        </dl>
      </section>

      <section aria-labelledby={eventsId} className="space-y-2">
        <h3 id={eventsId} className="text-sm font-semibold text-text">
          Bitácora del SIN
        </h3>
        {detail.events.length === 0 ? (
          <p className="text-sm text-text-muted">Todavía no hay pasos registrados.</p>
        ) : (
          <ol className="space-y-3 border-l border-border pl-4" data-testid="bitacora-sin">
            {eventsNewestFirst(detail.events).map((event, index) => (
              <li key={`${event.occurredAt}-${index}`} className="space-y-0.5 text-sm">
                <StatusBadge status={event.action} statuses={EVENT_ACTIONS} />
                <p className="font-medium">{eventTitle(event)}</p>
                <p className="text-xs text-text-muted">
                  {formatDateTime(event.occurredAt)}
                  {event.user ? ` · ${event.user}` : ''}
                  {event.receptionCode ? ` · recepción ${event.receptionCode}` : ''}
                </p>
                {event.description && <p className="text-xs text-text-muted">{event.description}</p>}
                {event.messages && <p className="text-xs whitespace-pre-line break-words text-danger-text">{event.messages}</p>}
              </li>
            ))}
          </ol>
        )}
      </section>

      <section aria-labelledby={deliveriesId} className="space-y-2">
        <h3 id={deliveriesId} className="text-sm font-semibold text-text">
          Entregas al comprador
        </h3>
        {detail.deliveries.length === 0 ? (
          <p className="text-sm text-text-muted">Todavía no se imprimió, descargó ni envió por correo.</p>
        ) : (
          <ul className="space-y-2">
            {detail.deliveries.map((delivery, index) => (
              <li key={`${delivery.occurredAt}-${index}`} className="flex flex-wrap items-center justify-between gap-2 text-sm">
                <span>
                  {channelLabel(delivery.channel)}
                  {delivery.recipient ? ` · ${delivery.recipient}` : ''}
                  <span className="block text-xs text-text-muted">{formatDateTime(delivery.occurredAt)}</span>
                </span>
                <StatusBadge tone={delivery.succeeded ? 'success' : 'danger'}>{delivery.succeeded ? 'Entregado' : (delivery.error ?? 'No se pudo entregar')}</StatusBadge>
              </li>
            ))}
          </ul>
        )}
      </section>

      <Button variant="outline" leftIcon={<FileCode />} onClick={onShowXml}>
        Ver el XML enviado al SIN
      </Button>
    </div>
  );
}
