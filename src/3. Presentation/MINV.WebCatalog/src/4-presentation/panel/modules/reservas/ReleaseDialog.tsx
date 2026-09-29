// Módulo «Reservas» · LIBERAR una reserva (`ReleasePcBuildReservationCommand`): el stock reservado vuelve a estar
// disponible y la reserva queda anulada con su motivo, que queda en la bitácora. No se deshace: se confirma y el motivo es
// obligatorio (uno de los frecuentes o escrito a mano). El error del servidor se muestra DENTRO del diálogo.

import { useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { ConfirmDialog, SelectField, TextField } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { ErrorDetails } from './ErrorDetails';
import { OTHER_REASON, REASON_MAX_LENGTH, RELEASE_REASONS, releaseReason, type BuildRecord } from './reservations';

/** La reserva que se va a liberar. */
export interface ReleaseTarget {
  number: string;
  name: string;
  total: number;
}

export interface ReleaseDialogProps {
  /** null = diálogo cerrado. */
  target: ReleaseTarget | null;
  onClose: () => void;
  /** La reserva ya liberada (lo que devolvió el servidor). */
  onReleased: (row: BuildRecord) => void;
}

const REASON_OPTIONS = [...RELEASE_REASONS.map((reason) => ({ value: reason, label: reason })), { value: OTHER_REASON, label: 'Otro motivo (escribirlo)' }];

export function ReleaseDialog({ target, onClose, onReleased }: ReleaseDialogProps) {
  const release = useRpcCommand('ReleasePcBuildReservationCommand', {
    success: (row) => `Reserva ${row.number} liberada: el stock volvió a estar disponible`,
    notifyError: false,
  });
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [touched, setTouched] = useState(false);
  // La última reserva sigue a la vista mientras el diálogo se cierra (animación de salida).
  const [shown, setShown] = useState<ReleaseTarget | null>(target);
  if (target && target !== shown) setShown(target);

  const reason = releaseReason(choice, other);
  const choiceProblem = choice ? null : 'Elija el motivo.';
  const otherProblem = choice !== OTHER_REASON ? null : !other.trim() ? 'Escriba el motivo.' : other.trim().length > REASON_MAX_LENGTH ? `Use como máximo ${REASON_MAX_LENGTH} caracteres.` : null;

  const close = () => {
    setChoice('');
    setOther('');
    setTouched(false);
    release.reset();
    onClose();
  };

  const confirm = async () => {
    setTouched(true);
    if (!target || choiceProblem || otherProblem) return false;
    const outcome = await release.run({ number: target.number, reason });
    if (outcome.ok) onReleased(outcome.result);
    return outcome;
  };

  return (
    <ConfirmDialog
      open={target !== null}
      onClose={close}
      tone="danger"
      title={`¿Liberar la reserva ${shown?.number ?? ''}?`}
      message={
        shown
          ? `El stock reservado de «${shown.name}» (${formatMoney(shown.total)}) vuelve a estar disponible y la reserva queda anulada con el motivo. No se puede deshacer.`
          : ''
      }
      confirmLabel="Liberar reserva"
      onConfirm={confirm}
      error={release.errorText && <ErrorDetails text={release.errorText} details={release.error?.errors} />}
    >
      <SelectField
        label="Motivo"
        allLabel={false}
        placeholder="Elija el motivo"
        value={choice}
        onChange={(value) => {
          setChoice(value);
          if (release.error) release.reset();
        }}
        options={REASON_OPTIONS}
        error={touched ? choiceProblem : undefined}
        hint="Queda en la bitácora de la reserva."
        required
      />
      {choice === OTHER_REASON && (
        <TextField
          label="Escriba el motivo"
          value={other}
          onChange={(value) => {
            setOther(value);
            if (release.error) release.reset();
          }}
          maxLength={REASON_MAX_LENGTH}
          error={touched ? otherProblem : undefined}
          required
          data-autofocus
        />
      )}
    </ConfirmDialog>
  );
}
