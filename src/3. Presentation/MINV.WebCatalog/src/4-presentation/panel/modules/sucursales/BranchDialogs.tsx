// Módulo «Sucursales» · diálogos de alta y edición, como el escritorio:
//   · Nueva sucursal (`CreateBranchCommand`): código y nombre, su almacén (se proponen «ALM» + código y «Almacén » +
//     nombre mientras no se escriban a mano) y, si se pide, su caja. Se crea con la posición GENERAL y quien la crea queda
//     asignado a ella.
//   · Editar (`UpdateBranchCommand`): nombre y si está activa (una inactiva no recibe transferencias; no se puede
//     desactivar la única activa).
// Validan en la página (comodidad) y muestran el error del servidor DENTRO del diálogo. Se montan de nuevo en cada
// apertura (la pantalla les cambia la `key`).

import { Building2, Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, Form, FormGrid, Switch, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  LIMITS,
  branchProblems,
  createBranchPayload,
  editProblem,
  emptyBranchDraft,
  plainMessage,
  proposedWarehouseCode,
  proposedWarehouseName,
  updateBranchPayload,
  type BranchDraft,
  type BranchRecord,
} from './branches';

export interface NewBranchDialogProps {
  open: boolean;
  existing: readonly BranchRecord[];
  onClose: () => void;
  onCreated: () => void;
}

export function NewBranchDialog({ open, existing, onClose, onCreated }: NewBranchDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [draft, setDraft] = useState<BranchDraft>(emptyBranchDraft);
  // Mientras no se escriban a mano, el código y el nombre del almacén siguen a los de la sucursal.
  const [autoCode, setAutoCode] = useState(true);
  const [autoName, setAutoName] = useState(true);
  const [touched, setTouched] = useState(false);
  const create = useRpcCommand('CreateBranchCommand', { notifyError: false });
  const problems = branchProblems(draft, existing);
  const shown = (field: keyof BranchDraft) => (touched ? problems[field] : undefined);

  const change = (update: Partial<BranchDraft>) => {
    setDraft((current) => {
      const next = { ...current, ...update };
      if (update.code !== undefined && autoCode) next.warehouseCode = update.code ? proposedWarehouseCode(update.code) : '';
      if (update.name !== undefined && autoName) next.warehouseName = proposedWarehouseName(update.name);
      return next;
    });
    if (create.error) create.reset();
  };

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await create.run(createBranchPayload(draft));
    if (outcome.ok) {
      notify.success('Sucursal creada', `${plainMessage(outcome.result)} Vuelva a ingresar para verla en el selector de sucursales.`);
      onCreated();
      close();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!create.sending}
      size="lg"
      title="Nueva sucursal"
      description="Se crea con su almacén y la posición GENERAL. Después asigne los usuarios que trabajan en ella y abastézcala con una transferencia."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Building2 />} loading={create.sending}>
            Crear sucursal
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        <FormGrid>
          <TextField
            label="Código de la sucursal"
            value={draft.code}
            onChange={(value) => change({ code: value.toUpperCase() })}
            maxLength={LIMITS.code}
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            hint="Letras y números, por ejemplo CB. Va en los números de los documentos."
            error={shown('code')}
            required
            data-autofocus
          />
          <TextField label="Nombre" value={draft.name} onChange={(value) => change({ name: value })} maxLength={LIMITS.name} autoComplete="off" error={shown('name')} required />
          <TextField
            label="Código del almacén"
            value={draft.warehouseCode}
            onChange={(value) => {
              setAutoCode(false);
              change({ warehouseCode: value.toUpperCase() });
            }}
            maxLength={LIMITS.code}
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            error={shown('warehouseCode')}
            required
          />
          <TextField
            label="Nombre del almacén"
            value={draft.warehouseName}
            onChange={(value) => {
              setAutoName(false);
              change({ warehouseName: value });
            }}
            maxLength={LIMITS.name}
            autoComplete="off"
            error={shown('warehouseName')}
            required
          />
          <Checkbox
            label="Crear también su caja (punto de venta)"
            description="Queda lista para abrir turnos en la caja de la nueva sucursal."
            checked={draft.createPosRegister}
            onChange={(value) => change({ createPosRegister: value })}
            className="sm:col-span-2"
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}

export interface EditBranchDialogProps {
  /** La sucursal (null = cerrado). */
  target: BranchRecord | null;
  all: readonly BranchRecord[];
  onClose: () => void;
  onSaved: () => void;
}

export function EditBranchDialog({ target, all, onClose, onSaved }: EditBranchDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const branch = target ?? shown;
  const [name, setName] = useState(branch?.name ?? '');
  const [isActive, setIsActive] = useState(branch?.isActive ?? true);
  const [touched, setTouched] = useState(false);
  const update = useRpcCommand('UpdateBranchCommand', { notifyError: false });
  if (!branch) return null;
  const problems = editProblem(name, isActive, branch, all);

  const close = () => {
    update.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (problems.name || problems.isActive) return;
    const outcome = await update.run(updateBranchPayload(branch, name, isActive));
    if (outcome.ok) {
      notify.success('Sucursal actualizada', plainMessage(outcome.result));
      onSaved();
      close();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!update.sending}
      title={`Editar la sucursal ${branch.code}`}
      description="El código no cambia: va en los números de sus documentos."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={update.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={update.sending}>
            Guardar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={update.errorText} busy={update.sending}>
        <TextField
          label="Nombre"
          value={name}
          onChange={(value) => {
            setName(value);
            update.reset();
          }}
          maxLength={LIMITS.name}
          autoComplete="off"
          error={touched ? problems.name : undefined}
          required
          data-autofocus
        />
        <Switch
          label="Sucursal activa"
          description="Una sucursal inactiva no recibe transferencias ni se elige para operar."
          checked={isActive}
          onChange={(value) => {
            setIsActive(value);
            update.reset();
          }}
        />
        {problems.isActive && <Alert tone="warning">{problems.isActive}</Alert>}
      </Form>
    </Dialog>
  );
}
