// Módulo «Series» · dar destino a una unidad devuelta por falla o que quedó en garantía (`DisposeSerialCommand`, permiso
// `inventory.serials.manage`): devolverla al proveedor o darla de baja, con el motivo. La unidad sale definitivamente del
// inventario y queda en su bitácora: por eso se confirma (no se puede deshacer). El servidor decide si se puede (por
// ejemplo, si todavía está en un caso de garantía abierto, lo dice).
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza siempre vacío.

import { useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { ConfirmDialog, RadioGroup, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  DISPOSALS,
  DISPOSE_REASONS,
  OTHER_REASON,
  REASON_MAX,
  plainMessage,
  reasonOptions,
  reasonProblem,
  reasonText,
  statusLabel,
  type DisposalCode,
} from './serials';
import { ServerError } from './ServerError';

/** La unidad a la que se da destino. */
export interface DisposeTarget {
  serial: string;
  sku: string;
  product: string;
  status: string;
}

export interface DisposeSerialDialogProps {
  /** null = cerrado. */
  target: DisposeTarget | null;
  onClose: () => void;
  /** Después de registrar el destino (la pantalla recarga la lista y el detalle). */
  onDisposed: () => void;
}

export function DisposeSerialDialog({ target, onClose, onDisposed }: DisposeSerialDialogProps) {
  const notify = useNotify();
  // La unidad sigue a la vista mientras el diálogo se cierra.
  const [shown] = useState(target);
  const [disposal, setDisposal] = useState<DisposalCode>('ReturnToSupplier');
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [touched, setTouched] = useState(false);
  const dispose = useRpcCommand('DisposeSerialCommand', { notifyError: false });

  const unit = target ?? shown;
  if (!unit) return null;

  const reason = reasonText(choice, other);
  const problem = reasonProblem(reason);
  const choiceError = touched && choice === '' ? 'Elija el motivo o escriba uno.' : undefined;
  const otherError = touched && choice === OTHER_REASON && problem ? problem : undefined;

  const confirm = async () => {
    setTouched(true);
    if (choice === '' || problem) return false;
    const outcome = await dispose.run({ serial: unit.serial, disposal, reason, sku: unit.sku });
    if (outcome.ok) {
      notify.success('Destino registrado', plainMessage(outcome.result));
      onDisposed();
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={target !== null}
      onClose={onClose}
      tone="danger"
      title={`¿Dar destino a la serie ${unit.serial}?`}
      message={`${unit.product} · ${statusLabel(unit.status)}. La unidad sale definitivamente del inventario y queda en su bitácora. No se puede deshacer.`}
      confirmLabel="Registrar destino"
      onConfirm={confirm}
      error={dispose.errorText && <ServerError text={dispose.errorText} details={dispose.error?.errors} />}
    >
      <RadioGroup
        label="Destino"
        value={disposal}
        onChange={(value) => {
          setDisposal(value);
          dispose.reset();
        }}
        options={DISPOSALS}
        required
      />
      <SelectField
        label="Motivo"
        allLabel={false}
        placeholder="Elija el motivo"
        value={choice}
        onChange={(value) => {
          setChoice(value);
          dispose.reset();
        }}
        options={reasonOptions(DISPOSE_REASONS)}
        error={choiceError}
        required
      />
      {choice === OTHER_REASON && (
        <TextField
          label="Escriba el motivo"
          value={other}
          onChange={(value) => {
            setOther(value);
            dispose.reset();
          }}
          maxLength={REASON_MAX}
          hint="Queda en la bitácora de la serie."
          error={otherError}
          required
        />
      )}
    </ConfirmDialog>
  );
}
