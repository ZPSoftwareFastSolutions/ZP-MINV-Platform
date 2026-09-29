// Módulo «Configuración» · conexión con el SIN de un ambiente (`SaveSiatProfileCommand`), como el escritorio: la dirección
// de cada uno de los seis servicios, el namespace, la consulta por QR, el tiempo de espera y el TOKEN DELEGADO. El token
// solo se escribe: el servidor lo cifra y nunca lo devuelve, así que el campo empieza vacío («vacío = conservar el
// guardado») y se borra al cerrar. «Completar las direcciones» arma las seis a partir de la dirección base (el patrón
// conocido de los servicios del SIN; confírmelo con el WSDL del ambiente).

import { Save, WandSparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, FormGrid, NumberField, TextField } from '@/4-presentation/panel/kit';
import {
  ENDPOINT_FIELDS,
  endpointsFromBase,
  environmentName,
  knownNamespace,
  plainMessage,
  profileFormOf,
  profileOf,
  profilePayload,
  profileProblems,
  tokenStatusText,
  type ProfileField,
  type ProfileForm,
  type SiatSettingsData,
} from './settings';

export interface SiatProfileDialogProps {
  /** Ambiente cuya conexión se edita; null = cerrado. */
  environment: number | null;
  view: SiatSettingsData;
  onClose: () => void;
  onSaved: () => void;
}

export function SiatProfileDialog({ environment, view, onClose, onSaved }: SiatProfileDialogProps) {
  const formId = useId();
  const save = useRpcCommand('SaveSiatProfileCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Conexión guardada' });
  const [shownEnvironment, setShownEnvironment] = useState<number | null>(null);
  const [form, setForm] = useState<ProfileForm>(() => profileFormOf(undefined, ''));
  const [base, setBase] = useState('');
  const [baseError, setBaseError] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if ((environment !== null) !== wasOpen) {
    setWasOpen(environment !== null);
    if (environment !== null) {
      setShownEnvironment(environment);
      setForm(profileFormOf(profileOf(view, environment), knownNamespace(view)));
      setBase('');
      setBaseError(null);
      setTouched(false);
    }
  }
  const profile = shownEnvironment === null ? undefined : profileOf(view, shownEnvironment);
  const problems = profileProblems(form);
  const show = (field: ProfileField) => (touched ? problems[field] : undefined);

  const set = <K extends keyof ProfileForm>(key: K, value: ProfileForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (save.error) save.reset();
  };

  const fillFromBase = () => {
    const endpoints = endpointsFromBase(base);
    if (!endpoints) {
      setBaseError('Escriba la dirección base completa, con https (http solo en este mismo equipo).');
      return;
    }
    setBaseError(null);
    set('endpoints', endpoints);
  };

  const close = () => {
    save.reset();
    // El token no queda en la memoria de la pantalla.
    setForm((current) => ({ ...current, token: '' }));
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (shownEnvironment === null || Object.keys(problems).length > 0) return;
    const outcome = await save.run(profilePayload(shownEnvironment, form));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  return (
    <Dialog
      open={environment !== null}
      onClose={close}
      dismissible={!save.sending}
      size="xl"
      title="Conexión con el SIN"
      description={shownEnvironment === null ? undefined : `Ambiente ${environmentName(shownEnvironment).toLowerCase()}`}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending}>
            Guardar la conexión
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <div className="space-y-2 rounded-xl border border-border bg-surface-2 p-4">
          <TextField
            label="Dirección base de los servicios"
            value={base}
            onChange={(value) => {
              setBase(value);
              setBaseError(null);
            }}
            error={baseError ?? undefined}
            hint="Escríbala completa, con https al inicio. Piloto del SIN: pilotosiatservicios.impuestos.gob.bo · producción: siatrest.impuestos.gob.bo · simulador de este equipo: localhost, puerto 5095 (con http)."
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            inputClassName="font-mono text-sm"
            optional
          />
          <Button variant="subtle" leftIcon={<WandSparkles />} onClick={fillFromBase}>
            Completar las direcciones
          </Button>
        </div>

        {ENDPOINT_FIELDS.map((field) => (
          <TextField
            key={field.key}
            label={field.label}
            value={form.endpoints[field.key]}
            onChange={(value) => set('endpoints', { ...form.endpoints, [field.key]: value })}
            error={show(field.key)}
            maxLength={400}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            inputClassName="font-mono text-xs"
            required
          />
        ))}
        <FormGrid>
          <TextField
            label="Namespace de los servicios"
            value={form.namespace}
            onChange={(value) => set('namespace', value)}
            error={show('namespace')}
            maxLength={200}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            inputClassName="font-mono text-xs"
            required
          />
          <NumberField label="Tiempo de espera" value={form.timeout} onChange={(value) => set('timeout', value)} error={show('timeout')} unit="s" hint="De 3 a 120 segundos." required />
          <TextField
            label="Dirección de la consulta por QR"
            className="sm:col-span-2"
            value={form.qrBaseUrl}
            onChange={(value) => set('qrBaseUrl', value)}
            error={show('qrBaseUrl')}
            hint="La de producción la comunica el SIN al terminar la autorización."
            maxLength={400}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            inputClassName="font-mono text-xs"
            required
          />
          <TextField
            label="Token delegado nuevo"
            type="password"
            value={form.token}
            onChange={(value) => set('token', value)}
            error={show('token')}
            hint={`${tokenStatusText(profile)} Vacío = conservar el guardado.`}
            autoComplete="new-password"
            autoCapitalize="none"
            spellCheck={false}
            optional
          />
          <TextField
            label="Vigencia del token"
            type="date"
            value={form.tokenValidUntil}
            onChange={(value) => set('tokenValidUntil', value)}
            hint="La fecha «Hasta» que eligió en el Portal SIAT."
            optional
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
