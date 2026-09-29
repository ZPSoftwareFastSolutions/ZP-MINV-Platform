// Módulo «Caja» · el COBRO: cliente (buscar o consumidor final), datos de la factura si la caja factura, medio de pago
// (efectivo recibido y vuelto, referencia o tarjeta) y la confirmación con el total. Envía `CheckoutCommand` (venta
// normal) o `SellPcBuildCommand` (reserva o cotización) con la forma exacta del contrato. El error del servidor (sin
// stock, serie no disponible, NIT, medio sin homologar…) se muestra dentro del diálogo, sin cerrarlo, para corregir y
// reintentar (el reintento viaja con el mismo `requestId`: el servidor no cobra dos veces).

import { Banknote, CreditCard, Receipt } from 'lucide-react';
import { useId, useState, type Dispatch, type SetStateAction } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, Dialog, Form, FormGrid, MoneyField, SelectField, TextField, type ComboOption } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { BuyerFields } from './BuyerFields';
import type { PosCapabilities } from './capabilities';
import { cartTotals, includedTax, itemsText, type CajaCart } from './cart';
import { documentTypeOptions, fiscalBand, type BuyerDraft } from './fiscal';
import { cashSuggestions, changeInfo, checkoutErrors, checkoutPayload, errorCount, isCard, isCash, methodOf, sellBuildPayload, type PaymentDraft } from './payment';
import type { CajaProduct } from './products';
import type { BuildRowData, FiscalStateData, PosStateData, SaleResultData } from './types';

/** Lo que el cobro deja para después de la venta. */
export interface SoldContext {
  build: BuildRowData | null;
  /** Correo que escribió el cajero para la factura (para «Enviar por correo»). */
  buyerEmail: string | null;
}

export interface CheckoutDialogProps {
  open: boolean;
  onClose: () => void;
  state: PosStateData | undefined;
  fiscal: FiscalStateData | undefined;
  caps: PosCapabilities;
  cart: CajaCart;
  products: readonly CajaProduct[];
  sessionOpen: boolean;
  /** Cobro con los valores por defecto ya aplicados (consumidor final, efectivo). */
  payment: PaymentDraft;
  onPaymentChange: Dispatch<SetStateAction<PaymentDraft>>;
  buyer: BuyerDraft;
  onBuyerChange: Dispatch<SetStateAction<BuyerDraft>>;
  onSold: (sale: SaleResultData, context: SoldContext) => void;
}

export function CheckoutDialog({ open, onClose, state, fiscal, caps, cart, products, sessionOpen, payment, onPaymentChange, buyer, onBuyerChange, onSold }: CheckoutDialogProps) {
  const formId = useId();
  const [touched, setTouched] = useState(false);
  const checkout = useRpcCommand('CheckoutCommand', { success: (sale) => `Venta ${sale.invoiceNumber} cobrada`, notifyError: false });
  const sell = useRpcCommand('SellPcBuildCommand', { success: (sale) => `Reserva cobrada: venta ${sale.invoiceNumber}`, notifyError: false });
  const command = cart.build ? sell : checkout;

  const billing = fiscal?.billingEnabled === true;
  const band = fiscalBand(fiscal);
  const method = methodOf(state, payment.methodCode);
  const cash = isCash(method);
  const card = isCard(method, billing);
  const totals = cartTotals(cart.lines);
  const tax = includedTax(totals.total, state?.taxRate ?? 13, state?.vatOnInvoicedAmount ?? true);
  const errors = checkoutErrors({ cart, products, total: totals.total, payment, buyer, method, billing, sessionOpen });
  const shown = touched ? errors : { general: [] };
  const change = changeInfo(totals.total, payment.cashReceived);

  const customers: ComboOption[] = (state?.customers ?? []).map((option) => ({ value: option.code, label: option.name, description: option.code }));
  const customer = customers.find((option) => option.value === payment.customerCode) ?? null;

  const edit = (update: Partial<PaymentDraft>) => {
    command.reset();
    onPaymentChange((current) => ({ ...current, ...update }));
  };

  const close = () => {
    setTouched(false);
    checkout.reset();
    sell.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (errorCount(errors) > 0) return;
    const outcome = cart.build
      ? await sell.run(sellBuildPayload(cart.build.number, cart, payment, buyer, method, billing))
      : await checkout.run(checkoutPayload(cart, payment, buyer, method, billing));
    if (!outcome.ok) return;
    setTouched(false);
    onSold(outcome.result, { build: cart.build, buyerEmail: buyer.email.trim().length > 0 ? buyer.email.trim() : null });
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!command.sending}
      size="lg"
      title={cart.build ? `Cobrar ${cart.build.kind === 'Cart' ? 'la reserva' : 'el armado'} ${cart.build.number}` : 'Cobrar la venta'}
      description="Revise el total, elija el cliente y el medio de pago y confirme."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={command.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Receipt />} loading={command.sending}>
            Cobrar {formatMoney(totals.total)}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={command.errorText} busy={command.sending}>
        <div className="flex flex-wrap items-end justify-between gap-3 rounded-xl border border-border bg-surface-2 p-4" data-testid="cobro-total">
          <div>
            <p className="text-sm font-semibold uppercase tracking-wide text-text-muted">Total a cobrar</p>
            <p className="text-sm text-text-muted">
              {itemsText(totals)} · IVA incluido {formatMoney(tax)}
              {totals.discount > 0 && ` · descuentos ${formatMoney(totals.discount)}`}
            </p>
          </div>
          <p className="font-display text-3xl font-bold text-text tabular-nums">{formatMoney(totals.total)}</p>
        </div>

        {band && band.tone === 'danger' && (
          <Alert tone="danger" title={band.title}>
            {band.detail}
          </Alert>
        )}
        {shown.general.length > 0 && (
          <Alert tone="warning" title="Antes de cobrar">
            <ul className="list-disc space-y-1 pl-5">
              {shown.general.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          </Alert>
        )}

        <ComboBox
          label="Cliente"
          value={customer}
          onChange={(option) => edit({ customerCode: option?.value ?? '' })}
          options={customers}
          placeholder="Nombre o código del cliente"
          hint="«Consumidor final» si el cliente no está registrado."
          error={touched ? errors.customer : undefined}
          required
        />

        {billing && (
          <BuyerFields
            buyer={buyer}
            onChange={(update) => {
              command.reset();
              onBuyerChange(update);
            }}
            documentTypes={documentTypeOptions(fiscal?.documentTypes)}
            errors={touched ? errors : {}}
            customerCode={payment.customerCode}
            onCustomerFound={(code) => {
              if (state?.customers.some((option) => option.code === code)) edit({ customerCode: code });
            }}
            canLookup={caps.findBuyer}
            canVerifyNit={caps.verifyNit}
          />
        )}

        <FormGrid>
          <SelectField
            label="Medio de pago"
            value={payment.methodCode}
            onChange={(value) => edit({ methodCode: value, reference: '', cardNumber: '' })}
            options={(state?.paymentMethods ?? []).map((option) => ({ value: option.code, label: option.name }))}
            allLabel={false}
            placeholder="Elija el medio de pago"
            error={touched ? errors.method : undefined}
            required
          />
          {cash ? (
            <MoneyField
              label="Efectivo recibido"
              value={payment.cashReceived}
              onChange={(value) => edit({ cashReceived: value })}
              error={touched ? errors.cash : undefined}
              hint="Opcional: para calcular el vuelto."
              optional
              data-autofocus
            />
          ) : (
            <TextField
              label="Referencia del pago"
              value={payment.reference}
              onChange={(value) => edit({ reference: value })}
              error={touched ? errors.reference : undefined}
              hint="N.º de operación, voucher o comprobante."
              autoComplete="off"
              maxLength={60}
              required
              data-autofocus
            />
          )}
        </FormGrid>

        {cash && (
          <div className="space-y-2">
            <div className="flex flex-wrap gap-2" role="group" aria-label="Montos recibidos frecuentes">
              <Button variant="subtle" leftIcon={<Banknote />} onClick={() => edit({ cashReceived: totals.total })}>
                Exacto
              </Button>
              {cashSuggestions(totals.total).map((amount) => (
                <Button key={amount} variant="subtle" onClick={() => edit({ cashReceived: amount })}>
                  {formatMoney(amount)}
                </Button>
              ))}
            </div>
            <p
              role="status"
              data-testid="vuelto"
              className={change.kind === 'short' ? 'text-sm font-semibold text-danger-text' : change.kind === 'change' ? 'text-lg font-semibold text-success-text' : 'text-sm text-text-muted'}
            >
              {change.text}
            </p>
          </div>
        )}

        {card && (
          <TextField
            label="Número de la tarjeta"
            value={payment.cardNumber}
            onChange={(value) => edit({ cardNumber: value })}
            error={touched ? errors.card : undefined}
            hint="Se envía al cobrar y la factura lo lleva enmascarado (4 primeros y 4 últimos dígitos). No se guarda en la página."
            leading={<CreditCard />}
            inputMode="numeric"
            autoComplete="off"
            maxLength={23}
            required
          />
        )}
      </Form>
    </Dialog>
  );
}
