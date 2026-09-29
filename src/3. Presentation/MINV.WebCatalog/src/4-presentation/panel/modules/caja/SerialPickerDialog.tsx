// Módulo «Caja» · elegir las unidades de un producto con serie o IMEI (regla T-02): las disponibles en la sucursal
// (`GetAvailableSerialsQuery`), sin las que ya usan otras líneas de la venta. Se escanea la serie (Enter la marca) o se
// marca en la lista. En una venta normal la cantidad es la de las unidades elegidas; en una reserva, exactamente las de
// la pieza. La validación que manda es la del servidor (serie en stock, de esta sucursal, sin repetir).

import { Barcode, Check } from 'lucide-react';
import { useId, useMemo, useState, type KeyboardEvent } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, EmptyState, ErrorState, LoadingState, TextField } from '@/4-presentation/panel/kit';
import { formatDate, matchesSearch } from '@/4-presentation/panel/lib';
import { serialLabel, serialNoun } from './products';
import type { SerialKindData } from './types';

/** Qué unidades elegir. */
export interface SerialPickTarget {
  /** Línea de la venta (null = el producto todavía no está en la venta). */
  lineKey: string | null;
  sku: string;
  name: string;
  serialKind: SerialKindData;
  /** Las que ya tiene la línea (quedan marcadas). */
  chosen: readonly string[];
  /** Las que usan otras líneas del mismo producto (no se ofrecen). */
  exclude: readonly string[];
  /** Pieza de una reserva: exactamente estas unidades (null = las que se elijan, al menos una). */
  exact: number | null;
}

export interface SerialPickerDialogProps {
  target: SerialPickTarget | null;
  onClose: () => void;
  onConfirm: (target: SerialPickTarget, serials: string[]) => void;
}

/** Cuántas se muestran a la vez (con más, se pide escanear o buscar). */
const MAX_SHOWN = 60;

export function SerialPickerDialog({ target, onClose, onConfirm }: SerialPickerDialogProps) {
  const listId = useId();
  // El último pedido sigue a la vista mientras el diálogo se cierra; al cambiar de pedido se reinicia la selección.
  const [shown, setShown] = useState<SerialPickTarget | null>(target);
  const [selected, setSelected] = useState<string[]>(() => [...(target?.chosen ?? [])]);
  const [text, setText] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  if (target && target !== shown) {
    setShown(target);
    setSelected([...target.chosen]);
    setText('');
    setNotice(null);
  }

  const available = useRpcQuery('GetAvailableSerialsQuery', { sku: shown?.sku ?? '', warehouseCode: null }, { enabled: target !== null });
  const excluded = useMemo(() => new Set((shown?.exclude ?? []).map((serial) => serial.toUpperCase())), [shown]);
  const options = useMemo(() => {
    const rows = (available.data ?? []).filter((row) => !excluded.has(row.serial.toUpperCase()));
    // Las ya elegidas siempre se ven (aunque el servidor ya no las liste, para poder quitarlas).
    for (const serial of shown?.chosen ?? []) {
      if (!rows.some((row) => row.serial.toUpperCase() === serial.toUpperCase())) rows.unshift({ ...EMPTY_ROW, serial, sku: shown?.sku ?? '' });
    }
    return rows;
  }, [available.data, excluded, shown]);
  const visible = options.filter((row) => matchesSearch(text, [row.serial, row.warehouse]));

  const kind = shown?.serialKind ?? 'Serial';
  const exact = shown?.exact ?? null;
  const isSelected = (serial: string) => selected.some((item) => item.toUpperCase() === serial.toUpperCase());
  const full = exact !== null && selected.length >= exact;

  const toggle = (serial: string) => {
    setNotice(null);
    if (isSelected(serial)) setSelected((current) => current.filter((item) => item.toUpperCase() !== serial.toUpperCase()));
    else if (!full) setSelected((current) => [...current, serial]);
    else setNotice(`Esta pieza lleva ${exact} ${serialNoun(kind, exact ?? 2)}: quite una antes de marcar otra.`);
  };

  /** El lector de códigos escribe la serie y pulsa Enter: se marca (o se desmarca) al instante. */
  const onScan = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const code = text.trim();
    if (code.length === 0) return;
    const match = options.find((row) => row.serial.toUpperCase() === code.toUpperCase());
    if (!match) {
      setNotice(`«${code}» no está entre las unidades disponibles de esta sucursal.`);
      return;
    }
    toggle(match.serial);
    setText('');
  };

  const count = selected.length;
  const ready = exact !== null ? count === exact : count > 0;
  const status = exact !== null ? `Elegidas ${count} de ${exact}` : count === 1 ? '1 unidad elegida' : `${count} unidades elegidas`;

  const confirm = () => {
    if (!shown || !ready) return;
    onConfirm(shown, selected);
  };

  let body;
  if (available.error) body = <ErrorState error={available.error} operation="GetAvailableSerialsQuery" onRetry={available.reload} retrying={available.fetching} />;
  else if (available.data === undefined) body = <LoadingState label="Cargando las unidades disponibles…" rows={2} />;
  else if (options.length === 0)
    body = <EmptyState size="sm" icon={<Barcode />} title="No quedan unidades disponibles" description={`No hay unidades de este producto con ${serialNoun(kind)} en esta sucursal.`} />;
  else
    body = (
      <fieldset aria-describedby={`${listId}-estado`}>
        <legend className="sr-only">Unidades disponibles</legend>
        <ul className="max-h-72 divide-y divide-border overflow-y-auto rounded-xl border border-border" data-testid="series-disponibles">
          {visible.slice(0, MAX_SHOWN).map((row) => (
            <li key={row.serial} className="px-3">
              <Checkbox
                label={<span className="font-mono">{row.serial}</span>}
                description={[row.warehouse, row.receivedAt ? `ingresó el ${formatDate(row.receivedAt)}` : null].filter(Boolean).join(' · ') || undefined}
                checked={isSelected(row.serial)}
                onChange={() => toggle(row.serial)}
              />
            </li>
          ))}
        </ul>
        {visible.length > MAX_SHOWN && <p className="mt-2 text-sm text-text-muted">Se muestran {MAX_SHOWN} de {visible.length}: escanee o busque la serie.</p>}
        {visible.length === 0 && <p className="mt-2 text-sm text-text-muted">Ninguna unidad coincide con «{text}».</p>}
      </fieldset>
    );

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      size="lg"
      title={shown ? `${serialLabel(kind)} de ${shown.name}` : 'Elegir unidades'}
      description="Escanee la unidad que se lleva el cliente o márquela en la lista (disponibles en esta sucursal)."
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cancelar
          </Button>
          <Button leftIcon={<Check />} disabled={!ready} onClick={confirm}>
            {shown?.lineKey ? 'Usar estas unidades' : 'Agregar a la venta'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <TextField
          label={`Escanear o buscar ${kind === 'Imei' ? 'el IMEI' : 'la serie'}`}
          type="search"
          value={text}
          onChange={(value) => {
            setText(value);
            setNotice(null);
          }}
          onKeyDown={onScan}
          leading={<Barcode />}
          autoComplete="off"
          spellCheck={false}
          enterKeyHint="done"
          data-autofocus
        />
        <p id={`${listId}-estado`} role="status" className="text-sm font-medium text-text" data-testid="series-elegidas">
          {status}
          {selected.length > 0 && <span className="ml-2 font-mono text-xs text-text-muted">{selected.join(', ')}</span>}
        </p>
        {notice && <Alert tone="warning">{notice}</Alert>}
        {body}
      </div>
    </Dialog>
  );
}

/** Fila mínima para una serie ya elegida que el servidor ya no lista. */
const EMPTY_ROW = {
  serial: '',
  kind: 'Serial',
  sku: '',
  product: '',
  status: 'InStock',
  branch: null,
  warehouse: null,
  receivedAt: null,
  soldAt: null,
  invoiceNumber: null,
  customer: null,
  warrantyUntil: null,
} as const;
