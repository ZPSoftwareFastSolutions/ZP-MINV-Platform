// Módulo «Reservas» · REENVIAR la confirmación de una reserva vigente (`ResendReservationMailCommand`): encola un correo
// nuevo al correo de la reserva o a otro que se indique; los que seguían pendientes se cancelan (el cliente no recibe dos).
// No envía nada en el momento: lo envía el despachador de correos del servidor (regla P-06). El correo del cliente vive solo
// en el estado del diálogo y no aparece en los avisos.

import { MailPlus } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextField } from '@/4-presentation/panel/kit';
import { ErrorDetails } from './ErrorDetails';
import { CART_LIMITS, isEmail, type MailRecord } from './reservations';

/** La reserva cuya confirmación se reenvía. */
export interface ResendTarget {
  number: string;
  /** Correo de contacto de la reserva (null si no dejó uno). */
  contactEmail: string | null;
}

export interface ResendMailDialogProps {
  /** null = diálogo cerrado. */
  target: ResendTarget | null;
  onClose: () => void;
  /** El correo que quedó en la cola. */
  onQueued: (mail: MailRecord) => void;
}

export function ResendMailDialog({ target, onClose, onQueued }: ResendMailDialogProps) {
  const formId = useId();
  const resend = useRpcCommand('ResendReservationMailCommand', {
    success: (mail) => `Correo de la reserva ${mail.reservation} en la cola de envío`,
    notifyError: false,
  });
  const [email, setEmail] = useState('');
  const [touched, setTouched] = useState(false);
  const [shown, setShown] = useState<ResendTarget | null>(target);
  if (target && target !== shown) setShown(target);

  const typed = email.trim();
  const problem = typed
    ? typed.length > CART_LIMITS.email || !isEmail(typed)
      ? 'Escriba un correo válido, por ejemplo nombre@correo.com.'
      : null
    : shown && !shown.contactEmail
      ? 'La reserva no tiene correo de contacto: indique a qué correo enviar la confirmación.'
      : null;

  const close = () => {
    setEmail('');
    setTouched(false);
    resend.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!target || problem) return;
    const outcome = await resend.run({ number: target.number, email: typed || null });
    if (outcome.ok) {
      onQueued(outcome.result);
      close();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!resend.sending}
      title="Reenviar el correo de la reserva"
      description={shown ? `Reserva ${shown.number}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={resend.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<MailPlus />} loading={resend.sending}>
            Reenviar correo
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={resend.errorText && <ErrorDetails text={resend.errorText} details={resend.error?.errors} />} busy={resend.sending}>
        <p className="text-sm text-text-muted">
          Se envía otra vez la confirmación con el número, los productos y el plazo para recoger. Si había un correo pendiente de esta reserva, se
          reemplaza: el cliente no recibe dos.
        </p>
        <TextField
          label="Enviar a"
          type="email"
          inputMode="email"
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          value={email}
          onChange={(value) => {
            setEmail(value);
            if (resend.error) resend.reset();
          }}
          placeholder={shown?.contactEmail ?? 'nombre@correo.com'}
          hint={shown?.contactEmail ? `Déjelo vacío para usar el correo de la reserva (${shown.contactEmail}).` : 'La reserva no tiene correo de contacto.'}
          optional={Boolean(shown?.contactEmail)}
          required={!shown?.contactEmail}
          maxLength={CART_LIMITS.email}
          error={touched ? problem : undefined}
          data-autofocus
        />
      </Form>
    </Dialog>
  );
}
