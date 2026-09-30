// Módulo «Garantías» · avanzar un caso al estado siguiente (`MoveWarrantyClaimCommand`, permiso `service.rma.manage`):
// SOLO a uno de los que el servidor ofrece (`nextStatuses`, regla T-05). Cada paso se confirma (los casos solo avanzan: no
// se puede deshacer) y pide lo que exige el servidor: la resolución al reparar, reemplazar o rechazar; el proveedor al
// enviarlo (o el preferido del producto); y una nota opcional que queda en la bitácora. Reemplazar SIN una unidad de
// reemplazo registrada no pasa por aquí: se hace con «Entregar reemplazo» (ReplaceClaimDialog).
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza vacío.

import { useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { ConfirmDialog, SelectField, TextArea, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  OTHER_OPTION,
  RESOLUTIONS,
  STEP_HELP,
  STEP_LABELS,
  TEXT_MAX,
  chosenText,
  movedText,
  needsResolution,
  needsSupplier,
  stepQuestion,
  suggestionOptions,
  type ClaimRecord,
  type ClaimStatusCode,
} from './claims';
import { ServerError } from './ServerError';

export interface MoveTarget {
  claim: ClaimRecord;
  next: ClaimStatusCode;
}

export interface MoveClaimDialogProps {
  /** null = cerrado. */
  target: MoveTarget | null;
  onClose: () => void;
  /** Después de avanzar (la pantalla recarga la lista y el detalle). */
  onMoved: (row: ClaimRecord) => void;
}

export function MoveClaimDialog({ target, onClose, onMoved }: MoveClaimDialogProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [shown] = useState(target);
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [supplier, setSupplier] = useState('');
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);
  const move = useRpcCommand('MoveWarrantyClaimCommand', { notifyError: false });

  const current = target ?? shown;
  const open = target !== null;
  const next = current?.next ?? 'Diagnosing';
  const wantsSupplier = needsSupplier(next);
  // Proveedores para elegir (con el permiso del catálogo); sin él, el servidor usa el preferido del producto.
  const canListSuppliers = canRun('GetCatalogOptionsQuery');
  const options = useRpcQuery('GetCatalogOptionsQuery', {}, { enabled: open && wantsSupplier && canListSuppliers });

  if (!current) return null;
  const { claim } = current;
  const wantsResolution = needsResolution(next);
  const resolution = chosenText(choice, other);
  const resolutionError =
    touched && wantsResolution
      ? choice === ''
        ? 'Elija la resolución o escriba una.'
        : resolution === ''
          ? 'Escriba la resolución.'
          : resolution.length > TEXT_MAX
            ? `La resolución admite hasta ${TEXT_MAX} caracteres.`
            : undefined
      : undefined;
  const noteError = note.trim().length > TEXT_MAX ? `La nota admite hasta ${TEXT_MAX} caracteres.` : undefined;
  const suppliers = (options.data?.suppliers ?? []).map((item) => ({ value: item.code, label: item.name }));

  const reset = () => {
    if (move.error) move.reset();
  };

  const confirm = async () => {
    setTouched(true);
    if ((wantsResolution && (choice === '' || resolution === '' || resolution.length > TEXT_MAX)) || noteError) return false;
    const outcome = await move.run({
      number: claim.number,
      next,
      resolution: wantsResolution ? resolution : null,
      supplierCode: wantsSupplier && supplier ? supplier : null,
      note: note.trim() || null,
    });
    if (outcome.ok) {
      notify.success(movedText(outcome.result), `${claim.product} · ${claim.serial}`);
      onMoved(outcome.result);
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      tone={next === 'Rejected' ? 'danger' : 'primary'}
      title={stepQuestion(next, claim.number)}
      message={`${claim.product} · ${claim.serial} · ${claim.customer}. ${STEP_HELP[next]} El cambio queda en la bitácora del caso y no se puede deshacer.`}
      confirmLabel={STEP_LABELS[next]}
      onConfirm={confirm}
      error={move.errorText && <ServerError text={move.errorText} details={move.error?.errors} />}
    >
      {wantsSupplier && (
        <SelectField
          label="Proveedor"
          allLabel="El proveedor preferido del producto"
          value={supplier}
          onChange={(value) => {
            setSupplier(value);
            reset();
          }}
          options={suppliers}
          disabled={canListSuppliers && !options.data && !options.error}
          hint={options.error ? 'No se pudo leer la lista de proveedores: se usará el preferido del producto.' : 'A quién se envía el equipo (o al servicio técnico autorizado).'}
        />
      )}
      {wantsResolution && (
        <>
          <SelectField
            label="Resolución"
            allLabel={false}
            placeholder="Elija la resolución"
            value={choice}
            onChange={(value) => {
              setChoice(value);
              reset();
            }}
            options={suggestionOptions(RESOLUTIONS[next] ?? [], 'Otra resolución (escribirla)')}
            error={choice === OTHER_OPTION ? undefined : resolutionError}
            required
          />
          {choice === OTHER_OPTION && (
            <TextField
              label="Escriba la resolución"
              value={other}
              onChange={(value) => {
                setOther(value);
                reset();
              }}
              maxLength={TEXT_MAX}
              error={resolutionError}
              required
            />
          )}
        </>
      )}
      <TextArea
        label="Nota"
        value={note}
        onChange={(value) => {
          setNote(value);
          reset();
        }}
        maxLength={TEXT_MAX}
        rows={2}
        hint="Queda en la bitácora del caso."
        error={noteError}
        optional
      />
    </ConfirmDialog>
  );
}
