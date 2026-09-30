// Módulo «Órdenes de compra» · registrar la factura del proveedor de una recepción (`RegisterSupplierInvoiceCommand`),
// como el diálogo del escritorio: número, CUF o código de autorización, fecha (no posterior a hoy), importe total (se
// PROPONE recepción ÷ 0,87, porque la recepción entró al costo neto de IVA; se escribe el que dice la factura), descuentos,
// importe no sujeto a crédito fiscal, tipo de compra y código de control. El crédito fiscal y el asiento los calcula el
// servidor: su mensaje se muestra al terminar. El error se muestra DENTRO del diálogo.
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { FileCheck2 } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, MoneyField, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, laPazToday } from '@/4-presentation/panel/lib';
import {
  LIMITS,
  PURCHASE_TYPES,
  invoiceDraftFor,
  invoicePayload,
  invoiceProblems,
  plainMessage,
  proposedInvoiceTotal,
  type InvoiceDraft,
  type PendingReceiptRecord,
} from './purchasing';

export interface SupplierInvoiceDialogProps {
  /** La recepción que se factura (null = cerrado). */
  target: PendingReceiptRecord | null;
  onClose: () => void;
  onRegistered: () => void;
}

export function SupplierInvoiceDialog({ target, onClose, onRegistered }: SupplierInvoiceDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [today] = useState(() => laPazToday());
  const [shown] = useState(target);
  const receipt = target ?? shown;
  const [draft, setDraft] = useState<InvoiceDraft | null>(() => (target ? invoiceDraftFor(target) : null));
  const [touched, setTouched] = useState(false);
  const register = useRpcCommand('RegisterSupplierInvoiceCommand', { notifyError: false });

  if (!receipt || !draft) return null;
  const problems = invoiceProblems(draft, today);
  const shownError = (field: keyof InvoiceDraft) => (touched ? problems[field] : undefined);

  const change = <K extends keyof InvoiceDraft>(field: K, value: InvoiceDraft[K]) => {
    setDraft((current) => (current ? { ...current, [field]: value } : current));
    if (register.error) register.reset();
  };

  const close = () => {
    register.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await register.run(invoicePayload(receipt.receiptNumber, draft));
    if (outcome.ok) {
      notify.success('Factura del proveedor registrada', plainMessage(outcome.result));
      onRegistered();
      close();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!register.sending}
      size="lg"
      title={`Factura del proveedor · recepción ${receipt.receiptNumber}`}
      description={`${receipt.supplier} · recibida el ${formatDate(receipt.receivedOn)} · ${formatMoney(receipt.total)} al costo neto`}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={register.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FileCheck2 />} loading={register.sending}>
            Registrar factura
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={register.errorText} busy={register.sending}>
        <Alert tone="info">
          La recepción entró al costo neto de IVA ({formatMoney(receipt.total)}): se propone el importe con IVA que le corresponde,{' '}
          {formatMoney(proposedInvoiceTotal(receipt.total))} = recepción ÷ 0,87. Escriba el importe que dice la factura del proveedor; el crédito fiscal y
          el asiento los calcula el sistema.
        </Alert>
        <FormGrid>
          <TextField
            label="Número de factura"
            value={draft.invoiceNumber}
            onChange={(value) => change('invoiceNumber', value)}
            maxLength={LIMITS.invoiceNumber}
            autoComplete="off"
            error={shownError('invoiceNumber')}
            required
            data-autofocus
          />
          <TextField
            label="Fecha de la factura"
            type="date"
            value={draft.invoiceDate}
            max={today}
            onChange={(value) => change('invoiceDate', value)}
            error={shownError('invoiceDate')}
            required
          />
          <TextField
            label="CUF o código de autorización"
            value={draft.authorizationCode}
            onChange={(value) => change('authorizationCode', value)}
            maxLength={LIMITS.authorizationCode}
            autoComplete="off"
            spellCheck={false}
            error={shownError('authorizationCode')}
            className="sm:col-span-2"
            required
          />
          <MoneyField label="Importe total" value={draft.totalAmount} onChange={(value) => change('totalAmount', value)} error={shownError('totalAmount')} required />
          <MoneyField label="Descuentos" value={draft.discounts} onChange={(value) => change('discounts', value)} error={shownError('discounts')} optional />
          <MoneyField
            label="Importe no sujeto a crédito fiscal"
            value={draft.notSubjectToVat}
            onChange={(value) => change('notSubjectToVat', value)}
            error={shownError('notSubjectToVat')}
            optional
          />
          <SelectField
            label="Tipo de compra"
            allLabel={false}
            value={draft.purchaseType}
            onChange={(value) => change('purchaseType', value)}
            options={PURCHASE_TYPES}
            error={shownError('purchaseType')}
            required
          />
          <TextField
            label="Código de control"
            value={draft.controlCode}
            onChange={(value) => change('controlCode', value.toUpperCase())}
            maxLength={LIMITS.controlCode}
            autoComplete="off"
            hint="Solo en facturas antiguas que lo traen."
            error={shownError('controlCode')}
            optional
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
