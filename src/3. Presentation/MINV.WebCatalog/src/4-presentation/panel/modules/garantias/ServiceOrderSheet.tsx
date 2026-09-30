// Módulo «Garantías» · la ORDEN DE SERVICIO de un caso (lo que se entrega al cliente al recibir su equipo y se firma al
// retirarlo): empresa y sucursal, número del caso, cliente, equipo con su serie, cobertura y garantía, falla reportada,
// resolución, bitácora y las firmas. Se muestra en el diálogo «Orden de servicio» (con los colores del tema) y en el área
// de impresión (`paper`: negro sobre blanco). Todo sale del detalle del caso (`GetWarrantyClaimQuery`).

import clsx from 'clsx';
import { formatDateTime } from '@/4-presentation/panel/lib';
import { COVERAGES, claimStatusLabel, claimTimeline, coverageOf, eventTitle, saleLine, warrantyLine, type ClaimDetailData } from './claims';

export interface ServiceOrderSheetProps {
  detail: ClaimDetailData;
  /** Nombre de la empresa (de la sesión). */
  company: string;
  /** «CM · Casa Matriz». */
  branch: string;
  /** Cuándo se imprime. */
  printedAt: Date;
  /** Para imprimir: negro sobre blanco, sin los colores del tema. */
  paper?: boolean;
}

export function ServiceOrderSheet({ detail, company, branch, printedAt, paper = false }: ServiceOrderSheetProps) {
  const claim = detail.claim;
  const muted = paper ? 'text-black/70' : 'text-text-muted';
  const rule = paper ? 'border-black/30' : 'border-border';
  const sale = saleLine(detail);
  const warranty = warrantyLine(detail.warranty);
  const events = claimTimeline(detail.events).reverse();

  const block = (title: string, content: React.ReactNode) => (
    <section className={clsx('space-y-1 border-b pb-3', rule)}>
      <h3 className="text-xs font-semibold tracking-wide uppercase">{title}</h3>
      <div>{content}</div>
    </section>
  );

  return (
    <article className={clsx('space-y-3 text-sm', paper ? 'text-black' : 'text-text')} aria-label={`Orden de servicio ${claim.number}`} data-testid="orden-de-servicio">
      <header className={clsx('space-y-1 border-b pb-3', rule)}>
        <p className="font-semibold">{company}</p>
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="font-display text-lg font-semibold uppercase">Orden de servicio técnico</p>
          <p className="font-display text-lg font-semibold">{claim.number}</p>
        </div>
        <p className={muted}>
          Sucursal {branch} · recibido el {formatDateTime(claim.receivedAt)}
        </p>
        <p>
          Estado: <span className="font-semibold">{claimStatusLabel(claim.status)}</span> · Cobertura: <span className="font-semibold">{COVERAGES[coverageOf(claim)].label}</span>
          {claim.isInWarranty ? '' : ' (reparación con cargo)'}
        </p>
      </header>

      {block('Cliente', <p className="font-semibold">{claim.customer}</p>)}

      {block(
        'Equipo',
        <>
          <p className="font-semibold">{claim.product}</p>
          <p>
            SKU {claim.sku} · Serie o IMEI <span className="font-mono">{claim.serial}</span>
          </p>
        </>,
      )}

      {block(
        'Garantía',
        <>
          <p>{warranty.text}</p>
          {sale && <p className={muted}>{sale.charAt(0).toUpperCase() + sale.slice(1)}</p>}
        </>,
      )}

      {block('Falla reportada', <p className="whitespace-pre-line">{claim.issue}</p>)}

      {(claim.resolution || claim.supplier || claim.replacementSerial) &&
        block(
          'Resolución',
          <>
            {claim.resolution && <p>{claim.resolution}</p>}
            {claim.supplier && <p>Proveedor: {claim.supplier}</p>}
            {claim.replacementSerial && (
              <p>
                Unidad de reemplazo: <span className="font-mono">{claim.replacementSerial}</span>
              </p>
            )}
          </>,
        )}

      {events.length > 0 &&
        block(
          'Bitácora',
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className={clsx('border-b', rule)}>
                <th scope="col" className="py-1 pr-2 font-semibold">
                  Fecha
                </th>
                <th scope="col" className="py-1 pr-2 font-semibold">
                  Hecho
                </th>
                <th scope="col" className="py-1 pr-2 font-semibold">
                  Detalle
                </th>
                <th scope="col" className="py-1 font-semibold">
                  Usuario
                </th>
              </tr>
            </thead>
            <tbody>
              {events.map((event, index) => (
                <tr key={`${event.occurredAt}-${index}`} className={clsx('border-b align-top last:border-0', rule)}>
                  <td className="py-1 pr-2 whitespace-nowrap tabular-nums">{formatDateTime(event.occurredAt)}</td>
                  <td className="py-1 pr-2">{eventTitle(event)}</td>
                  <td className="py-1 pr-2">{event.note ?? ''}</td>
                  <td className="py-1">{event.user}</td>
                </tr>
              ))}
            </tbody>
          </table>,
        )}

      <footer className="space-y-6 pt-2">
        <p className={muted}>Conserve esta orden: se pide para retirar el equipo. La garantía la decide la revisión técnica.</p>
        <div className="grid grid-cols-2 gap-8 pt-6">
          <p className={clsx('border-t pt-1 text-center', rule)}>Firma del cliente</p>
          <p className={clsx('border-t pt-1 text-center', rule)}>Recibido por</p>
        </div>
        <p className={clsx('text-xs', muted)}>Impreso el {formatDateTime(printedAt)}</p>
      </footer>
    </article>
  );
}
