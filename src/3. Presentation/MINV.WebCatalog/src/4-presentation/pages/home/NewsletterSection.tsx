import { CircleCheck, MessageCircle, Send, Sparkles } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { STORE } from '@/shared/constants';
import { isValidEmail } from './homeSelectors';

/**
 * Cierre de la portada: «¿No sabés qué elegir?» con acceso a los armados sugeridos y a WhatsApp, más un boletín
 * visual. El formulario es controlado y solo confirma en pantalla: no envía nada a ningún servicio (alcance de la V5).
 */
export function NewsletterSection() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [subscribed, setSubscribed] = useState<string | null>(null);
  const emailId = useId();
  const hintId = useId();
  const errorId = useId();

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const value = email.trim();
    if (!isValidEmail(value)) {
      setError(value ? 'Revisá el correo: le falta el «@» o el dominio (por ejemplo, vos@correo.com).' : 'Escribí tu correo para suscribirte.');
      setSubscribed(null);
      return;
    }
    setError(null);
    setSubscribed(value);
    setEmail('');
  };

  return (
    <section aria-labelledby="ayuda-titulo" className="overflow-hidden rounded-card border border-border bg-surface shadow-card">
      <div className="grid gap-8 p-6 sm:p-8 lg:grid-cols-2 lg:gap-12 lg:p-12">
        <div className="relative">
          <div aria-hidden="true" className="pointer-events-none absolute -top-16 -left-16 size-56 rounded-full bg-primary/20 blur-3xl" />
          <p className="relative inline-flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">
            <Sparkles aria-hidden="true" className="size-4" />
            Te ayudamos a elegir
          </p>
          <h2 id="ayuda-titulo" className="relative mt-3 font-display text-2xl font-semibold text-text sm:text-3xl">
            ¿No sabés qué elegir? Empezá por un armado sugerido.
          </h2>
          <p className="relative mt-3 max-w-lg text-base text-text-muted">
            Nuestros técnicos arman configuraciones por nivel y presupuesto. Cargá una, cambiá las piezas que quieras y, si te quedan dudas, escribinos: respondemos en horario de tienda ({STORE.hours}).
          </p>
          <div className="relative mt-6 flex flex-wrap gap-3">
            <Button to={ROUTES.presets} variant="brand" leftIcon={<Sparkles />}>
              Ver armados sugeridos
            </Button>
            <Button href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" variant="outline" leftIcon={<MessageCircle />}>
              Escribinos por WhatsApp
            </Button>
          </div>
        </div>

        <div className="rounded-card border border-border bg-surface-2 p-5 sm:p-6">
          <h3 className="font-display text-lg font-semibold text-text">Ofertas y lanzamientos en tu correo</h3>
          <p className="mt-1 text-sm text-text-muted">Una vez por semana, sin spam. Te avisamos primero cuando llega stock nuevo.</p>
          <form noValidate onSubmit={onSubmit} className="mt-5">
            <label htmlFor={emailId} className="block text-sm font-medium text-text">
              Tu correo
            </label>
            <div className="mt-2 flex flex-col gap-2 sm:flex-row">
              <input
                id={emailId}
                type="email"
                name="email"
                inputMode="email"
                autoComplete="email"
                placeholder="vos@correo.com"
                value={email}
                onChange={(event) => {
                  setEmail(event.target.value);
                  if (error) setError(null);
                }}
                aria-invalid={error ? true : undefined}
                aria-describedby={error ? errorId : hintId}
                className="h-11 w-full min-w-0 rounded-xl border border-border-strong bg-bg px-4 text-base text-text placeholder:text-text-faint transition-colors duration-200 focus-visible:border-accent aria-invalid:border-danger sm:flex-1"
              />
              <Button type="submit" variant="accent" leftIcon={<Send />} className="sm:shrink-0">
                Suscribirme
              </Button>
            </div>
            {error ? (
              <p id={errorId} role="alert" className="mt-2 text-sm text-danger-text">
                {error}
              </p>
            ) : (
              <p id={hintId} className="mt-2 text-xs text-text-faint">
                Podés darte de baja cuando quieras. El boletín todavía no está conectado: no se envía ningún dato.
              </p>
            )}
            {subscribed && (
              <p role="status" className="mt-3 flex items-start gap-2 rounded-xl border border-success/40 bg-success-soft px-3 py-2 text-sm text-success-text">
                <CircleCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                <span>
                  Listo: te avisaremos a <span className="font-semibold">{subscribed}</span> cuando haya ofertas y lanzamientos nuevos.
                </span>
              </p>
            )}
          </form>
        </div>
      </div>
    </section>
  );
}
