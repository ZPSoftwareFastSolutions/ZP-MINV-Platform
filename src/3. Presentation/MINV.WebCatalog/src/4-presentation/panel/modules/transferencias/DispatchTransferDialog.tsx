// Módulo «Transferencias» · despachar (`DispatchTransferCommand`, lo hace el ORIGEN; regla B-03), como el escritorio:
// confirma lo que sale (productos, cantidades y las series que viajan, del detalle `GetTransferQuery`). La mercadería
// sale del almacén de origen (lotes que vencen primero), queda EN TRÁNSITO y se registra el asiento de envío. El error
// del servidor (por ejemplo, stock insuficiente) se muestra dentro de la confirmación.

import { useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { ConfirmDialog, ErrorState, LoadingState, useNotify } from '@/4-presentation/panel/kit';
import { formatQuantity } from '@/4-presentation/panel/lib';
import { plainMessage, type TransferItem, type TransferOutcome } from './transfers';

export interface DispatchTransferDialogProps {
  target: TransferItem | null;
  onClose: () => void;
  onDone: (outcome: TransferOutcome) => void;
}

export function DispatchTransferDialog({ target, onClose, onDone }: DispatchTransferDialogProps) {
  const notify = useNotify();
  const [shown] = useState(target);
  const item = target ?? shown;
  const open = target !== null;
  const detail = useRpcQuery('GetTransferQuery', { id: item?.key ?? '' }, { enabled: open && item !== null });
  const dispatch = useRpcCommand('DispatchTransferCommand', { notifyError: false });
  if (!item) return null;

  const confirm = async () => {
    const outcome = await dispatch.run({ id: item.key });
    if (outcome.ok) {
      notify.success('Transferencia despachada', plainMessage(outcome.result.message));
      onDone(outcome.result);
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={open}
      onClose={() => {
        dispatch.reset();
        onClose();
      }}
      title={`¿Despachar la transferencia ${item.row.number}?`}
      message={`${item.route} · ${item.row.toBranch}. La mercadería sale ahora del almacén ${item.row.fromWarehouse} (lotes que vencen primero) y queda EN TRÁNSITO hasta que el destino la reciba. Se registra el asiento de envío.`}
      confirmLabel="Despachar"
      onConfirm={confirm}
      confirmDisabled={!detail.data}
      error={dispatch.errorText ? plainMessage(dispatch.errorText) : undefined}
    >
      {detail.error ? (
        <ErrorState error={detail.error} operation="GetTransferQuery" onRetry={detail.reload} retrying={detail.fetching} />
      ) : !detail.data ? (
        <LoadingState label="Cargando lo que sale…" rows={1} />
      ) : (
        <ul className="divide-y divide-border rounded-xl border border-border text-sm" aria-label="Lo que sale" data-testid="despacho-lineas">
          {detail.data.lines.map((line) => (
            <li key={line.lineId} className="px-3 py-2">
              <span className="flex flex-wrap justify-between gap-2">
                <span className="font-medium text-text">{line.name}</span>
                <span className="tabular-nums">{formatQuantity(line.quantity, { unit: line.unit })}</span>
              </span>
              {line.serials && line.serials.length > 0 && <span className="block font-mono text-xs text-text-muted">Series: {line.serials.join(', ')}</span>}
            </li>
          ))}
        </ul>
      )}
    </ConfirmDialog>
  );
}
