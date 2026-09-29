// Módulo «Reservas» · NUEVA RESERVA EN MOSTRADOR (`ReserveCartCommand`): el personal reserva productos para un cliente
// que pasa a recogerlos (carrito RES-<sucursal>-000001). El stock de cada producto se reserva en la sucursal activa, todo o
// nada: si falta de alguno, el servidor no reserva nada y dice qué falta (el mensaje queda DENTRO del formulario).
//
//   Productos: buscador (nombre, SKU o código de barras) sobre `GetSellableProductsQuery`, con precio y disponible.
//   Quién la recoge: cliente registrado (opcional, completa sus datos), nombre, teléfono, correo (opcional: recibe la
//   confirmación), días para recoger (1 a 3), notas y un nombre para la reserva.
//   Datos para la factura (opcionales): tipo de documento en lista desplegable, número, complemento y razón social; la caja
//   los precarga al cobrar.
//
// La validación de aquí es comodidad (las mismas reglas del servidor); el servidor vuelve a validar todo (regla P-01).

import { PackageSearch, ShoppingBag, X } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, Dialog, Form, FormGrid, IconButton, NumberField, SelectField, TextField, type ComboOption } from '@/4-presentation/panel/kit';
import { describePanelError, formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { ErrorDetails } from './ErrorDetails';
import {
  CART_LIMITS,
  DOCUMENT_TYPES,
  EMPTY_CART,
  HOLD_DAY_OPTIONS,
  addCartLine,
  cartProblems,
  cartTotal,
  customerOptions,
  toReserveCartPayload,
  type BuildRecord,
  type CartField,
  type CartForm,
  type CustomerRecord,
  type ProductRecord,
} from './reservations';

export interface NewReservationDialogProps {
  open: boolean;
  onClose: () => void;
  /** La reserva creada (lo que devolvió el servidor). */
  onCreated: (row: BuildRecord) => void;
}

function productDescription(product: ProductRecord): string {
  const parts = [product.sku, formatMoney(product.price), product.available > 0 ? `disponible ${formatQuantity(product.available)}` : 'sin stock disponible'];
  if (product.barcodes.length > 0) parts.push(product.barcodes.join(', '));
  return parts.join(' · ');
}

export function NewReservationDialog({ open, onClose, onCreated }: NewReservationDialogProps) {
  const formId = useId();
  const productsTitleId = useId();
  const contactTitleId = useId();
  const invoiceTitleId = useId();
  const products = useRpcQuery('GetSellableProductsQuery', {}, { enabled: open });
  const customers = useRpcQuery('GetCustomersQuery', {}, { enabled: open });
  const reserve = useRpcCommand('ReserveCartCommand', { success: (row) => `Reserva ${row.number} creada`, notifyError: false });
  const [form, setForm] = useState<CartForm>(EMPTY_CART);
  const [touched, setTouched] = useState(false);

  const problems = cartProblems(form);
  const problem = (field: CartField) => (touched ? problems[field] : undefined);

  const productOptions = useMemo<ComboOption<ProductRecord>[]>(
    () => (products.data ?? []).map((product) => ({ value: product.sku, label: product.name, description: productDescription(product), data: product })),
    [products.data],
  );
  const customerChoices = useMemo<ComboOption<CustomerRecord>[]>(() => customerOptions(customers.data?.customers ?? []), [customers.data]);
  const customer = customerChoices.find((option) => option.value === form.customerCode) ?? null;
  const documentType = DOCUMENT_TYPES.find((type) => type.value === form.documentType);

  const change = <K extends keyof CartForm>(key: K, value: CartForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (reserve.error) reserve.reset();
  };

  const addProduct = (option: ComboOption<ProductRecord> | null) => {
    const product = option?.data;
    if (product) change('lines', addCartLine(form.lines, product));
  };

  const setQuantity = (sku: string, quantity: number | null) =>
    change(
      'lines',
      form.lines.map((line) => (line.sku === sku ? { ...line, quantity } : line)),
    );

  const pickCustomer = (option: ComboOption<CustomerRecord> | null) => {
    const picked = option?.data;
    setForm((current) => ({
      ...current,
      customerCode: option?.value ?? '',
      // Completa lo que falta con los datos del cliente (lo ya escrito no se pisa).
      contactName: current.contactName || picked?.name || '',
      contactPhone: current.contactPhone || picked?.phone || '',
      contactEmail: current.contactEmail || picked?.email || '',
    }));
    if (reserve.error) reserve.reset();
  };

  const close = () => {
    setForm(EMPTY_CART);
    setTouched(false);
    reserve.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await reserve.run(toReserveCartPayload(form));
    if (outcome.ok) {
      onCreated(outcome.result);
      close();
    }
  };

  const total = cartTotal(form.lines);

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!reserve.sending}
      size="xl"
      title="Nueva reserva en mostrador"
      description="El stock de cada producto queda reservado en la sucursal activa hasta el día elegido; el cliente la cobra en la caja."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={reserve.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ShoppingBag />} loading={reserve.sending}>
            Reservar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={reserve.errorText && <ErrorDetails text={reserve.errorText} details={reserve.error?.errors} />} busy={reserve.sending}>
        <section aria-labelledby={productsTitleId} className="space-y-3">
          <h3 id={productsTitleId} className="text-base font-semibold">
            Productos
          </h3>
          {products.error ? (
            <Alert
              tone="danger"
              title="No se pudieron cargar los productos"
              actions={
                <Button variant="outline" onClick={products.reload}>
                  Reintentar
                </Button>
              }
            >
              {describePanelError(products.error, { operation: 'GetSellableProductsQuery' })}
            </Alert>
          ) : (
            <ComboBox<ProductRecord>
              label="Agregar un producto"
              placeholder="Nombre, SKU o código de barras"
              value={null}
              onChange={addProduct}
              options={productOptions}
              disabled={!products.data}
              hint={products.data ? `Precios de la lista vigente. Hasta ${CART_LIMITS.lines} productos distintos y ${CART_LIMITS.quantity} unidades de cada uno.` : 'Cargando los productos…'}
              emptyText="Ningún producto coincide."
              error={problem('lines')}
            />
          )}
          {form.lines.length === 0 ? (
            <p className="flex items-center gap-2 rounded-xl border border-dashed border-border-strong px-3 py-4 text-sm text-text-muted">
              <PackageSearch aria-hidden="true" className="size-5 shrink-0 text-accent" />
              Todavía no hay productos: búsquelos arriba y elíjalos de la lista.
            </p>
          ) : (
            <ul aria-label="Productos de la reserva" className="divide-y divide-border rounded-xl border border-border" data-testid="lineas-de-la-reserva">
              {form.lines.map((line) => {
                const short = line.quantity !== null && line.quantity > line.available;
                return (
                  <li key={line.sku} className="flex flex-wrap items-end gap-3 px-3 py-3" data-sku={line.sku}>
                    <div className="min-w-0 flex-1 basis-48">
                      <p className="font-medium break-words text-text">{line.name}</p>
                      <p className="text-xs text-text-muted">
                        {line.sku} · {formatMoney(line.price)} c/u · {line.available > 0 ? `disponible ${formatQuantity(line.available)}` : 'sin stock disponible'}
                      </p>
                      {short && (
                        <p className="mt-1 text-xs text-warning-text">
                          Hay {formatQuantity(line.available)} disponible{line.available === 1 ? '' : 's'}: si no alcanza, el servidor no reserva nada.
                        </p>
                      )}
                    </div>
                    <div className="w-28">
                      <span aria-hidden="true" className="mb-1 block text-xs text-text-muted">
                        Cantidad
                      </span>
                      <NumberField label={`Cantidad de ${line.name}`} hideLabel value={line.quantity} onChange={(quantity) => setQuantity(line.sku, quantity)} />
                    </div>
                    <p className="w-28 pb-3 text-right text-sm font-semibold text-text tabular-nums">{formatMoney(line.price * (line.quantity ?? 0))}</p>
                    <IconButton
                      label={`Quitar ${line.name}`}
                      icon={<X />}
                      onClick={() =>
                        change(
                          'lines',
                          form.lines.filter((item) => item.sku !== line.sku),
                        )
                      }
                    />
                  </li>
                );
              })}
              <li className="flex items-center justify-between gap-3 bg-surface-2 px-3 py-3 font-semibold">
                <span>Total a precios de hoy</span>
                <span className="tabular-nums" data-testid="total-de-la-reserva">
                  {formatMoney(total)}
                </span>
              </li>
            </ul>
          )}
        </section>

        <section aria-labelledby={contactTitleId} className="space-y-3">
          <h3 id={contactTitleId} className="text-base font-semibold">
            Quién la recoge
          </h3>
          <FormGrid>
            <ComboBox<CustomerRecord>
              label="Cliente registrado"
              placeholder="Nombre, código o NIT"
              value={customer}
              onChange={pickCustomer}
              options={customerChoices}
              hint="Liga la reserva al cliente y completa sus datos."
              optional
              className="sm:col-span-2"
            />
            <TextField
              label="Nombre"
              value={form.contactName}
              onChange={(value) => change('contactName', value)}
              maxLength={CART_LIMITS.name}
              autoComplete="off"
              error={problem('contactName')}
              required
            />
            <TextField
              label="Teléfono o WhatsApp"
              type="tel"
              inputMode="tel"
              value={form.contactPhone}
              onChange={(value) => change('contactPhone', value)}
              maxLength={CART_LIMITS.phone}
              autoComplete="off"
              hint="7 u 8 dígitos, con o sin +591."
              error={problem('contactPhone')}
              required
            />
            <TextField
              label="Correo"
              type="email"
              inputMode="email"
              autoCapitalize="none"
              spellCheck={false}
              value={form.contactEmail}
              onChange={(value) => change('contactEmail', value)}
              maxLength={CART_LIMITS.email}
              autoComplete="off"
              hint="Recibe la confirmación con el número de la reserva."
              error={problem('contactEmail')}
              optional
            />
            <SelectField
              label="Días para recoger"
              allLabel={false}
              value={form.holdDays}
              onChange={(value) => change('holdDays', value)}
              options={HOLD_DAY_OPTIONS}
              hint="Después vence sola y el stock vuelve a estar disponible."
            />
            <TextField
              label="Notas"
              value={form.notes}
              onChange={(value) => change('notes', value)}
              maxLength={CART_LIMITS.notes}
              hint="En una sola línea (por ejemplo, a qué hora pasa)."
              error={problem('notes')}
              optional
            />
            <TextField
              label="Nombre de la reserva"
              value={form.name}
              onChange={(value) => change('name', value)}
              maxLength={CART_LIMITS.title}
              placeholder="Por defecto, «Reserva de» y el nombre"
              error={problem('name')}
              optional
            />
          </FormGrid>
        </section>

        <section aria-labelledby={invoiceTitleId} className="space-y-3">
          <h3 id={invoiceTitleId} className="text-base font-semibold">
            Datos para la factura <span className="text-sm font-normal text-text-faint">(opcional)</span>
          </h3>
          <FormGrid>
            <SelectField
              label="Tipo de documento"
              allLabel="Sin datos de factura (los pide el cajero)"
              value={form.documentType}
              onChange={(value) => {
                setForm((current) => ({ ...current, documentType: value, complement: value === '1' ? current.complement : '' }));
                if (reserve.error) reserve.reset();
              }}
              options={DOCUMENT_TYPES}
              error={problem('documentType')}
            />
            {documentType && (
              <TextField
                label="Número de documento"
                inputMode={documentType.numeric ? 'numeric' : 'text'}
                value={form.documentNumber}
                onChange={(value) => change('documentNumber', value)}
                maxLength={CART_LIMITS.document}
                autoComplete="off"
                error={problem('documentNumber')}
                required
              />
            )}
            {documentType?.complement && (
              <TextField
                label="Complemento"
                value={form.complement}
                onChange={(value) => change('complement', value)}
                maxLength={CART_LIMITS.complement}
                autoComplete="off"
                hint="Solo si la cédula lo tiene (por ejemplo 1A)."
                error={problem('complement')}
                optional
              />
            )}
            {documentType && (
              <TextField
                label="Nombre o razón social"
                value={form.legalName}
                onChange={(value) => change('legalName', value)}
                maxLength={CART_LIMITS.legalName}
                autoComplete="off"
                error={problem('legalName')}
                optional
              />
            )}
          </FormGrid>
        </section>
      </Form>
    </Dialog>
  );
}
