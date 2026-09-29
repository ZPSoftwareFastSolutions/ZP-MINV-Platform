// Módulo «Ventas» · reenviar la factura por correo (`SendFiscalDocumentEmailCommand`, permiso «emitir facturas»): se
// envían el XML y el PDF. El correo es opcional: vacío = el del comprador registrado en la factura (como el escritorio);
// si la factura no tiene correo, el servidor lo pide y el error se muestra aquí, sin cerrar el diálogo.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { Mail } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextField, useNotify } from '@/4-presentation/panel/kit';
import { emailProblem, plainMessage, type SaleFiscalRecord } from './sales';

/** La factura de una venta (reimprimir o enviar). */
export interface InvoiceTarget {
  invoiceNumber: string;
  customer: string;
  fiscal: SaleFiscalRecord;
}

export interface SendInvoiceDialogProps {
  /** null = cerrado. */
  target: InvoiceTarget | null;
  onClose: () => void;
}

export function SendInvoiceDialog({ target, onClose }: SendInvoiceDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const [email, setEmail] = useState('');
  const [touched, setTouched] = useState(false);
  const send = useRpcCommand('SendFiscalDocumentEmailCommand', { notifyError: false });

  const current = target ?? shown;
  if (!current) return null;
  const problem = emailProblem(email);

  const submit = async () => {
    setTouched(true);
    if (problem) return;
    const outcome = await send.run({ documentId: current.fiscal.documentId, email: email.trim() || null });
    if (outcome.ok) {
      notify.success('Factura enviada por correo', plainMessage(outcome.result));
      onClose();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!send.sending}
      title={`Enviar la factura N° ${current.fiscal.number} por correo`}
      description={`Venta ${current.invoiceNumber} · ${current.customer}`}
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
        <p className="text-sm text-text-muted">Se envían el XML y el PDF (la representación gráfica) de la factura.</p>
        <TextField
          label="Correo"
          type="email"
          inputMode="email"
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          value={email}
          onChange={(value) => {
            setEmail(value);
            if (send.error) send.reset();
          }}
          maxLength={150}
          optional
          hint="Si lo deja vacío, se envía al correo del comprador registrado en la factura."
          error={touched && problem ? problem : undefined}
          data-autofocus
        />
      </Form>
    </Dialog>
  );
}
