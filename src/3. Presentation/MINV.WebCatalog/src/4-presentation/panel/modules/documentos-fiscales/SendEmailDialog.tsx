// Módulo «Documentos fiscales» · enviar el documento por correo (`SendFiscalDocumentEmailCommand`, permiso «emitir
// facturas»): el XML y el PDF al correo del comprador (el del documento viene escrito) o al que se indique. La entrega
// queda en el detalle del documento. El error del servidor se muestra dentro del diálogo.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Mail } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextField, useNotify } from '@/4-presentation/panel/kit';
import { documentTitle, isEmail, plainMessage, type DocumentRecord } from './fiscal';

export interface SendEmailDialogProps {
  /** null = cerrado. */
  target: DocumentRecord | null;
  onClose: () => void;
  onDone: () => void;
}

export function SendEmailDialog({ target, onClose, onDone }: SendEmailDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const documentId = current?.id ?? '';
  // El correo del comprador está en el detalle del documento.
  const detail = useRpcQuery('GetFiscalDocumentQuery', { documentId }, { enabled: open && documentId !== '' });
  const [email, setEmail] = useState('');
  const [filledFrom, setFilledFrom] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);
  const send = useRpcCommand('SendFiscalDocumentEmailCommand', { notifyError: false });

  if (detail.data && filledFrom !== detail.data.row.id) {
    setFilledFrom(detail.data.row.id);
    if (email.trim().length === 0 && detail.data.buyerEmail) setEmail(detail.data.buyerEmail);
  }

  if (!current) return null;
  const problem = email.trim().length === 0 ? 'Escriba el correo del comprador.' : isEmail(email) ? undefined : 'Escriba un correo válido, por ejemplo nombre@correo.com.';

  const submit = async () => {
    setTouched(true);
    if (problem) return;
    const outcome = await send.run({ documentId: current.id, email: email.trim() });
    if (!outcome.ok) return;
    notify.success('Correo enviado', plainMessage(outcome.result));
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!send.sending}
      title="Enviar por correo"
      description={`Se envían el XML y el PDF de ${documentTitle(current).toLocaleLowerCase('es')}.`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={send.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Mail />} loading={send.sending}>
            Enviar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={send.errorText} busy={send.sending}>
        <TextField
          label="Correo del comprador"
          type="email"
          value={email}
          onChange={(value) => {
            setEmail(value);
            if (send.error) send.reset();
          }}
          error={touched ? problem : undefined}
          hint={detail.data?.buyerEmail ? `El documento tiene el correo ${detail.data.buyerEmail}.` : 'El documento no tiene correo: escriba a cuál enviarlo.'}
          autoComplete="off"
          maxLength={254}
          required
          data-autofocus
        />
      </Form>
    </Dialog>
  );
}
