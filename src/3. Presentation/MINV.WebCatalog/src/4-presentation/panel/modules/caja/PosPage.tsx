// Ventas › Caja: el punto de venta del panel (el equivalente web de «Punto de venta» del escritorio, `PosView` +
// `PosViewModel` + `PosFiscal`), a pantalla completa y pensado para trabajar rápido con el teclado y el lector de códigos.
//   1. Turno: sin turno, «Abrir caja» (caja y fondo inicial); con turno, su resumen y «Cerrar caja» con arqueo.
//   2. Banda fiscal (si la empresa factura) y atajos visibles: F2 buscar o escanear, F4 cobrar, Esc cancelar.
//   3. Productos a la venta (buscador + lector de códigos, filtros, tabla) y la venta en curso (series o IMEI,
//      descuentos, lo disponible nunca incluye lo reservado).
//   4. Cobro (cliente, datos de la factura, medio de pago, vuelto) y, después, la factura enviada al SIN con el
//      comprobante para imprimir, el PDF y el correo.
//   5. Vender una reserva o cotización: `?reserva=<NÚMERO>` (desde Reservas y Armador de PC) o «Vender una reserva».
// Todo lo decide el servidor (regla P-01): la página guía, muestra y envía los casos de uso de `MINV.Application`.

import { Maximize2, Minimize2, PackageCheck, RefreshCw, Receipt, ShoppingCart, LockOpen, Lock, Keyboard } from 'lucide-react';
import { useEffect, useId, useMemo, useReducer, useRef, useState, type ReactNode } from 'react';
import { useBlocker, useSearchParams } from 'react-router-dom';
import { rpcOperation } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { AccessDenied, Alert, Button, ConfirmDialog, Dialog, ErrorState, Page, Skeleton, StatusBadge, useNotify } from '@/4-presentation/panel/kit';
import { formatMoney, missingPermissionsText } from '@/4-presentation/panel/lib';
import { buildProblem, buyerFromBuild, normalizeBuildNumber } from './builds';
import { posCapabilities } from './capabilities';
import { CartPanel } from './CartPanel';
import { EMPTY_CART, buildLines, cartProblems, cartReducer, cartTotals, itemsText, lineInfoFrom, serialsInOtherLines, type CajaLine } from './cart';
import { CheckoutDialog, type SoldContext } from './CheckoutDialog';
import { CloseSessionDialog } from './CloseSessionDialog';
import { EMPTY_BUYER, fiscalAfterDispatch, fiscalBand, initialFiscal, type BuyerDraft } from './fiscal';
import { OpenSessionDialog } from './OpenSessionDialog';
import { EMPTY_PAYMENT, afterSale, customerByName, withDefaults, type PaymentDraft } from './payment';
import { ProductCatalog } from './ProductCatalog';
import { productBySku, toCajaProducts, type CajaProduct } from './products';
import { ReservationPickerDialog } from './ReservationPickerDialog';
import { SaleResultDialog, type CompletedSale } from './SaleResultDialog';
import { SerialPickerDialog, type SerialPickTarget } from './SerialPickerDialog';
import { registerLabel, sessionSummary, suggestedRegister } from './session';
import type { BuildDetailData, PosSessionData, PosStateData, SaleResultData } from './types';
import { useShortcuts } from './useShortcuts';

// Pedidos fijos (TODOS los parámetros, regla del contrato): se comparan por valor.
const TECH_REQUEST = { text: null, categoryCode: null, platform: null, filters: null, onlyInStock: false, max: 2000 };
const RESERVED_REQUEST = { warehouseCode: null };
const SPECS_REQUEST = { categoryCode: null };
const EMPTY_PRODUCTS: readonly CajaProduct[] = [];

/** Parámetros de la dirección propios de la caja (además de los filtros de la lista). */
const RESERVATION_PARAM = 'reserva';
const PICKER_PARAM = 'elegir';

export function PosPage() {
  const notify = useNotify();
  const { canRun, can, missing, session } = usePermissions();
  const caps = useMemo(() => posCapabilities(canRun), [canRun]);
  const [params, setParams] = useSearchParams();
  const searchRef = useRef<HTMLInputElement>(null);
  const cartId = useId();

  // ------------------------------------------------------------------------------------------------ el servidor
  const pos = useRpcQuery('GetPosStateQuery', {});
  const fiscal = useRpcQuery('GetPosFiscalStateQuery', {});
  const sellable = useRpcQuery('GetSellableProductsQuery', {}, { enabled: caps.products });
  const tech = useRpcQuery('SearchTechProductsQuery', TECH_REQUEST, { enabled: caps.products && caps.tech });
  const held = useRpcQuery('GetStockReservationsQuery', RESERVED_REQUEST, { enabled: caps.products && caps.reservedStock });
  const specs = useRpcQuery('GetSpecDefinitionsQuery', SPECS_REQUEST, { enabled: caps.products && caps.specs });
  const products = useMemo(() => (sellable.data ? toCajaProducts(sellable.data, tech.data, held.data) : undefined), [sellable.data, tech.data, held.data]);
  const productList = products ?? EMPTY_PRODUCTS;

  const state = pos.data;
  const posSession = state?.session ?? null;
  const sessionOpen = posSession !== null;
  const band = fiscalBand(fiscal.data);

  // ------------------------------------------------------------------------------------------------ la venta en curso
  const [cart, dispatch] = useReducer(cartReducer, EMPTY_CART);
  const [paymentBase, setPayment] = useState<PaymentDraft>(EMPTY_PAYMENT);
  const payment = withDefaults(paymentBase, state);
  const [buyer, setBuyer] = useState<BuyerDraft>(EMPTY_BUYER);
  const [status, setStatus] = useState<string | null>(null);
  const [problems, setProblems] = useState<string[]>([]);
  const totals = cartTotals(cart.lines);

  // Cambió la sucursal activa (barra superior): la venta era de la otra sucursal y se descarta.
  const branchId = session?.access.activeBranchId ?? null;
  const [cartBranch, setCartBranch] = useState(branchId);
  const [branchNotice, setBranchNotice] = useState(false);
  if (cartBranch !== branchId) {
    setCartBranch(branchId);
    if (cart.lines.length > 0 || cart.build) {
      dispatch({ type: 'clear' });
      setBuyer(EMPTY_BUYER);
      setBranchNotice(true);
    }
  }

  // ------------------------------------------------------------------------------------------------ ventanas
  const [openingOpen, setOpeningOpen] = useState(false);
  const [closingOpen, setClosingOpen] = useState(false);
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [clearOpen, setClearOpen] = useState(false);
  const [serialTarget, setSerialTarget] = useState<SerialPickTarget | null>(null);
  const [completed, setCompleted] = useState<CompletedSale | null>(null);
  const pickerOpen = params.get(PICKER_PARAM) === 'reserva';

  /** Cambia parámetros propios de la dirección sin tocar los filtros de la lista. */
  const setParam = (changes: Record<string, string | null>) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        for (const [key, value] of Object.entries(changes)) {
          if (value === null) next.delete(key);
          else next.set(key, value);
        }
        return next;
      },
      { replace: true },
    );

  // ------------------------------------------------------------------------------------------------ vender una reserva
  const reservaParam = params.get(RESERVATION_PARAM);
  const reservaNumber = reservaParam && reservaParam.trim().length > 0 ? normalizeBuildNumber(reservaParam) : null;
  const buildQuery = useRpcQuery('GetPcBuildQuery', { number: reservaNumber ?? '' }, { enabled: reservaNumber !== null && caps.sellBuild });
  const buildData = buildQuery.data && reservaNumber !== null && normalizeBuildNumber(buildQuery.data.build.number) === reservaNumber ? buildQuery.data : undefined;
  // Para saber qué piezas llevan serie hace falta la lista de productos y su ficha técnica.
  const infoReady = !caps.products || (products !== undefined && (!caps.tech || tech.data !== undefined || tech.error !== null));
  const [seenBuild, setSeenBuild] = useState<BuildDetailData | null>(null);
  const [pendingBuild, setPendingBuild] = useState<BuildDetailData | null>(null);
  const confirmedBuild = useRef(false);

  const loadBuild = (detail: BuildDetailData) => {
    dispatch({ type: 'loadBuild', build: detail.build, lines: buildLines(detail, lineInfoFrom(productList, tech.data)) });
    // Los datos para la factura que dejó quien reservó (el cajero los puede corregir) y su cliente, si es de la caja.
    setBuyer(buyerFromBuild(detail.build) ?? EMPTY_BUYER);
    const customer = customerByName(state, detail.build.customer);
    setPayment((current) => ({ ...afterSale(current), customerCode: customer?.code ?? '' }));
    setProblems([]);
    setBranchNotice(false);
    setStatus(`Se cargó ${detail.build.kind === 'Cart' ? 'la reserva' : 'el armado'} ${detail.build.number} en la venta, a sus precios congelados.`);
  };

  if (buildData && infoReady && buildData !== seenBuild) {
    setSeenBuild(buildData);
    if (!buildProblem(buildData.build) && cart.build?.number !== buildData.build.number) {
      if (cart.lines.length > 0) setPendingBuild(buildData);
      else loadBuild(buildData);
    }
  }

  const removeBuild = () => {
    dispatch({ type: 'clear' });
    setBuyer(EMPTY_BUYER);
    setSeenBuild(null);
    setPendingBuild(null);
    setProblems([]);
    setParam({ [RESERVATION_PARAM]: null });
    setStatus('Se quitó la reserva de la venta.');
  };

  const pickBuild = (number: string) => {
    setParam({ [PICKER_PARAM]: null, [RESERVATION_PARAM]: number });
    if (number === reservaNumber) {
      setSeenBuild(null);
      buildQuery.reload();
    }
  };

  // ------------------------------------------------------------------------------------------------ agregar productos
  const addBlocked =
    !caps.checkout && !caps.sellBuild
      ? missingPermissionsText(missing(rpcOperation('CheckoutCommand')?.permissions ?? []))
      : state === undefined
        ? 'Cargando el estado de la caja…'
        : !sessionOpen
          ? 'Abra la caja para vender.'
          : cart.build
            ? `Está cobrando ${cart.build.kind === 'Cart' ? 'la reserva' : 'el armado'} ${cart.build.number}: cóbrelo o quítelo antes de agregar otros productos.`
            : null;

  const openSerials = (target: SerialPickTarget) => {
    if (!caps.serials) {
      notify.warning('No puede elegir las unidades', missingPermissionsText(missing(rpcOperation('GetAvailableSerialsQuery')?.permissions ?? [])));
      return;
    }
    setSerialTarget(target);
  };

  const addProduct = (product: CajaProduct) => {
    if (addBlocked) {
      notify.warning('No se puede agregar a la venta', addBlocked);
      return;
    }
    if (product.available <= 0) {
      notify.warning('Producto agotado', `${product.name} no tiene unidades disponibles (lo reservado no se vende a otro cliente).`);
      return;
    }
    if (product.serialized) {
      const line = cart.lines.find((item) => !item.locked && item.sku === product.sku) ?? null;
      openSerials({
        lineKey: line?.key ?? null,
        sku: product.sku,
        name: product.name,
        serialKind: product.serialKind,
        chosen: line?.serials ?? [],
        exclude: serialsInOtherLines(cart, product.sku, line?.key ?? null),
        exact: null,
      });
      return;
    }
    dispatch({ type: 'add', product });
    setProblems([]);
    setBranchNotice(false);
    setStatus(`Se agregó ${product.name} a la venta.`);
  };

  const pickLineSerials = (line: CajaLine) =>
    openSerials({
      lineKey: line.key,
      sku: line.sku,
      name: line.name,
      serialKind: line.serialKind,
      chosen: line.serials,
      exclude: serialsInOtherLines(cart, line.sku, line.key),
      exact: line.locked ? Math.round(line.quantity) : null,
    });

  const confirmSerials = (target: SerialPickTarget, serials: string[]) => {
    const line = target.lineKey ? cart.lines.find((item) => item.key === target.lineKey) : undefined;
    if (line?.locked) {
      dispatch({ type: 'setLineSerials', key: line.key, serials });
    } else {
      const product = productBySku(productList, target.sku);
      if (product) dispatch({ type: 'setSerials', product, serials });
    }
    setSerialTarget(null);
    setProblems([]);
    setBranchNotice(false);
    setStatus(`${target.name}: ${serials.length === 1 ? '1 unidad elegida' : `${serials.length} unidades elegidas`}.`);
  };

  // ------------------------------------------------------------------------------------------------ cobrar
  const chargeBlocked =
    state === undefined
      ? 'Cargando el estado de la caja…'
      : !sessionOpen
        ? 'Abra la caja para cobrar.'
        : cart.lines.length === 0
          ? 'Agregue productos a la venta.'
          : cart.build
            ? caps.sellBuild
              ? null
              : missingPermissionsText(missing(rpcOperation('SellPcBuildCommand')?.permissions ?? []))
            : caps.checkout
              ? null
              : missingPermissionsText(missing(rpcOperation('CheckoutCommand')?.permissions ?? []));

  const openCheckout = () => {
    if (chargeBlocked) {
      notify.warning('Todavía no se puede cobrar', chargeBlocked);
      return;
    }
    const found = cartProblems(cart, productList);
    setProblems(found);
    if (found.length > 0) {
      const panel = document.getElementById(cartId);
      panel?.scrollIntoView?.({ block: 'nearest' });
      panel?.focus();
      return;
    }
    setCheckoutOpen(true);
  };

  const dispatchFiscal = useRpcCommand('DispatchFiscalDocumentsCommand', { notifyError: false });

  /** Envía la factura al SIN (como el escritorio, recién después de cobrar) y muestra el documento definitivo. */
  const sendToSin = async (sale: SaleResultData, documentId: string) => {
    const outcome = await dispatchFiscal.run({ documentId, max: 50 });
    const next = fiscalAfterDispatch(sale, outcome.ok ? { ok: true, result: outcome.result } : { ok: false, message: outcome.message });
    setCompleted((current) => (current && current.sale === sale ? { ...current, fiscal: next } : current));
    fiscal.reload();
  };

  const onSold = (sale: SaleResultData, context: SoldContext) => {
    setCheckoutOpen(false);
    setCompleted({ sale, build: context.build, buyerEmail: context.buyerEmail, fiscal: initialFiscal(sale, caps.dispatch), state, cashier: session?.displayName ?? null });
    dispatch({ type: 'clear' });
    setPayment(afterSale);
    setBuyer(EMPTY_BUYER);
    setProblems([]);
    setSeenBuild(null);
    setStatus(`Venta ${sale.invoiceNumber} cobrada.`);
    if (context.build) setParam({ [RESERVATION_PARAM]: null });
    pos.reload();
    if (caps.products) {
      sellable.reload();
      held.reload();
    }
    if (sale.fiscalDocumentId && caps.dispatch) void sendToSin(sale, sale.fiscalDocumentId);
    else if (sale.fiscalDocumentId) fiscal.reload();
  };

  const focusSearch = () => {
    const input = searchRef.current;
    if (!input) return;
    input.focus();
    input.select();
  };

  const closeResult = () => {
    setCompleted(null);
    // El foco vuelve al buscador cuando el diálogo termina de cerrarse (el fondo deja de estar inerte).
    setTimeout(focusSearch, 260);
  };

  const reloadAll = () => {
    pos.reload();
    fiscal.reload();
    if (caps.products) {
      sellable.reload();
      if (caps.tech) tech.reload();
      if (caps.reservedStock) held.reload();
      if (caps.specs) specs.reload();
    }
  };

  // ------------------------------------------------------------------------------------------------ no perder la venta
  const dirty = cart.lines.length > 0;
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [dirty]);

  const anyWindow =
    openingOpen || closingOpen || checkoutOpen || clearOpen || serialTarget !== null || completed !== null || (pickerOpen && caps.sellBuild) || pendingBuild !== null || blocker.state === 'blocked';
  useShortcuts({ search: focusSearch, charge: openCheckout, escape: focusSearch }, !anyWindow);

  // ------------------------------------------------------------------------------------------------ aviso de la reserva
  let buildNotice: ReactNode = null;
  if (reservaNumber !== null && cart.build?.number !== reservaNumber) {
    if (!caps.sellBuild) {
      buildNotice = <AccessDenied title={`No puede cobrar ${reservaNumber}`} operation={canRun('GetPcBuildQuery') ? 'SellPcBuildCommand' : 'GetPcBuildQuery'} />;
    } else if (buildQuery.error && !buildData) {
      buildNotice = (
        <div className="space-y-2">
          <ErrorState error={buildQuery.error} operation="GetPcBuildQuery" title={`No se pudo cargar ${reservaNumber}`} onRetry={buildQuery.reload} retrying={buildQuery.fetching} />
          <Button variant="ghost" onClick={removeBuild}>
            Quitar
          </Button>
        </div>
      );
    } else if (!buildData || !infoReady) {
      buildNotice = (
        <p role="status" className="text-sm text-text-muted">
          Cargando {reservaNumber}…
        </p>
      );
    } else {
      const problem = buildProblem(buildData.build);
      if (problem) {
        buildNotice = (
          <Alert
            tone="warning"
            title="Esta reserva no se puede cobrar"
            actions={
              <>
                <Button variant="outline" onClick={removeBuild}>
                  Quitar
                </Button>
                {can('sales.pcbuild.manage') && (
                  <Button variant="ghost" to={ROUTES.panelModule(`reservas?q=${encodeURIComponent(reservaNumber)}`)}>
                    Ver en Reservas
                  </Button>
                )}
              </>
            }
          >
            {problem}
          </Alert>
        );
      }
    }
  }

  const sessionBadge =
    state === undefined ? undefined : sessionOpen ? <StatusBadge tone="success">Caja abierta</StatusBadge> : <StatusBadge tone="warning">Caja cerrada</StatusBadge>;

  return (
    <Page
      title="Caja"
      description="Vender y cobrar en el mostrador: productos con el lector de códigos, factura del SIN, comprobante y arqueo."
      badge={sessionBadge}
      actions={
        <>
          {cart.lines.length > 0 && (
            <Button
              className="xl:hidden"
              leftIcon={<Receipt />}
              onClick={() => {
                const panel = document.getElementById(cartId);
                panel?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
                panel?.focus();
              }}
            >
              Ir al cobro · {formatMoney(totals.total)}
            </Button>
          )}
          {caps.sellBuild && (
            <Button variant="outline" leftIcon={<PackageCheck />} onClick={() => setParam({ [PICKER_PARAM]: 'reserva' })}>
              Vender una reserva
            </Button>
          )}
          <Button variant="outline" leftIcon={<RefreshCw />} loading={pos.fetching && !pos.loading} onClick={reloadAll}>
            Actualizar
          </Button>
          <FullscreenButton />
        </>
      }
    >
      <SessionBar
        state={state}
        loading={pos.loading}
        error={pos.error}
        onRetry={pos.reload}
        retrying={pos.fetching}
        canOpen={caps.open}
        canClose={caps.close}
        onOpen={() => setOpeningOpen(true)}
        onClose={() => setClosingOpen(true)}
      />

      {band && (
        <Alert tone={band.tone} title={band.title}>
          <span data-testid="banda-fiscal">{band.detail}</span>
        </Alert>
      )}
      {branchNotice && (
        <Alert tone="info" title="Cambió la sucursal activa">
          Se vació la venta en curso: los productos y la caja son de cada sucursal.
        </Alert>
      )}

      <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-text-muted" data-testid="atajos">
        <Keyboard aria-hidden="true" className="size-4" />
        <span>Atajos:</span>
        <span>
          <kbd className="rounded border border-border-strong px-1.5 font-mono text-xs">F2</kbd> buscar o escanear
        </span>
        <span>
          <kbd className="rounded border border-border-strong px-1.5 font-mono text-xs">F4</kbd> cobrar
        </span>
        <span>
          <kbd className="rounded border border-border-strong px-1.5 font-mono text-xs">Esc</kbd> cancelar
        </span>
      </p>

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_24rem] 2xl:grid-cols-[minmax(0,1fr)_28rem]">
        {caps.products ? (
          <ProductCatalog
            products={products}
            loading={sellable.loading}
            refreshing={sellable.fetching && !sellable.loading}
            error={sellable.error}
            onRetry={sellable.reload}
            specs={specs.data}
            techError={caps.tech ? tech.error : null}
            onRetryTech={tech.reload}
            canLookup={caps.lookup}
            blocked={addBlocked}
            inCart={quantitiesBySku(cart.lines)}
            onAdd={addProduct}
            searchRef={searchRef}
          />
        ) : (
          <AccessDenied title="No puede ver los productos a la venta" operation="GetSellableProductsQuery" />
        )}
        <CartPanel
          id={cartId}
          cart={cart}
          products={productList}
          dispatch={dispatch}
          taxRate={state?.taxRate ?? 13}
          vatOnInvoicedAmount={state?.vatOnInvoicedAmount ?? true}
          chargeBlocked={chargeBlocked}
          onCharge={openCheckout}
          onClear={() => setClearOpen(true)}
          onPickSerials={pickLineSerials}
          canPickSerials={caps.serials}
          onRemoveBuild={removeBuild}
          buildNotice={buildNotice}
          problems={problems}
          status={status}
        />
      </div>

      <OpenSessionDialog
        open={openingOpen}
        onClose={() => setOpeningOpen(false)}
        state={state}
        suggested={suggestedRegister(state)}
        onOpened={() => {
          pos.reload();
          fiscal.reload();
          setTimeout(focusSearch, 260);
        }}
      />
      <CloseSessionDialog
        open={closingOpen}
        onClose={() => setClosingOpen(false)}
        session={posSession}
        onClosed={() => {
          dispatch({ type: 'clear' });
          setBuyer(EMPTY_BUYER);
          setProblems([]);
          if (cart.build) setParam({ [RESERVATION_PARAM]: null });
          pos.reload();
          fiscal.reload();
        }}
      />
      <CheckoutDialog
        open={checkoutOpen}
        onClose={() => setCheckoutOpen(false)}
        state={state}
        fiscal={fiscal.data}
        caps={caps}
        cart={cart}
        products={productList}
        sessionOpen={sessionOpen}
        payment={payment}
        onPaymentChange={setPayment}
        buyer={buyer}
        onBuyerChange={setBuyer}
        onSold={onSold}
      />
      <SerialPickerDialog target={serialTarget} onClose={() => setSerialTarget(null)} onConfirm={confirmSerials} />
      <ReservationPickerDialog open={pickerOpen && caps.sellBuild} onClose={() => setParam({ [PICKER_PARAM]: null })} onPick={pickBuild} canList={caps.pickBuild} />
      <SaleResultDialog completed={completed} onClose={closeResult} caps={caps} />

      <ConfirmDialog
        open={clearOpen}
        onClose={() => setClearOpen(false)}
        tone="danger"
        title="¿Vaciar la venta?"
        message={cart.build ? `Se quita ${cart.build.kind === 'Cart' ? 'la reserva' : 'el armado'} ${cart.build.number} de la venta (la reserva sigue vigente).` : `Se quitan ${itemsText(totals).toLocaleLowerCase('es')} de la venta actual.`}
        confirmLabel="Vaciar la venta"
        onConfirm={() => {
          if (cart.build) setParam({ [RESERVATION_PARAM]: null });
          dispatch({ type: 'clear' });
          setBuyer(EMPTY_BUYER);
          setSeenBuild(null);
          setProblems([]);
          setStatus('Se vació la venta.');
        }}
      />
      <ConfirmDialog
        open={pendingBuild !== null}
        onClose={() => {
          setPendingBuild(null);
          if (!confirmedBuild.current) setParam({ [RESERVATION_PARAM]: null });
          confirmedBuild.current = false;
        }}
        title={pendingBuild ? `¿Cobrar ${pendingBuild.build.kind === 'Cart' ? 'la reserva' : 'el armado'} ${pendingBuild.build.number}?` : 'Cobrar la reserva'}
        message={`La venta actual tiene ${itemsText(totals).toLocaleLowerCase('es')}: se quitan para cobrar la reserva a sus precios congelados.`}
        confirmLabel="Cargar la reserva"
        onConfirm={() => {
          confirmedBuild.current = true;
          if (pendingBuild) loadBuild(pendingBuild);
        }}
      />
      <Dialog
        open={blocker.state === 'blocked'}
        onClose={() => blocker.reset?.()}
        alert
        title="¿Salir de la caja?"
        description={`La venta en curso tiene ${itemsText(totals).toLocaleLowerCase('es')}. Si sale de la caja, la venta se descarta.`}
        footer={
          <>
            <Button variant="outline" data-autofocus onClick={() => blocker.reset?.()}>
              Seguir en la caja
            </Button>
            <Button variant="danger" onClick={() => blocker.proceed?.()}>
              Salir y descartar la venta
            </Button>
          </>
        }
      />
    </Page>
  );
}

/** Cantidad de cada SKU (en mayúsculas) en la venta. */
function quantitiesBySku(lines: readonly CajaLine[]): Map<string, number> {
  const result = new Map<string, number>();
  for (const line of lines) result.set(line.sku.toUpperCase(), (result.get(line.sku.toUpperCase()) ?? 0) + line.quantity);
  return result;
}

interface SessionBarProps {
  state: PosStateData | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
  retrying: boolean;
  canOpen: boolean;
  canClose: boolean;
  onOpen: () => void;
  onClose: () => void;
}

/** El turno de caja: abierto (resumen y «Cerrar caja») o cerrado (botón grande «Abrir caja»). */
function SessionBar({ state, loading, error, onRetry, retrying, canOpen, canClose, onOpen, onClose }: SessionBarProps) {
  if (error && !state) return <ErrorState error={error} operation="GetPosStateQuery" title="No se pudo leer el estado de la caja" onRetry={onRetry} retrying={retrying} />;
  if (loading || !state) {
    return (
      <div role="status" className="rounded-card border border-border bg-surface p-4" data-testid="turno-cargando">
        <span className="sr-only">Cargando el estado de la caja…</span>
        <Skeleton className="h-5 w-48" />
        <Skeleton className="mt-2 h-4 w-full max-w-md" />
      </div>
    );
  }
  const session: PosSessionData | null = state.session;
  if (session) {
    return (
      <section aria-label="Turno de caja" className="flex flex-wrap items-center justify-between gap-3 rounded-card border border-success/40 bg-success-soft/40 p-4" data-testid="turno">
        <div className="flex min-w-0 items-start gap-3">
          <ShoppingCart aria-hidden="true" className="mt-0.5 size-6 shrink-0 text-success-text" />
          <div className="min-w-0">
            <p className="font-semibold">
              {registerLabel(session)} abierta · {state.branchName}
            </p>
            <p className="text-sm text-text-muted" data-testid="turno-resumen">
              {sessionSummary(session)}
            </p>
          </div>
        </div>
        {canClose && (
          <Button variant="outline" leftIcon={<Lock />} onClick={onClose}>
            Cerrar caja
          </Button>
        )}
      </section>
    );
  }
  return (
    <section aria-label="Turno de caja" className="flex flex-wrap items-center justify-between gap-4 rounded-card border border-warning/40 bg-warning-soft/40 p-5" data-testid="turno">
      <div className="flex min-w-0 items-start gap-3">
        <Lock aria-hidden="true" className="mt-1 size-7 shrink-0 text-warning-text" />
        <div className="min-w-0">
          <p className="font-display text-xl font-semibold">La caja está cerrada</p>
          <p className="text-sm text-text-muted">
            {canOpen ? `Abra la caja con el fondo inicial para empezar a vender en ${state.branchName}.` : 'Su cuenta no puede abrir la caja: pida al administrador que le asigne el permiso.'}
          </p>
        </div>
      </div>
      {canOpen && (
        <Button size="lg" leftIcon={<LockOpen />} onClick={onOpen}>
          Abrir caja
        </Button>
      )}
    </section>
  );
}

/** Pantalla completa (sin las barras del navegador) para trabajar en el mostrador. Sin soporte, no se muestra. */
function FullscreenButton() {
  const supported = typeof document !== 'undefined' && document.fullscreenEnabled === true && typeof document.documentElement.requestFullscreen === 'function';
  const [active, setActive] = useState(() => supported && document.fullscreenElement !== null);
  useEffect(() => {
    if (!supported) return;
    const onChange = () => setActive(document.fullscreenElement !== null);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, [supported]);
  if (!supported) return null;
  const toggle = () => {
    const request = document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
    void request.catch(() => undefined);
  };
  return (
    <Button variant="outline" leftIcon={active ? <Minimize2 /> : <Maximize2 />} onClick={toggle}>
      {active ? 'Salir de pantalla completa' : 'Pantalla completa'}
    </Button>
  );
}
