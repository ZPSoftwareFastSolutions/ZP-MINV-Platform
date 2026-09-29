// Administración › Usuarios (paquete M10). Lo mismo que «Usuarios y roles» del escritorio (UsersView + UsersViewModel) y
// más: el personal y los clientes de la tienda web por separado (filtro «Tipo»), con rol, sucursal y estado en listas
// desplegables; alta y edición (`SaveUserCommand`), sucursales con casillas (`AssignUserBranchesCommand`), contraseña
// temporal que se muestra UNA vez con «Copiar» (`ResetUserPasswordCommand`), activar y desactivar con confirmación, y la
// pestaña de solo lectura «Roles y permisos» (`GetRolesQuery`). Los parámetros de la empresa (margen de alerta y días sin
// rotación, la tercera pestaña del escritorio) están en Administración › Configuración › Empresa.
//
// Botones del tablero: `?accion=nuevo` abre «Nuevo usuario» y `?accion=restablecer`, «Restablecer una contraseña».
// Cada botón solo se ofrece a quien puede ejecutar su comando (`canRun`); el servidor decide igual (regla P-01).

import { KeyRound, ShieldCheck, UserPlus, Users } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ConfirmDialog, Page, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { AssignBranchesDialog } from './AssignBranchesDialog';
import { ResetPasswordDialog, type ResetDone, type ResetRequest } from './ResetPasswordDialog';
import { RolesPanel } from './RolesPanel';
import { SecretDialog, type SecretNotice } from './SecretDialog';
import { UserFormDialog, type CreatedUser, type UserFormMode } from './UserFormDialog';
import { UsersList } from './UsersList';
import { useActionParam, useTabParam } from './params';
import { CUSTOMER_ROLE, activationPayload, branchChoicesOf, roleChoices, toUserItems, type UserItem } from './users';

const TABS = ['usuarios', 'roles'] as const;

function createdNotice(created: CreatedUser): SecretNotice {
  return {
    title: 'Usuario creado',
    description: `${created.name} · ${created.email}`,
    message: created.mustChange
      ? `${created.name} ya puede ingresar con esta contraseña inicial y deberá cambiarla la primera vez.`
      : `${created.name} ya puede ingresar con esta contraseña.`,
    label: 'Contraseña inicial',
    value: created.password,
  };
}

function resetNotice(done: ResetDone): SecretNotice {
  return {
    title: 'Contraseña temporal',
    description: `${done.name} · ${done.email}`,
    message: done.mustChange
      ? `La cuenta de ${done.name} quedó desbloqueada. Deberá cambiar esta contraseña al ingresar.`
      : `La cuenta de ${done.name} quedó desbloqueada con esta contraseña.`,
    label: 'Contraseña temporal',
    value: done.password,
  };
}

export function UsersPage() {
  const { canRun, session } = usePermissions();
  const [tab, setTab] = useTabParam(TABS, 'usuarios');
  const [action, clearAction] = useActionParam();

  const canListBranches = canRun('GetBranchesQuery');
  const users = useRpcQuery('GetUsersQuery', {});
  const roles = useRpcQuery('GetRolesQuery', {});
  const branches = useRpcQuery('GetBranchesQuery', {}, { enabled: canListBranches });

  const canSave = canRun('SaveUserCommand');
  const canReset = canRun('ResetUserPasswordCommand');
  const canAssign = canRun('AssignUserBranchesCommand');

  const sessionEmail = session?.email ?? null;
  const items = useMemo(() => toUserItems(users.data ?? [], roles.data, sessionEmail), [users.data, roles.data, sessionEmail]);
  const choices = useMemo(() => roleChoices(roles.data), [roles.data]);
  const branchChoices = useMemo(() => branchChoicesOf(canListBranches ? branches.data : undefined, users.data ?? []), [canListBranches, branches.data, users.data]);
  const activeBranchCode = session?.access.branches.find((branch) => branch.id === session.access.activeBranchId)?.code ?? null;

  const [form, setForm] = useState<UserFormMode | null>(null);
  const [reset, setReset] = useState<ResetRequest | null>(null);
  const [assigning, setAssigning] = useState<UserItem | null>(null);
  const [toggle, setToggle] = useState<{ item: UserItem; open: boolean } | null>(null);
  const [secret, setSecret] = useState<SecretNotice | null>(null);
  const activation = useRpcCommand('SaveUserCommand', {
    success: (_result, payload) => (payload.isActive ? `${payload.name} quedó activo` : `${payload.name} quedó desactivado: ya no puede ingresar`),
    notifyError: false,
  });

  // Botones del tablero: la acción de la dirección abre su diálogo una sola vez y después se quita de la dirección.
  const [handled, setHandled] = useState('');
  if (action !== handled) {
    setHandled(action);
    if (action === 'nuevo' && canSave) setForm({ kind: 'new' });
    else if (action === 'restablecer' && canReset) setReset({ item: null });
  }
  useEffect(() => {
    if (action) clearAction();
  }, [action, clearAction]);

  const openNew = () => setForm({ kind: 'new' });
  const askToggle = (item: UserItem) => {
    activation.reset();
    setToggle({ item, open: true });
  };
  const showUsersOf = (roleCode: string) => setTab('usuarios', roleCode === CUSTOMER_ROLE ? { rol: roleCode, tipo: 'clientes' } : { rol: roleCode });

  const toggled = toggle?.item ?? null;
  const deactivating = toggled?.record.isActive ?? false;

  return (
    <Page
      title="Usuarios"
      description="Cuentas del personal y de los clientes de la tienda web: rol, sucursales, contraseñas y estado. En «Roles y permisos» vea qué puede hacer cada rol."
      actions={
        <>
          {canReset && (
            <Button variant="outline" leftIcon={<KeyRound />} onClick={() => setReset({ item: null })}>
              Restablecer una contraseña
            </Button>
          )}
          {canSave && (
            <Button leftIcon={<UserPlus />} onClick={openNew}>
              Nuevo usuario
            </Button>
          )}
        </>
      }
    >
      <Tabs
        label="Secciones de usuarios"
        value={tab}
        onChange={(next) => setTab(next)}
        tabs={[
          { id: 'usuarios', label: 'Usuarios', icon: <Users />, count: users.data?.length },
          { id: 'roles', label: 'Roles y permisos', icon: <ShieldCheck /> },
        ]}
      >
        <TabPanel id="usuarios">
          <UsersList
            users={users}
            items={items}
            roles={roles.data}
            roleChoices={choices}
            branches={branchChoices}
            canSave={canSave}
            canReset={canReset}
            canAssign={canAssign}
            onNew={openNew}
            onEdit={(item) => setForm({ kind: 'edit', item })}
            onReset={(item) => setReset({ item })}
            onAssign={setAssigning}
            onToggle={askToggle}
          />
        </TabPanel>
        <TabPanel id="roles">
          <RolesPanel roles={roles} onShowUsers={showUsersOf} />
        </TabPanel>
      </Tabs>

      <UserFormDialog
        mode={form}
        roles={choices}
        branches={canListBranches ? branchChoices : null}
        activeBranchCode={activeBranchCode}
        onClose={() => setForm(null)}
        onSaved={users.reload}
        onCreated={(created) => setSecret(createdNotice(created))}
      />
      <ResetPasswordDialog
        request={reset}
        users={items}
        onClose={() => setReset(null)}
        onDone={(done) => {
          users.reload();
          setSecret(resetNotice(done));
        }}
      />
      <AssignBranchesDialog target={assigning} branches={branchChoices} onClose={() => setAssigning(null)} onDone={users.reload} />
      <ConfirmDialog
        open={toggle?.open ?? false}
        onClose={() => setToggle((current) => (current ? { ...current, open: false } : current))}
        tone={deactivating ? 'danger' : 'primary'}
        title={toggled ? `¿${deactivating ? 'Desactivar' : 'Activar'} a ${toggled.record.name}?` : ''}
        message={
          deactivating
            ? 'No podrá ingresar al panel, al escritorio ni a la tienda con esta cuenta. Sus datos y su actividad se conservan y puede volver a activarla cuando quiera.'
            : 'Podrá volver a ingresar con su contraseña (si no la recuerda, restablézcala).'
        }
        confirmLabel={deactivating ? 'Desactivar' : 'Activar'}
        onConfirm={async () => {
          const payload = toggled ? activationPayload(toggled.record, !toggled.record.isActive) : null;
          if (!payload) return false;
          const outcome = await activation.run(payload);
          if (outcome.ok) users.reload();
          return outcome;
        }}
        error={activation.errorText}
      />
      <SecretDialog secret={secret} onClose={() => setSecret(null)} />
    </Page>
  );
}
