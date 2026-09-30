// Módulo «Libros fiscales» · registrar la factura del proveedor de una recepción (`RegisterSupplierInvoiceCommand`,
// permiso «gestionar compras»), como `SupplierInvoiceDialog` del escritorio: entra al libro de compras con su crédito fiscal.
// Las compras entran al costo NETO de IVA, así que se propone el importe con IVA que corresponde (recepción ÷ 0,87); se
// escribe el de la factura. La base y el crédito estimados se ven antes de registrar (el servidor calcula los de verdad).
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { FileCheck } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, FormGrid, MoneyField, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney } from '@/4-presentation/panel/lib';
import {
  PURCHASE_TYPES,
  estimatedCredit,
  plainMessage,
  proposedInvoiceTotal,
  supplierInvoiceErrors,
  supplierInvoicePayload,
  type PendingReceiptData,
  type SupplierInvoiceDraft,
} from './books';

export interface SupplierInvoiceDialogProps {
  /** null = cerrado. */
  target: PendingReceiptData | null;
  onClose: () => void;
  onDone: () => void;
}

export function SupplierInvoiceDialog({ target, onClose, onDone }: SupplierInvoiceDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const [draft, setDraft] = useState<SupplierInvoiceDraft>(() => ({
    invoiceNumber: '',
    authorizationCode: '',
    invoiceDate: current?.receivedOn ?? '',
    total: current ? proposedInvoiceTotal(current.total) : null,
    discounts: null,
    notSubject: null,
    purchaseType: '1',
    controlCode: '',
  }));
  const [touched, setTouched] = useState(false);
  const register = useRpcCommand('RegisterSupplierInvoiceCommand', { notifyError: false });

  if (!current) return null;
  const errors = supplierInvoiceErrors(draft);
  const credit = estimatedCredit(draft.total, draft.discounts, draft.notSubject);
  const set = (patch: Partial<SupplierInvoiceDraft>) => {
    setDraft((value) => ({ ...value, ...patch }));
    if (register.error) register.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.values(errors).some(Boolean)) return;
    const outcome = await register.run(supplierInvoicePayload(current.receiptNumber, draft));
    if (!outcome.ok) return;
    notify.success('Factura del proveedor registrada', plainMessage(outcome.result));
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!register.sending}
      size="lg"
      title={`Factura del proveedor · recepción ${current.receiptNumber}`}
      description={`${current.supplier} · recibida el ${formatDate(current.receivedOn)} · ${formatMoney(current.total)} al costo neto`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={register.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FileCheck />} loading={register.sending}>
            Registrar factura
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={register.errorText} busy={register.sending}>
        <p className="text-sm text-text-muted">
          La recepción entró al costo neto de IVA ({formatMoney(current.total)}, el 87 % de lo facturado): se propone el importe con IVA que le corresponde,{' '}
          {formatMoney(proposedInvoiceTotal(current.total))} = recepción ÷ 0,87. Escriba el importe que dice la factura del proveedor.
        </p>
        <FormGrid>
          <TextField label="N° de factura" value={draft.invoiceNumber} onChange={(value) => set({ invoiceNumber: value })} error={touched ? errors.invoiceNumber : undefined} maxLength={30} required data-autofocus />
          <TextField label="Fecha de la factura" type="date" value={draft.invoiceDate} onChange={(value) => set({ invoiceDate: value })} error={touched ? errors.invoiceDate : undefined} required />
          <TextField
            label="Código de autorización o CUF"
            value={draft.authorizationCode}
            onChange={(value) => set({ authorizationCode: value })}
            error={touched ? errors.authorizationCode : undefined}
            maxLength={100}
            className="sm:col-span-2"
            autoComplete="off"
            required
          />
          <MoneyField label="Importe total" value={draft.total} onChange={(value) => set({ total: value })} error={touched ? errors.total : undefined} required />
          <MoneyField label="Descuentos" value={draft.discounts} onChange={(value) => set({ discounts: value })} error={touched ? errors.discounts : undefined} optional />
          <MoneyField label="No sujeto a IVA" value={draft.notSubject} onChange={(value) => set({ notSubject: value })} optional hint="ICE, IEHD, tasas y lo que no da crédito fiscal." />
          <SelectField label="Tipo de compra" allLabel={false} value={draft.purchaseType} onChange={(value) => set({ purchaseType: value || '1' })} options={PURCHASE_TYPES} required />
          <TextField label="Código de control" value={draft.controlCode} onChange={(value) => set({ controlCode: value })} maxLength={20} optional hint="Solo las facturas antiguas (manuales) lo tienen." />
        </FormGrid>
        <p className="rounded-xl border border-border bg-surface-2 p-3 text-sm" data-testid="credito-estimado">
          Base para crédito fiscal {formatMoney(credit.base)} · crédito fiscal IVA 13 % estimado {formatMoney(credit.credit)}
        </p>
      </Form>
    </Dialog>
  );
}
