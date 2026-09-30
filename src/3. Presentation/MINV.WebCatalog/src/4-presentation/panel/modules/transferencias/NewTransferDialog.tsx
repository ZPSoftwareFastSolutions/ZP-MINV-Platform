// Módulo «Transferencias» · solicitar una transferencia (`CreateTransferCommand`), como el editor del escritorio: sale
// de un almacén de la SUCURSAL ACTIVA (en la vista de todas las sucursales, de una sucursal visible) y llega al almacén
// de otra sucursal (lista desplegable), con productos y cantidades y, para los serializados, las series que viajan (una
// por unidad, regla T-02). Queda PENDIENTE: todavía no mueve stock; el origen la despacha después. El error del servidor
// se muestra DENTRO del diálogo. Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { ArrowRightLeft, RotateCcw, Trash2 } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, Dialog, Form, FormGrid, IconButton, NumberField, SelectField, TextArea } from '@/4-presentation/panel/kit';
import { formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { SerialPicker } from './SerialPicker';
import {
  LIMITS,
  addLine,
  createTransferPayload,
  destinationChoices,
  hasTransferProblems,
  originChoices,
  productChoices,
  transferProblems,
  warehouseLabel,
  type BranchRecord,
  type TechRecord,
  type TransferDraft,
  type TransferDraftLine,
  type TransferOutcome,
} from './transfers';

/** Los productos activos con su stock en la sucursal activa y si llevan serie. */
const PRODUCTS_REQUEST = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 };

export interface NewTransferDialogProps {
  open: boolean;
  branches: readonly BranchRecord[];
  /** Sucursal activa de la sesión (null = vista de todas las sucursales). */
  activeBranchId: string | null;
  onClose: () => void;
  onCreated: (outcome: TransferOutcome) => void;
}

function firstDraft(branches: readonly BranchRecord[], activeBranchId: string | null): TransferDraft {
  const origin = originChoices(branches, activeBranchId)[0] ?? null;
  const destination = destinationChoices(branches, origin?.branchCode ?? null)[0] ?? null;
  return { fromWarehouseCode: origin?.code ?? '', toWarehouseCode: destination?.code ?? '', notes: '', lines: [] };
}

export function NewTransferDialog({ open, branches, activeBranchId, onClose, onCreated }: NewTransferDialogProps) {
  const formId = useId();
  const [draft, setDraft] = useState<TransferDraft>(() => firstDraft(branches, activeBranchId));
  const [touched, setTouched] = useState(false);
  const products = useRpcQuery('SearchTechProductsQuery', PRODUCTS_REQUEST, { enabled: open });
  const create = useRpcCommand('CreateTransferCommand', { notifyError: false });

  // Si el directorio llega después de abrir, se proponen el primer origen y el primer destino.
  if (draft.fromWarehouseCode === '' && draft.toWarehouseCode === '' && draft.lines.length === 0 && branches.length > 0) {
    const next = firstDraft(branches, activeBranchId);
    if (next.fromWarehouseCode) setDraft(next);
  }

  const origins = originChoices(branches, activeBranchId);
  const origin = origins.find((choice) => choice.code === draft.fromWarehouseCode) ?? null;
  const destinations = destinationChoices(branches, origin?.branchCode ?? null);
  const problems = transferProblems(draft);
  const shown = <K extends 'fromWarehouseCode' | 'toWarehouseCode' | 'notes' | 'lines'>(field: K) => (touched ? problems[field] : undefined);
  const choices = productChoices(products.data ?? [], draft.lines);

  const change = (update: Partial<TransferDraft>) => {
    setDraft((current) => ({ ...current, ...update }));
    if (create.error) create.reset();
  };
  const changeOrigin = (code: string) => {
    const branch = origins.find((choice) => choice.code === code)?.branchCode ?? null;
    setDraft((current) => {
      const still = destinationChoices(branches, branch).some((choice) => choice.code === current.toWarehouseCode);
      // Las series dependen del almacén de origen: se vuelven a elegir.
      return { ...current, fromWarehouseCode: code, toWarehouseCode: still ? current.toWarehouseCode : '', lines: current.lines.map((line) => ({ ...line, serials: [] })) };
    });
    if (create.error) create.reset();
  };
  const changeLine = (sku: string, update: Partial<TransferDraftLine>) => {
    setDraft((current) => ({ ...current, lines: current.lines.map((line) => (line.sku === sku ? { ...line, ...update } : line)) }));
    if (create.error) create.reset();
  };
  const removeLine = (sku: string) => setDraft((current) => ({ ...current, lines: current.lines.filter((line) => line.sku !== sku) }));
  const pick = (product: TechRecord | undefined) => {
    if (product) setDraft((current) => addLine(current, product));
  };

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (hasTransferProblems(problems)) return;
    const outcome = await create.run(createTransferPayload(draft));
    if (outcome.ok) {
      onCreated(outcome.result);
      close();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!create.sending}
      size="xl"
      title="Nueva transferencia"
      description="Queda pendiente: la mercadería sale recién cuando el origen la despacha."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ArrowRightLeft />} loading={create.sending}>
            Solicitar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        {branches.length > 0 && origins.length === 0 && (
          <Alert tone="warning">Su sucursal activa no tiene almacenes visibles: elija otra sucursal activa en la barra superior.</Alert>
        )}
        <FormGrid>
          <SelectField
            label="Sale de (almacén de su sucursal)"
            allLabel={false}
            placeholder="Elija el almacén de origen"
            value={draft.fromWarehouseCode}
            onChange={changeOrigin}
            options={origins.map((choice) => ({ value: choice.code, label: warehouseLabel(choice) }))}
            error={shown('fromWarehouseCode')}
            required
          />
          <SelectField
            label="Llega a (almacén de destino)"
            allLabel={false}
            placeholder="Elija el almacén de destino"
            value={draft.toWarehouseCode}
            onChange={(value) => change({ toWarehouseCode: value })}
            options={destinations.map((choice) => ({ value: choice.code, label: warehouseLabel(choice) }))}
            error={shown('toWarehouseCode')}
            required
            data-autofocus
          />
          <TextArea label="Notas" value={draft.notes} onChange={(value) => change({ notes: value })} maxLength={LIMITS.notes} rows={2} error={shown('notes')} className="sm:col-span-2" optional />
        </FormGrid>

        <fieldset className="min-w-0 space-y-3">
          <legend className="text-sm font-semibold text-text">Productos</legend>
          {products.error && (
            <Alert
              tone="danger"
              title="No se pudo cargar el catálogo"
              actions={
                <Button variant="outline" leftIcon={<RotateCcw />} onClick={products.reload}>
                  Reintentar
                </Button>
              }
            >
              Sin el catálogo no se pueden agregar productos.
            </Alert>
          )}
          <ComboBox<TechRecord>
            label="Agregar producto"
            placeholder="Nombre o SKU"
            value={null}
            onChange={(option) => pick(option?.data)}
            options={choices}
            disabled={!products.data}
            hint={products.loading ? 'Cargando el catálogo…' : 'El stock es el de su sucursal activa.'}
          />
          {draft.lines.length === 0 ? (
            <p className={touched && problems.lines ? 'text-sm text-danger-text' : 'text-sm text-text-muted'} role={touched && problems.lines ? 'alert' : undefined}>
              {touched && problems.lines ? problems.lines : 'Agregue productos con la lista «Agregar producto».'}
            </p>
          ) : (
            <ul className="space-y-2" aria-label="Productos de la transferencia">
              {draft.lines.map((line) => {
                const errors = touched ? problems.byLine[line.sku] : undefined;
                return (
                  <li key={line.sku} className="space-y-3 rounded-xl border border-border bg-surface-2/40 p-3" data-testid={`linea-${line.sku}`}>
                    <div className="grid grid-cols-1 items-start gap-3 sm:grid-cols-[minmax(0,1fr)_10rem_auto]">
                      <div className="min-w-0 pt-1">
                        <p className="font-medium break-words text-text">{line.name}</p>
                        <p className="text-xs text-text-muted">
                          {line.sku} · stock {formatQuantity(line.stock)}
                          {line.trackSerials ? ` · con ${line.serialKind === 'Imei' ? 'IMEI' : 'serie'}` : ''}
                        </p>
                      </div>
                      <NumberField
                        label="Cantidad"
                        value={line.quantity}
                        onChange={(value) => changeLine(line.sku, { quantity: value, serials: line.trackSerials ? line.serials.slice(0, Math.max(0, Math.trunc(value ?? 0))) : line.serials })}
                        decimals={line.trackSerials ? 0 : 3}
                        error={errors?.quantity}
                        required
                      />
                      <IconButton label={`Quitar ${line.name}`} icon={<Trash2 />} className="sm:mt-6" onClick={() => removeLine(line.sku)} />
                    </div>
                    {line.trackSerials && (line.quantity ?? 0) > 0 && (
                      <SerialPicker
                        key={`${line.sku}-${draft.fromWarehouseCode}`}
                        line={line}
                        warehouseCode={draft.fromWarehouseCode}
                        onChange={(serials) => changeLine(line.sku, { serials })}
                        error={errors?.serials}
                      />
                    )}
                  </li>
                );
              })}
            </ul>
          )}
          <p className="text-right text-sm text-text-muted">{draft.lines.length === 1 ? '1 producto' : `${formatNumber(draft.lines.length)} productos`}</p>
        </fieldset>
      </Form>
    </Dialog>
  );
}
