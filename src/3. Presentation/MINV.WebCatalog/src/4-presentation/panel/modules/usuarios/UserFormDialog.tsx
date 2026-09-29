// Módulo «Usuarios» · crear o editar un usuario (`SaveUserCommand`), como el editor del escritorio: nombre, correo, rol en
// lista desplegable, activo y, para uno nuevo, sus sucursales (casillas) y la contraseña inicial con «Pedir que la
// cambie al ingresar». La contraseña se genera sola (se puede escribir otra) y, al crear el usuario, se muestra UNA vez
// con «Copiar» (SecretDialog). El servidor siempre crea la contraseña inicial para cambiarla: si se desmarca la casilla,
// se envía además `ResetUserPasswordCommand` con `mustChange: false` (ver «Pendientes» del informe M10).
//
// Validación por campo en la página (comodidad); el servidor valida igual y su mensaje se ve dentro del diálogo, sin
// cerrarlo. Nadie puede desactivarse ni cambiarse el rol a sí mismo (como el escritorio).

import { Save, UserPlus, WandSparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, FieldGroup, Form, FormGrid, SelectField, Switch, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  branchLabel,
  editUserPayload,
  formRoleOptions,
  generateTemporaryPassword,
  initialUserForm,
  newUserPayload,
  orderedCodes,
  roleDescription,
  roleLabel,
  userFormProblems,
  type BranchChoice,
  type RoleChoice,
  type UserFormField,
  type UserFormValues,
  type UserItem,
} from './users';

export type UserFormMode = { kind: 'new' } | { kind: 'edit'; item: UserItem };

/** Usuario recién creado (para mostrar su contraseña inicial una vez). */
export interface CreatedUser {
  name: string;
  email: string;
  password: string;
  mustChange: boolean;
}

export interface UserFormDialogProps {
  /** null = cerrado. */
  mode: UserFormMode | null;
  roles: readonly RoleChoice[];
  /** Sucursales para elegir; null = la sesión no puede listarlas (el servidor usa su sucursal activa). */
  branches: readonly BranchChoice[] | null;
  /** Sucursal activa de la sesión (se propone para el usuario nuevo). */
  activeBranchCode: string | null;
  onClose: () => void;
  /** Después de guardar (volver a leer la lista). */
  onSaved: () => void;
  /** Usuario NUEVO creado: la pantalla muestra su contraseña una vez. */
  onCreated: (created: CreatedUser) => void;
}

export function UserFormDialog({ mode, roles, branches, activeBranchCode, onClose, onSaved, onCreated }: UserFormDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const save = useRpcCommand('SaveUserCommand', { notifyError: false });
  const unlock = useRpcCommand('ResetUserPasswordCommand', { notifyError: false });
  const [shown, setShown] = useState<UserFormMode | null>(null);
  const [values, setValues] = useState<UserFormValues>(() => initialUserForm(mode?.kind === 'edit' ? mode.item : null, roles, activeBranchCode));
  const [touched, setTouched] = useState(false);
  // Cada vez que se abre, el formulario empieza de nuevo (el último sigue a la vista mientras se cierra).
  if (mode && mode !== shown) {
    setShown(mode);
    setValues(initialUserForm(mode.kind === 'edit' ? mode.item : null, roles, activeBranchCode));
    setTouched(false);
  }

  const isNew = shown?.kind === 'new';
  const editing = shown?.kind === 'edit' ? shown.item : null;
  const isSelf = editing?.isSelf ?? false;
  const isCustomer = editing?.kind === 'clientes';
  const pickBranches = isNew && branches !== null;
  const problems = userFormProblems(values, { isNew, branchesRequired: pickBranches });
  const busy = save.sending || unlock.sending;
  const show = (field: UserFormField) => (touched ? problems[field] : undefined);
  const roleOptions =
    editing && editing.kind === 'clientes' ? [{ value: editing.roleCode, label: roleLabel(editing.roleCode, roles) }] : formRoleOptions(roles, editing?.roleCode ?? null);

  const set = <K extends keyof UserFormValues>(key: K, value: UserFormValues[K]) => {
    setValues((current) => ({ ...current, [key]: value }));
    if (save.error) save.reset();
  };

  const close = () => {
    save.reset();
    unlock.reset();
    // La contraseña no queda en la memoria de la pantalla.
    setValues((current) => ({ ...current, password: '' }));
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!shown || Object.keys(problems).length > 0) return;
    if (shown.kind === 'new') {
      const payload = newUserPayload(values, branches !== null);
      const outcome = await save.run(payload);
      if (!outcome.ok) return;
      const email = outcome.result || payload.email;
      let mustChange = true;
      if (!values.mustChange) {
        const unlocked = await unlock.run({ email, newPassword: values.password, mustChange: false });
        if (unlocked.ok) mustChange = false;
        else notify.warning('El usuario se creó, pero deberá cambiar la contraseña al ingresar', unlocked.message);
      }
      onSaved();
      onCreated({ name: payload.name, email, password: values.password, mustChange });
      close();
      return;
    }
    const payload = editUserPayload(values, shown.item.record);
    const outcome = await save.run(payload);
    if (!outcome.ok) return;
    notify.success('Usuario actualizado', `${payload.name} · ${roleLabel(payload.roleCode, roles)}${payload.isActive ? '' : ' · desactivado'}`);
    onSaved();
    close();
  };

  const toggleBranch = (code: string, checked: boolean) => {
    const next = checked ? [...values.branchCodes, code] : values.branchCodes.filter((item) => item !== code);
    set('branchCodes', orderedCodes(next, branches ?? []));
  };

  return (
    <Dialog
      open={mode !== null}
      onClose={close}
      dismissible={!busy}
      size="lg"
      title={isNew ? 'Nuevo usuario' : 'Editar usuario'}
      description={editing ? `${editing.record.name} · ${editing.record.email}` : 'Una cuenta del personal para ingresar al panel y al escritorio.'}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={busy}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={isNew ? <UserPlus /> : <Save />} loading={busy}>
            {isNew ? 'Crear usuario' : 'Guardar cambios'}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText ?? undefined} busy={busy}>
        <FormGrid>
          <TextField
            label="Nombre completo"
            value={values.name}
            onChange={(value) => set('name', value)}
            error={show('name')}
            maxLength={120}
            autoComplete="off"
            required
            data-autofocus
          />
          <TextField
            label="Correo (usuario para ingresar)"
            type="email"
            value={values.email}
            onChange={(value) => set('email', value)}
            error={show('email')}
            maxLength={150}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            required
          />
          <SelectField
            label="Rol"
            className="sm:col-span-2"
            value={values.roleCode}
            onChange={(value) => set('roleCode', value)}
            options={roleOptions}
            allLabel={false}
            placeholder="Elija el rol"
            error={show('roleCode')}
            hint={
              isSelf
                ? 'No puede cambiar su propio rol: pídaselo a otro administrador.'
                : isCustomer
                  ? 'Una cuenta de la tienda web siempre es de «Cliente web».'
                  : roleDescription(values.roleCode)
            }
            disabled={isSelf || isCustomer}
            required
          />
          <Switch
            className="sm:col-span-2"
            label="Usuario activo (puede ingresar)"
            description={isSelf ? 'No puede desactivar su propio usuario.' : 'Si lo desactiva no podrá ingresar; sus datos y su actividad se conservan.'}
            checked={values.isActive}
            onChange={(value) => set('isActive', value)}
            disabled={isSelf}
          />
        </FormGrid>

        {pickBranches && (
          <FieldGroup
            label="Sucursales donde trabaja"
            required
            error={show('branchCodes')}
            hint="Solo ve y opera las sucursales marcadas (la gerencia global ve todas igual)."
            bodyClassName="grid gap-x-4 sm:grid-cols-2"
          >
            {(branches ?? [])
              .filter((branch) => branch.isActive)
              .map((branch) => (
                <Checkbox
                  key={branch.code}
                  label={branchLabel(branch)}
                  checked={values.branchCodes.includes(branch.code)}
                  onChange={(checked) => toggleBranch(branch.code, checked)}
                />
              ))}
          </FieldGroup>
        )}

        {isNew && (
          <div className="space-y-3 rounded-xl border border-border bg-surface-2 p-4">
            <TextField
              label="Contraseña inicial"
              value={values.password}
              onChange={(value) => set('password', value)}
              error={show('password')}
              hint="De 8 a 128 caracteres, con letras y números. Se muestra una vez al crear el usuario, para entregarla."
              autoComplete="off"
              autoCapitalize="none"
              spellCheck={false}
              maxLength={128}
              inputClassName="font-mono"
              required
            />
            <Button variant="subtle" leftIcon={<WandSparkles />} onClick={() => set('password', generateTemporaryPassword())}>
              Generar otra
            </Button>
            <Checkbox
              label="Pedir que la cambie al ingresar"
              description="Recomendado: la contraseña inicial deja de servir en cuanto la persona elige la suya."
              checked={values.mustChange}
              onChange={(value) => set('mustChange', value)}
            />
          </div>
        )}
      </Form>
    </Dialog>
  );
}
