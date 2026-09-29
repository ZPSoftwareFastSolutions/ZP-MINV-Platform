// Módulo «Pedido sugerido» · crear la orden de compra de UN proveedor con las cantidades revisadas en la pantalla
// (`CreatePurchaseOrderCommand`, permiso `purchasing.manage`). La orden queda en borrador; el servidor valida el
// proveedor, las cantidades (decimales según la unidad) y calcula los importes. Se piden la entrega esperada (opcional:
// sin fecha, el servidor usa el plazo del proveedor) y una observación. El error del servidor se muestra DENTRO del
// diálogo, sin cerrarlo.

import { FilePlus2, TriangleAlert } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, TextArea, TextField } from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber, formatQuantity, isIsoDate } from '@/4-presentation/panel/lib';
import { REVIEWED_ORDER_NOTE, cannotOrderReason, purchaseOrderPayload, supplierName, type SupplierGroup } from './order';

export interface SupplierOrderDialogProps {
  /** Proveedor cuya orden se crea (null = diálogo cerrado). */
  group: SupplierGroup | null;
  onClose: () => void;
  /** La orden quedó creada: su número y el código del proveedor. */
  onCreated: (number: string, supplierCode: string) => void;
}

export function SupplierOrderDialog({ group, onClose, onCreated }: SupplierOrderDialogProps) {
  const formId = useId();
  const [expected, setExpected] = useState('');
  const [notes, setNotes] = useState(REVIEWED_ORDER_NOTE);
  const [touched, setTouched] = useState(false);
  const create = useRpcCommand('CreatePurchaseOrderCommand', {
    success: (row) => `Orden ${row.number} creada en borrador`,
    notifyError: false,
  });
  // El último proveedor sigue a la vista mientras el diálogo se cierra; cada vez que se abre, el formulario vuelve a
  // empezar con la entrega estimada del proveedor.
  const [shown, setShown] = useState<SupplierGroup | null>(null);
  const [openedKey, setOpenedKey] = useState<string | null>(null);
  if (group && group.key !== openedKey) {
    setOpenedKey(group.key);
    setShown(group);
    setExpected(group.estimatedDelivery ?? '');
    setNotes(REVIEWED_ORDER_NOTE);
    setTouched(false);
  } else if (!group && openedKey !== null) {
    setOpenedKey(null);
  }

  const current = group ?? shown;
  const reason = current ? cannotOrderReason(current) : null;
  const dateProblem = expected.trim() && !isIsoDate(expected.trim()) ? 'Escriba una fecha válida.' : null;

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!group || reason || dateProblem) return;
    const outcome = await create.run(purchaseOrderPayload(group, expected.trim() || null, notes));
    if (outcome.ok) {
      onCreated(outcome.result.number, group.code ?? '');
      close();
    }
  };

  return (
    <Dialog
      open={group !== null}
      onClose={close}
      dismissible={!create.sending}
      size="lg"
      title={current ? `Orden de compra para ${supplierName(current.name)}` : 'Orden de compra'}
      description="Se crea en borrador con las cantidades de esta pantalla. Después se aprueba y se recibe en «Órdenes de compra»."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FilePlus2 />} loading={create.sending} disabled={reason !== null}>
            Crear orden en borrador
          </Button>
        </>
      }
    >
      {current && (
        <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
          {reason && <Alert tone="warning">{reason}</Alert>}
          {current.openOrders > 0 && (
            <Alert tone="warning" title="Ya tiene órdenes abiertas">
              {supplierName(current.name)} tiene {formatNumber(current.openOrders)} {current.openOrders === 1 ? 'orden abierta' : 'órdenes abiertas'}. Revise que no se
              duplique el pedido.
            </Alert>
          )}
          <ul className="divide-y divide-border rounded-xl border border-border text-sm" data-testid="lineas-de-la-orden">
            {current.toOrder.map((entry) => (
              <li key={entry.key} className="flex flex-wrap justify-between gap-x-4 gap-y-1 px-3 py-2">
                <span className="min-w-0">
                  <span className="block text-text">{entry.line.name}</span>
                  <span className="block text-xs text-text-muted">
                    {entry.line.sku} · {formatQuantity(entry.quantity, { unit: entry.line.unit })} × {formatMoney(entry.line.unitCost)}
                  </span>
                </span>
                <span className="shrink-0 font-semibold tabular-nums">{formatMoney(entry.subtotal)}</span>
              </li>
            ))}
            <li className="flex justify-between gap-4 px-3 py-2 font-semibold">
              <span>Total estimado</span>
              <span className="tabular-nums">{formatMoney(current.total)}</span>
            </li>
          </ul>
          {current.entries.length > current.toOrder.length && (
            <p className="flex items-start gap-2 text-sm text-text-muted">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
              {formatNumber(current.entries.length - current.toOrder.length)} producto(s) con cantidad 0 no van en la orden.
            </p>
          )}
          <FormGrid>
            <TextField
              label="Entrega esperada"
              type="date"
              value={expected}
              onChange={setExpected}
              optional
              error={touched && dateProblem ? dateProblem : undefined}
              hint="Sin fecha, se calcula con el plazo de entrega del proveedor."
            />
            <TextArea label="Observaciones" value={notes} onChange={setNotes} maxLength={250} rows={3} optional className="sm:col-span-2" />
          </FormGrid>
        </Form>
      )}
    </Dialog>
  );
}
