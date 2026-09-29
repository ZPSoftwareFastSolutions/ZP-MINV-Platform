// Módulo «Usuarios» · restablecer la contraseña de una persona (`ResetUserPasswordCommand`), como el escritorio: se
// genera una contraseña temporal fácil de dictar, se asigna (la cuenta además se desbloquea) y, al terminar, la pantalla
// la muestra UNA vez con «Copiar». Desde la fila ya se sabe a quién; desde el botón del tablero se elige a la persona en
// una lista con búsqueda.
//
// La contraseña se genera al abrir (no al enviar): si hay que reintentar por una falla de red, viaja el MISMO pedido y el
// servidor no la cambia dos veces (idempotencia, regla B-09). Vive solo en el estado del diálogo y se borra al cerrarlo.

import { KeyRound } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, ComboBox, Dialog, Form, type ComboOption } from '@/4-presentation/panel/kit';
import { KIND_LABELS, generateTemporaryPassword, type UserItem } from './users';

export interface ResetRequest {
  /** La persona; null = elegirla en el diálogo (botón del tablero). */
  item: UserItem | null;
}

/** Contraseña asignada (para mostrarla una vez). */
export interface ResetDone {
  name: string;
  email: string;
  password: string;
  mustChange: boolean;
}

export interface ResetPasswordDialogProps {
  /** null = cerrado. */
  request: ResetRequest | null;
  /** Personas para elegir cuando no viene una. */
  users: readonly UserItem[];
  onClose: () => void;
  onDone: (done: ResetDone) => void;
}

function userOption(item: UserItem): ComboOption<UserItem> {
  return { value: item.key, label: item.record.name, description: `${item.record.email} · ${item.roleText || KIND_LABELS[item.kind]}`, data: item };
}

export function ResetPasswordDialog({ request, users, onClose, onDone }: ResetPasswordDialogProps) {
  const formId = useId();
  const reset = useRpcCommand('ResetUserPasswordCommand', { notifyError: false, success: (_result, payload) => `Contraseña temporal asignada a ${payload.email}` });
  const [shown, setShown] = useState<ResetRequest | null>(null);
  const [picked, setPicked] = useState<ComboOption<UserItem> | null>(null);
  const [mustChange, setMustChange] = useState(true);
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  if (request && request !== shown) {
    setShown(request);
    setPicked(null);
    setMustChange(true);
    setPassword(generateTemporaryPassword());
    setTouched(false);
  }
  const options = useMemo(() => users.map(userOption), [users]);
  const target = shown?.item ?? picked?.data ?? null;

  const close = () => {
    reset.reset();
    setPassword('');
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!target || !password) return;
    const outcome = await reset.run({ email: target.record.email, newPassword: password, mustChange });
    if (!outcome.ok) return;
    onDone({ name: target.record.name, email: target.record.email, password, mustChange });
    close();
  };

  return (
    <Dialog
      open={request !== null}
      onClose={close}
      dismissible={!reset.sending}
      title="Restablecer la contraseña"
      description={shown?.item ? `${shown.item.record.name} · ${shown.item.record.email}` : 'Elija a la persona: se le asigna una contraseña temporal.'}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={reset.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<KeyRound />} loading={reset.sending}>
            Generar contraseña
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={reset.errorText} busy={reset.sending}>
        {!shown?.item && (
          <ComboBox
            label="Persona"
            value={picked}
            onChange={(option) => {
              setPicked(option);
              reset.reset();
            }}
            options={options}
            placeholder="Nombre o correo"
            error={touched && !picked ? 'Elija a la persona.' : undefined}
            required
          />
        )}
        <p className="text-sm text-text-muted">
          Se genera una contraseña temporal y la cuenta queda desbloqueada. La contraseña se muestra UNA sola vez para que la entregue en persona o por
          un medio seguro.
        </p>
        <Checkbox
          label="Pedir que la cambie al ingresar"
          description="Recomendado: la temporal deja de servir en cuanto la persona elige la suya."
          checked={mustChange}
          onChange={setMustChange}
        />
      </Form>
    </Dialog>
  );
}
