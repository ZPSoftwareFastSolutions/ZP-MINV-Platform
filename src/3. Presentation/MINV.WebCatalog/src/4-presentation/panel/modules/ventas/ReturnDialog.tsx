// Módulo «Ventas» · devolución guiada de una venta (`CreateSalesReturnCommand`), como el escritorio (SalesReturnDialog):
//   1. La venta: llega elegida desde la lista o se escribe su número (botón «Registrar devolución» y tablero).
//   2. Qué se devuelve: lo que queda de cada producto (vendido − devuelto, `GetReturnableLinesQuery`); la cantidad o, si el
//      producto lleva serie (T-02), las series de ESTA venta que siguen vendidas (`GetSaleLinesQuery` + `SearchSerialsQuery`);
//      el motivo, si es POR FALLA (se reembolsa pero no vuelve al stock vendible: queda en garantía) y el medio de
//      reembolso (medios de la caja, `GetPosStateQuery`). El reembolso que se muestra es una estimación.
//   3. Confirmación (no se puede deshacer) y envío. Si la venta tenía factura válida, el servidor emite la nota
//      crédito-débito en la misma operación y la página la envía enseguida al SIN (`DispatchFiscalDocumentsCommand`, como
//      el escritorio); si no se puede, la envía después el trabajo automático del servidor.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza siempre vacío.

import { ArrowLeft, CheckCheck, Search, Undo2 } from 'lucide-react';
import { useCallback, useId, useMemo, useState, type ReactNode } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  EmptyState,
  ErrorState,
  Form,
  LoadingState,
  NumberField,
  SelectField,
  TextField,
  useNotify,
} from '@/4-presentation/panel/kit';
import { formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { SerialChoices } from './SerialChoices';
import {
  DEFECT_REASON,
  OTHER_REASON,
  REASON_MAX,
  RETURN_REASONS,
  buildReturnDraft,
  defaultRefundMethod,
  estimatedRefund,
  isSerialized,
  lineRefund,
  normalizeSaleNumber,
  plainMessage,
  reasonOptions,
  reasonText,
  returnProblems,
  returnQuantity,
  toReturnPayload,
  type ReturnDraftLine,
  type ReturnOutcome,
} from './sales';

/** Qué venta se devuelve: con número (desde la lista) o null para escribirlo. */
export interface ReturnTarget {
  invoiceNumber: string | null;
}

export interface ReturnDialogProps {
  /** null = cerrado. */
  target: ReturnTarget | null;
  onClose: () => void;
  /** Después de registrar la devolución (la pantalla recarga y muestra «Devoluciones»). */
  onReturned: (outcome: ReturnOutcome, invoiceNumber: string) => void;
}

type Step = 'lineas' | 'confirmar';

const DEFECTIVE_HELP =
  'Se reembolsa al cliente, pero la mercadería NO vuelve al stock vendible: queda en garantía para devolverla al proveedor, repararla o darla de baja.';

export function ReturnDialog({ target, onClose, onReturned }: ReturnDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [initial] = useState(target);
  const open = target !== null;
  const asksNumber = (initial?.invoiceNumber ?? null) === null;

  const [invoice, setInvoice] = useState<string | null>(initial?.invoiceNumber ?? null);
  const [numberText, setNumberText] = useState('');
  const [numberTouched, setNumberTouched] = useState(false);
  const [step, setStep] = useState<Step>('lineas');
  const [quantities, setQuantities] = useState<Record<string, number | null>>({});
  const [serials, setSerials] = useState<Record<string, string[]>>({});
  const [available, setAvailable] = useState<Record<string, readonly string[] | null>>({});
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [defective, setDefective] = useState(false);
  const [method, setMethod] = useState('');
  const [touched, setTouched] = useState(false);

  const enabled = open && invoice !== null;
  const returnable = useRpcQuery('GetReturnableLinesQuery', { invoiceNumber: invoice ?? '' }, { enabled });
  const saleLines = useRpcQuery('GetSaleLinesQuery', { invoiceNumber: invoice ?? '' }, { enabled });
  const pos = useRpcQuery('GetPosStateQuery', {}, { enabled });
  const create = useRpcCommand('CreateSalesReturnCommand', { notifyError: false });
  const dispatch = useRpcCommand('DispatchFiscalDocumentsCommand', { notifyError: false });

  const lines = useMemo(() => (returnable.data && saleLines.data ? buildReturnDraft(returnable.data, saleLines.data) : []), [returnable.data, saleLines.data]);
  const methods = useMemo(() => pos.data?.paymentMethods ?? [], [pos.data]);
  const methodCode = method || defaultRefundMethod(methods);
  const chosenMethod = methods.find((item) => item.code === methodCode) ?? null;
  const reason = reasonText(choice, other);
  const problems = returnProblems(lines, quantities, serials, reason, methodCode);
  const refund = estimatedRefund(lines, quantities, serials);
  const nothingLeft = lines.length > 0 && lines.every((line) => line.available <= 0);

  // Series que siguen vendidas de cada producto (las informa `SerialChoices` al consultarlas).
  const onResolved = useCallback((sku: string, list: readonly string[] | null) => {
    setAvailable((current) => {
      const before = current[sku];
      if (before === list || (before && list && before.join('\n') === list.join('\n'))) return current;
      return { ...current, [sku]: list };
    });
  }, []);

  const clearErrors = () => {
    if (create.error) create.reset();
  };

  const loadingLines = enabled && (returnable.loading || saleLines.loading || pos.loading);
  const loadError = returnable.error ?? saleLines.error ?? pos.error;
  const retryLoad = () => {
    if (returnable.error) returnable.reload();
    if (saleLines.error) saleLines.reload();
    if (pos.error) pos.reload();
  };

  const returnAll = () => {
    const nextQuantities: Record<string, number | null> = {};
    const nextSerials: Record<string, string[]> = {};
    for (const line of lines) {
      if (line.available <= 0) continue;
      if (isSerialized(line)) nextSerials[line.sku] = [...(available[line.sku] ?? line.serials)].slice(0, Math.max(0, Math.round(line.available)));
      else nextQuantities[line.sku] = line.available;
    }
    setQuantities(nextQuantities);
    setSerials(nextSerials);
    clearErrors();
  };

  const submit = async () => {
    if (invoice === null) {
      setNumberTouched(true);
      const number = normalizeSaleNumber(numberText);
      if (number) setInvoice(number);
      return;
    }
    if (step === 'lineas') {
      setTouched(true);
      if (problems) return;
      setStep('confirmar');
      return;
    }
    const payload = toReturnPayload(invoice, lines, quantities, serials, reason, methodCode, defective);
    const outcome = await create.run(payload);
    if (!outcome.ok) return;
    notify.success(`Devolución ${outcome.result.number} registrada`, plainMessage(outcome.result.message));
    // La nota crédito-débito se envía al SIN enseguida (como el escritorio); si no se puede, la envía el servidor después.
    if (outcome.result.creditNoteId && canRun('DispatchFiscalDocumentsCommand')) {
      await dispatch.run({ documentId: outcome.result.creditNoteId, max: 50 });
    }
    onReturned(outcome.result, invoice);
    onClose();
  };

  const busy = create.sending || dispatch.sending;
  const title = invoice ? `Devolución de la venta ${invoice}` : 'Registrar una devolución';

  // ---------------------------------------------------------------------------------------------- pie del diálogo
  let footer: ReactNode;
  if (invoice === null) {
    footer = (
      <>
        <Button variant="outline" onClick={onClose}>
          Cancelar
        </Button>
        <Button type="submit" form={formId} leftIcon={<Search />}>
          Buscar la venta
        </Button>
      </>
    );
  } else if (step === 'confirmar') {
    footer = (
      <>
        <Button variant="outline" leftIcon={<ArrowLeft />} disabled={busy} onClick={() => setStep('lineas')}>
          Volver
        </Button>
        <Button type="submit" form={formId} leftIcon={<Undo2 />} loading={busy}>
          Registrar devolución
        </Button>
      </>
    );
  } else {
    footer = (
      <>
        <Button variant="outline" onClick={onClose}>
          {nothingLeft || loadError ? 'Cerrar' : 'Cancelar'}
        </Button>
        {!nothingLeft && !loadError && (
          <Button type="submit" form={formId} disabled={loadingLines || lines.length === 0}>
            Revisar la devolución
          </Button>
        )}
      </>
    );
  }

  // ---------------------------------------------------------------------------------------------- contenido
  let body: ReactNode;
  if (invoice === null) {
    body = (
      <Form id={formId} onSubmit={submit}>
        <p className="text-sm text-text-muted">Escriba el número de la venta que el cliente devuelve. También puede elegirla en la lista y usar «Devolver productos».</p>
        <TextField
          label="Número de la venta"
          placeholder="F-CM-000123"
          value={numberText}
          onChange={setNumberText}
          hint="Es el número de la factura de M-INV: está en el comprobante y en la lista de ventas."
          error={numberTouched && normalizeSaleNumber(numberText) === '' ? 'Escriba el número de la venta.' : undefined}
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          maxLength={40}
          required
          data-autofocus
        />
      </Form>
    );
  } else if (loadError) {
    body = (
      <div className="space-y-3">
        <ErrorState error={loadError} title="No se pudo leer la venta" onRetry={retryLoad} />
        {asksNumber && (
          <Button
            variant="outline"
            leftIcon={<Search />}
            onClick={() => {
              setInvoice(null);
              setNumberTouched(false);
            }}
          >
            Buscar otra venta
          </Button>
        )}
      </div>
    );
  } else if (loadingLines || !returnable.data || !saleLines.data || !pos.data) {
    body = <LoadingState label="Cargando lo que se puede devolver…" rows={2} />;
  } else if (nothingLeft) {
    body = <EmptyState size="sm" icon={<CheckCheck />} title="Nada por devolver" description={`Todo lo vendido en ${invoice} ya se devolvió.`} />;
  } else if (step === 'confirmar') {
    body = (
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={busy}>
        <Alert tone="warning" title="Revise antes de registrar: no se puede deshacer">
          {defective
            ? 'Se reembolsa al cliente y la mercadería queda en garantía (no vuelve al stock vendible).'
            : 'Vuelve el stock al almacén y se reembolsa al cliente.'}{' '}
          Si la venta tiene factura válida, se emite la nota crédito-débito.
        </Alert>
        <ul className="divide-y divide-border rounded-xl border border-border text-sm" aria-label="Productos que se devuelven">
          {lines
            .filter((line) => returnQuantity(line, quantities, serials) > 0)
            .map((line) => {
              const quantity = returnQuantity(line, quantities, serials);
              return (
                <li key={line.sku} className="flex items-start justify-between gap-3 p-3">
                  <span className="min-w-0">
                    <span className="block text-text">
                      {formatQuantity(quantity)} {line.unit} × {line.name}
                    </span>
                    <span className="block text-xs text-text-muted">{line.sku}</span>
                    {isSerialized(line) && <span className="block text-xs break-words text-text-muted">Series: {(serials[line.sku] ?? []).join(', ')}</span>}
                  </span>
                  <span className="shrink-0 font-semibold tabular-nums">{formatMoney(lineRefund(line, quantity))}</span>
                </li>
              );
            })}
        </ul>
        <dl className="grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-4 gap-y-1.5 text-sm">
          <dt className="text-text-muted">Motivo</dt>
          <dd className="break-words text-text">{reason}</dd>
          <dt className="text-text-muted">Por falla</dt>
          <dd className="text-text">{defective ? 'Sí: queda en garantía' : 'No'}</dd>
          <dt className="text-text-muted">Reembolso</dt>
          <dd className="text-text">{chosenMethod?.name ?? methodCode}</dd>
          <dt className="text-text-muted">Reembolso estimado</dt>
          <dd className="font-semibold text-text tabular-nums">{formatMoney(refund)}</dd>
        </dl>
      </Form>
    );
  } else {
    body = (
      <Form id={formId} onSubmit={submit} error={create.errorText}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-sm text-text-muted">Indique qué devuelve el cliente. Solo aparece lo que todavía no se devolvió.</p>
          <Button variant="subtle" leftIcon={<CheckCheck />} onClick={returnAll}>
            Devolver todo
          </Button>
        </div>
        {touched && problems?.lines && (
          <Alert tone="danger" className="text-sm">
            {problems.lines}
          </Alert>
        )}
        <ul className="space-y-3" aria-label="Productos de la venta">
          {lines.map((line) => (
            <ReturnLineRow
              key={line.sku}
              line={line}
              quantity={quantities[line.sku] ?? null}
              onQuantity={(value) => {
                setQuantities((current) => ({ ...current, [line.sku]: value }));
                clearErrors();
              }}
              selected={serials[line.sku] ?? []}
              onSerials={(list) => {
                setSerials((current) => ({ ...current, [line.sku]: list }));
                clearErrors();
              }}
              onResolved={onResolved}
              error={problems?.bySku[line.sku] && (touched || returnQuantity(line, quantities, serials) > line.available) ? problems.bySku[line.sku] : undefined}
            />
          ))}
        </ul>
        <SelectField
          label="Motivo de la devolución"
          allLabel={false}
          placeholder="Elija el motivo"
          value={choice}
          onChange={(value) => {
            setChoice(value);
            // Un producto con falla sugiere la devolución POR FALLA (se puede desmarcar).
            if (value === DEFECT_REASON) setDefective(true);
            clearErrors();
          }}
          options={reasonOptions(RETURN_REASONS)}
          error={touched && choice === '' ? 'Elija el motivo o escriba uno.' : undefined}
          required
        />
        {choice === OTHER_REASON && (
          <TextField
            label="Escriba el motivo"
            value={other}
            onChange={setOther}
            maxLength={REASON_MAX}
            error={touched && problems?.reason ? problems.reason : undefined}
            required
          />
        )}
        <Checkbox label="Devolución por falla" description={DEFECTIVE_HELP} checked={defective} onChange={setDefective} />
        <SelectField
          label="Se reembolsa con"
          allLabel={false}
          placeholder="Elija el medio"
          value={methodCode}
          onChange={(value) => {
            setMethod(value);
            clearErrors();
          }}
          options={methods.map((item) => ({ value: item.code, label: item.name }))}
          hint={chosenMethod?.opensCashDrawer ? 'Si usted tiene un turno de caja abierto, el reembolso se registra como una salida de efectivo de su caja.' : undefined}
          error={touched && problems?.method ? problems.method : undefined}
          required
        />
        <p className="text-sm text-text" role="status" aria-live="polite" data-testid="reembolso-estimado">
          Reembolso estimado: <strong className="tabular-nums">{formatMoney(refund)}</strong>
        </p>
      </Form>
    );
  }

  return (
    <Dialog open={open} onClose={onClose} dismissible={!busy} size="lg" title={title} description={invoice ? 'Vuelve el stock, se reembolsa y, si la venta tiene factura, se emite la nota crédito-débito.' : undefined} footer={footer}>
      {body}
    </Dialog>
  );
}

interface ReturnLineRowProps {
  line: ReturnDraftLine;
  quantity: number | null;
  onQuantity: (value: number | null) => void;
  selected: readonly string[];
  onSerials: (list: string[]) => void;
  onResolved: (sku: string, list: readonly string[] | null) => void;
  error?: string;
}

/** Un producto de la venta: lo que queda por devolver y la cantidad (o las series) a devolver. */
function ReturnLineRow({ line, quantity, onQuantity, selected, onSerials, onResolved, error }: ReturnLineRowProps) {
  const price = `${formatMoney(line.unitPrice)}${line.discountPercent > 0 ? ` · descuento ${formatQuantity(line.discountPercent)} %` : ''}${line.mixedPrices ? ' (y otros precios)' : ''}`;
  const count = isSerialized(line) ? selected.length : (quantity ?? 0);
  return (
    <li className="space-y-2 rounded-xl border border-border p-3" data-testid={`devolver-${line.sku}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-medium text-text">{line.name}</p>
          <p className="text-xs text-text-muted">
            {line.sku} · {price}
          </p>
          <p className="text-xs text-text-muted">
            Vendido {formatQuantity(line.sold)} · devuelto {formatQuantity(line.returned)} · quedan {formatQuantity(line.available)} {line.unit}
          </p>
        </div>
        {count > 0 && <p className="shrink-0 text-sm font-semibold tabular-nums text-text">{formatMoney(lineRefund(line, count))}</p>}
      </div>
      {line.available <= 0 ? (
        <p className="text-sm text-text-muted">Ya se devolvió todo lo vendido de este producto.</p>
      ) : isSerialized(line) ? (
        <SerialChoices line={line} selected={selected} onChange={onSerials} onResolved={onResolved} error={error} />
      ) : (
        <NumberField label="Cantidad a devolver" value={quantity} onChange={onQuantity} decimals={3} unit={line.unit} hint={`De 0 a ${formatQuantity(line.available)}.`} error={error} />
      )}
    </li>
  );
}
