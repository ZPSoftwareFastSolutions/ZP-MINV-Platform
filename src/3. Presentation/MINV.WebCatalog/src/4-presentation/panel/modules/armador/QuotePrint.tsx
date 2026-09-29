// Módulo «Armador de PC» · la COTIZACIÓN IMPRIMIBLE (la proforma del escritorio): empresa, sucursal, número, fechas,
// cliente, piezas a sus precios cotizados, total con IVA incluido, notas de compatibilidad y quién atendió. Se monta fuera
// de la página (portal en <body>) y solo se ve al imprimir desde el armador (`printing.ts` + `quotePrint.css`).

import './quotePrint.css';
import { createPortal } from 'react-dom';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { slotLabel, sortedIssues, type BuildDetailData } from './builder';

export interface QuotePrintProps {
  detail: BuildDetailData;
  company: string;
  /** «CM · Casa matriz La Paz». */
  branch: string;
  /** Quien imprime (atendió al cliente). */
  seller: string;
  issuedAt: Date;
}

export function QuotePrint({ detail, company, branch, seller, issuedAt }: QuotePrintProps) {
  const build = detail.build;
  const issues = sortedIssues(detail.check);
  return createPortal(
    <section className="minv-cotizacion-print" aria-hidden="true" data-testid="cotizacion-imprimible">
      <header className="minv-cotizacion-print__header">
        <div>
          <h1>{company}</h1>
          <p>Sucursal {branch}</p>
        </div>
        <div className="minv-cotizacion-print__meta">
          <p>
            <strong>Cotización {build.number}</strong>
          </p>
          <p>Emitida el {formatDate(build.createdAt)}</p>
          <p>Válida hasta el {formatDate(build.validUntil)}</p>
        </div>
      </header>
      <p>
        <strong>{build.name}</strong>
      </p>
      <p>Cliente: {build.customer ?? build.contactName ?? 'Consumidor final'}</p>
      <h2>Piezas</h2>
      <table>
        <thead>
          <tr>
            <th>Ranura</th>
            <th>Producto</th>
            <th>SKU</th>
            <th className="minv-cotizacion-print__num">Cant.</th>
            <th className="minv-cotizacion-print__num">Precio</th>
            <th className="minv-cotizacion-print__num">Subtotal</th>
          </tr>
        </thead>
        <tbody>
          {detail.quotedItems.map((item, index) => (
            <tr key={`${item.slot ?? ''}-${item.sku}-${index}`}>
              <td>{slotLabel(item.slot)}</td>
              <td>{item.name}</td>
              <td>{item.sku}</td>
              <td className="minv-cotizacion-print__num">{formatQuantity(item.quantity)}</td>
              <td className="minv-cotizacion-print__num">{formatMoney(item.unitPrice)}</td>
              <td className="minv-cotizacion-print__num">{formatMoney(item.subtotal)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <th colSpan={5}>Total (IVA incluido)</th>
            <td className="minv-cotizacion-print__num">{formatMoney(build.total)}</td>
          </tr>
        </tfoot>
      </table>
      {issues.length > 0 && (
        <>
          <h2>Compatibilidad</h2>
          <ul>
            {issues.map((issue, index) => (
              <li key={`${issue.code}-${index}`}>
                {issue.isError ? (build.quotedWithErrors ? 'Error aceptado por el cliente: ' : 'Error: ') : 'Aviso: '}
                {issue.message}
              </li>
            ))}
          </ul>
        </>
      )}
      <p className="minv-cotizacion-print__note">
        Precios en bolivianos con IVA incluido, congelados hasta el {formatDate(build.validUntil)}. La disponibilidad se confirma al cobrar en la caja. Atendió:{' '}
        {seller}. Impreso el {formatDateTime(issuedAt)}.
      </p>
    </section>,
    document.body,
  );
}
