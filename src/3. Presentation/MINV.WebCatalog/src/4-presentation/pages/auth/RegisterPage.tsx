// «Registrarse» (/registrarse): crea una cuenta de CLIENTE (nombre, correo, teléfono, contraseña y repetirla) y deja la
// sesión iniciada. La web no envía rol, sucursal ni permisos: el servidor crea siempre una cuenta de cliente; las
// cuentas del personal las crea el administrador (regla P-03). Errores por campo, indicador de requisitos de la
// contraseña y aviso con enlace a ingresar cuando el correo ya tiene cuenta.

import { UserPlus } from 'lucide-react';
import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import { asWebApiError, describeWebApiError, isEmailTaken, type WebApiError } from '@/1-domain/auth/errors';
import {
  EMAIL_MAX_LENGTH,
  NAME_MAX_LENGTH,
  PASSWORD_LIMITS,
  PHONE_MAX_LENGTH,
  toRegistration,
  validateRegisterForm,
  type RegisterFormErrors,
  type RegisterFormInput,
} from '@/1-domain/auth/validation';
import { destinationAfterLogin, RETURN_PARAM, safeReturnPath } from '@/2-application/auth/navigation';
import { ROUTES } from '@/4-presentation/app/routes';
import { AuthLayout, TEXT_LINK } from '@/4-presentation/components/auth/AuthLayout';
import { PasswordChecklist } from '@/4-presentation/components/auth/PasswordChecklist';
import { PasswordField } from '@/4-presentation/components/auth/PasswordField';
import { SessionLoadingScreen } from '@/4-presentation/components/auth/RequireSession';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { TextField } from '@/4-presentation/components/ui/TextField';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useToast } from '@/4-presentation/hooks/useToast';

const EMPTY: RegisterFormInput = { name: '', email: '', phone: '', password: '', confirm: '' };

export function RegisterPage() {
  useDocumentMeta({ title: 'Crear cuenta', description: 'Creá tu cuenta de Tech Zone Gaming para reservar más rápido y seguir tus reservas.' });
  const { status, session, register } = useSession();
  const toast = useToast();
  const [params] = useSearchParams();
  const returnTo = safeReturnPath(params.get(RETURN_PARAM));
  const checklistId = useId();

  const [form, setForm] = useState<RegisterFormInput>(EMPTY);
  const [errors, setErrors] = useState<RegisterFormErrors>({});
  const [touched, setTouched] = useState(false);
  const [failure, setFailure] = useState<WebApiError | null>(null);
  /** Correo que el servidor rechazó porque ya tiene cuenta (el aviso se va cuando la persona escribe otro). */
  const [takenEmail, setTakenEmail] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const alertRef = useRef<HTMLDivElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (failure) alertRef.current?.focus();
  }, [failure]);

  useEffect(() => {
    if (takenEmail) emailRef.current?.focus();
  }, [takenEmail]);

  if (session) return <Navigate to={destinationAfterLogin(session, returnTo)} replace />;
  if (status === 'loading') return <SessionLoadingScreen />;

  const update = (field: keyof RegisterFormInput, value: string) => {
    const next = { ...form, [field]: value };
    setForm(next);
    if (touched) setErrors(validateRegisterForm(next));
    if (field === 'email') setTakenEmail(null);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (sending) return;
    const validation = validateRegisterForm(form);
    setTouched(true);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;
    setFailure(null);
    setTakenEmail(null);
    setSending(true);
    try {
      const created = await register(toRegistration(form));
      toast.notify({ tone: 'success', title: 'Cuenta creada', description: `¡Hola, ${created.displayName}! Ya podés ver tus reservas y tus datos.` });
    } catch (error) {
      const problem = asWebApiError(error);
      if (isEmailTaken(problem)) setTakenEmail(form.email.trim());
      else setFailure(problem);
    } finally {
      setSending(false);
    }
  };

  const emailTaken = takenEmail !== null;

  return (
    <AuthLayout
      eyebrow="Tu cuenta"
      title="Crear cuenta"
      description="Con tu cuenta reservás más rápido y seguís tus reservas desde cualquier dispositivo."
      footer={
        <p>
          ¿Ya tenés cuenta?{' '}
          <Link to={ROUTES.loginReturning(returnTo)} className={TEXT_LINK}>
            Ingresá
          </Link>
        </p>
      }
    >
      <form onSubmit={submit} noValidate aria-busy={sending || undefined} className="space-y-4">
        {failure && (
          <Alert ref={alertRef} tone="danger" title={failure.kind === 'rate_limited' ? 'Demasiados intentos' : 'No pudimos crear la cuenta'}>
            <p>{describeWebApiError(failure)}</p>
            {failure.errors.length > 1 && (
              <ul className="mt-1 list-disc pl-5 text-text-muted">
                {failure.errors.map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
        {emailTaken && (
          <Alert
            tone="warning"
            title="Ese correo ya tiene una cuenta"
            actions={
              <Button to={ROUTES.loginReturning(returnTo)} size="sm" variant="outline">
                Ingresar con ese correo
              </Button>
            }
          >
            Si es tuyo, ingresá con él. Si no recordás la contraseña, pedí que te la restablezcan en la tienda.
          </Alert>
        )}
        <TextField
          label="Nombre y apellido"
          name="name"
          autoComplete="name"
          required
          maxLength={NAME_MAX_LENGTH}
          value={form.name}
          onChange={(event) => update('name', event.target.value)}
          error={errors.name}
          disabled={sending}
        />
        <TextField
          ref={emailRef}
          label="Correo"
          type="email"
          name="email"
          inputMode="email"
          autoComplete="email"
          autoCapitalize="none"
          spellCheck={false}
          required
          maxLength={EMAIL_MAX_LENGTH}
          value={form.email}
          onChange={(event) => update('email', event.target.value)}
          error={errors.email ?? (emailTaken ? 'Este correo ya tiene una cuenta.' : undefined)}
          hint="Con este correo vas a ingresar y ahí te llega el detalle de tus reservas."
          disabled={sending}
        />
        <TextField
          label="Teléfono o WhatsApp"
          type="tel"
          name="phone"
          inputMode="tel"
          autoComplete="tel"
          required
          placeholder="+591 71234567"
          maxLength={PHONE_MAX_LENGTH}
          value={form.phone}
          onChange={(event) => update('phone', event.target.value)}
          error={errors.phone}
          hint="7 u 8 dígitos de Bolivia, con o sin +591."
          disabled={sending}
        />
        <PasswordField
          label="Contraseña"
          name="new-password"
          autoComplete="new-password"
          required
          maxLength={PASSWORD_LIMITS.max}
          value={form.password}
          onChange={(event) => update('password', event.target.value)}
          error={errors.password}
          describedBy={checklistId}
          disabled={sending}
        />
        <PasswordChecklist id={checklistId} password={form.password} />
        <PasswordField
          label="Repetí la contraseña"
          revealName="la repetición de la contraseña"
          name="confirm-password"
          autoComplete="new-password"
          required
          maxLength={PASSWORD_LIMITS.max}
          value={form.confirm}
          onChange={(event) => update('confirm', event.target.value)}
          error={errors.confirm}
          disabled={sending}
        />
        <Button type="submit" variant="brand" fullWidth leftIcon={<UserPlus />} loading={sending}>
          {sending ? 'Creando tu cuenta…' : 'Crear cuenta'}
        </Button>
        <p className="text-xs text-text-faint">Usamos tus datos solo para tus reservas. No hay pagos en línea: la reserva se confirma y paga en la tienda.</p>
      </form>
    </AuthLayout>
  );
}
