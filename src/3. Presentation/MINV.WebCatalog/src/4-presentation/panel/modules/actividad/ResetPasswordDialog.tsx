// Módulo «Actividad» · el COMANDO de ejemplo: asignar una contraseña temporal a un usuario (`ResetUserPasswordCommand`),
// que además desbloquea su cuenta. Sirve cuando la actividad muestra ingresos rechazados o una cuenta bloqueada. Solo lo
// ofrece a quien puede ejecutarlo (`canRun`), y el servidor igual decide (regla P-01).
//
// Patrón de un comando con formulario:
//   - `useRpcCommand(operación, { success, notifyError: false })`: el aviso de éxito sale solo; el error se muestra DENTRO
//     del diálogo (`errorText`), sin cerrarlo, para corregir y reintentar (el reintento viaja con el mismo `requestId`).
//   - `run(contenido)` nunca lanza: se mira `ok`.
//   - La contraseña vive solo en el estado del diálogo y se borra al cerrarlo (nada se guarda ni se registra).

import { KeyRound, WandSparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, Form, TextField } from '@/4-presentation/panel/kit';
import { generateTemporaryPassword, temporaryPasswordProblem } from './activity';

/** A quién se le asigna la contraseña. */
export interface ResetTarget {
  email: string;
  name: string;
}

export interface ResetPasswordDialogProps {
  /** null = diálogo cerrado. */
  target: ResetTarget | null;
  onClose: () => void;
}

export function ResetPasswordDialog({ target, onClose }: ResetPasswordDialogProps) {
  const formId = useId();
  const [password, setPassword] = useState('');
  const [mustChange, setMustChange] = useState(true);
  const [touched, setTouched] = useState(false);
  const reset = useRpcCommand('ResetUserPasswordCommand', {
    success: (_result, payload) => `Contraseña temporal asignada a ${payload.email}`,
    notifyError: false,
  });
  // El último destinatario sigue a la vista mientras el diálogo se cierra (animación de salida).
  const [shown, setShown] = useState<ResetTarget | null>(target);
  if (target && target !== shown) setShown(target);

  const problem = temporaryPasswordProblem(password);

  const close = () => {
    setPassword('');
    setMustChange(true);
    setTouched(false);
    reset.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!target || problem) return;
    const outcome = await reset.run({ email: target.email, newPassword: password, mustChange });
    if (outcome.ok) close();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!reset.sending}
      title="Asignar una contraseña temporal"
      description={shown ? `${shown.name} · ${shown.email}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={reset.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<KeyRound />} loading={reset.sending}>
            Asignar contraseña
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={reset.errorText} busy={reset.sending}>
        <p className="text-sm text-text-muted">
          La cuenta se desbloquea y la persona ingresa con esta contraseña. Comuníquesela por un medio seguro; no queda registrada en ningún lado.
        </p>
        <TextField
          label="Contraseña temporal"
          value={password}
          onChange={(value) => {
            setPassword(value);
            if (reset.error) reset.reset();
          }}
          error={touched && problem ? problem : undefined}
          hint="De 8 a 128 caracteres, con letras y números."
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          maxLength={128}
          required
          data-autofocus
        />
        <Button variant="subtle" leftIcon={<WandSparkles />} onClick={() => setPassword(generateTemporaryPassword())}>
          Generar una contraseña
        </Button>
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
