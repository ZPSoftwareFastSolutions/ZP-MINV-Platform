// Módulo «Caja» · la venta en curso: las líneas (cantidad con − y +, descuento por línea, series o IMEI con «Elegir
// unidades», quitar), los avisos que impiden cobrar (series que faltan, más de lo disponible: disponible = existencias −
// reservado, lo reservado nunca se vende a otro cliente), la reserva o cotización cargada (líneas fijas a precio
// congelado), los totales con el IVA incluido y el botón «Cobrar» (F4).

import { Barcode, Minus, PackageCheck, Plus, Receipt, ShoppingCart, Trash2, X } from 'lucide-react';
import { useId, useState, type Dispatch, type ReactNode } from 'react';
import { Alert, Button, EmptyState, IconButton, NumberField, SelectField, StatusBadge } from '@/4-presentation/panel/kit';
import { formatMoney, formatQuantity } from '@/4-presentation/panel/lib';
import { BUILD_KINDS, buildStatusLabel, buildSummary } from './builds';
import {
  DISCOUNT_OPTIONS,
  cartTotals,
  includedTax,
  itemsText,
  lineAmount,
  lineStock,
  missingSerials,
  serialsNeededText,
  stockWarning,
  type CajaCart,
  type CajaLine,
  type CartAction,
} from './cart';
import { serialLabel, type CajaProduct } from './products';

export interface CartPanelProps {
  id: string;
  cart: CajaCart;
  products: readonly CajaProduct[];
  dispatch: Dispatch<CartAction>;
  taxRate: number;
  vatOnInvoicedAmount: boolean;
  /** Por qué no se puede cobrar ahora (null = se puede). */
  chargeBlocked: string | null;
  onCharge: () => void;
  onClear: () => void;
  onPickSerials: (line: CajaLine) => void;
  canPickSerials: boolean;
  onRemoveBuild: () => void;
  /** Aviso de la reserva de la dirección (cargando, no se puede cobrar, sin permiso). */
  buildNotice?: ReactNode;
  /** Lo que impidió cobrar en el último intento. */
  problems: readonly string[];
  /** Última acción (se anuncia a los lectores de pantalla). */
  status: string | null;
}

const DISCOUNTS = DISCOUNT_OPTIONS.map((value) => ({ value: String(value), label: `${value} %` }));

export function CartPanel({
  id,
  cart,
  products,
  dispatch,
  taxRate,
  vatOnInvoicedAmount,
  chargeBlocked,
  onCharge,
  onClear,
  onPickSerials,
  canPickSerials,
  onRemoveBuild,
  buildNotice,
  problems,
  status,
}: CartPanelProps) {
  const titleId = useId();
  const totals = cartTotals(cart.lines);
  const tax = includedTax(totals.total, taxRate, vatOnInvoicedAmount);
  const build = cart.build;

  return (
    <section
      id={id}
      aria-labelledby={titleId}
      tabIndex={-1}
      className="min-w-0 rounded-card border border-border bg-surface shadow-card outline-none xl:sticky xl:top-4"
      data-testid="venta-actual"
    >
      <header className="flex flex-wrap items-start justify-between gap-2 border-b border-border p-4">
        <div className="min-w-0">
          <h2 id={titleId} className="text-lg font-semibold">
            Venta actual
          </h2>
          <p className="text-sm text-text-muted" data-testid="venta-resumen">
            {itemsText(totals)}
          </p>
        </div>
        <Button variant="ghost" leftIcon={<Trash2 />} disabled={cart.lines.length === 0 && !build} onClick={onClear}>
          Vaciar
        </Button>
      </header>

      {buildNotice && <div className="border-b border-border p-4">{buildNotice}</div>}

      {build && (
        <div className="space-y-1 border-b border-border bg-primary-soft/40 p-4" data-testid="reserva-en-venta">
          <div className="flex flex-wrap items-center gap-2">
            <PackageCheck aria-hidden="true" className="size-5 text-primary-text" />
            <p className="font-semibold">
              {build.kind === 'Cart' ? 'Reserva' : 'Armado'} {build.number}
            </p>
            <StatusBadge status={build.kind} statuses={BUILD_KINDS} />
            <StatusBadge tone={build.status === 'Reserved' ? 'accent' : 'info'}>{buildStatusLabel(build)}</StatusBadge>
          </div>
          <p className="text-sm font-medium">{build.name}</p>
          <p className="text-sm text-text-muted">{buildSummary(build)}</p>
          <Button variant="ghost" leftIcon={<X />} onClick={onRemoveBuild}>
            Quitar la reserva de la venta
          </Button>
        </div>
      )}

      <p role="status" aria-live="polite" className="sr-only">
        {status ?? ''}
      </p>

      {cart.lines.length === 0 ? (
        <EmptyState
          size="sm"
          icon={<ShoppingCart />}
          title="La venta está vacía"
          description="Agregue productos con el buscador o el lector de códigos (F2), o venda una reserva."
        />
      ) : (
        <ul className="divide-y divide-border" aria-label="Productos de la venta">
          {cart.lines.map((line) => (
            <CartLineItem key={line.key} line={line} cart={cart} products={products} dispatch={dispatch} onPickSerials={onPickSerials} canPickSerials={canPickSerials} />
          ))}
        </ul>
      )}

      <div className="space-y-3 border-t border-border bg-surface-2/60 p-4">
        {totals.discount > 0 && (
          <p className="flex justify-between text-sm text-text-muted">
            <span>Descuentos</span>
            <span className="tabular-nums">−{formatMoney(totals.discount)}</span>
          </p>
        )}
        <div className="flex items-end justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-text-muted">Total</p>
            <p className="text-xs text-text-muted">IVA incluido ({formatQuantity(taxRate)} %): {formatMoney(tax)}</p>
          </div>
          <p className="font-display text-3xl font-bold tabular-nums" data-testid="venta-total">
            {formatMoney(totals.total)}
          </p>
        </div>
        {problems.length > 0 && (
          <Alert tone="warning" title="Antes de cobrar">
            <ul className="list-disc space-y-1 pl-5">
              {problems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          </Alert>
        )}
        <Button size="lg" fullWidth leftIcon={<Receipt />} disabled={chargeBlocked !== null} onClick={onCharge} aria-keyshortcuts="F4">
          {cart.lines.length === 0 ? 'Agregue productos para cobrar' : `Cobrar ${formatMoney(totals.total)} (F4)`}
        </Button>
        {chargeBlocked && cart.lines.length > 0 && <p className="text-sm text-text-muted">{chargeBlocked}</p>}
      </div>
    </section>
  );
}

interface CartLineItemProps {
  line: CajaLine;
  cart: CajaCart;
  products: readonly CajaProduct[];
  dispatch: Dispatch<CartAction>;
  onPickSerials: (line: CajaLine) => void;
  canPickSerials: boolean;
}

function CartLineItem({ line, cart, products, dispatch, onPickSerials, canPickSerials }: CartLineItemProps) {
  // El campo de la cantidad se vuelve a dibujar si queda vacío o en cero al salir (vuelve a la cantidad de la venta).
  const [revision, setRevision] = useState(0);
  const [draft, setDraft] = useState<number | null>(line.quantity);
  const stock = lineStock(line, cart, products);
  const missing = missingSerials(line);
  const editable = !line.locked && !line.serialized;

  return (
    <li className="space-y-2 p-4" data-testid="linea-venta" data-sku={line.sku}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium break-words">{line.name}</p>
          <p className="text-xs text-text-muted">
            <span className="font-mono">{line.sku}</span> · {formatMoney(line.unitPrice)} por {line.unit}
            {line.locked && ' · precio congelado'}
            {line.discountPercent > 0 && ` · descuento ${line.discountPercent} %`}
          </p>
        </div>
        <div className="flex shrink-0 items-start gap-1">
          <p className="pt-2 font-semibold tabular-nums">{formatMoney(lineAmount(line))}</p>
          {!line.locked && <IconButton label={`Quitar ${line.name} de la venta`} icon={<Trash2 />} onClick={() => dispatch({ type: 'remove', key: line.key })} />}
        </div>
      </div>

      {editable && (
        <div className="flex flex-wrap items-end gap-2">
          <IconButton label={`Una unidad menos de ${line.name}`} icon={<Minus />} variant="subtle" onClick={() => dispatch({ type: 'decrease', key: line.key })} />
          <NumberField
            key={revision}
            label={`Cantidad de ${line.name}`}
            hideLabel
            value={line.quantity}
            decimals={line.allowsDecimals ? 3 : 0}
            unit={line.unit}
            className="w-32"
            onChange={(value) => {
              setDraft(value);
              if (value !== null && value > 0) dispatch({ type: 'setQuantity', key: line.key, quantity: value });
            }}
            onBlur={() => {
              if (draft === null || draft <= 0) {
                setDraft(line.quantity);
                setRevision((current) => current + 1);
              }
            }}
          />
          <IconButton label={`Una unidad más de ${line.name}`} icon={<Plus />} variant="subtle" onClick={() => dispatch({ type: 'increase', key: line.key })} />
          <SelectField
            label={`Descuento de ${line.name}`}
            hideLabel
            value={String(line.discountPercent)}
            onChange={(value) => dispatch({ type: 'setDiscount', key: line.key, discount: Number(value) || 0 })}
            options={DISCOUNTS}
            allLabel={false}
            className="w-28"
          />
        </div>
      )}

      {line.locked && (
        <p className="text-sm text-text-muted">
          {formatQuantity(line.quantity)} {line.quantity === 1 ? 'unidad' : 'unidades'} · cantidad y precio de la reserva
        </p>
      )}

      {line.serialized && (
        <div className="space-y-1 rounded-xl bg-surface-2 p-2">
          {line.serials.length > 0 ? (
            <p className="text-xs break-all">
              <span className="font-semibold">{serialLabel(line.serialKind)}:</span> <span className="font-mono">{line.serials.join(', ')}</span>
            </p>
          ) : null}
          {missing > 0 && <p className="text-sm font-medium text-warning-text">{serialsNeededText(line)}</p>}
          {canPickSerials && (
            <Button variant="subtle" leftIcon={<Barcode />} onClick={() => onPickSerials(line)}>
              {line.locked ? 'Elegir las unidades' : 'Cambiar o agregar unidades'}
            </Button>
          )}
        </div>
      )}

      {stock.exceeds && (
        <p className="text-sm font-medium text-danger-text" role="alert">
          {stockWarning(stock)}
        </p>
      )}
    </li>
  );
}
