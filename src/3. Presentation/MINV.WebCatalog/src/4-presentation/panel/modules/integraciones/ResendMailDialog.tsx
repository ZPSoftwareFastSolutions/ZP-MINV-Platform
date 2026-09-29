// Módulo «Integraciones» · reenviar la confirmación de una reserva (`ResendReservationMailCommand`): a su correo de
// contacto o a otro. El servidor solo lo acepta si la reserva sigue vigente, cancela los correos pendientes de esa
// reserva (el reenvío los reemplaza) y respeta los topes de 24 horas; nada se envía en el momento: queda en la cola y lo
// envía el despachador (regla P-06).

import { Send } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextField } from '@/4-presentation/panel/kit';
import { resendPayload, resendProblems, type ResendForm } from './integrations';

export interface ResendTarget {
  /** Número de la reserva ('' = escribirlo en el diálogo). */
  number: string;
  /** Destinatario del último correo (para mostrarlo). */
  recipient?: string;
}

export interface ResendMailDialogProps {
  /** null = cerrado. */
  target: ResendTarget | null;
  onClose: () => void;
  onDone: () => void;
}

export function ResendMailDialog({ target, onClose, onDone }: ResendMailDialogProps) {
  const formId = useId();
  const resend = useRpcCommand('ResendReservationMailCommand', { notifyError: false, success: (mail) => `Confirmación de ${mail.reservation} en cola para ${mail.recipient}` });
  const [shown, setShown] = useState<ResendTarget | null>(null);
  const [form, setForm] = useState<ResendForm>({ number: '', email: '' });
  const [touched, setTouched] = useState(false);
  if (target && target !== shown) {
    setShown(target);
    setForm({ number: target.number, email: '' });
    setTouched(false);
  }
  const fixed = Boolean(shown?.number);
  const problems = resendProblems(form);

  const set = (key: keyof ResendForm, value: string) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (resend.error) resend.reset();
  };

  const close = () => {
    resend.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await resend.run(resendPayload(form));
    if (!outcome.ok) return;
    onDone();
    close();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!resend.sending}
      title="Reenviar la confirmación"
      description={fixed ? `Reserva ${shown?.number}${shown?.recipient ? ` · último envío a ${shown.recipient}` : ''}` : 'De una reserva vigente de la tienda web o del mostrador.'}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={resend.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Send />} loading={resend.sending}>
            Reenviar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={resend.errorText} busy={resend.sending}>
        {!fixed && (
          <TextField
            label="Número de la reserva"
            value={form.number}
            onChange={(value) => set('number', value)}
            error={touched ? problems.number : undefined}
            placeholder="ARM-WEB-000001"
            maxLength={40}
            autoCapitalize="characters"
            spellCheck={false}
            required
            data-autofocus
          />
        )}
        <TextField
          label="Enviar a otro correo"
          type="email"
          value={form.email}
          onChange={(value) => set('email', value)}
          error={touched ? problems.email : undefined}
          hint="Vacío = al correo de contacto de la reserva."
          maxLength={254}
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          optional
        />
        <p className="text-sm text-text-muted">
          Solo se reenvía la confirmación de una reserva vigente. Los correos que seguían pendientes de esa reserva se cancelan (el cliente no recibe dos) y
          cada dirección recibe como máximo 3 correos en 24 horas.
        </p>
      </Form>
    </Dialog>
  );
}
