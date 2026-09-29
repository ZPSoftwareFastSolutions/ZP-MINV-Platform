// Módulo «Usuarios» · sucursales donde trabaja una persona del personal (`AssignUserBranchesCommand`, reemplaza las
// anteriores): una casilla por sucursal y al menos una marcada (la misma regla del servidor). Una sucursal inactiva no
// se puede asignar, pero si ya la tenía se puede quitar.

import { Building2 } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, FieldGroup, Form } from '@/4-presentation/panel/kit';
import { branchLabel, orderedCodes, plainMessage, type BranchChoice, type UserItem } from './users';

export interface AssignBranchesDialogProps {
  /** null = cerrado. */
  target: UserItem | null;
  branches: readonly BranchChoice[];
  onClose: () => void;
  onDone: () => void;
}

export function AssignBranchesDialog({ target, branches, onClose, onDone }: AssignBranchesDialogProps) {
  const formId = useId();
  const assign = useRpcCommand('AssignUserBranchesCommand', { notifyError: false, success: (result) => plainMessage(result) || 'Sucursales asignadas' });
  const [shown, setShown] = useState<UserItem | null>(target);
  const [selected, setSelected] = useState<string[]>([]);
  const [touched, setTouched] = useState(false);
  if (target && target !== shown) {
    setShown(target);
    setSelected([...target.record.branchCodes]);
    setTouched(false);
  }
  const problem = selected.length === 0 ? 'Elija al menos una sucursal.' : null;
  // También las asignadas que ya no están en el directorio (para poder quitarlas).
  const choices: BranchChoice[] = [...branches, ...(shown?.record.branchCodes ?? []).filter((code) => !branches.some((branch) => branch.code === code)).map((code) => ({ code, name: code, isActive: false }))];

  const close = () => {
    assign.reset();
    onClose();
  };

  const toggle = (code: string, checked: boolean) => {
    setSelected((current) => orderedCodes(checked ? [...current, code] : current.filter((item) => item !== code), choices));
    if (assign.error) assign.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (!shown || problem) return;
    const outcome = await assign.run({ email: shown.record.email, branchCodes: orderedCodes(selected, choices) });
    if (!outcome.ok) return;
    onDone();
    close();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!assign.sending}
      title="Asignar sucursales"
      description={shown ? `${shown.record.name} · ${shown.record.email}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={assign.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Building2 />} loading={assign.sending}>
            Guardar sucursales
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={assign.errorText} busy={assign.sending}>
        {shown?.allBranches && (
          <Alert tone="info">Con su rol ve y opera todas las sucursales igual (gerencia global): las marcadas son las suyas por defecto.</Alert>
        )}
        <FieldGroup label="Sucursales donde trabaja" required error={touched ? problem : undefined} hint="Reemplaza las que tenía." bodyClassName="grid gap-x-4 sm:grid-cols-2">
          {choices.map((branch) => {
            const checked = selected.includes(branch.code);
            return (
              <Checkbox
                key={branch.code}
                label={branchLabel(branch)}
                description={branch.isActive ? undefined : 'Sucursal inactiva'}
                checked={checked}
                disabled={!branch.isActive && !checked}
                onChange={(value) => toggle(branch.code, value)}
              />
            );
          })}
        </FieldGroup>
      </Form>
    </Dialog>
  );
}
