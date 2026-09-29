// Módulo «Caja» · el comprobante dibujado (vista previa en pantalla dentro de «Ver el comprobante») y su impresión con la
// vista imprimible del navegador: `ReceiptPrint` agrega el comprobante al final de <body>, marca <html
// data-imprimir="caja"> (la hoja `caja-impresion.css` oculta todo lo demás en el papel) y llama a `window.print()`. Al
// terminar (evento `afterprint`) o al cerrarse, quita la marca y el comprobante.

import './caja-impresion.css';
import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import type { ReceiptView } from './receipt';

export interface ReceiptSheetProps {
  view: ReceiptView;
}

export function ReceiptSheet({ view }: ReceiptSheetProps) {
  return (
    <article aria-label={view.title} className="mx-auto max-w-md space-y-3 text-sm text-text" data-testid="comprobante">
      <header className="space-y-0.5 text-center">
        {view.header.map((line, index) => (
          <p key={`${line}-${index}`} className={index === 0 ? 'font-semibold' : 'text-xs text-text-muted'}>
            {line}
          </p>
        ))}
      </header>
      <div className="text-center">
        <p className="font-display text-base font-bold uppercase tracking-wide">{view.title}</p>
        {view.subtitle && <p className="text-xs text-text-muted">{view.subtitle}</p>}
      </div>
      {view.warning && <p className="rounded-lg border border-danger/50 px-2 py-1 text-center text-xs font-bold uppercase text-danger-text">{view.warning}</p>}
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs">
        {view.meta.map((pair) => (
          <div key={pair.label} className="contents">
            <dt className="text-text-muted">{pair.label}</dt>
            <dd className="min-w-0 break-all text-right text-text">{pair.value}</dd>
          </div>
        ))}
      </dl>
      <ul className="divide-y divide-border border-y border-border">
        {view.lines.map((line, index) => (
          <li key={`${line.description}-${index}`} className="py-1.5">
            <div className="flex items-start justify-between gap-3">
              <p className="min-w-0 break-words">{line.description}</p>
              <p className="shrink-0 font-medium tabular-nums">{formatMoney(line.amount)}</p>
            </div>
            <p className="text-xs text-text-muted tabular-nums">
              {formatQuantity(line.quantity)} × {formatMoney(line.unitPrice)}
              {line.discount > 0 && ` · descuento ${formatMoney(line.discount)}`}
            </p>
            {line.serials && <p className="break-all text-xs">{line.serials}</p>}
            {line.warranty && <p className="text-xs text-text-muted">{line.warranty}</p>}
          </li>
        ))}
      </ul>
      <dl className="space-y-0.5">
        {view.totals.map((pair) => (
          <div key={pair.label} className={pair.strong ? 'flex justify-between gap-3 text-base font-bold' : 'flex justify-between gap-3 text-xs'}>
            <dt>{pair.label}</dt>
            <dd className="tabular-nums">{pair.value}</dd>
          </div>
        ))}
      </dl>
      {view.notes.length > 0 && (
        <ul className="space-y-1 text-center text-xs text-text-muted">
          {view.notes.map((note, index) => (
            <li key={`${note}-${index}`} className="break-words">
              {note}
            </li>
          ))}
        </ul>
      )}
    </article>
  );
}

export interface ReceiptPrintProps {
  view: ReceiptView;
  /** El navegador terminó de imprimir (o se canceló la impresión). */
  onDone: () => void;
}

/** Imprime el comprobante al montarse (una impresión por montaje: use `key` para imprimir otra vez). */
export function ReceiptPrint({ view, onDone }: ReceiptPrintProps) {
  const onDoneRef = useRef(onDone);
  // En desarrollo (modo estricto) el efecto corre dos veces: la impresión se pide una sola.
  const printed = useRef(false);
  useEffect(() => {
    onDoneRef.current = onDone;
  });

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('data-imprimir', 'caja');
    const finish = () => onDoneRef.current();
    window.addEventListener('afterprint', finish, { once: true });
    if (!printed.current && typeof window.print === 'function') {
      printed.current = true;
      window.print();
    }
    return () => {
      window.removeEventListener('afterprint', finish);
      root.removeAttribute('data-imprimir');
    };
  }, []);

  return createPortal(
    <div className="caja-impresion" aria-hidden="true" data-testid="comprobante-impresion">
      <ReceiptSheet view={view} />
    </div>,
    document.body,
  );
}
