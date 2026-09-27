// Vista imprimible del resumen: se monta fuera de #root (portal) y solo se muestra al imprimir desde el armador,
// cuando `<html data-print="armado">` está puesto (ver builder.css). Sin backend: window.print() y estilos @media print.

import { createPortal } from 'react-dom';
import type { BuildSummary } from '@/1-domain/builder/build';
import { ivaBreakdown } from '@/1-domain/catalog/money';
import { STORE } from '@/shared/constants';
import { formatMoney, pluralize } from '@/shared/format';
import { formatLongDate, referenceData } from '../builderSteps';

export interface PrintSummaryProps {
  summary: BuildSummary;
  buildNumber: string | null;
  issuedAt: Date;
}

export function PrintSummary({ summary, buildNumber, issuedAt }: PrintSummaryProps) {
  const breakdown = ivaBreakdown(summary.total);
  const filledSlots = summary.slots.filter((entry) => entry.lines.length > 0);
  const references = referenceData(summary.lines);

  return createPortal(
    <section className="builder-print" aria-hidden="true">
      <header className="builder-print__header">
        <div>
          <p className="builder-print__brand">{STORE.wordmark}</p>
          <p>{STORE.legalName}</p>
          <p>
            {STORE.address} · {STORE.city} · {STORE.phone}
          </p>
        </div>
        <div className="builder-print__meta">
          <h1>Resumen de armado</h1>
          {buildNumber && <p>{buildNumber}</p>}
          <p>{formatLongDate(issuedAt)}</p>
        </div>
      </header>

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
          {filledSlots.flatMap(({ slot, lines }) =>
            lines.map((line) => (
              <tr key={line.product.sku}>
                <td>{slot.label}</td>
                <td>{line.product.name}</td>
                <td>{line.product.sku}</td>
                <td className="builder-print__num">{line.quantity}</td>
                <td className="builder-print__num">{formatMoney(line.product.price)}</td>
                <td className="builder-print__num">{formatMoney(line.product.price * line.quantity)}</td>
              </tr>
            )),
          )}
        </tbody>
        <tfoot>
          <tr>
            <th scope="row" colSpan={5}>
              Total ({pluralize(summary.count, 'pieza', 'piezas')}), IVA incluido
            </th>
            <td className="builder-print__num builder-print__total">{formatMoney(summary.total)}</td>
          </tr>
          <tr>
            <td colSpan={5}>IVA (13 %) incluido en el total</td>
            <td className="builder-print__num">{formatMoney(breakdown.iva)}</td>
          </tr>
        </tfoot>
      </table>

      {summary.missing.length > 0 && <p>Piezas esenciales pendientes: {summary.missing.map((slot) => slot.label).join(', ')}.</p>}

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
          <p className="builder-print__note">Tomados de la ficha de cada pieza; este resumen no valida la compatibilidad entre componentes.</p>
        </>
      )}

      <p className="builder-print__note">
        Documento de demostración generado en el navegador: no es una cotización ni un comprobante de compra. Precios en bolivianos con IVA
        incluido, sujetos a cambio sin previo aviso.
      </p>
    </section>,
    document.body,
  );
}
