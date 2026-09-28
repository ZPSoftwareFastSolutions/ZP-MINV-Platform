// Vista imprimible: se monta fuera de #root (portal) y solo se muestra al imprimir desde el armador, cuando
// `<html data-print="armado">` está puesto (ver builder.css). Imprime el armado en curso (a los precios del catálogo) o,
// si el armado ya se reservó y quedó vacío, el comprobante de la reserva (número, vigencia y precios congelados).
// Sin backend: window.print() y estilos @media print.

import { createPortal } from 'react-dom';
import type { BuildSummary } from '@/1-domain/builder/build';
import { BUILD_SLOTS } from '@/1-domain/builder/slots';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import type { Reservation, StoreInfo } from '@/1-domain/storefront/types';
import { branchName, formatDateTime } from '@/4-presentation/components/reservation/reservationText';
import { STORE } from '@/shared/constants';
import { formatMoney, pluralize } from '@/shared/format';
import { formatLongDate, referenceData } from '../builderSteps';

export interface PrintSummaryProps {
  summary: BuildSummary;
  /** Última reserva aceptada por la tienda: se imprime cuando el armado quedó vacío tras reservar. */
  reservation: Reservation | null;
  store: StoreInfo;
  issuedAt: Date;
}

interface PrintLine {
  key: string;
  slot: string;
  name: string;
  sku: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

function slotLabel(slot: string): string {
  return BUILD_SLOTS.find((candidate) => candidate.key === slot)?.label ?? slot;
}

export function PrintSummary({ summary, reservation, store, issuedAt }: PrintSummaryProps) {
  const reserved = summary.lines.length === 0 && reservation ? reservation : null;
  const lines: PrintLine[] = reserved
    ? reserved.lines.map((line) => ({ key: `${line.sku}-${line.slot}`, slot: slotLabel(line.slot), name: line.name, sku: line.sku, quantity: line.quantity, unitPrice: line.unitPrice, subtotal: line.subtotal }))
    : summary.slots.flatMap(({ slot, lines: slotLines }) =>
        slotLines.map((line) => ({
          key: line.product.sku,
          slot: slot.label,
          name: line.product.name,
          sku: line.product.sku,
          quantity: line.quantity,
          unitPrice: line.product.price,
          subtotal: line.product.price * line.quantity,
        })),
      );
  const total = reserved ? reserved.total : summary.total;
  const count = reserved ? reserved.lines.reduce((acc, line) => acc + line.quantity, 0) : summary.count;
  const breakdown = ivaBreakdown(total);
  const references = reserved ? [] : referenceData(summary.lines);

  return createPortal(
    <section className="builder-print" aria-hidden="true">
      <header className="builder-print__header">
        <div>
          <p className="builder-print__brand">{STORE.wordmark}</p>
          <p>{store.company.name}</p>
          <p>
            {store.branch.name} · {STORE.phone}
          </p>
        </div>
        <div className="builder-print__meta">
          <h1>{reserved ? 'Comprobante de reserva' : 'Resumen de armado'}</h1>
          {reserved && <p>{reserved.number}</p>}
          <p>{formatLongDate(reserved ? reserved.createdAt : issuedAt)}</p>
        </div>
      </header>

      {reserved && (
        <p>
          Estado: {reserved.statusText}. A nombre de {reserved.contactName}. {reserved.status === 'Reserved' ? 'Vence' : 'Vencía'} el{' '}
          {formatDateTime(reserved.reservedUntil)}. Retiro en {branchName(reserved, store)}.
        </p>
      )}

      <table>
        <thead>
          <tr>
            <th scope="col">Paso</th>
            <th scope="col">Pieza</th>
            <th scope="col">SKU</th>
            <th scope="col" className="builder-print__num">
              Cant.
            </th>
            <th scope="col" className="builder-print__num">
              Precio
            </th>
            <th scope="col" className="builder-print__num">
              Subtotal
            </th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line) => (
            <tr key={line.key}>
              <td>{line.slot}</td>
              <td>{line.name}</td>
              <td>{line.sku}</td>
              <td className="builder-print__num">{line.quantity}</td>
              <td className="builder-print__num">{formatMoney(line.unitPrice)}</td>
              <td className="builder-print__num">{formatMoney(line.subtotal)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colSpan={5}>
              Total ({pluralize(count, 'pieza', 'piezas')}), IVA incluido
            </th>
            <td className="builder-print__num builder-print__total">{formatMoney(total)}</td>
          </tr>
          <tr>
            <td colSpan={5}>IVA (13 %) incluido en el total</td>
            <td className="builder-print__num">{formatMoney(breakdown.iva)}</td>
          </tr>
        </tfoot>
      </table>

      {!reserved && summary.missing.length > 0 && <p>Piezas esenciales pendientes: {summary.missing.map((slot) => slot.label).join(', ')}.</p>}

      {references.length > 0 && (
        <>
          <h2>Datos de referencia</h2>
          <ul>
            {references.map((item) => (
              <li key={item.key}>
                {item.label}: {item.value}
              </li>
            ))}
          </ul>
          <p className="builder-print__note">Tomados de la ficha de cada pieza; la compatibilidad la revisa un técnico de la tienda al confirmar.</p>
        </>
      )}

      <p className="builder-print__note">
        {reserved
          ? `Reserva registrada desde la tienda web a los precios cotizados. Se confirma y paga en ${branchName(reserved, store)}; no es una factura ni un comprobante de pago.`
          : 'Resumen generado en el navegador a los precios vigentes del catálogo: no es una reserva ni una cotización. Precios en bolivianos con IVA incluido, sujetos a cambio sin previo aviso.'}
      </p>
    </section>,
    document.body,
  );
}
