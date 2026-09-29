// Módulo «Armador de PC» · los diálogos de los comandos sobre un armado guardado (los usan la lista y el armador):
//   · Reservar stock  → `ReservePcBuildCommand` (horas: 24, 48, 72, 168 u otra cantidad de 1 a 720). Todo o nada: si falta
//                       stock de una pieza, el servidor no reserva nada y dice qué falta (el mensaje queda en el diálogo).
//   · Liberar reserva → `ReleasePcBuildReservationCommand` con motivo; el stock vuelve y el armado queda anulado. Confirma.
//   · Anular          → `CancelPcBuildCommand` (motivo opcional); no se puede cobrar después. Confirma.
// El error del servidor se muestra DENTRO del diálogo para corregir y reintentar.

import { Clock } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, ConfirmDialog, Dialog, Form, NumberField, SelectField, TextField } from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney } from '@/4-presentation/panel/lib';
import { ErrorDetails } from './ErrorDetails';
import {
  DEFAULT_HOURS,
  HOURS_OPTIONS,
  MAX_HOURS,
  OTHER_REASON,
  REASON_MAX_LENGTH,
  RELEASE_REASONS,
  releaseReason,
  reserveHours,
  type BuildRecord,
} from './builder';

/** El armado sobre el que se actúa (lo necesario para los textos y el pedido). */
export type BuildTarget = Pick<BuildRecord, 'number' | 'name' | 'total'>;

/** Mantiene a la vista el último armado mientras el diálogo se cierra (animación de salida). */
function useShown(target: BuildTarget | null): BuildTarget | null {
  const [shown, setShown] = useState<BuildTarget | null>(target);
  if (target && target !== shown) setShown(target);
  return shown;
}

// ---------------------------------------------------------------------------------------------------- reservar

export interface ReserveDialogProps {
  target: BuildTarget | null;
  onClose: () => void;
  onReserved: (row: BuildRecord) => void;
}

export function ReserveDialog({ target, onClose, onReserved }: ReserveDialogProps) {
  const formId = useId();
  const reserve = useRpcCommand('ReservePcBuildCommand', {
    success: (row) => `Stock reservado para ${row.number}${row.reservedUntil ? ` hasta el ${formatDateTime(row.reservedUntil)}` : ''}`,
    notifyError: false,
  });
  const [choice, setChoice] = useState(DEFAULT_HOURS);
  const [other, setOther] = useState<number | null>(null);
  const [touched, setTouched] = useState(false);
  const shown = useShown(target);
  const hours = reserveHours(choice, other);

  const close = () => {
    setChoice(DEFAULT_HOURS);
    setOther(null);
    setTouched(false);
    reserve.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!target || hours === null) return;
    const outcome = await reserve.run({ number: target.number, hours });
    if (outcome.ok) {
      onReserved(outcome.result);
      close();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!reserve.sending}
      title={`Reservar el stock de ${shown?.number ?? ''}`}
      description={shown ? `${shown.name} · ${formatMoney(shown.total)}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={reserve.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Clock />} loading={reserve.sending}>
            Reservar stock
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={reserve.errorText && <ErrorDetails text={reserve.errorText} details={reserve.error?.errors} />} busy={reserve.sending}>
        <p className="text-sm text-text-muted">
          Cada pieza queda reservada en la sucursal del armado: mientras dure la reserva no se vende a otro cliente ni en la web. Si falta stock de alguna
          pieza no se reserva nada. El cliente la cobra en la caja (la venta consume la reserva).
        </p>
        <SelectField
          label="Horas de reserva"
          allLabel={false}
          value={choice}
          onChange={(value) => {
            setChoice(value || DEFAULT_HOURS);
            if (reserve.error) reserve.reset();
          }}
          options={HOURS_OPTIONS}
          data-autofocus
        />
        {choice === 'otra' && (
          <NumberField
            label="Cantidad de horas"
            value={other}
            onChange={setOther}
            unit="h"
            hint={`De 1 a ${MAX_HOURS} horas (30 días).`}
            error={touched && hours === null ? `Escriba un número entero de horas entre 1 y ${MAX_HOURS}.` : undefined}
            required
          />
        )}
      </Form>
    </Dialog>
  );
}

// ---------------------------------------------------------------------------------------------------- liberar

export interface ReleaseDialogProps {
  target: BuildTarget | null;
  onClose: () => void;
  onReleased: (row: BuildRecord) => void;
}

const REASON_OPTIONS = [...RELEASE_REASONS.map((reason) => ({ value: reason, label: reason })), { value: OTHER_REASON, label: 'Otro motivo (escribirlo)' }];

export function ReleaseDialog({ target, onClose, onReleased }: ReleaseDialogProps) {
  const release = useRpcCommand('ReleasePcBuildReservationCommand', {
    success: (row) => `Reserva de ${row.number} liberada: el stock volvió a estar disponible`,
    notifyError: false,
  });
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [touched, setTouched] = useState(false);
  const shown = useShown(target);
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
    const outcome = await release.run({ number: target.number, reason: releaseReason(choice, other) });
    if (outcome.ok) onReleased(outcome.result);
    return outcome;
  };

  return (
    <ConfirmDialog
      open={target !== null}
      onClose={close}
      tone="danger"
      title={`¿Liberar la reserva de ${shown?.number ?? ''}?`}
      message={shown ? `El stock reservado de «${shown.name}» (${formatMoney(shown.total)}) vuelve a estar disponible y el armado queda anulado con el motivo. No se puede deshacer.` : ''}
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
        hint="Queda en la bitácora del armado."
        required
      />
      {choice === OTHER_REASON && (
        <TextField
          label="Escriba el motivo"
          value={other}
          onChange={setOther}
          maxLength={REASON_MAX_LENGTH}
          error={touched ? otherProblem : undefined}
          required
          data-autofocus
        />
      )}
    </ConfirmDialog>
  );
}

// ---------------------------------------------------------------------------------------------------- anular

export interface CancelDialogProps {
  target: BuildTarget | null;
  onClose: () => void;
  onCancelled: (number: string) => void;
}

export function CancelDialog({ target, onClose, onCancelled }: CancelDialogProps) {
  const cancel = useRpcCommand('CancelPcBuildCommand', { success: (_result, payload) => `Armado ${payload.number} anulado`, notifyError: false });
  const [reason, setReason] = useState('');
  const shown = useShown(target);

  const close = () => {
    setReason('');
    cancel.reset();
    onClose();
  };

  const confirm = async () => {
    if (!target) return false;
    const outcome = await cancel.run({ number: target.number, reason: reason.trim() || null });
    if (outcome.ok) onCancelled(target.number);
    return outcome;
  };

  return (
    <ConfirmDialog
      open={target !== null}
      onClose={close}
      tone="danger"
      title={`¿Anular el armado ${shown?.number ?? ''}?`}
      message={shown ? `«${shown.name}» por ${formatMoney(shown.total)} queda anulado (no se borra) y ya no se puede cobrar en la caja.` : ''}
      confirmLabel="Anular armado"
      onConfirm={confirm}
      error={cancel.errorText && <ErrorDetails text={cancel.errorText} details={cancel.error?.errors} />}
    >
      <TextField label="Motivo" value={reason} onChange={setReason} maxLength={REASON_MAX_LENGTH} hint="Queda en la bitácora del armado." optional />
    </ConfirmDialog>
  );
}
