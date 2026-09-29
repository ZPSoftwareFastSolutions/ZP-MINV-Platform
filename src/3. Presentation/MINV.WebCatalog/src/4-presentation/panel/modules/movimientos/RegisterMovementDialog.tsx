// Módulo «Movimientos» · registrar un movimiento (en el escritorio: MovementView + MovementViewModel): tipo → producto →
// posición y cantidad, con la vista previa de lo que quedará en la posición y el POKA-YOKE visible: si la salida
// dejaría la posición en negativo, el campo «Cantidad» se marca en rojo y el botón se deshabilita (la validación que
// manda es la del servidor). Observación obligatoria en los ajustes y devoluciones; series (una por unidad) si el
// producto las exige: las que entran se escriben; las que salen se eligen de las disponibles (o se escriben, si la
// sesión no puede ver las series).
//
// Operaciones: GetWorkspaceQuery (almacén de trabajo), GetBinsQuery, GetProductLookupQuery, GetProductCardQuery
// (existencias por posición), GetProductTechQuery (¿lleva serie?), GetAvailableSerialsQuery y RegisterMovementCommand
// (con TODOS sus parámetros). El error del servidor se muestra dentro del diálogo, sin cerrarlo.

import { ArrowLeftRight, Info, TriangleAlert } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, ComboBox, Dialog, FieldGroup, Form, FormGrid, NumberField, SelectField, TextArea, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  binAvailable,
  defaultBin,
  hasProblems,
  increases,
  initialType,
  movementProblems,
  parseSerials,
  projectedStock,
  registerPayload,
  registeredText,
  serialKindLabel,
  serialTypeNotice,
  typeOptions,
  typeTitle,
  wouldBeNegative,
  type MovementTypeRecord,
  type ProductLookupRecord,
} from './movements';

export interface RegisterMovementDialogProps {
  open: boolean;
  /** Lo pedido en la dirección (`registrar`): un código de tipo, `ajuste` o `1`. */
  requestedType: string | null;
  /** Producto ya elegido (`sku` de la dirección), si vino de otra pantalla. */
  presetSku: string | null;
  /** Tipos que la sesión puede registrar. */
  types: readonly MovementTypeRecord[];
  typesLoading: boolean;
  onClose: () => void;
  /** El movimiento quedó registrado (para volver a leer la lista). */
  onRegistered: () => void;
}

/** Cuántas series disponibles se muestran para elegir (las demás, escribiendo). */
const MAX_PICKABLE = 60;

function productOption(item: ProductLookupRecord): ComboOption<ProductLookupRecord> {
  const barcodes = item.barcodes.length > 0 ? ` · ${item.barcodes.join(' ')}` : '';
  return { value: item.sku, label: item.name, description: `${item.sku} · ${item.category}${barcodes}`, data: item };
}

export function RegisterMovementDialog({ open, requestedType, presetSku, types, typesLoading, onClose, onRegistered }: RegisterMovementDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [typeCode, setTypeCode] = useState(() => initialType(requestedType, types));
  const [product, setProduct] = useState<ComboOption<ProductLookupRecord> | null>(null);
  const [presetDone, setPresetDone] = useState(presetSku === null);
  const [missingPreset, setMissingPreset] = useState<string | null>(null);
  const [binCode, setBinCode] = useState('');
  const [binFor, setBinFor] = useState<string | null>(null);
  const [quantity, setQuantity] = useState<number | null>(null);
  const [documentRef, setDocumentRef] = useState('');
  const [notes, setNotes] = useState('');
  const [serialsText, setSerialsText] = useState('');
  const [picked, setPicked] = useState<ReadonlySet<string>>(() => new Set());
  const [touched, setTouched] = useState(false);
  const command = useRpcCommand('RegisterMovementCommand', { notifyError: false });

  // Los tipos pueden llegar después de abrir: entonces se elige el pedido (o el primero permitido).
  if (typeCode === '' && types.length > 0) setTypeCode(initialType(requestedType, types));
  const type = types.find((item) => item.code === typeCode);

  const workspace = useRpcQuery('GetWorkspaceQuery', {}, { enabled: open });
  const warehouseCode = workspace.data?.warehouseCode ?? null;
  const bins = useRpcQuery('GetBinsQuery', { warehouseCode }, { enabled: open && warehouseCode !== null });
  const lookup = useRpcQuery('GetProductLookupQuery', { includeInactive: false }, { enabled: open });
  const options = useMemo(() => (lookup.data ?? []).map(productOption), [lookup.data]);

  // Producto que vino en la dirección: se elige cuando llega el catálogo.
  if (!presetDone && presetSku && lookup.data) {
    const wanted = presetSku.trim().toUpperCase();
    const found = lookup.data.find((item) => item.sku.toUpperCase() === wanted || item.barcodes.includes(presetSku.trim()));
    setPresetDone(true);
    if (found) setProduct(productOption(found));
    else setMissingPreset(presetSku);
  }

  const sku = product?.value ?? null;
  const card = useRpcQuery('GetProductCardQuery', { skuOrBarcode: sku ?? '', take: 1 }, { enabled: open && sku !== null, keepPreviousData: false });
  const tech = useRpcQuery('GetProductTechQuery', { sku: sku ?? '' }, { enabled: open && sku !== null, keepPreviousData: false });
  // Posición con la que se abre cada producto (la principal o la que más tiene); después la elige la persona.
  if (sku !== null && card.data && binFor !== sku) {
    setBinFor(sku);
    setBinCode(defaultBin(card.data, bins.data ?? []));
  }

  const trackSerials = tech.data?.trackSerials ?? false;
  const serialKind = tech.data?.serialKind ?? 'Serial';
  const leaving = type !== undefined && !increases(type);
  const canPick = trackSerials && leaving && canRun('GetAvailableSerialsQuery');
  const availableSerials = useRpcQuery('GetAvailableSerialsQuery', { sku: sku ?? '', warehouseCode }, { enabled: open && canPick && sku !== null, keepPreviousData: false });
  const pickMode = canPick && availableSerials.error === null;
  const serials = pickMode ? [...picked] : parseSerials(serialsText);

  const unit = card.data?.unit ?? product?.data?.unit ?? '';
  const allowsDecimals = card.data?.allowsDecimals ?? product?.data?.allowsDecimals ?? true;
  const draft = { type, sku, binCode, quantity, allowsDecimals, document: documentRef, notes, trackSerials, serials, available: binAvailable(card.data, binCode) };
  const problems = movementProblems(draft);
  const negative = wouldBeNegative(draft);
  const projected = projectedStock(draft.available, type, quantity);
  const loadingProduct = sku !== null && (card.loading || tech.loading);
  // El poka-yoke se ve al instante; el resto de los problemas, al intentar registrar.
  const shown = (message: string | undefined) => (touched ? message : undefined);

  const changed = () => {
    if (command.error) command.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (!type || !sku || hasProblems(problems)) return;
    const outcome = await command.run(registerPayload(draft));
    if (outcome.ok) {
      notify.success('Movimiento registrado', `${typeTitle(type.name)}: ${registeredText(type, quantity ?? 0, unit, sku, outcome.result.quantityOnHand, binCode)}`);
      onRegistered();
      onClose();
    }
  };

  const togglePicked = (serial: string, checked: boolean) =>
    setPicked((current) => {
      const next = new Set(current);
      if (checked) next.add(serial);
      else next.delete(serial);
      return next;
    });

  const binOptions = (bins.data ?? []).map((bin) => ({ value: bin.code, label: bin.zone ? `${bin.code} · ${bin.zone}` : bin.code }));
  const kindLabel = serialKindLabel(serialKind);
  const pickable = (availableSerials.data ?? []).slice(0, MAX_PICKABLE);

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!command.sending}
      size="lg"
      title="Registrar un movimiento"
      description={`Se registra en ${warehouseCode ? `el almacén ${warehouseCode} de la sucursal activa` : 'la sucursal activa'}. Para corregir un error, registre un ajuste: los movimientos no se borran.`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={command.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ArrowLeftRight />} loading={command.sending} disabled={negative || loadingProduct}>
            {type ? `Registrar ${typeTitle(type.name).toLocaleLowerCase('es')}` : 'Registrar'}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={command.errorText} busy={command.sending}>
        {missingPreset && (
          <Alert tone="warning">El producto {missingPreset} no está en el catálogo activo. Elija otro producto.</Alert>
        )}
        <FormGrid>
          <SelectField
            label="Tipo de movimiento"
            allLabel={false}
            placeholder={typesLoading ? 'Cargando los tipos…' : 'Elija el tipo'}
            value={typeCode}
            onChange={(value) => {
              setTypeCode(value);
              setPicked(new Set());
              changed();
            }}
            options={typeOptions(types)}
            disabled={types.length === 0}
            required
            error={shown(problems.type)}
            hint={type ? `${type.description ?? ''}${type.requiresNotes ? ' Exige observación.' : ''}`.trim() : undefined}
            className="sm:col-span-2"
          />
          <ComboBox
            label="Producto"
            placeholder="Nombre, SKU o código de barras"
            value={product}
            onChange={(option) => {
              setProduct(option);
              setSerialsText('');
              setPicked(new Set());
              setMissingPreset(null);
              changed();
            }}
            options={options}
            required
            error={shown(problems.product) ?? (lookup.error ? 'No se pudo cargar el catálogo de productos.' : undefined)}
            hint={lookup.loading ? 'Cargando el catálogo…' : undefined}
            className="sm:col-span-2"
          />
          {card.data && (
            <div className="rounded-xl border border-border bg-surface-2 p-3 text-sm sm:col-span-2" data-testid="producto-elegido">
              <p className="font-medium text-text">
                {card.data.name} <span className="font-normal text-text-muted">· {card.data.sku}</span>
              </p>
              <p className="text-text-muted">
                Stock total {formatQuantity(card.data.onHand, { unit: card.data.unit })} · disponible {formatQuantity(card.data.available)} · mín. {formatQuantity(card.data.minimum)} · máx.{' '}
                {formatQuantity(card.data.maximum)}
              </p>
            </div>
          )}
          {card.error && (
            <Alert tone="danger" className="sm:col-span-2">
              No se pudo abrir el producto: vuelva a elegirlo.
            </Alert>
          )}
          <SelectField
            label="Posición"
            allLabel={false}
            placeholder={bins.loading ? 'Cargando las posiciones…' : 'Elija la posición'}
            value={binCode}
            onChange={(value) => {
              setBinCode(value);
              changed();
            }}
            options={binOptions}
            required
            error={shown(problems.bin)}
          />
          <NumberField
            label="Cantidad"
            value={quantity}
            onChange={(value) => {
              setQuantity(value);
              changed();
            }}
            decimals={allowsDecimals ? 3 : 0}
            unit={unit || undefined}
            required
            error={negative ? problems.quantity : shown(problems.quantity)}
          />
          {sku !== null && binCode && type && (
            <div
              role="status"
              data-testid="vista-previa"
              className={
                negative
                  ? 'rounded-xl border border-danger/60 bg-danger-soft p-3 text-sm text-danger-text sm:col-span-2'
                  : 'rounded-xl border border-border bg-surface-2 p-3 text-sm text-text sm:col-span-2'
              }
            >
              En {binCode}: {formatQuantity(draft.available, { unit })} disponibles
              {projected !== null && ` → quedarán ${formatQuantity(projected, { unit })}`}
            </div>
          )}
          <TextField label="Documento" value={documentRef} onChange={setDocumentRef} placeholder="Factura, remisión, guía…" maxLength={30} optional />
          <TextArea
            label="Observaciones"
            value={notes}
            onChange={(value) => {
              setNotes(value);
              changed();
            }}
            maxLength={250}
            rows={3}
            required={type?.requiresNotes ?? false}
            optional={!(type?.requiresNotes ?? false)}
            error={shown(problems.notes)}
            hint={type?.requiresNotes ? 'Obligatorias para este tipo: el motivo queda en el kardex.' : undefined}
            className="sm:col-span-2"
          />
        </FormGrid>

        {trackSerials && (
          <section className="space-y-3" data-testid="series">
            <Alert tone="info" title={`Lleva ${kindLabel}`}>
              Indique {serialKindLabel(serialKind, true)} de cada unidad (una por unidad) · {formatNumber(tech.data?.serialsInStock ?? 0)} en stock con serie.
            </Alert>
            {type && serialTypeNotice(type.code) && (
              <p className="flex items-start gap-2 text-sm text-warning-text">
                <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                {serialTypeNotice(type.code)}
              </p>
            )}
            {pickMode ? (
              <FieldGroup
                label={`Elija las unidades que salen (${formatNumber(picked.size)} de ${formatNumber(quantity ?? 0)})`}
                error={shown(problems.serials)}
                hint={availableSerials.loading ? 'Cargando las series disponibles…' : undefined}
              >
                {!availableSerials.loading && pickable.length === 0 && <p className="text-sm text-text-muted">No hay unidades con serie disponibles en esta sucursal.</p>}
                <div className="grid grid-cols-1 gap-x-4 sm:grid-cols-2">
                  {pickable.map((unitSerial) => (
                    <Checkbox
                      key={unitSerial.serial}
                      label={unitSerial.serial}
                      description={unitSerial.warehouse ?? undefined}
                      checked={picked.has(unitSerial.serial)}
                      onChange={(checked) => togglePicked(unitSerial.serial, checked)}
                    />
                  ))}
                </div>
              </FieldGroup>
            ) : (
              <TextArea
                label={`${kindLabel === 'IMEI' ? 'IMEI' : 'Números de serie'} (uno por línea)`}
                value={serialsText}
                onChange={(value) => {
                  setSerialsText(value);
                  changed();
                }}
                rows={4}
                error={shown(problems.serials)}
                hint={`Escritos: ${formatNumber(parseSerials(serialsText).length)} de ${formatNumber(quantity ?? 0)}. Puede leerlos con el escáner.`}
              />
            )}
          </section>
        )}

        {!trackSerials && sku !== null && tech.error === null && tech.data && (
          <p className="flex items-center gap-2 text-sm text-text-muted">
            <Info aria-hidden="true" className="size-4" />
            Este producto no lleva serie.
          </p>
        )}
      </Form>
    </Dialog>
  );
}
