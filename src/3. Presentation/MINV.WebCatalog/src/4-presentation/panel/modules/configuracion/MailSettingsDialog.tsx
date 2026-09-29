// Módulo «Configuración» · correo de la empresa (`SaveMailSettingsCommand`), como el escritorio: servidor SMTP, puerto,
// conexión segura (STARTTLS o SSL), usuario, remitente y si se envía. La contraseña NUNCA se muestra: si ya hay una
// guardada solo se ofrece «Cambiar la contraseña» (sin marcarla viaja `null` y el servidor conserva la suya); el campo
// se borra al cerrar. «Usar la configuración de Gmail» completa smtp.gmail.com, 587 y STARTTLS.

import { Mail, Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, Form, FormGrid, NumberField, Switch, TextField } from '@/4-presentation/panel/kit';
import { applyGmail, mailFormOf, mailPayload, mailProblems, plainMessage, type MailData, type MailField, type MailForm } from './settings';

export type MailDialogMode = 'normal' | 'gmail';

export interface MailSettingsDialogProps {
  /** null = cerrado; 'gmail' abre con la configuración de Gmail ya puesta. */
  mode: MailDialogMode | null;
  mail: MailData | null;
  /** Nombre del remitente propuesto si todavía no hay correo. */
  companyName: string;
  onClose: () => void;
  onSaved: () => void;
}

export function MailSettingsDialog({ mode, mail, companyName, onClose, onSaved }: MailSettingsDialogProps) {
  const formId = useId();
  const save = useRpcCommand('SaveMailSettingsCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Correo guardado' });
  const [form, setForm] = useState<MailForm>(() => mailFormOf(mail, companyName));
  const [touched, setTouched] = useState(false);
  const [gmail, setGmail] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if ((mode !== null) !== wasOpen) {
    setWasOpen(mode !== null);
    if (mode !== null) {
      const initial = mailFormOf(mail, companyName);
      setForm(mode === 'gmail' ? applyGmail(initial) : initial);
      setGmail(mode === 'gmail');
      setTouched(false);
    }
  }
  const hasSavedPassword = mail?.hasPassword === true;
  const problems = mailProblems(form, hasSavedPassword);
  const show = (field: MailField) => (touched ? problems[field] : undefined);

  const set = <K extends keyof MailForm>(key: K, value: MailForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (save.error) save.reset();
  };

  const close = () => {
    save.reset();
    // La contraseña no queda en la memoria de la pantalla.
    setForm((current) => ({ ...current, password: '' }));
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await save.run(mailPayload(form));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  const showPassword = !hasSavedPassword || form.changePassword;

  return (
    <Dialog
      open={mode !== null}
      onClose={close}
      dismissible={!save.sending}
      size="lg"
      title="Correo de la empresa"
      description="El servidor con el que M-INV envía las facturas y las confirmaciones de las reservas."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending}>
            Guardar el correo
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        {gmail ? (
          <Alert tone="info" title="Gmail">
            Servidor smtp.gmail.com, puerto 587 y STARTTLS. En «Usuario» va su correo de Gmail y en la contraseña, una «contraseña de aplicación» de 16
            letras (sin espacios), no la de la cuenta: la crea el dueño de la cuenta de Google con la verificación en dos pasos activada.
          </Alert>
        ) : (
          <Button
            variant="subtle"
            leftIcon={<Mail />}
            onClick={() => {
              setForm((current) => applyGmail(current));
              setGmail(true);
            }}
          >
            Usar la configuración de Gmail
          </Button>
        )}
        <FormGrid>
          <TextField
            label="Servidor SMTP"
            value={form.host}
            onChange={(value) => set('host', value)}
            error={show('host')}
            placeholder="smtp.suempresa.com"
            maxLength={200}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            required
            data-autofocus
          />
          <NumberField label="Puerto" value={form.port} onChange={(value) => set('port', value)} error={show('port')} hint="587 con STARTTLS · 465 con SSL." required />
          <Checkbox
            className="sm:col-span-2"
            label="Conexión segura (STARTTLS o SSL)"
            description="Recomendado: sin cifrar, la contraseña viaja a la vista."
            checked={form.useSsl}
            onChange={(value) => set('useSsl', value)}
          />
          <TextField
            label="Usuario"
            className="sm:col-span-2"
            value={form.userName}
            onChange={(value) => set('userName', value)}
            error={show('userName')}
            hint="Casi siempre, el mismo correo."
            maxLength={200}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            optional
          />
          {hasSavedPassword && (
            <Checkbox
              className="sm:col-span-2"
              label="Cambiar la contraseña"
              description="Hay una contraseña guardada (cifrada) que nunca se muestra. Márquelo solo para reemplazarla."
              checked={form.changePassword}
              onChange={(value) => {
                set('changePassword', value);
                if (!value) set('password', '');
              }}
            />
          )}
          {showPassword && (
            <TextField
              label={hasSavedPassword ? 'Contraseña nueva' : 'Contraseña'}
              className="sm:col-span-2"
              type="password"
              value={form.password}
              onChange={(value) => set('password', value)}
              error={show('password')}
              hint="Se guarda cifrada y nunca se vuelve a mostrar."
              autoComplete="new-password"
              autoCapitalize="none"
              spellCheck={false}
              required={hasSavedPassword}
              optional={!hasSavedPassword}
            />
          )}
          <TextField
            label="Remitente (correo)"
            type="email"
            value={form.fromAddress}
            onChange={(value) => set('fromAddress', value)}
            error={show('fromAddress')}
            placeholder="facturas@suempresa.com"
            maxLength={254}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            required
          />
          <TextField label="Remitente (nombre)" value={form.fromName} onChange={(value) => set('fromName', value)} error={show('fromName')} maxLength={100} required />
          <Switch
            className="sm:col-span-2"
            label="Enviar correos con este servidor"
            description="Desactivado, las facturas no se envían por correo y las reservas usan el servidor de correo de M-INV (si lo configuró el instalador)."
            checked={form.enabled}
            onChange={(value) => set('enabled', value)}
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
