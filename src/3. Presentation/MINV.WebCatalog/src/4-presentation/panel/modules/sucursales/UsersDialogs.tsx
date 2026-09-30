// Módulo «Sucursales» · quién trabaja dónde (`AssignUserBranchesCommand`, que REEMPLAZA las sucursales de la persona;
// siempre al menos una). Dos formas, con casillas:
//   · «Usuarios de la sucursal»: una casilla por persona del personal activo; al guardar se envía, a cada persona que
//     cambió, la lista completa de sus sucursales (con o sin esta). Nadie puede quedar sin sucursales.
//   · «Asignar sucursales a un usuario» (el editor del escritorio): se elige la persona y se marcan sus sucursales.
// Las personas salen de `GetUsersQuery` (administrar usuarios). Rige desde su próximo ingreso. El error del servidor se
// muestra DENTRO del diálogo.

import { Building2, Users } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, ErrorState, FieldGroup, Form, LoadingState, SelectField, useNotify } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { membershipChanges, orderedCodes, orphaned, plainMessage, staffUsers, type BranchRecord, type UserRecord } from './branches';

export interface BranchUsersDialogProps {
  /** La sucursal (null = cerrado). */
  target: BranchRecord | null;
  branches: readonly BranchRecord[];
  onClose: () => void;
  onSaved: () => void;
}

export function BranchUsersDialog({ target, branches, onClose, onSaved }: BranchUsersDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const branch = target ?? shown;
  const users = useRpcQuery('GetUsersQuery', {}, { enabled: target !== null });
  const assign = useRpcCommand('AssignUserBranchesCommand', { notifyError: false });
  const [checked, setChecked] = useState<Set<string> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const staff = staffUsers(users.data ?? []);
  if (users.data && checked === null && branch) setChecked(new Set(staff.filter((user) => user.branchCodes.includes(branch.code)).map((user) => user.email)));
  if (!branch) return null;

  const changes = checked ? membershipChanges(staff, branch.code, checked, branches) : [];
  const alone = orphaned(changes);

  const toggle = (email: string, value: boolean) => {
    setChecked((current) => {
      const next = new Set(current ?? []);
      if (value) next.add(email);
      else next.delete(email);
      return next;
    });
    setError(null);
  };

  const close = () => {
    assign.reset();
    onClose();
  };

  const submit = async () => {
    if (alone.length > 0 || changes.length === 0) return;
    setError(null);
    const done: string[] = [];
    for (const change of changes) {
      const outcome = await assign.run(change.payload);
      if (!outcome.ok) {
        setError(done.length > 0 ? `Se guardaron ${done.join(', ')}; falló ${change.user.name}: ${outcome.message}` : `${change.user.name}: ${outcome.message}`);
        if (done.length > 0) onSaved();
        return;
      }
      done.push(change.user.name);
    }
    notify.success('Usuarios de la sucursal guardados', `${branch.code}: ${formatNumber(done.length)} ${done.length === 1 ? 'persona actualizada' : 'personas actualizadas'}. Rige desde su próximo ingreso.`);
    onSaved();
    close();
  };

  let body;
  if (users.error) body = <ErrorState error={users.error} operation="GetUsersQuery" onRetry={users.reload} retrying={users.fetching} />;
  else if (!users.data || !checked) body = <LoadingState label="Cargando el personal…" rows={2} />;
  else
    body = (
      <Form id={formId} onSubmit={submit} error={error} busy={assign.sending}>
        <FieldGroup
          label={`Trabajan en ${branch.code} · ${branch.name}`}
          hint={changes.length === 0 ? 'Marque o desmarque personas.' : `${formatNumber(changes.length)} ${changes.length === 1 ? 'cambio' : 'cambios'} por guardar.`}
          error={alone.length > 0 ? `${alone.map((user) => user.name).join(', ')} quedaría sin sucursales: asígnele otra primero.` : undefined}
          bodyClassName="grid grid-cols-1 gap-x-4 sm:grid-cols-2"
        >
          {staff.map((user) => (
            <Checkbox
              key={user.email}
              label={user.name}
              description={`${user.roles.join(', ')} · ${user.branchCodes.join(', ') || 'sin sucursales'}`}
              checked={checked.has(user.email)}
              onChange={(value) => toggle(user.email, value)}
            />
          ))}
        </FieldGroup>
      </Form>
    );

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!assign.sending}
      size="lg"
      title={`Usuarios de la sucursal ${branch.code}`}
      description="Cada persona ve y opera solo sus sucursales (la gerencia global las ve todas por su permiso)."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={assign.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Users />} loading={assign.sending} disabled={!checked || changes.length === 0}>
            Guardar
          </Button>
        </>
      }
    >
      {body}
    </Dialog>
  );
}

export interface UserBranchesDialogProps {
  open: boolean;
  branches: readonly BranchRecord[];
  onClose: () => void;
  onSaved: () => void;
}

export function UserBranchesDialog({ open, branches, onClose, onSaved }: UserBranchesDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const users = useRpcQuery('GetUsersQuery', {}, { enabled: open });
  const assign = useRpcCommand('AssignUserBranchesCommand', { notifyError: false });
  const [email, setEmail] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [touched, setTouched] = useState(false);
  const staff = staffUsers(users.data ?? []);
  const user: UserRecord | null = staff.find((item) => item.email === email) ?? null;
  const active = branches.filter((branch) => branch.isActive || selected.includes(branch.code));
  const problem = !user ? 'Elija la persona.' : selected.length === 0 ? 'Elija al menos una sucursal.' : null;

  const pickUser = (value: string) => {
    setEmail(value);
    setSelected([...(staff.find((item) => item.email === value)?.branchCodes ?? [])]);
    assign.reset();
  };

  const close = () => {
    assign.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (problem || !user) return;
    const outcome = await assign.run({ email: user.email, branchCodes: orderedCodes(selected, branches) });
    if (outcome.ok) {
      notify.success('Sucursales asignadas', `${plainMessage(outcome.result)} Rige desde su próximo ingreso.`);
      onSaved();
      close();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!assign.sending}
      size="lg"
      title="Asignar sucursales a un usuario"
      description="Reemplaza las sucursales en las que trabaja la persona."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={assign.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Building2 />} loading={assign.sending} disabled={!users.data}>
            Guardar sucursales
          </Button>
        </>
      }
    >
      {users.error ? (
        <ErrorState error={users.error} operation="GetUsersQuery" onRetry={users.reload} retrying={users.fetching} />
      ) : !users.data ? (
        <LoadingState label="Cargando el personal…" rows={2} />
      ) : (
        <Form id={formId} onSubmit={submit} error={assign.errorText} busy={assign.sending}>
          <SelectField
            label="Usuario"
            allLabel={false}
            placeholder="Elija la persona"
            value={email}
            onChange={pickUser}
            options={staff.map((item) => ({ value: item.email, label: `${item.name} · ${item.roles.join(', ')}` }))}
            error={touched && !user ? problem : undefined}
            required
            data-autofocus
          />
          {user && (
            <FieldGroup
              label="Trabaja en"
              required
              error={touched && problem ? problem : undefined}
              hint="Reemplaza las que tenía."
              bodyClassName="grid grid-cols-1 gap-x-4 sm:grid-cols-2"
            >
              {active.map((branch) => (
                <Checkbox
                  key={branch.code}
                  label={`${branch.code} · ${branch.name}`}
                  description={branch.isActive ? undefined : 'Sucursal inactiva'}
                  checked={selected.includes(branch.code)}
                  onChange={(value) => {
                    setSelected((current) => (value ? [...current, branch.code] : current.filter((code) => code !== branch.code)));
                    assign.reset();
                  }}
                />
              ))}
            </FieldGroup>
          )}
        </Form>
      )}
    </Dialog>
  );
}
