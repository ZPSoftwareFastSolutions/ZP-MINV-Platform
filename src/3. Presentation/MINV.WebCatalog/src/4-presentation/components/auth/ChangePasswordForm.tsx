// Formulario «Cambiar contraseña» (contraseña actual, nueva y repetirla, con el indicador de requisitos). Lo usan la
// pantalla /cambiar-contrasena (personal y cliente, también cuando el cambio es obligatorio) y la sección de «Mi cuenta».
// Envía `ChangePasswordCommand` por el RPC. Si la contraseña actual es incorrecta el servidor responde 401 SIN cerrar la
// sesión: se marca ese campo y la persona sigue en la pantalla.

import { KeyRound } from 'lucide-react';
import { useId, useRef, useState, type FormEvent } from 'react';
import { asWebApiError, describeWebApiError, type Voice, type WebApiError } from '@/1-domain/auth/errors';
import { PASSWORD_LIMITS, validateChangePasswordForm, type ChangePasswordFormErrors, type ChangePasswordFormInput } from '@/1-domain/auth/validation';
import { AttemptKey } from '@/2-application';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { useAccount } from '@/4-presentation/hooks/useRpc';
import { PasswordChecklist } from './PasswordChecklist';
import { PasswordField } from './PasswordField';

export interface ChangePasswordFormProps {
  /** «store» trata de vos (cliente); «panel» usa español neutro (personal). */
  voice?: Voice;
  /** La contraseña ya se cambió en el servidor. */
  onChanged: () => void | Promise<void>;
}

const EMPTY: ChangePasswordFormInput = { current: '', next: '', confirm: '' };

export function ChangePasswordForm({ voice = 'store', onChanged }: ChangePasswordFormProps) {
  const account = useAccount();
  const checklistId = useId();
  const [form, setForm] = useState<ChangePasswordFormInput>(EMPTY);
  const [errors, setErrors] = useState<ChangePasswordFormErrors>({});
  const [touched, setTouched] = useState(false);
  const [failure, setFailure] = useState<WebApiError | null>(null);
  const [sending, setSending] = useState(false);
  const [attempt] = useState(() => new AttemptKey());
  const currentRef = useRef<HTMLInputElement>(null);

  const update = (field: keyof ChangePasswordFormInput, value: string) => {
    const next = { ...form, [field]: value };
    setForm(next);
    if (touched) setErrors(validateChangePasswordForm(next, voice));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const validation = validateChangePasswordForm(form, voice);
    setTouched(true);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;
    setFailure(null);
    setSending(true);
    try {
      await attempt.run((requestId) => account.changePassword({ currentPassword: form.current, newPassword: form.next }, { requestId }));
      setForm(EMPTY);
      setTouched(false);
      await onChanged();
    } catch (error) {
      const problem = asWebApiError(error);
      if (problem.kind === 'authentication') {
        // Contraseña actual incorrecta (si la sesión hubiera vencido, la guarda ya habría llevado a ingresar).
        setErrors({ current: problem.message || 'La contraseña actual no es correcta.' });
        setForm((current) => ({ ...current, current: '' }));
        currentRef.current?.focus();
      } else {
        setFailure(problem);
      }
    } finally {
      setSending(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate aria-busy={sending || undefined} className="space-y-4">
      {failure && (
        <Alert tone="danger" title={voice === 'store' ? 'No pudimos cambiar la contraseña' : 'No se pudo cambiar la contraseña'}>
          <p>{describeWebApiError(failure, voice)}</p>
          {failure.errors.length > 1 && (
            <ul className="mt-1 list-disc pl-5 text-text-muted">
              {failure.errors.map((message) => (
                <li key={message}>{message}</li>
              ))}
            </ul>
          )}
        </Alert>
      )}
      <PasswordField
        ref={currentRef}
        label="Contraseña actual"
        revealName="la contraseña actual"
        name="current-password"
        autoComplete="current-password"
        required
        maxLength={PASSWORD_LIMITS.max}
        value={form.current}
        onChange={(event) => update('current', event.target.value)}
        error={errors.current}
        disabled={sending}
      />
      <PasswordField
        label="Nueva contraseña"
        revealName="la nueva contraseña"
        name="new-password"
        autoComplete="new-password"
        required
        maxLength={PASSWORD_LIMITS.max}
        value={form.next}
        onChange={(event) => update('next', event.target.value)}
        error={errors.next}
        describedBy={checklistId}
        disabled={sending}
      />
      <PasswordChecklist id={checklistId} password={form.next} />
      <PasswordField
        label={voice === 'store' ? 'Repetí la nueva contraseña' : 'Repita la nueva contraseña'}
        revealName="la repetición de la nueva contraseña"
        name="confirm-password"
        autoComplete="new-password"
        required
        maxLength={PASSWORD_LIMITS.max}
        value={form.confirm}
        onChange={(event) => update('confirm', event.target.value)}
        error={errors.confirm}
        disabled={sending}
      />
      <Button type="submit" variant="brand" leftIcon={<KeyRound />} loading={sending}>
        {sending ? 'Cambiando…' : 'Cambiar contraseña'}
      </Button>
    </form>
  );
}
