// Módulo «Transferencias» · anular una transferencia PENDIENTE (`CancelTransferCommand`, lo hace el ORIGEN; regla B-03),
// como el escritorio: motivo (uno sugerido o escrito, hasta 200 caracteres) y confirmación. Todavía no movió stock: se
// anula y queda en la bitácora. Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { ConfirmDialog, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { CANCEL_REASONS, LIMITS, OTHER_REASON, cancelTransferPayload, plainMessage, reasonOptions, reasonProblem, type TransferItem, type TransferOutcome } from './transfers';

export interface CancelTransferDialogProps {
  target: TransferItem | null;
  onClose: () => void;
  onDone: (outcome: TransferOutcome) => void;
}

export function CancelTransferDialog({ target, onClose, onDone }: CancelTransferDialogProps) {
  const notify = useNotify();
  const [shown] = useState(target);
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [touched, setTouched] = useState(false);
  const cancel = useRpcCommand('CancelTransferCommand', { notifyError: false });
  const item = target ?? shown;
  if (!item) return null;
  const problem = reasonProblem(choice, other);

  const confirm = async () => {
    setTouched(true);
    if (problem) return false;
    const outcome = await cancel.run(cancelTransferPayload(item.key, choice, other));
    if (outcome.ok) {
      notify.success('Transferencia anulada', plainMessage(outcome.result.message));
      onDone(outcome.result);
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={target !== null}
      onClose={() => {
        cancel.reset();
        onClose();
      }}
      tone="danger"
      title={`¿Anular la transferencia ${item.row.number}?`}
      message={`${item.route} · ${item.row.toBranch}. Todavía no movió stock: se anula y queda en la bitácora. No se puede deshacer.`}
      confirmLabel="Anular transferencia"
      onConfirm={confirm}
      error={cancel.errorText ? plainMessage(cancel.errorText) : undefined}
    >
      <SelectField
        label="Motivo de la anulación"
        allLabel={false}
        placeholder="Elija el motivo"
        value={choice}
        onChange={(value) => {
          setChoice(value);
          cancel.reset();
        }}
        options={reasonOptions(CANCEL_REASONS)}
        error={touched && !choice ? problem : undefined}
        required
      />
      {choice === OTHER_REASON && (
        <TextField
          label="Escriba el motivo"
          value={other}
          onChange={(value) => {
            setOther(value);
            cancel.reset();
          }}
          maxLength={LIMITS.reason}
          hint="Queda en la bitácora de la transferencia."
          error={touched && problem ? problem : undefined}
          required
        />
      )}
    </ConfirmDialog>
  );
}
