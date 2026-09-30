// Módulo «Garantías» · «Reemplazar con otra unidad» (`IssueWarrantyReplacementCommand`, permiso `service.rma.manage`):
// se elige (o escanea) una unidad disponible del MISMO producto; sale del stock de la sucursal del caso con el movimiento
// «REPOSICIÓN POR GARANTÍA» y su asiento (costo de garantías), queda vendida al cliente y el caso pasa a «reemplazado»
// (la defectuosa sigue en garantía para devolverla al proveedor). Regla T-05. Es irreversible: se confirma.
// Las unidades disponibles salen de `GetAvailableSerialsQuery` (stock de la sucursal ACTIVA); si no hay, se escribe la serie.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza vacío.

import { useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, ComboBox, ConfirmDialog, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { DEFAULT_REPLACEMENT_RESOLUTION, TEXT_MAX, movedText, replacementOptions, type AvailableSerialRecord, type ClaimRecord } from './claims';
import { ServerError } from './ServerError';

export interface ReplaceClaimDialogProps {
  /** El caso (null = cerrado). */
  target: ClaimRecord | null;
  onClose: () => void;
  /** Después de entregar el reemplazo (la pantalla recarga la lista y el detalle). */
  onReplaced: (row: ClaimRecord) => void;
}

export function ReplaceClaimDialog({ target, onClose, onReplaced }: ReplaceClaimDialogProps) {
  const notify = useNotify();
  const [shown] = useState(target);
  const [unit, setUnit] = useState<ComboOption<AvailableSerialRecord> | null>(null);
  const [typed, setTyped] = useState('');
  const [resolution, setResolution] = useState(DEFAULT_REPLACEMENT_RESOLUTION);
  const [touched, setTouched] = useState(false);
  const replace = useRpcCommand('IssueWarrantyReplacementCommand', { notifyError: false });

  const claim = target ?? shown;
  const open = target !== null;
  const available = useRpcQuery('GetAvailableSerialsQuery', { sku: claim?.sku ?? '', warehouseCode: null }, { enabled: open && Boolean(claim) });

  if (!claim) return null;
  const options = replacementOptions(available.data ?? [], claim.serial);
  // Sin unidades en la lista (o si no se pudo leer), se escribe o escanea la serie.
  const pickFromList = options.length > 0;
  const serial = (pickFromList ? (unit?.value ?? '') : typed).trim();
  const serialError = touched && !serial ? (pickFromList ? 'Elija la unidad de reemplazo.' : 'Escriba o escanee la serie de la unidad de reemplazo.') : undefined;
  const resolutionError =
    touched && !resolution.trim() ? 'Escriba la resolución.' : resolution.trim().length > TEXT_MAX ? `La resolución admite hasta ${TEXT_MAX} caracteres.` : undefined;

  const reset = () => {
    if (replace.error) replace.reset();
  };

  const confirm = async () => {
    setTouched(true);
    if (!serial || !resolution.trim() || resolution.trim().length > TEXT_MAX) return false;
    const outcome = await replace.run({ number: claim.number, replacementSerial: serial, resolution: resolution.trim() });
    if (outcome.ok) {
      notify.success(movedText(outcome.result), `${claim.customer} recibió la unidad ${outcome.result.replacementSerial ?? serial}.`);
      onReplaced(outcome.result);
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      title={`¿Entregar una unidad de reemplazo para el caso ${claim.number}?`}
      message={`${claim.customer} recibe otra unidad de ${claim.product}: sale del stock de la sucursal del caso (reposición por garantía, con su asiento al costo de garantías) y el caso pasa a «reemplazado». No se puede deshacer.`}
      confirmLabel="Entregar reemplazo"
      onConfirm={confirm}
      error={replace.errorText && <ServerError text={replace.errorText} details={replace.error?.errors} />}
    >
      {available.loading ? (
        <p className="text-sm text-text-muted" role="status">
          Buscando unidades disponibles…
        </p>
      ) : pickFromList ? (
        <ComboBox
          label="Unidad de reemplazo"
          placeholder="Serie o IMEI"
          value={unit}
          onChange={(option) => {
            setUnit(option);
            reset();
          }}
          options={options}
          emptyText="Ninguna unidad disponible con esa serie"
          hint={`${options.length === 1 ? '1 unidad disponible' : `${options.length} unidades disponibles`} de ${claim.sku} en la sucursal activa.`}
          error={serialError}
          required
        />
      ) : (
        <>
          <Alert tone="info">
            {available.error
              ? 'No se pudo leer las unidades disponibles: escriba o escanee la serie.'
              : `No hay otras unidades de ${claim.sku} en el stock de la sucursal activa: escriba o escanee la serie de la unidad de reemplazo.`}
          </Alert>
          <TextField
            label="Serie de la unidad de reemplazo"
            value={typed}
            onChange={(value) => {
              setTyped(value);
              reset();
            }}
            error={serialError}
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={80}
            required
          />
        </>
      )}
      <TextField
        label="Resolución"
        value={resolution}
        onChange={(value) => {
          setResolution(value);
          reset();
        }}
        maxLength={TEXT_MAX}
        error={resolutionError}
        required
      />
    </ConfirmDialog>
  );
}
