// Módulo «Transferencias» · recibir (`ReceiveTransferCommand`, lo hace el DESTINO; regla B-03), como el escritorio:
// por cada producto se cuenta lo que llegó (por defecto, todo lo despachado; nunca más). Si llegó menos, el faltante
// exige motivo y queda registrado como merma en tránsito (no se borra nada, regla B-04); en un producto serializado se
// marcan las series que NO llegaron (las demás entran al stock del destino). El error del servidor se muestra DENTRO del
// diálogo. Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { PackageCheck } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, ErrorState, FieldGroup, Form, LoadingState, NumberField, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { formatQuantity } from '@/4-presentation/panel/lib';
import {
  LIMITS,
  OTHER_REASON,
  SHORTAGE_REASONS,
  plainMessage,
  reasonOptions,
  receiptDraftOf,
  receiptProblems,
  receiveTransferPayload,
  shortageOf,
  type ReceiptDraftLine,
  type TransferItem,
  type TransferOutcome,
} from './transfers';

export interface ReceiveTransferDialogProps {
  target: TransferItem | null;
  onClose: () => void;
  onDone: (outcome: TransferOutcome) => void;
}

export function ReceiveTransferDialog({ target, onClose, onDone }: ReceiveTransferDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const item = target ?? shown;
  const open = target !== null;
  const detail = useRpcQuery('GetTransferQuery', { id: item?.key ?? '' }, { enabled: open && item !== null });
  const receive = useRpcCommand('ReceiveTransferCommand', { notifyError: false });
  const [draft, setDraft] = useState<Record<string, ReceiptDraftLine> | null>(null);
  const [touched, setTouched] = useState(false);

  // Cuando llega el detalle, cada línea empieza con todo lo despachado.
  const lines = detail.data?.lines ?? [];
  if (detail.data && draft === null) setDraft(receiptDraftOf(detail.data.lines));
  const entries = draft ?? {};
  const problems = receiptProblems(lines, entries);

  const change = (sku: string, update: Partial<ReceiptDraftLine>) => {
    setDraft((current) => (current ? { ...current, [sku]: { ...current[sku], ...update } } : current));
    if (receive.error) receive.reset();
  };

  const close = () => {
    receive.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!item || !draft || Object.keys(problems).length > 0) return;
    const outcome = await receive.run(receiveTransferPayload(item.key, lines, draft));
    if (outcome.ok) {
      notify.success('Transferencia recibida', plainMessage(outcome.result.message));
      onDone(outcome.result);
      close();
    }
  };

  let body;
  if (detail.error) body = <ErrorState error={detail.error} operation="GetTransferQuery" onRetry={detail.reload} retrying={detail.fetching} />;
  else if (!detail.data || !draft) body = <LoadingState label="Cargando lo despachado…" rows={2} />;
  else
    body = (
      <Form id={formId} onSubmit={submit} error={receive.errorText} busy={receive.sending}>
        <Alert tone="info">Cuente lo que llegó. Si falta algo, indique el motivo: el faltante queda registrado como merma en tránsito (no se borra nada).</Alert>
        <ul className="space-y-3" aria-label="Lo que llegó">
          {lines.map((line) => {
            const entry = entries[line.sku];
            const errors = touched ? problems[line.sku] : undefined;
            const shortage = shortageOf(line, entry);
            const serialized = (line.serials?.length ?? 0) > 0;
            return (
              <li key={line.lineId} className="space-y-3 rounded-xl border border-border bg-surface-2/40 p-3" data-testid={`recibir-${line.sku}`}>
                <div className="grid grid-cols-1 items-start gap-3 sm:grid-cols-[minmax(0,1fr)_11rem]">
                  <div className="min-w-0 pt-1">
                    <p className="font-medium break-words text-text">{line.name}</p>
                    <p className="text-xs text-text-muted">
                      {line.sku} · despachado {formatQuantity(line.quantity, { unit: line.unit })}
                      {shortage > 0 && entry?.received !== null ? ` · faltante ${formatQuantity(shortage)}` : ''}
                    </p>
                  </div>
                  <NumberField
                    label="Llegó"
                    value={entry?.received ?? null}
                    onChange={(value) => change(line.sku, { received: value, missing: [] })}
                    decimals={serialized ? 0 : 3}
                    unit={line.unit}
                    error={errors?.received}
                    required
                  />
                </div>
                {shortage > 0 && entry && entry.received !== null && entry.received <= line.quantity && (
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <SelectField
                      label="Motivo del faltante"
                      allLabel={false}
                      placeholder="Elija el motivo"
                      value={entry.reasonChoice}
                      onChange={(value) => change(line.sku, { reasonChoice: value })}
                      options={reasonOptions(SHORTAGE_REASONS)}
                      error={errors?.reason && !entry.reasonChoice ? errors.reason : undefined}
                      required
                    />
                    {entry.reasonChoice === OTHER_REASON && (
                      <TextField
                        label="Escriba el motivo"
                        value={entry.reasonOther}
                        onChange={(value) => change(line.sku, { reasonOther: value })}
                        maxLength={LIMITS.reason}
                        error={errors?.reason && entry.reasonChoice ? errors.reason : undefined}
                        required
                      />
                    )}
                    {serialized && (
                      <FieldGroup
                        label="Unidades que NO llegaron"
                        hint={`Marque ${shortage} (las demás entran al stock del destino).`}
                        error={errors?.missing}
                        className="sm:col-span-2"
                        bodyClassName="grid grid-cols-1 gap-x-4 sm:grid-cols-2"
                      >
                        {(line.serials ?? []).map((serial) => (
                          <Checkbox
                            key={serial}
                            label={<span className="font-mono">{serial}</span>}
                            checked={entry.missing.includes(serial)}
                            disabled={!entry.missing.includes(serial) && entry.missing.length >= shortage}
                            onChange={(checked) =>
                              change(line.sku, { missing: checked ? [...entry.missing.filter((value) => value !== serial), serial] : entry.missing.filter((value) => value !== serial) })
                            }
                          />
                        ))}
                      </FieldGroup>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </Form>
    );

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!receive.sending}
      size="lg"
      title={item ? `Recibir ${item.row.number}` : 'Recibir transferencia'}
      description={item ? `${item.row.fromBranch} → ${item.row.toBranch} · almacén ${item.row.toWarehouse}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={receive.sending}>
            Cancelar
          </Button>
          {draft && (
            <Button type="submit" form={formId} leftIcon={<PackageCheck />} loading={receive.sending}>
              Confirmar recepción
            </Button>
          )}
        </>
      }
    >
      {body}
    </Dialog>
  );
}
