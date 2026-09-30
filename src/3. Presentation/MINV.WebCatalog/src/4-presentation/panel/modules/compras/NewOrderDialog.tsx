// Módulo «Órdenes de compra» · orden nueva (el editor del escritorio): proveedor (lista desplegable de los activos),
// entrega esperada (opcional: sin fecha, el servidor usa el plazo del proveedor), notas y productos con cantidad y costo
// unitario (propuestos: del mínimo al máximo y el costo del catálogo). La orden queda en BORRADOR
// (`CreatePurchaseOrderCommand`); el servidor valida y calcula los importes. El error del servidor se muestra DENTRO del
// diálogo, sin cerrarlo. Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { FilePlus2, RotateCcw, Trash2 } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  ComboBox,
  Dialog,
  Form,
  FormGrid,
  IconButton,
  MoneyField,
  NumberField,
  SelectField,
  Switch,
  TextArea,
  TextField,
} from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, formatNumber, laPazToday } from '@/4-presentation/panel/lib';
import {
  LIMITS,
  addProduct,
  createOrderPayload,
  defaultDelivery,
  draftTotal,
  emptyDraft,
  hasOrderProblems,
  lineSubtotal,
  orderProblems,
  productChoices,
  type CatalogRecord,
  type OrderDraft,
  type OrderDraftLine,
  type OrderRecord,
  type SupplierRecord,
} from './purchasing';

const CATALOG_REQUEST = { categoryCode: null, specFilters: null };

export interface NewOrderDialogProps {
  open: boolean;
  /** Proveedores del directorio (se ofrecen los activos). */
  suppliers: readonly SupplierRecord[];
  /** Proveedor elegido al abrir (por ejemplo, desde `?proveedor=`). */
  initialSupplier: string | null;
  onClose: () => void;
  onCreated: (row: OrderRecord) => void;
}

export function NewOrderDialog({ open, suppliers, initialSupplier, onClose, onCreated }: NewOrderDialogProps) {
  const formId = useId();
  const [today] = useState(() => laPazToday());
  const active = suppliers.filter((supplier) => supplier.isActive);
  const [draft, setDraft] = useState<OrderDraft>(() => emptyDraft(initialSupplier ?? ''));
  const [touched, setTouched] = useState(false);
  // El proveedor que llega de la dirección se confirma cuando la lista está cargada: si no está activo, se quita.
  const [checkInitial, setCheckInitial] = useState(Boolean(initialSupplier));
  if (checkInitial && suppliers.length > 0) {
    setCheckInitial(false);
    if (!active.some((supplier) => supplier.code === draft.supplierCode)) setDraft((current) => ({ ...current, supplierCode: '' }));
  }
  const catalog = useRpcQuery('GetCatalogQuery', CATALOG_REQUEST, { enabled: open });
  const options = useRpcQuery('GetCatalogOptionsQuery', {}, { enabled: open });
  const create = useRpcCommand('CreatePurchaseOrderCommand', { notifyError: false });

  const units = options.data?.units ?? [];
  const supplier = active.find((item) => item.code === draft.supplierCode) ?? null;
  const problems = orderProblems(draft, today);
  const shown = <K extends keyof typeof problems>(field: K) => (touched ? problems[field] : undefined);
  const choices = productChoices(catalog.data ?? [], draft);

  const change = (update: Partial<OrderDraft>) => {
    setDraft((current) => ({ ...current, ...update }));
    if (create.error) create.reset();
  };
  const changeLine = (sku: string, update: Partial<OrderDraftLine>) => {
    setDraft((current) => ({ ...current, lines: current.lines.map((line) => (line.sku === sku ? { ...line, ...update } : line)) }));
    if (create.error) create.reset();
  };
  const removeLine = (sku: string) => setDraft((current) => ({ ...current, lines: current.lines.filter((line) => line.sku !== sku) }));
  const pick = (item: CatalogRecord | undefined) => {
    if (item) setDraft((current) => addProduct(current, item, units));
  };

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (hasOrderProblems(problems)) return;
    const outcome = await create.run(createOrderPayload(draft));
    if (outcome.ok) {
      onCreated(outcome.result);
      close();
    }
  };

  const catalogHint = catalog.loading ? 'Cargando el catálogo…' : supplier && draft.onlySupplier ? `Productos de ${supplier.name}.` : 'Todo el catálogo activo.';

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!create.sending}
      size="xl"
      title="Nueva orden de compra"
      description="Queda en borrador: apruébela para poder recibirla. El número lo asigna el sistema."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FilePlus2 />} loading={create.sending}>
            Crear en borrador
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        <FormGrid>
          <SelectField
            label="Proveedor"
            allLabel={false}
            placeholder="Elija el proveedor"
            value={draft.supplierCode}
            onChange={(value) => change({ supplierCode: value })}
            options={active.map((item) => ({ value: item.code, label: `${item.name} (${item.code})` }))}
            hint={supplier ? `Entrega en ${formatNumber(supplier.leadTimeDays)} ${supplier.leadTimeDays === 1 ? 'día' : 'días'}.` : undefined}
            error={shown('supplierCode')}
            required
            data-autofocus
          />
          <TextField
            label="Entrega esperada"
            type="date"
            value={draft.expectedDate}
            min={today}
            onChange={(value) => change({ expectedDate: value })}
            hint={`Sin fecha: ${formatDate(defaultDelivery(today, supplier?.leadTimeDays))} (plazo del proveedor).`}
            error={shown('expectedDate')}
            optional
          />
          <TextArea
            label="Notas"
            value={draft.notes}
            onChange={(value) => change({ notes: value })}
            maxLength={LIMITS.notes}
            rows={2}
            error={shown('notes')}
            className="sm:col-span-2"
            optional
          />
        </FormGrid>

        <fieldset className="min-w-0 space-y-3">
          <legend className="text-sm font-semibold text-text">Productos</legend>
          {catalog.error && (
            <Alert
              tone="danger"
              title="No se pudo cargar el catálogo"
              actions={
                <Button variant="outline" leftIcon={<RotateCcw />} onClick={catalog.reload}>
                  Reintentar
                </Button>
              }
            >
              Sin el catálogo no se pueden agregar productos.
            </Alert>
          )}
          <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-[minmax(0,1fr)_auto]">
            <ComboBox<CatalogRecord>
              label="Agregar producto"
              placeholder="Nombre o SKU"
              value={null}
              onChange={(option) => pick(option?.data)}
              options={choices}
              disabled={!catalog.data}
              hint={catalogHint}
              emptyText={draft.onlySupplier && supplier ? 'Sin productos de este proveedor: desmarque «Solo productos de este proveedor».' : 'Sin resultados'}
            />
            <Switch label="Solo productos de este proveedor" checked={draft.onlySupplier} onChange={(value) => change({ onlySupplier: value })} className="sm:w-64" />
          </div>
          {draft.lines.length === 0 ? (
            <p className={touched && problems.lines ? 'text-sm text-danger-text' : 'text-sm text-text-muted'} role={touched && problems.lines ? 'alert' : undefined}>
              {touched && problems.lines ? problems.lines : 'Agregue productos con la lista «Agregar producto».'}
            </p>
          ) : (
            <ul className="space-y-2" aria-label="Productos de la orden">
              {draft.lines.map((line) => {
                const errors = touched ? problems.byLine[line.sku] : undefined;
                return (
                  <li key={line.sku} className="grid grid-cols-1 items-start gap-3 rounded-xl border border-border bg-surface-2/40 p-3 sm:grid-cols-[minmax(0,1fr)_9rem_10rem_auto]" data-testid={`linea-${line.sku}`}>
                    <div className="min-w-0 pt-1">
                      <p className="font-medium break-words text-text">{line.name}</p>
                      <p className="text-xs text-text-muted">
                        {line.sku} · subtotal {formatMoney(lineSubtotal(line))}
                      </p>
                    </div>
                    <NumberField
                      label="Cantidad"
                      value={line.quantity}
                      onChange={(value) => changeLine(line.sku, { quantity: value })}
                      decimals={line.allowsDecimals ? 3 : 0}
                      unit={line.unit}
                      error={errors?.quantity}
                      required
                    />
                    <MoneyField
                      label="Costo unitario"
                      value={line.unitCost}
                      onChange={(value) => changeLine(line.sku, { unitCost: value })}
                      hint="Con 0 se usa el costo promedio."
                      error={errors?.unitCost}
                      required
                    />
                    <IconButton label={`Quitar ${line.name}`} icon={<Trash2 />} className="sm:mt-6" onClick={() => removeLine(line.sku)} />
                  </li>
                );
              })}
            </ul>
          )}
          <p className="text-right text-sm text-text-muted" data-testid="total-orden">
            {draft.lines.length === 1 ? '1 producto' : `${formatNumber(draft.lines.length)} productos`} · Total <strong className="text-text">{formatMoney(draftTotal(draft))}</strong>
          </p>
        </fieldset>
      </Form>
    </Dialog>
  );
}
