// «Mis datos»: nombre, teléfono y documento para la factura (opcional) del cliente de la sesión. El correo se muestra
// pero no se cambia desde la web. Estados de carga y error con «Reintentar»; errores por campo antes de enviar y el
// mensaje del servidor si rechaza el cambio.

import { Save, Undo2, UserRound } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { DOCUMENT_LIMITS, DOCUMENT_TYPES, documentType, parseDocumentType } from '@/1-domain/account/documents';
import type { CustomerAccount } from '@/1-domain/account/types';
import { toAccountForm, toAccountUpdate, validateAccountForm, type AccountFormErrors, type AccountFormInput } from '@/1-domain/account/validation';
import { asWebApiError, describeWebApiError, type WebApiError } from '@/1-domain/auth/errors';
import { NAME_MAX_LENGTH, PHONE_MAX_LENGTH } from '@/1-domain/auth/validation';
import { AttemptKey } from '@/2-application';
import { ErrorState, LoadingState } from '@/4-presentation/components/feedback/AsyncState';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { SelectField, TextField } from '@/4-presentation/components/ui/TextField';
import { useAsyncData } from '@/4-presentation/hooks/useAsyncData';
import { useAccount } from '@/4-presentation/hooks/useRpc';
import { useSession } from '@/4-presentation/hooks/useSession';
import { useToast } from '@/4-presentation/hooks/useToast';

const DOCUMENT_OPTIONS = [{ value: '', label: 'Sin documento' }, ...DOCUMENT_TYPES.map((type) => ({ value: String(type.code), label: type.label }))];

interface ProfileFormProps {
  account: CustomerAccount;
  onSaved: (account: CustomerAccount) => void;
}

function ProfileForm({ account, onSaved }: ProfileFormProps) {
  const accountCases = useAccount();
  const { refresh } = useSession();
  const toast = useToast();
  const [form, setForm] = useState<AccountFormInput>(() => toAccountForm(account));
  const [errors, setErrors] = useState<AccountFormErrors>({});
  const [touched, setTouched] = useState(false);
  const [failure, setFailure] = useState<WebApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const [attempt] = useState(() => new AttemptKey());

  const type = documentType(parseDocumentType(form.documentType));
  const original = toAccountForm(account);
  const dirty = (Object.keys(original) as (keyof AccountFormInput)[]).some((field) => original[field] !== form[field]);

  const update = (field: keyof AccountFormInput, value: string) => {
    const next = { ...form, [field]: value };
    // Al cambiar el tipo, lo que ya no corresponde se limpia (el complemento solo va con CI).
    if (field === 'documentType') {
      const chosen = documentType(parseDocumentType(value));
      if (!chosen) next.documentNumber = '';
      if (!chosen?.complement) next.complement = '';
    }
    setForm(next);
    if (touched) setErrors(validateAccountForm(next));
  };

  const reset = () => {
    setForm(original);
    setErrors({});
    setTouched(false);
    setFailure(null);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const validation = validateAccountForm(form);
    setTouched(true);
    setErrors(validation);
    if (Object.keys(validation).length > 0) return;
    setFailure(null);
    setSaving(true);
    try {
      const saved = await attempt.run((requestId) => accountCases.save(toAccountUpdate(form), { requestId }));
      setForm(toAccountForm(saved));
      setTouched(false);
      onSaved(saved);
      toast.notify({ tone: 'success', title: 'Datos guardados', description: 'Tus datos quedaron actualizados.' });
      // El nombre de la cabecera sale de la sesión: se vuelve a leer.
      void refresh();
    } catch (error) {
      setFailure(asWebApiError(error));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} noValidate aria-busy={saving || undefined} className="space-y-6">
      {failure && (
        <Alert tone="danger" title="No pudimos guardar tus datos">
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

      <fieldset className="space-y-4" disabled={saving}>
        <legend className="text-xs font-semibold uppercase tracking-wide text-accent">Contacto</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Nombre y apellido"
            name="name"
            autoComplete="name"
            required
            maxLength={NAME_MAX_LENGTH}
            value={form.name}
            onChange={(event) => update('name', event.target.value)}
            error={errors.name}
            className="sm:col-span-2"
          />
          <TextField label="Correo" type="email" name="email" value={account.email} readOnly hint="Es el correo con el que ingresás. Para cambiarlo, pedilo en la tienda." />
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
          />
        </div>
      </fieldset>

      <fieldset className="space-y-4" disabled={saving}>
        <legend className="text-xs font-semibold uppercase tracking-wide text-accent">Documento para la factura</legend>
        <p className="text-sm text-text-muted">Es opcional. Si lo cargás, la tienda lo usa para emitir la factura a tu nombre cuando retires tu reserva.</p>
        <div className="grid gap-4 sm:grid-cols-3">
          <SelectField
            label="Tipo de documento"
            name="documentType"
            options={DOCUMENT_OPTIONS}
            value={form.documentType}
            onChange={(event) => update('documentType', event.target.value)}
            error={errors.documentType}
          />
          <TextField
            label="Número de documento"
            name="documentNumber"
            inputMode={type?.numeric ? 'numeric' : 'text'}
            autoComplete="off"
            maxLength={DOCUMENT_LIMITS.numberMaxLength}
            value={form.documentNumber}
            onChange={(event) => update('documentNumber', event.target.value)}
            error={errors.documentNumber}
            hint={type?.numeric ? 'Solo dígitos.' : undefined}
            disabled={!type}
          />
          <TextField
            label="Complemento"
            name="complement"
            autoComplete="off"
            optional
            maxLength={DOCUMENT_LIMITS.complementMaxLength}
            value={form.complement}
            onChange={(event) => update('complement', event.target.value)}
            error={errors.complement}
            hint="Solo con cédula de identidad."
            disabled={!type?.complement}
          />
        </div>
      </fieldset>

      <div className="flex flex-wrap gap-2">
        <Button type="submit" variant="brand" leftIcon={<Save />} loading={saving} disabled={!dirty}>
          {saving ? 'Guardando…' : 'Guardar cambios'}
        </Button>
        <Button variant="ghost" leftIcon={<Undo2 />} onClick={reset} disabled={saving || !dirty}>
          Descartar cambios
        </Button>
      </div>
    </form>
  );
}

export function ProfileSection() {
  const accountCases = useAccount();
  const { state, reload, reloading, update } = useAsyncData((signal) => accountCases.load({ signal }));

  if (state.status === 'loading') return <LoadingState label="Cargando tus datos…" rows={2} />;
  if (state.status === 'error') {
    return <ErrorState title="No pudimos cargar tus datos" message={describeWebApiError(state.error)} retrying={reloading} onRetry={reload} />;
  }

  return (
    <Card as="section" aria-labelledby="datos-titulo" padding="md" className="max-w-3xl">
      <h2 id="datos-titulo" className="mb-4 flex items-center gap-2 text-2xl">
        <UserRound aria-hidden="true" className="size-6 text-accent" />
        Mis datos
      </h2>
      <ProfileForm account={state.data} onSaved={(saved) => update(() => saved)} />
    </Card>
  );
}
