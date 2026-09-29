// «Ingresar» (/ingresar): correo y contraseña. Con credenciales incorrectas la persona NO sale de la pantalla y ve el
// mensaje único del servidor, que no dice cuál de los dos datos falló (regla P-03). Después de ingresar: el personal va
// al panel y el cliente a su cuenta, o a `volver` si es una ruta interna válida. El personal y los clientes ingresan
// por la misma pantalla: el servidor decide qué tipo de sesión es.

import { LogIn } from 'lucide-react';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { asWebApiError, describeWebApiError, isAccountLocked, type WebApiError } from '@/1-domain/auth/errors';
import { EMAIL_MAX_LENGTH, PASSWORD_LIMITS, toCredentials, validateLoginForm, type LoginFormErrors, type LoginFormInput } from '@/1-domain/auth/validation';
import { destinationAfterLogin, RETURN_PARAM, safeReturnPath } from '@/2-application/auth/navigation';
import { ROUTES } from '@/4-presentation/app/routes';
import { AuthLayout, TEXT_LINK } from '@/4-presentation/components/auth/AuthLayout';
import { PasswordField } from '@/4-presentation/components/auth/PasswordField';
import { SessionLoadingScreen, type LoginLocationState } from '@/4-presentation/components/auth/RequireSession';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { TextField } from '@/4-presentation/components/ui/TextField';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useSession } from '@/4-presentation/hooks/useSession';

const EMPTY: LoginFormInput = { email: '', password: '' };

function failureTitle(error: WebApiError): string {
  if (isAccountLocked(error)) return 'Cuenta bloqueada por intentos fallidos';
  if (error.kind === 'authentication') return 'No pudimos ingresar';
  if (error.kind === 'rate_limited') return 'Demasiados intentos';
  if (error.kind === 'network') return 'Sin conexión con el servidor';
  return 'No pudimos ingresar';
}

export function LoginPage() {
  useDocumentMeta({ title: 'Ingresar', description: 'Ingresá a tu cuenta de Tech Zone Gaming para ver tus reservas y tus datos.' });
  const { status, session, login, demoUsers } = useSession();
  const [params] = useSearchParams();
  const location = useLocation();
  const returnTo = safeReturnPath(params.get(RETURN_PARAM));
  const expired = (location.state as LoginLocationState | null)?.reason === 'expired';

  const [form, setForm] = useState<LoginFormInput>(EMPTY);
  const [errors, setErrors] = useState<LoginFormErrors>({});
  const [touched, setTouched] = useState(false);
  const [failure, setFailure] = useState<WebApiError | null>(null);
  const [sending, setSending] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);
  const alertRef = useRef<HTMLDivElement>(null);

  // Tras un intento fallido el foco va al aviso: quien usa lector de pantalla o teclado se entera sin buscarlo.
  useEffect(() => {
    if (failure) alertRef.current?.focus();
  }, [failure]);

  if (session) return <Navigate to={destinationAfterLogin(session, returnTo)} replace />;
  if (status === 'loading') return <SessionLoadingScreen />;

  const update = (field: keyof LoginFormInput, value: string) => {
    const next = { ...form, [field]: value };
    setForm(next);
    if (touched) setErrors(validateLoginForm(next));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (sending) return;
    const validation = validateLoginForm(form);
    setTouched(true);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;
    setFailure(null);
    setSending(true);
    try {
      // Al ingresar, la sesión queda en el proveedor y esta pantalla redirige sola (el <Navigate> de arriba).
      await login(toCredentials(form));
    } catch (error) {
      // Se queda en la pantalla: sin navegar, con la contraseña en blanco para volver a escribirla.
      setFailure(asWebApiError(error));
      setForm((current) => ({ ...current, password: '' }));
      setTouched(false);
      setErrors({});
    } finally {
      setSending(false);
    }
  };

  const locked = failure !== null && isAccountLocked(failure);

  return (
    <AuthLayout
      eyebrow="Tu cuenta"
      title="Ingresar"
      description="Ingresá con tu correo y tu contraseña para ver tus reservas y tus datos."
      footer={
        <>
          <p>
            ¿Todavía no tenés cuenta?{' '}
            <Link to={ROUTES.registerReturning(returnTo)} className={TEXT_LINK}>
              Registrate
            </Link>
          </p>
          <p className="text-xs text-text-faint">¿Olvidaste tu contraseña? Pedí que te la restablezcan en la tienda o por WhatsApp.</p>
        </>
      }
    >
      <form onSubmit={submit} noValidate aria-busy={sending || undefined} className="space-y-4">
        {expired && !failure && (
          <Alert tone="info" title="Tu sesión venció">
            Ingresá de nuevo para continuar donde estabas.
          </Alert>
        )}
        {failure && (
          <Alert ref={alertRef} tone={locked ? 'warning' : 'danger'} title={failureTitle(failure)}>
            <p>{describeWebApiError(failure)}</p>
            {locked && <p className="mt-1 text-text-muted">Por seguridad, esperá ese tiempo antes de volver a intentar. Si no fuiste vos, avisá a la tienda.</p>}
          </Alert>
        )}
        <TextField
          label="Correo"
          type="email"
          name="email"
          inputMode="email"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          required
          maxLength={EMAIL_MAX_LENGTH}
          value={form.email}
          onChange={(event) => update('email', event.target.value)}
          error={errors.email}
          disabled={sending}
        />
        <PasswordField
          ref={passwordRef}
          label="Contraseña"
          name="password"
          autoComplete="current-password"
          required
          maxLength={PASSWORD_LIMITS.max}
          value={form.password}
          onChange={(event) => update('password', event.target.value)}
          error={errors.password}
          disabled={sending}
        />
        <Button type="submit" variant="brand" fullWidth leftIcon={<LogIn />} loading={sending}>
          {sending ? 'Ingresando…' : 'Ingresar'}
        </Button>
      </form>

      {demoUsers.length > 0 && (
        <section aria-labelledby="demo-titulo" className="mt-5 rounded-xl border border-border bg-surface-2 p-3 text-sm">
          <h2 id="demo-titulo" className="text-xs font-semibold uppercase tracking-wide text-text-faint">
            Demostración sin servidor
          </h2>
          <p className="mt-1 text-text-muted">Usuarios de muestra: existen solo en esta pestaña y se pierden al recargar.</p>
          <ul className="mt-2 space-y-2">
            {demoUsers.map((user) => (
              <li key={user.email} className="flex flex-wrap items-center justify-between gap-2">
                <span className="min-w-0 break-words text-text">
                  <span className="font-semibold">{user.label}:</span> {user.email}
                </span>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={sending}
                  onClick={() => {
                    setForm({ email: user.email, password: user.password });
                    setErrors({});
                    setFailure(null);
                    passwordRef.current?.focus();
                  }}
                >
                  Usar estos datos
                </Button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </AuthLayout>
  );
}
