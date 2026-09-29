// Módulo «Integraciones» · registrar un webhook (`CreateWebhookCommand`), como el editor del escritorio: la dirección https
// que recibirá los avisos, una descripción, los eventos con casillas (se marca «Venta cobrada») y la sucursal. M-INV
// firma cada aviso (cabecera X-MINV-Signature) con un secreto que el servidor devuelve UNA sola vez: la pantalla lo
// muestra con «Copiar» (SecretDialog).

import { Webhook } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, FieldGroup, Form, SelectField, TextArea, TextField, type SelectOption } from '@/4-presentation/panel/kit';
import { createWebhookPayload, initialWebhookForm, webhookFormProblems, type CreatedHook, type NamedOption, type WebhookForm } from './integrations';

export interface CreateWebhookDialogProps {
  open: boolean;
  events: readonly NamedOption[];
  branches: readonly SelectOption[];
  onClose: () => void;
  onCreated: (created: CreatedHook) => void;
}

export function CreateWebhookDialog({ open, events, branches, onClose, onCreated }: CreateWebhookDialogProps) {
  const formId = useId();
  const create = useRpcCommand('CreateWebhookCommand', { notifyError: false, success: 'Webhook registrado' });
  const [form, setForm] = useState<WebhookForm>(() => initialWebhookForm(events));
  const [touched, setTouched] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setForm(initialWebhookForm(events));
      setTouched(false);
    }
  }
  const problems = webhookFormProblems(form);

  const set = <K extends keyof WebhookForm>(key: K, value: WebhookForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (create.error) create.reset();
  };

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await create.run(createWebhookPayload(form, events));
    if (!outcome.ok) return;
    onCreated(outcome.result);
    close();
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!create.sending}
      size="lg"
      title="Nuevo webhook"
      description="M-INV avisará a su sistema, por internet y con firma, cada vez que pase uno de los eventos elegidos."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Webhook />} loading={create.sending}>
            Registrar el webhook
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        <TextField
          label="Dirección (URL) que recibe los avisos"
          type="url"
          inputMode="url"
          value={form.url}
          onChange={(value) => set('url', value)}
          error={touched ? problems.url : undefined}
          hint="Con https. La da el sistema que recibe los avisos (su tienda o su ERP)."
          maxLength={500}
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          inputClassName="font-mono text-sm"
          required
          data-autofocus
        />
        <TextArea
          label="Descripción"
          value={form.description}
          onChange={(value) => set('description', value)}
          error={touched ? problems.description : undefined}
          maxLength={200}
          rows={2}
          optional
        />
        <FieldGroup label="Eventos" required error={touched ? problems.events : undefined} hint="Qué le avisa M-INV a su sistema.">
          {events.map((event) => (
            <Checkbox
              key={event.code}
              label={event.name}
              description={event.code}
              checked={form.events.includes(event.code)}
              onChange={(checked) => set('events', checked ? [...form.events, event.code] : form.events.filter((item) => item !== event.code))}
            />
          ))}
        </FieldGroup>
        <SelectField
          label="Sucursal"
          allLabel="Todas mis sucursales"
          value={form.branch}
          onChange={(value) => set('branch', value)}
          options={branches}
          hint="Solo los eventos de esa sucursal (quien trabaja en algunas sucursales recibe solo los de su sucursal activa)."
        />
      </Form>
    </Dialog>
  );
}
