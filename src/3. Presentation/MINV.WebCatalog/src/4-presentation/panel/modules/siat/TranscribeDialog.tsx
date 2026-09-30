// Módulo «Estado del SIAT» · transcribir una factura manual del talonario CAFC emitida durante una contingencia manual
// (`TranscribeManualInvoiceCommand`, permiso «gestionar eventos significativos, paquetes y CAFC»), como `TranscribeDialog`
// del escritorio: evento, número del talonario, fecha y hora escritas en la factura, comprador, medio de pago y productos
// (con las series de los productos serializados). Registra la venta (stock, pago, asiento) y el documento fuera de línea
// que viaja en el paquete de la contingencia. Medios de pago: los de la caja o, sin permiso de caja, los homologados.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { FileText, Trash2 } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, Dialog, Form, FormGrid, IconButton, NumberField, SelectField, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { addDays, formatMoney, laPazToday } from '@/4-presentation/panel/lib';
import { BuyerFields } from './BuyerFields';
import {
  CATALOG_DOCUMENT_TYPES,
  EMPTY_BUYER,
  addMinutesLocal,
  buyerErrors,
  buyerPayload,
  documentTypeOptions,
  eventOptionLabel,
  laPazLocalNow,
  lineProblem,
  linePayload,
  newLine,
  toFiscalDateTime,
  transcribableEvents,
  type BuyerDraft,
  type ProductData,
  type TranscribeLineDraft,
} from './siat';

export interface TranscribeDialogProps {
  open: boolean;
  /** Evento elegido de antemano (desde la fila del evento). */
  initialEventId?: string | null;
  onClose: () => void;
  onDone: () => void;
}

export function TranscribeDialog({ open, initialEventId = null, onClose, onDone }: TranscribeDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { can } = usePermissions();
  const fromPos = can('sales.pos.operate');
  // Contingencias de los últimos 10 días (las que todavía admiten facturas manuales).
  const [today] = useState(() => laPazToday());
  const eventsQuery = useRpcQuery('GetSignificantEventsQuery', { from: addDays(today, -10), to: today }, { enabled: open });
  const events = eventsQuery.data;
  const pos = useRpcQuery('GetPosStateQuery', {}, { enabled: open && fromPos });
  const homologation = useRpcQuery('GetHomologationQuery', {}, { enabled: open && !fromPos });
  const types = useRpcQuery('GetSiatCatalogQuery', { catalog: CATALOG_DOCUMENT_TYPES }, { enabled: open });
  const products = useRpcQuery('GetSellableProductsQuery', {}, { enabled: open });
  const transcribe = useRpcCommand('TranscribeManualInvoiceCommand', { notifyError: false });

  const choices = transcribableEvents(events);
  const [eventId, setEventId] = useState(initialEventId ?? '');
  const chosenEvent = choices.find((row) => row.id === eventId) ?? (eventId ? undefined : choices[0]);
  const [number, setNumber] = useState<number | null>(null);
  const [issuedAt, setIssuedAt] = useState('');
  const [buyer, setBuyer] = useState<BuyerDraft>(EMPTY_BUYER);
  const [method, setMethod] = useState('');
  const [lines, setLines] = useState<TranscribeLineDraft[]>([]);
  const [picked, setPicked] = useState<ComboOption<ProductData> | null>(null);
  const [touched, setTouched] = useState(false);

  const methods = fromPos ? (pos.data?.paymentMethods ?? []) : (homologation.data?.paymentMethods ?? []);
  const methodOptions = methods.map((item) => ({ value: item.code, label: item.name }));
  const chosenMethod = method || methodOptions.find((option) => option.value === 'EFECTIVO')?.value || methodOptions[0]?.value || '';
  // La hora que se propone: 5 minutos después del inicio de la contingencia (como el escritorio).
  const proposedTime = chosenEvent ? addMinutesLocal(chosenEvent.startedAt.slice(0, 16), 5) : laPazLocalNow();
  const time = issuedAt || proposedTime;
  const productOptions: ComboOption<ProductData>[] = (products.data ?? []).map((product) => ({
    value: product.sku,
    label: product.name,
    description: `${product.sku} · ${formatMoney(product.price)}`,
    data: product,
  }));

  const buyerProblems = buyerErrors(buyer);
  const errors = {
    event: chosenEvent ? undefined : 'Elija la contingencia manual de la factura.',
    number: number !== null && number > 0 ? undefined : 'Escriba el número de la factura manual (el del talonario).',
    time: toFiscalDateTime(time) ? undefined : 'Escriba la fecha y hora de la factura manual.',
    method: chosenMethod ? undefined : 'Elija el medio de pago.',
    lines: lines.length === 0 ? 'Agregue los productos de la factura.' : (lines.map(lineProblem).find(Boolean) ?? undefined),
  };
  const invalid = Object.values(errors).some(Boolean) || Object.values(buyerProblems).some(Boolean);

  const addProduct = (option: ComboOption<ProductData> | null) => {
    setPicked(null);
    const product = option?.data;
    if (!product || lines.some((line) => line.sku === product.sku)) return;
    setLines((current) => [...current, newLine(product)]);
  };
  const updateLine = (sku: string, patch: Partial<TranscribeLineDraft>) => setLines((current) => current.map((line) => (line.sku === sku ? { ...line, ...patch } : line)));

  const submit = async () => {
    setTouched(true);
    const fiscalTime = toFiscalDateTime(time);
    if (invalid || !chosenEvent || number === null || !fiscalTime) return;
    const outcome = await transcribe.run({
      significantEventId: chosenEvent.id,
      number,
      issuedAt: fiscalTime,
      buyer: buyerPayload(buyer),
      paymentMethodCode: chosenMethod,
      lines: lines.map(linePayload),
    });
    if (!outcome.ok) return;
    notify.success(`Factura manual N° ${outcome.result.number} transcrita`, 'Quedó fuera de línea: viaja en el paquete de la contingencia al recuperarse.');
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!transcribe.sending}
      size="xl"
      title="Transcribir factura manual (CAFC)"
      description="Copie los datos de la factura manual tal como se entregó al comprador (número del talonario, fecha y hora, comprador y productos)."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={transcribe.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FileText />} loading={transcribe.sending} disabled={choices.length === 0}>
            Transcribir
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={transcribe.errorText} busy={transcribe.sending}>
        {events !== undefined && choices.length === 0 && (
          <Alert tone="info" title="No hay contingencias manuales abiertas o por transcribir">
            Declare la contingencia manual desde la tarjeta del punto de venta.
          </Alert>
        )}
        <FormGrid>
          <SelectField
            label="Contingencia"
            allLabel={false}
            placeholder="Elija la contingencia"
            value={chosenEvent?.id ?? ''}
            onChange={setEventId}
            options={choices.map((row) => ({ value: row.id, label: eventOptionLabel(row) }))}
            error={touched ? errors.event : undefined}
            className="sm:col-span-2"
            required
          />
          <NumberField label="Número de la factura manual" value={number} onChange={setNumber} error={touched ? errors.number : undefined} required />
          <TextField
            label="Fecha y hora de la factura"
            type="datetime-local"
            value={time}
            onChange={setIssuedAt}
            error={touched ? errors.time : undefined}
            hint="La escrita en la factura manual (hora de Bolivia)."
            required
          />
          <SelectField
            label="Medio de pago"
            allLabel={false}
            placeholder="Elija el medio de pago"
            value={chosenMethod}
            onChange={setMethod}
            options={methodOptions}
            error={touched ? errors.method : undefined}
            required
          />
        </FormGrid>

        <fieldset className="space-y-2">
          <legend className="text-sm font-semibold text-text">Comprador</legend>
          <BuyerFields buyer={buyer} onChange={setBuyer} typeOptions={documentTypeOptions(types.data)} errors={touched ? buyerProblems : {}} disabled={transcribe.sending} />
        </fieldset>

        <fieldset className="space-y-3">
          <legend className="text-sm font-semibold text-text">Productos</legend>
          <ComboBox
            label="Agregar un producto"
            value={picked}
            onChange={addProduct}
            options={productOptions}
            placeholder={products.data ? 'Nombre o SKU' : 'Cargando los productos…'}
            error={touched && lines.length === 0 ? errors.lines : undefined}
          />
          {lines.length > 0 && (
            <ul className="space-y-3" aria-label="Productos de la factura manual">
              {lines.map((line) => (
                <li key={line.sku} className="space-y-2 rounded-xl border border-border p-3" data-testid={`linea-${line.sku}`}>
                  <div className="flex items-start justify-between gap-2">
                    <p className="min-w-0 text-sm font-medium">
                      {line.name}
                      <span className="block text-xs text-text-muted">{line.sku}</span>
                    </p>
                    <IconButton label={`Quitar ${line.name}`} icon={<Trash2 />} onClick={() => setLines((current) => current.filter((item) => item.sku !== line.sku))} />
                  </div>
                  <FormGrid>
                    <NumberField label="Cantidad" value={line.quantity} onChange={(value) => updateLine(line.sku, { quantity: value })} decimals={3} unit={line.unit} required />
                    <NumberField label="Descuento" value={line.discount} onChange={(value) => updateLine(line.sku, { discount: value })} decimals={2} unit="%" optional />
                    <TextField
                      label="Series o IMEI"
                      value={line.serials}
                      onChange={(value) => updateLine(line.sku, { serials: value })}
                      hint="Solo si el producto lleva serie: una por unidad, separadas por coma."
                      className="sm:col-span-2"
                      optional
                    />
                  </FormGrid>
                </li>
              ))}
            </ul>
          )}
          {touched && lines.length > 0 && errors.lines && <Alert tone="danger">{errors.lines}</Alert>}
        </fieldset>
      </Form>
    </Dialog>
  );
}
