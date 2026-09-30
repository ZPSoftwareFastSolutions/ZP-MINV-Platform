// Módulo «Órdenes de compra» · recibir mercadería (`ReceivePurchaseOrderCommand`), como el escritorio: entra al stock TODO
// lo pendiente de la orden, se actualiza el costo promedio y se registra el asiento (inventario contra proveedores).
// Se pide la factura o remisión del proveedor (opcional) y, para los productos serializados (regla T-02), una serie o
// IMEI por unidad pendiente (se escanean o se pegan, una por renglón). Qué productos llevan serie sale de la ficha técnica
// (`SearchTechProductsQuery`); el servidor vuelve a validar las series. El error se muestra DENTRO del diálogo.
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { PackageCheck } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, ErrorState, Form, LoadingState, TextArea, TextField } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  LIMITS,
  parseSerials,
  pendingLines,
  pendingOf,
  pendingValue,
  receivePayload,
  serialKinds,
  serialWord,
  serialsProblem,
  type OrderItem,
  type ReceiptOutcome,
} from './purchasing';

/** Ficha técnica de todos los productos activos (solo interesa si llevan serie). */
const TECH_REQUEST = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 };

export interface ReceiveOrderDialogProps {
  /** La orden a recibir (null = cerrado). */
  target: OrderItem | null;
  onClose: () => void;
  onReceived: (result: ReceiptOutcome) => void;
}

export function ReceiveOrderDialog({ target, onClose, onReceived }: ReceiveOrderDialogProps) {
  const formId = useId();
  const { canRun } = usePermissions();
  const [shown] = useState(target);
  const order = target ?? shown;
  const open = target !== null;
  const detail = useRpcQuery('GetPurchaseOrderQuery', { id: order?.key ?? '' }, { enabled: open && order !== null });
  const tech = useRpcQuery('SearchTechProductsQuery', TECH_REQUEST, { enabled: open && canRun('SearchTechProductsQuery') });
  const receive = useRpcCommand('ReceivePurchaseOrderCommand', { notifyError: false });
  const [supplierDocument, setSupplierDocument] = useState('');
  const [serialText, setSerialText] = useState<Record<string, string>>({});
  const [touched, setTouched] = useState(false);

  const lines = pendingLines(detail.data);
  const kinds = serialKinds(tech.data);
  const serialized = lines.filter((line) => kinds.has(line.sku.toUpperCase()));
  const techReady = !canRun('SearchTechProductsQuery') || tech.data !== undefined || tech.error !== null;
  const documentProblem = supplierDocument.trim().length > LIMITS.supplierDocument ? `Hasta ${LIMITS.supplierDocument} caracteres.` : null;
  const serialProblems = new Map(
    serialized.map((line) => {
      const pending = pendingOf(line);
      const kind = kinds.get(line.sku.toUpperCase());
      const problem = Number.isInteger(pending) ? serialsProblem(parseSerials(serialText[line.sku] ?? ''), pending, kind) : 'La cantidad pendiente no es entera: revise la orden.';
      return [line.sku, problem] as const;
    }),
  );
  const hasProblems = documentProblem !== null || [...serialProblems.values()].some((problem) => problem !== null);

  const close = () => {
    receive.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!order || hasProblems || lines.length === 0) return;
    const serials = new Map(serialized.map((line) => [line.sku, parseSerials(serialText[line.sku] ?? '')] as const));
    const outcome = await receive.run(receivePayload(order.key, supplierDocument, serials));
    if (outcome.ok) {
      onReceived(outcome.result);
      close();
    }
  };

  let body;
  if (detail.error) body = <ErrorState error={detail.error} operation="GetPurchaseOrderQuery" onRetry={detail.reload} retrying={detail.fetching} />;
  else if (!detail.data || !techReady) body = <LoadingState label="Cargando lo pendiente de la orden…" rows={2} />;
  else if (lines.length === 0) body = <Alert tone="info">La orden {order?.row.number} ya se recibió completa: no hay nada pendiente.</Alert>;
  else
    body = (
      <Form id={formId} onSubmit={submit} error={receive.errorText} busy={receive.sending}>
        <Alert tone="info">
          Entra al stock TODO lo pendiente, se actualiza el costo promedio y se registra el asiento (inventario contra proveedores).
          {serialized.length > 0 && ` ${serialized.length === 1 ? 'Un producto lleva' : `${serialized.length} productos llevan`} serie: escriba una por unidad.`}
        </Alert>
        <ul className="divide-y divide-border rounded-xl border border-border" aria-label="Lo que entra al stock" data-testid="pendiente-recepcion">
          {lines.map((line) => (
            <li key={line.lineId} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 px-3 py-2 text-sm">
              <span className="min-w-0">
                <span className="block font-medium text-text">{line.name}</span>
                <span className="block text-xs text-text-muted">{line.sku}</span>
              </span>
              <span className="tabular-nums text-text-muted">
                {formatQuantity(pendingOf(line), { unit: line.unit })} × {formatMoney(line.unitCost)}
              </span>
            </li>
          ))}
          <li className="flex justify-between px-3 py-2 text-sm font-semibold">
            <span>Valor pendiente</span>
            <span className="tabular-nums">{formatMoney(pendingValue(lines))}</span>
          </li>
        </ul>
        <TextField
          label="Factura o remisión del proveedor"
          value={supplierDocument}
          onChange={(value) => {
            setSupplierDocument(value);
            if (receive.error) receive.reset();
          }}
          maxLength={LIMITS.supplierDocument}
          placeholder="Ej.: FAC-12345"
          autoComplete="off"
          error={touched && documentProblem ? documentProblem : undefined}
          optional
          data-autofocus
        />
        {serialized.map((line) => {
          const kind = kinds.get(line.sku.toUpperCase());
          const pending = pendingOf(line);
          const count = parseSerials(serialText[line.sku] ?? '').length;
          return (
            <TextArea
              key={line.sku}
              label={`${kind === 'Imei' ? 'IMEI' : 'Series'} de ${line.name}`}
              value={serialText[line.sku] ?? ''}
              onChange={(value) => {
                setSerialText((current) => ({ ...current, [line.sku]: value }));
                if (receive.error) receive.reset();
              }}
              rows={Math.min(6, Math.max(2, pending))}
              spellCheck={false}
              autoCapitalize="characters"
              hint={`${formatNumber(count)} de ${formatNumber(pending)} ${serialWord(kind, pending)}: una por renglón (escanee o pegue).`}
              error={touched ? (serialProblems.get(line.sku) ?? undefined) : undefined}
              required
            />
          );
        })}
      </Form>
    );

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!receive.sending}
      size="lg"
      title={order ? `Recibir ${order.row.number}` : 'Recibir mercadería'}
      description={order ? `${order.row.supplier} · ${formatMoney(order.row.total)}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={receive.sending}>
            Cancelar
          </Button>
          {lines.length > 0 && techReady && (
            <Button type="submit" form={formId} leftIcon={<PackageCheck />} loading={receive.sending}>
              Recibir mercadería
            </Button>
          )}
        </>
      }
    >
      {body}
    </Dialog>
  );
}
