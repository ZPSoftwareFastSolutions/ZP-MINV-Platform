// Módulo «Caja» · enviar la factura por correo al comprador (`SendFiscalDocumentEmailCommand`: el servidor envía el XML
// y el PDF). El correo se propone con el que escribió el cajero en los datos de la factura; el error del servidor (correo
// de la empresa sin configurar, documento sin validar…) se muestra dentro del diálogo.

import { Mail } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextField } from '@/4-presentation/panel/kit';
import { isEmail, plainMessage } from './fiscal';

export interface EmailTarget {
  documentId: string;
  /** Número de la factura (para el título). */
  number: number | null;
  email: string | null;
}

export interface EmailInvoiceDialogProps {
  target: EmailTarget | null;
  onClose: () => void;
}

export function EmailInvoiceDialog({ target, onClose }: EmailInvoiceDialogProps) {
  const formId = useId();
  const [email, setEmail] = useState(target?.email ?? '');
  const [touched, setTouched] = useState(false);
  const [shown, setShown] = useState<EmailTarget | null>(target);
  if (target && target !== shown) {
    setShown(target);
    setEmail(target.email ?? '');
    setTouched(false);
  }
  const command = useRpcCommand('SendFiscalDocumentEmailCommand', {
    success: (message) => plainMessage(message) || 'Factura enviada por correo',
    notifyError: false,
  });
  const problem = isEmail(email) ? null : 'Escriba un correo válido, por ejemplo nombre@correo.com.';

  const close = () => {
    command.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!shown || problem) return;
    const outcome = await command.run({ documentId: shown.documentId, email: email.trim() });
    if (outcome.ok) close();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!command.sending}
      title={shown?.number != null ? `Enviar la factura N° ${shown.number} por correo` : 'Enviar la factura por correo'}
      description="Se envían el XML y el PDF de la factura al comprador."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={command.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Mail />} loading={command.sending}>
            Enviar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={command.errorText} busy={command.sending}>
        <TextField
          label="Correo del comprador"
          type="email"
          value={email}
          onChange={(value) => {
            setEmail(value);
            command.reset();
          }}
          error={touched && problem ? problem : undefined}
          autoComplete="off"
          required
          data-autofocus
        />
      </Form>
    </Dialog>
  );
}
