// Módulo «Toma física» · «Registrar un conteo» (el panel izquierdo del escritorio): el LECTOR DE CÓDIGOS funciona como
// teclado (escribe el código y Enter: el producto queda elegido y el foco pasa a la cantidad), o se elige el producto de
// la lista con búsqueda; la posición del almacén de la toma; lo que dice el sistema en esa posición y la diferencia
// (guía: la que manda es la del servidor al registrar) y `RecordCountCommand`. Si el producto ya se contó en esa
// posición, el nuevo conteo lo corrige.

import { ClipboardPlus, ScanBarcode } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, ComboBox, Form, FormGrid, NumberField, Section, SelectField, StatusBadge, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { formatQuantity, formatTime } from '@/4-presentation/panel/lib';
import {
  DIFFERENCES,
  binOptions,
  defaultBin,
  differenceText,
  findByCode,
  kindOf,
  systemAt,
  toRecordCount,
  type BinRecord,
  type CountSheetRecord,
  type LookupRecord,
} from './count';

/** Pedido de la tabla («Corregir conteo», «Contar»): carga ese producto (y posición) en el formulario. */
export interface CountRequest {
  sku: string;
  binCode: string | null;
  /** Cambia en cada pedido (dos pedidos iguales seguidos también se atienden). */
  nonce: number;
}

export interface RecordCountFormProps {
  sheet: CountSheetRecord;
  products: readonly LookupRecord[];
  productsLoading: boolean;
  bins: readonly BinRecord[];
  /** La toma es del almacén de trabajo: se puede mostrar lo que dice el sistema antes de registrar. */
  preview: boolean;
  request: CountRequest | null;
  onRecorded: () => void;
}

function optionOf(product: LookupRecord): ComboOption<LookupRecord> {
  return {
    value: product.sku,
    label: product.name,
    description: [product.sku, product.category, product.primaryBin].filter(Boolean).join(' · '),
    data: product,
  };
}

export function RecordCountForm({ sheet, products, productsLoading, bins, preview, request, onRecorded }: RecordCountFormProps) {
  const formId = useId();
  const notify = useNotify();
  const scanRef = useRef<HTMLInputElement>(null);
  const quantityRef = useRef<HTMLInputElement>(null);
  const [scan, setScan] = useState('');
  const [product, setProduct] = useState<ComboOption<LookupRecord> | null>(null);
  const [binCode, setBinCode] = useState('');
  const [quantity, setQuantity] = useState<number | null>(null);
  const [touched, setTouched] = useState(false);
  const [focus, setFocus] = useState<{ target: 'cantidad' | 'lector'; count: number }>({ target: 'lector', count: 0 });
  const record = useRpcCommand('RecordCountCommand', { notifyError: false });

  const chosen = product?.data ?? null;
  const card = useRpcQuery('GetProductCardQuery', { skuOrBarcode: chosen?.sku ?? '', take: 1 }, { enabled: chosen !== null && preview, keepPreviousData: false });

  const choose = (next: LookupRecord | null, bin?: string | null) => {
    setProduct(next ? optionOf(next) : null);
    setBinCode(next ? (bin ?? defaultBin(next, undefined, bins)) : '');
    setQuantity(null);
    setTouched(false);
    record.reset();
  };

  // Pedido de la tabla: se atiende una vez por `nonce`.
  const [handled, setHandled] = useState(0);
  if (request && request.nonce !== handled) {
    setHandled(request.nonce);
    const found = products.find((item) => item.sku.toUpperCase() === request.sku.toUpperCase());
    if (found) {
      choose(found, request.binCode);
      setFocus((current) => ({ target: 'cantidad', count: current.count + 1 }));
    }
  }

  useEffect(() => {
    if (focus.count === 0) return;
    (focus.target === 'cantidad' ? quantityRef.current : scanRef.current)?.focus();
  }, [focus]);

  const onScan = () => {
    const code = scan.trim();
    if (!code) return;
    const found = findByCode(products, code);
    setScan('');
    if (!found) {
      notify.warning('Código no encontrado', `«${code}» no es un SKU ni un código de barras de un producto activo.`);
      return;
    }
    choose(found);
    setFocus((current) => ({ target: 'cantidad', count: current.count + 1 }));
  };

  const system = preview && chosen ? systemAt(card.data, binCode) : null;
  const difference = system !== null && quantity !== null ? quantity - system : null;
  const already = chosen ? sheet.lines.find((line) => line.sku.toUpperCase() === chosen.sku.toUpperCase() && line.binCode === binCode) : undefined;
  const problems = {
    product: chosen ? undefined : 'Escanee o elija el producto.',
    bin: binCode ? undefined : 'Elija la posición.',
    quantity: quantity === null ? 'Indique la cantidad contada (0 si no hay ninguno).' : undefined,
  };

  const submit = async () => {
    setTouched(true);
    if (!chosen || !binCode || quantity === null) return;
    const outcome = await record.run(toRecordCount(sheet.id, chosen.sku, binCode, quantity));
    if (!outcome.ok) return;
    notify.success('Conteo registrado', `${chosen.sku}: ${formatQuantity(quantity, { unit: chosen.unit })} en ${binCode}`);
    choose(null);
    onRecorded();
    setFocus((current) => ({ target: 'lector', count: current.count + 1 }));
  };

  const binChoices = binOptions(bins);
  if (binCode && !binChoices.some((option) => option.value === binCode)) binChoices.unshift({ value: binCode, label: binCode });

  return (
    <Section title="Registrar un conteo" description="Escanee el código (el lector escribe y termina con Enter) o elija el producto de la lista. Si ya se contó en esa posición, se corrige.">
      <Form
        id={formId}
        onSubmit={submit}
        error={record.errorText}
        busy={record.sending}
        actions={
          <Button type="submit" leftIcon={<ClipboardPlus />} loading={record.sending}>
            Registrar conteo
          </Button>
        }
      >
        <TextField
          ref={scanRef}
          label="Código de barras o SKU"
          value={scan}
          onChange={setScan}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              onScan();
            }
          }}
          leading={<ScanBarcode aria-hidden="true" className="size-4" />}
          placeholder="Escanee o escriba y pulse Enter"
          autoComplete="off"
          spellCheck={false}
          hint={productsLoading ? 'Cargando los productos…' : undefined}
          autoFocus
        />
        <FormGrid>
          <ComboBox<LookupRecord>
            label="Producto"
            value={product}
            onChange={(option) => choose(option?.data ?? null)}
            options={products.map(optionOf)}
            placeholder="Nombre, SKU o categoría"
            error={touched ? problems.product : undefined}
            required
          />
          <SelectField
            label="Posición"
            allLabel={false}
            placeholder="Elija la posición"
            value={binCode}
            onChange={(value) => {
              setBinCode(value);
              record.reset();
            }}
            options={binChoices}
            error={touched ? problems.bin : undefined}
            required
          />
          <NumberField
            ref={quantityRef}
            label="Cantidad contada"
            value={quantity}
            onChange={(value) => {
              setQuantity(value);
              record.reset();
            }}
            decimals={chosen?.allowsDecimals ? 3 : 0}
            unit={chosen?.unit}
            error={touched ? problems.quantity : undefined}
            required
          />
          <div className="min-w-0 space-y-1" data-testid="guia-conteo" aria-live="polite">
            <p className="text-sm font-medium text-text">En el sistema</p>
            <p className="text-sm text-text-muted">
              {!chosen
                ? '—'
                : !preview
                  ? 'Se ve al registrar (la toma es de otro almacén).'
                  : card.loading
                    ? 'Leyendo…'
                    : system === null
                      ? '—'
                      : formatQuantity(system, { unit: chosen.unit })}
            </p>
            {difference !== null && (
              <StatusBadge tone={DIFFERENCES[kindOf(difference)].tone}>
                {difference === 0 ? 'Cuadra' : `${DIFFERENCES[kindOf(difference)].label}: ${differenceText(difference, chosen?.unit)}`}
              </StatusBadge>
            )}
            {already && (
              <p className="text-xs text-text-muted">
                Ya contado: {formatQuantity(already.countedQuantity, { unit: already.unit })}
                {already.countedBy ? ` por ${already.countedBy}` : ''} a las {formatTime(already.countedAt)}. Registrar de nuevo lo corrige.
              </p>
            )}
          </div>
        </FormGrid>
      </Form>
    </Section>
  );
}
