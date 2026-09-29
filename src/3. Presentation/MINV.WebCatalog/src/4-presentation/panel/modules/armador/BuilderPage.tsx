// Tecnología › Armador de PC › Armar una PC (`/panel/armador/nuevo`) y un armado guardado (`/panel/armador/<NÚMERO>`).
//
// Armado PASO A PASO por ranura (procesador, placa, memoria, gráfica, almacenamiento, fuente, gabinete, refrigeración,
// monitor, periféricos, software y servicios): los candidatos de la ranura (`GetPcBuildCandidatesQuery`, compatibles
// primero y los incompatibles atenuados con el motivo) con búsqueda, categoría (en los extras), marca, precio, «solo con
// stock» y «solo compatibles»; la compatibilidad EN VIVO (`CheckPcBuildQuery`: errores, avisos y energía) la decide SOLO el
// servidor (regla T-06); total, nombre, cliente y vigencia; «Guardar borrador» y «Guardar cotización» (`SavePcBuildCommand`,
// con confirmación explícita si hay errores de compatibilidad). Un armado cotizado queda congelado: se ven sus piezas a los
// precios cotizados, su bitácora y las acciones de su estado (Vender en caja, Reservar stock, Liberar reserva,
// Publicar/Quitar de la web, Imprimir cotización y Anular).
//
// Con solo `sales.view` + `inventory.stock.view` se puede armar y revisar la compatibilidad; guardar y el resto de los
// comandos piden `sales.pcbuild.manage` (los botones solo aparecen con ese permiso; el servidor decide igual, regla P-01).

import clsx from 'clsx';
import {
  AppWindow,
  ArrowLeft,
  ArrowRight,
  Ban,
  CircleAlert,
  CircuitBoard,
  Clock,
  Cpu,
  Fan,
  FileText,
  Globe,
  GlobeLock,
  Gpu,
  HardDrive,
  History,
  Keyboard,
  LockOpen,
  MemoryStick,
  Monitor,
  PackageSearch,
  PcCase,
  Plus,
  Minus,
  Power,
  Printer,
  Save,
  ShoppingCart,
  Wrench,
  X,
  type LucideIcon,
} from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { permissionName } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Checkbox,
  ComboBox,
  ConfirmDialog,
  DetailList,
  EmptyState,
  ErrorState,
  IconButton,
  LoadingState,
  MoneyField,
  Page,
  SearchField,
  Section,
  SelectField,
  Skeleton,
  StatusBadge,
  TextField,
  useNotify,
  type ComboOption,
} from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { CancelDialog, ReleaseDialog, ReserveDialog, type BuildTarget } from './BuildDialogs';
import { ErrorDetails } from './ErrorDetails';
import { IssueList } from './IssueList';
import { QuotePrint } from './QuotePrint';
import {
  BUILD_STATES,
  CANDIDATE_PAGE,
  DEFAULT_VALIDITY,
  EMPTY_CANDIDATE_FILTERS,
  MAX_LINES,
  MAX_QUANTITY,
  NAME_MAX_LENGTH,
  SLOTS,
  VALIDITY_OPTIONS,
  addPart,
  autoName,
  availabilityText,
  brandOptions,
  buildNote,
  buildState,
  builderPath,
  canAddLine,
  canCancelBuild,
  canPrintBuild,
  canPublishBuild,
  canReleaseBuild,
  canReserveBuild,
  canSellBuild,
  canUnpublishBuild,
  changeQuantity,
  checkSummary,
  customerCodeByName,
  customerOptions,
  defaultCategory,
  filterCandidates,
  historyAction,
  isEditable,
  nextRequiredSlot,
  partsCount,
  partsFromItems,
  partsTotal,
  piecesText,
  removePart,
  sellPath,
  slotInfo,
  slotParts,
  slotSummary,
  toInputs,
  toSavePayload,
  withLivePrices,
  type BuildDetailData,
  type CandidateFilters,
  type CandidateRecord,
  type CategoryRecord,
  type CustomerRecord,
  type PartLine,
  type SlotCode,
  type SlotInfo,
} from './builder';
import { printQuote } from './printing';
import { useNow } from './useNow';

const SLOT_ICONS: Record<SlotCode, LucideIcon> = {
  Cpu,
  Motherboard: CircuitBoard,
  Ram: MemoryStick,
  Gpu,
  Storage: HardDrive,
  Psu: Power,
  Case: PcCase,
  Cooler: Fan,
  Monitor,
  Peripheral: Keyboard,
  Software: AppWindow,
  Service: Wrench,
};

const AVAILABILITY_TONES = { accent: 'text-primary-text', success: 'text-success-text', danger: 'text-danger-text' } as const;

/** `/panel/armador/nuevo` o `/panel/armador/<NÚMERO>`: al cambiar de armado la pantalla empieza de cero. */
export function BuilderPage() {
  const { numero } = useParams();
  const number = numero ? numero.trim().toUpperCase() : null;
  return <BuilderScreen key={number ?? 'nuevo'} number={number} />;
}

function BuilderScreen({ number }: { number: string | null }) {
  const navigate = useNavigate();
  const notify = useNotify();
  const { canRun, session } = usePermissions();
  const now = useNow();
  const canSave = canRun('SavePcBuildCommand');
  const allowed = {
    sell: canRun('SellPcBuildCommand'),
    reserve: canRun('ReservePcBuildCommand'),
    release: canRun('ReleasePcBuildReservationCommand'),
    publish: canRun('PublishPcBuildCommand'),
    cancel: canRun('CancelPcBuildCommand'),
  };

  const detail = useRpcQuery('GetPcBuildQuery', { number: number ?? '' }, { enabled: number !== null, keepPreviousData: false });
  const customers = useRpcQuery('GetCustomersQuery', {});
  const options = useRpcQuery('GetCatalogOptionsQuery', {});

  // Estado del armado en la pantalla. Un armado guardado se carga del servidor (y se vuelve a cargar después de cada
  // comando); uno nuevo empieza vacío.
  const [applied, setApplied] = useState<BuildDetailData | null>(null);
  const [parts, setParts] = useState<PartLine[]>([]);
  const [name, setName] = useState('');
  const [customerCode, setCustomerCode] = useState('');
  const [pendingCustomer, setPendingCustomer] = useState<string | null>(null);
  const [validDays, setValidDays] = useState(DEFAULT_VALIDITY);
  const [slot, setSlot] = useState<SlotCode>('Cpu');
  const [categoryBySlot, setCategoryBySlot] = useState<Partial<Record<SlotCode, string>>>({});
  const [onlyInStock, setOnlyInStock] = useState(false);

  if (detail.data && detail.data !== applied) {
    const data = detail.data;
    setApplied(data);
    setParts(partsFromItems(data.build.status === 'Draft' ? data.check.items : data.quotedItems));
    setName(data.build.name);
    setCustomerCode('');
    setPendingCustomer(data.build.customer);
  }
  // La fila trae el NOMBRE del cliente: se busca su código cuando llega la lista de clientes.
  if (pendingCustomer !== null && customers.data) {
    setCustomerCode(customerCodeByName(customers.data.customers, pendingCustomer));
    setPendingCustomer(null);
  }

  const current = applied?.build ?? null;
  const ready = number === null || applied !== null;
  const editable = ready && isEditable(current);
  const inputs = useMemo(() => toInputs(parts), [parts]);
  const check = useRpcQuery('CheckPcBuildQuery', { items: inputs }, { enabled: ready && inputs.length > 0 });
  const checkData = inputs.length > 0 ? check.data : undefined;
  const shownParts = editable ? withLivePrices(parts, checkData) : parts;
  const total = !editable && current ? current.total : checkData && !check.fetching ? checkData.total : partsTotal(shownParts);
  const summary = checkSummary(checkData, parts.length > 0);

  const categories = useMemo<CategoryRecord[]>(() => options.data?.categories ?? [], [options.data]);
  const info = slotInfo(slot);
  const category = categoryBySlot[slot] ?? defaultCategory(info, categories);
  const customerChoices = useMemo<ComboOption<CustomerRecord>[]>(() => customerOptions(customers.data?.customers ?? []), [customers.data]);
  const customer = customerChoices.find((option) => option.value === customerCode) ?? null;

  // Comandos.
  const save = useRpcCommand('SavePcBuildCommand', {
    success: (row, payload) => (payload.quote ? `Cotización ${row.number} emitida · ${formatMoney(row.total)}` : `Borrador ${row.number} guardado`),
    notifyError: false,
  });
  const publish = useRpcCommand('PublishPcBuildCommand', {
    success: (row, payload) => (payload.published ? `${row.number} publicado en la tienda web` : `${row.number} retirado de la tienda web`),
    errorTitle: 'No se pudo cambiar la publicación',
  });
  const [savingQuote, setSavingQuote] = useState(false);
  const [confirmErrors, setConfirmErrors] = useState(false);
  const [confirmNew, setConfirmNew] = useState(false);
  const [reserving, setReserving] = useState<BuildTarget | null>(null);
  const [releasing, setReleasing] = useState<BuildTarget | null>(null);
  const [cancelling, setCancelling] = useState<BuildTarget | null>(null);

  const touch = () => {
    if (save.error) save.reset();
  };

  const add = (candidate: CandidateRecord) => {
    if (!canAddLine(parts, slot, candidate.sku)) {
      notify.warning('No se pueden agregar más piezas', `Un armado admite como máximo ${MAX_LINES} piezas distintas.`);
      return;
    }
    const next = addPart(parts, slot, candidate);
    setParts(next);
    touch();
    if (!candidate.isCompatible) notify.warning('Pieza incompatible', candidate.reason ?? 'No es compatible con lo elegido.');
    // Ayuda a armar en orden: después de una ranura de una sola pieza, la siguiente obligatoria vacía.
    if (!info.multi) {
      const following = nextRequiredSlot(next);
      if (following) setSlot(following);
    }
  };

  const doSave = async (quote: boolean, accept: boolean) => {
    setSavingQuote(quote);
    const outcome = await save.run(toSavePayload({ current, name, customerCode, parts, quote, validDays, acceptIncompatible: accept }));
    if (outcome.ok) {
      if (current?.number === outcome.result.number) detail.reload();
      else navigate(ROUTES.panelModule(builderPath(outcome.result.number)), { replace: true });
    }
    return outcome;
  };

  const quote = () => {
    if (summary.errors > 0) setConfirmErrors(true);
    else void doSave(true, false);
  };

  const reset = () => {
    setParts([]);
    setName('');
    setCustomerCode('');
    setValidDays(DEFAULT_VALIDITY);
    setSlot('Cpu');
    save.reset();
  };

  const startNew = () => {
    if (current) navigate(ROUTES.panelModule(builderPath(null)));
    else if (parts.length > 0) setConfirmNew(true);
  };

  const togglePublish = async () => {
    if (!current) return;
    const outcome = await publish.run({ number: current.number, published: !current.publishedToWeb });
    if (outcome.ok) detail.reload();
  };

  const printQuotation = () => {
    if (!printQuote()) notify.warning('No se pudo imprimir', 'Este navegador no permite imprimir desde la página.');
  };

  const step = (delta: number) => {
    const index = SLOTS.findIndex((item) => item.code === slot);
    const next = SLOTS[Math.min(SLOTS.length - 1, Math.max(0, index + delta))];
    setSlot(next.code);
  };

  const state = current ? buildState(current, now) : null;
  const target = current ? { number: current.number, name: current.name, total: current.total } : null;
  const branch = current ? session?.access.branches.find((item) => item.code === current.branchCode) : undefined;

  return (
    <Page
      title={current ? `${current.number} · ${current.name}` : number ?? 'Armar una PC'}
      description={ready ? buildNote(current, now) : 'Cargando el armado…'}
      badge={state && <StatusBadge status={state} statuses={BUILD_STATES} />}
      actions={
        <>
          <Button variant="outline" leftIcon={<ArrowLeft />} to={ROUTES.panelModule('armador')}>
            Ver cotizaciones
          </Button>
          {(current !== null || parts.length > 0) && (
            <Button variant="outline" leftIcon={<Plus />} onClick={startNew}>
              Armado nuevo
            </Button>
          )}
        </>
      }
    >
      {number !== null && !applied && detail.error && (
        <ErrorState error={detail.error} operation="GetPcBuildQuery" onRetry={detail.reload} retrying={detail.fetching} />
      )}
      {number !== null && !applied && !detail.error && <LoadingState label="Cargando el armado…" rows={3} />}

      {current?.kind === 'Cart' ? (
        <Alert
          tone="info"
          title={`${current.number} es una reserva de compra, no un armado de PC`}
          actions={
            <Button variant="outline" to={ROUTES.panelModule(`reservas?q=${encodeURIComponent(current.number)}`)}>
              Verla en Reservas
            </Button>
          }
        >
          Las reservas de productos sueltos se atienden en Ventas › Reservas.
        </Alert>
      ) : (
        ready && (
          <div className={clsx('grid gap-4', editable ? 'lg:grid-cols-[15rem_minmax(0,1fr)] xl:grid-cols-[15rem_minmax(0,1fr)_22rem]' : 'lg:grid-cols-[minmax(0,1fr)_22rem]')}>
            {editable ? (
              <>
                <SlotNav parts={parts} selected={slot} onSelect={setSlot} />
                <CandidatesPanel
                  key={slot}
                  info={info}
                  inputs={inputs}
                  categories={categories}
                  categoriesError={options.data ? null : options.error}
                  onRetryCategories={options.reload}
                  category={category}
                  onCategory={(code) => setCategoryBySlot((current) => ({ ...current, [slot]: code }))}
                  onlyInStock={onlyInStock}
                  onOnlyInStock={setOnlyInStock}
                  onAdd={add}
                  onStep={step}
                />
              </>
            ) : (
              <FrozenDetail data={applied!} now={now} />
            )}

            <aside className={clsx('min-w-0 space-y-4', editable && 'lg:col-span-2 xl:col-span-1')} aria-label="Revisión y cotización">
              <IssueList check={editable ? checkData : applied?.check} hasParts={parts.length > 0} checking={editable && check.fetching} accepted={current?.quotedWithErrors} />

              {editable && (
                <Section title="Piezas elegidas" level={2} description={piecesText(partsCount(parts))}>
                  <ChosenParts
                    parts={shownParts}
                    onRemove={(part) => {
                      setParts(removePart(parts, part.slot, part.sku));
                      touch();
                    }}
                    onQuantity={(part, delta) => {
                      setParts(changeQuantity(parts, part.slot, part.sku, delta));
                      touch();
                    }}
                  />
                </Section>
              )}

              <div className="flex items-end justify-between gap-3 rounded-card border border-border bg-surface p-4 shadow-card">
                <div>
                  <p className="text-xs font-semibold tracking-wide text-text-faint uppercase">Total</p>
                  <p className="text-xs text-text-muted">{editable ? 'IVA incluido · lista de precios vigente' : 'IVA incluido · precios de la cotización'}</p>
                </div>
                <p className="font-display text-2xl font-semibold text-text tabular-nums" data-testid="total-del-armado">
                  {formatMoney(total)}
                </p>
              </div>

              {editable &&
                (canSave ? (
                  <Section title="Cotización" level={2} description="Guarde el borrador o congele los precios y la vigencia: es la oferta al cliente.">
                    <div className="space-y-3">
                      <TextField
                        label="Nombre del armado"
                        value={name}
                        onChange={(value) => {
                          setName(value);
                          touch();
                        }}
                        maxLength={NAME_MAX_LENGTH}
                        placeholder={autoName(parts)}
                        hint="Si lo deja vacío, se usa el nombre propuesto."
                        optional
                      />
                      <ComboBox<CustomerRecord>
                        label="Cliente"
                        placeholder="Consumidor final"
                        value={customer}
                        onChange={(option) => {
                          setCustomerCode(option?.value ?? '');
                          touch();
                        }}
                        options={customerChoices}
                        hint="Sin cliente, la cotización es para consumidor final."
                        optional
                      />
                      <SelectField
                        label="Vigencia de la cotización"
                        allLabel={false}
                        value={validDays}
                        onChange={(value) => setValidDays(value || DEFAULT_VALIDITY)}
                        options={VALIDITY_OPTIONS}
                      />
                      {save.errorText && (
                        <Alert tone="danger">
                          <ErrorDetails text={save.errorText} details={save.error?.errors} />
                        </Alert>
                      )}
                      <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-1">
                        <Button
                          variant="outline"
                          leftIcon={<Save />}
                          loading={save.sending && !savingQuote}
                          disabled={parts.length === 0 || save.sending}
                          onClick={() => void doSave(false, false)}
                        >
                          Guardar borrador
                        </Button>
                        <Button leftIcon={<FileText />} loading={save.sending && savingQuote} disabled={parts.length === 0 || save.sending || check.fetching} onClick={quote}>
                          Guardar cotización
                        </Button>
                      </div>
                    </div>
                  </Section>
                ) : (
                  <Alert tone="info" title="Puede armar y revisar la compatibilidad">
                    Para guardar o cotizar hace falta el permiso «{permissionName('sales.pcbuild.manage')}». Pida al administrador que se lo asigne.
                  </Alert>
                ))}

              {current && (
                <section aria-label="Acciones del armado" className="flex flex-col gap-2">
                  {allowed.sell && canSellBuild(current) && (
                    <Button to={ROUTES.panelModule(sellPath(current.number))} leftIcon={<ShoppingCart />} fullWidth>
                      Vender en caja
                    </Button>
                  )}
                  {allowed.reserve && canReserveBuild(current) && (
                    <Button variant="outline" leftIcon={<Clock />} fullWidth onClick={() => setReserving(target)}>
                      Reservar stock
                    </Button>
                  )}
                  {allowed.publish && (canPublishBuild(current) || canUnpublishBuild(current)) && (
                    <Button variant="outline" leftIcon={current.publishedToWeb ? <GlobeLock /> : <Globe />} fullWidth loading={publish.sending} onClick={() => void togglePublish()}>
                      {current.publishedToWeb ? 'Quitar de la web' : 'Publicar en la web'}
                    </Button>
                  )}
                  {canPrintBuild(current) && (
                    <Button variant="outline" leftIcon={<Printer />} fullWidth onClick={printQuotation}>
                      Imprimir cotización
                    </Button>
                  )}
                  {allowed.release && canReleaseBuild(current) && (
                    <Button variant="danger" leftIcon={<LockOpen />} fullWidth onClick={() => setReleasing(target)}>
                      Liberar reserva
                    </Button>
                  )}
                  {allowed.cancel && canCancelBuild(current) && (
                    <Button variant="ghost" leftIcon={<Ban />} fullWidth className="text-danger-text" onClick={() => setCancelling(target)}>
                      Anular armado
                    </Button>
                  )}
                </section>
              )}
            </aside>
          </div>
        )
      )}

      {applied && current && canPrintBuild(current) && (
        <QuotePrint
          detail={applied}
          company={session?.company ?? ''}
          branch={branch ? `${branch.code} · ${branch.name}` : current.branchCode}
          seller={session?.displayName ?? ''}
          issuedAt={now}
        />
      )}

      <ConfirmDialog
        open={confirmErrors}
        onClose={() => setConfirmErrors(false)}
        tone="danger"
        title="¿Cotizar con errores de compatibilidad?"
        message="El armado tiene errores de compatibilidad. Si el cliente lo pide igual, la cotización queda marcada «con errores aceptados»."
        confirmLabel="Cotizar igual"
        cancelLabel="Volver a revisar"
        onConfirm={() => doSave(true, true)}
        error={save.errorText && <ErrorDetails text={save.errorText} details={save.error?.errors} />}
      >
        <ul className="space-y-1 text-sm" aria-label="Errores de compatibilidad">
          {(checkData?.issues ?? [])
            .filter((issue) => issue.isError)
            .map((issue, index) => (
              <li key={`${issue.code}-${index}`} className="flex items-start gap-2">
                <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-danger-text" />
                {issue.message}
              </li>
            ))}
        </ul>
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmNew}
        onClose={() => setConfirmNew(false)}
        title="¿Empezar un armado nuevo?"
        message="Se descartan las piezas elegidas (no se guardaron)."
        confirmLabel="Empezar de nuevo"
        cancelLabel="Volver"
        onConfirm={reset}
      />

      <ReserveDialog target={reserving} onClose={() => setReserving(null)} onReserved={() => detail.reload()} />
      <ReleaseDialog target={releasing} onClose={() => setReleasing(null)} onReleased={() => detail.reload()} />
      <CancelDialog target={cancelling} onClose={() => setCancelling(null)} onCancelled={() => detail.reload()} />
    </Page>
  );
}

// ---------------------------------------------------------------------------------------------------- ranuras

function SlotNav({ parts, selected, onSelect }: { parts: readonly PartLine[]; selected: SlotCode; onSelect: (slot: SlotCode) => void }) {
  const titleId = useId();
  const groups: { key: SlotInfo['group']; title: string }[] = [
    { key: 'base', title: 'Componentes' },
    { key: 'extra', title: 'Extras' },
  ];
  return (
    <nav aria-labelledby={titleId} className="min-w-0 rounded-card border border-border bg-surface p-3 shadow-card">
      <h2 id={titleId} className="px-1 text-base font-semibold">
        Ranuras
      </h2>
      {groups.map((group) => (
        <div key={group.key} className="mt-2">
          <p className="px-1 text-xs font-semibold tracking-wide text-text-faint uppercase">{group.title}</p>
          <ol className="mt-1 grid grid-cols-1 gap-1 min-[26rem]:grid-cols-2 lg:grid-cols-1">
            {SLOTS.filter((info) => info.group === group.key).map((info) => {
              const Icon = SLOT_ICONS[info.code];
              const empty = slotParts(parts, info.code).length === 0;
              const active = info.code === selected;
              return (
                <li key={info.code}>
                  <button
                    type="button"
                    aria-current={active ? 'step' : undefined}
                    onClick={() => onSelect(info.code)}
                    className={clsx(
                      'flex min-h-11 w-full cursor-pointer items-start gap-2 rounded-xl border px-2 py-2 text-left transition-colors duration-200',
                      active ? 'border-primary bg-primary-soft/60' : 'border-transparent hover:bg-surface-2',
                    )}
                  >
                    <Icon aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-primary-text" />
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-semibold text-text">{info.label}</span>
                      <span className={clsx('block text-xs', empty && info.required ? 'text-warning-text' : 'text-text-muted')}>{slotSummary(parts, info)}</span>
                    </span>
                  </button>
                </li>
              );
            })}
          </ol>
        </div>
      ))}
    </nav>
  );
}

// ---------------------------------------------------------------------------------------------------- candidatos

interface CandidatesPanelProps {
  info: SlotInfo;
  inputs: ReturnType<typeof toInputs>;
  categories: readonly CategoryRecord[];
  /** Falla de `GetCatalogOptionsQuery` (sin categorías no se pueden elegir los extras). */
  categoriesError: unknown;
  onRetryCategories: () => void;
  /** Categoría elegida en las ranuras por categoría ('' = ninguna). */
  category: string;
  onCategory: (code: string) => void;
  onlyInStock: boolean;
  onOnlyInStock: (value: boolean) => void;
  onAdd: (candidate: CandidateRecord) => void;
  onStep: (delta: number) => void;
}

function CandidatesPanel({ info, inputs, categories, categoriesError, onRetryCategories, category, onCategory, onlyInStock, onOnlyInStock, onAdd, onStep }: CandidatesPanelProps) {
  const titleId = useId();
  const [search, setSearch] = useState('');
  const [filters, setFilters] = useState<CandidateFilters>(EMPTY_CANDIDATE_FILTERS);
  const [limit, setLimit] = useState(CANDIDATE_PAGE);
  const waiting = info.byCategory && !category;
  const candidates = useRpcQuery(
    'GetPcBuildCandidatesQuery',
    { slot: info.code, current: inputs, text: search || null, onlyInStock, categoryCode: info.byCategory ? category || null : null },
    { enabled: !waiting },
  );
  const all = useMemo(() => (waiting ? [] : (candidates.data ?? [])), [waiting, candidates.data]);
  const brands = useMemo(() => brandOptions(all), [all]);
  const list = useMemo(() => filterCandidates(all, filters), [all, filters]);
  const compatible = all.filter((candidate) => candidate.isCompatible).length;
  const index = SLOTS.findIndex((slot) => slot.code === info.code);
  const change = (next: Partial<CandidateFilters>) => {
    setFilters((current) => ({ ...current, ...next }));
    setLimit(CANDIDATE_PAGE);
  };

  return (
    <section aria-labelledby={titleId} className="min-w-0 space-y-4 rounded-card border border-border bg-surface p-4 shadow-card" data-testid="candidatos">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-semibold tracking-wide text-text-faint uppercase">
            Paso {index + 1} de {SLOTS.length}
          </p>
          <h2 id={titleId} className="text-xl">
            {info.label}
          </h2>
          <p className="text-sm text-text-muted">
            {info.required ? 'Obligatoria' : 'Opcional'} · {info.multi ? 'admite varias piezas' : 'una sola pieza'}. Primero las compatibles con lo elegido; las demás,
            atenuadas con el motivo.
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" leftIcon={<ArrowLeft />} disabled={index === 0} onClick={() => onStep(-1)}>
            Anterior
          </Button>
          <Button variant="outline" rightIcon={<ArrowRight />} disabled={index === SLOTS.length - 1} onClick={() => onStep(1)}>
            Siguiente
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 items-start gap-3 sm:grid-cols-2">
        <SearchField label="Buscar" placeholder="Nombre o SKU" value={search} onChange={setSearch} />
        {info.byCategory && (
          <SelectField
            label="Categoría"
            allLabel={false}
            placeholder="Elija la categoría"
            value={category}
            onChange={onCategory}
            options={categories.map((item) => ({ value: item.code, label: item.name }))}
            hint="Los productos de esta ranura se eligen por categoría."
          />
        )}
        <SelectField label="Marca" allLabel="Todas las marcas" value={filters.brand} onChange={(brand) => change({ brand })} options={brands} />
        <div className="grid grid-cols-2 gap-2">
          <MoneyField label="Precio desde" value={filters.minPrice} onChange={(minPrice) => change({ minPrice })} />
          <MoneyField label="Precio hasta" value={filters.maxPrice} onChange={(maxPrice) => change({ maxPrice })} />
        </div>
        <Checkbox label="Solo con stock en la sucursal" checked={onlyInStock} onChange={onOnlyInStock} />
        <Checkbox label="Solo compatibles con lo elegido" checked={filters.onlyCompatible} onChange={(onlyCompatible) => change({ onlyCompatible })} />
      </div>

      {candidates.data && !waiting && (
        <p className="text-sm text-text-muted" data-testid="candidatos-resumen">
          {all.length === 1 ? '1 opción' : `${formatNumber(all.length)} opciones`} · {compatible === 1 ? '1 compatible' : `${formatNumber(compatible)} compatibles`}
          {list.length !== all.length && ` · ${formatNumber(list.length)} con estos filtros`}
        </p>
      )}

      {waiting ? (
        categoriesError ? (
          <ErrorState error={categoriesError} operation="GetCatalogOptionsQuery" title="No se pudieron cargar las categorías" onRetry={onRetryCategories} />
        ) : (
          <EmptyState size="sm" icon={<PackageSearch />} title="Elija la categoría" description="Los productos de esta ranura se eligen por categoría." />
        )
      ) : candidates.error ? (
        <ErrorState error={candidates.error} operation="GetPcBuildCandidatesQuery" onRetry={candidates.reload} retrying={candidates.fetching} />
      ) : !candidates.data ? (
        <div role="status" className="space-y-2">
          <span className="sr-only">Buscando las opciones…</span>
          {Array.from({ length: 3 }, (_, key) => (
            <Skeleton key={key} className="h-20 w-full rounded-xl" />
          ))}
        </div>
      ) : list.length === 0 ? (
        <EmptyState
          size="sm"
          icon={<PackageSearch />}
          title={all.length === 0 ? 'No hay productos para esta ranura' : 'Ninguna opción con estos filtros'}
          description={all.length === 0 ? 'El catálogo técnico no tiene productos de esta ranura (o no con esa búsqueda).' : 'Cambie la marca, el precio o quite «Solo compatibles».'}
        />
      ) : (
        <>
          <ul className="space-y-2" aria-label={`Opciones de ${info.label.toLowerCase()}`} aria-busy={candidates.fetching || undefined}>
            {list.slice(0, limit).map((candidate) => (
              <li
                key={candidate.sku}
                data-sku={candidate.sku}
                className={clsx('flex flex-wrap items-start gap-3 rounded-xl border border-border p-3', !candidate.isCompatible && 'opacity-70')}
              >
                <div className="min-w-0 flex-1 basis-56">
                  <p className="font-semibold break-words text-text">{candidate.name}</p>
                  <p className="text-xs text-text-muted">{candidate.brand ? `${candidate.sku} · ${candidate.brand}` : candidate.sku}</p>
                  {candidate.keySpecs.length > 0 && <p className="mt-0.5 text-xs text-text-muted">{candidate.keySpecs.join(' · ')}</p>}
                  {!candidate.isCompatible && (
                    <p className="mt-1 flex items-start gap-1.5 text-xs font-medium text-danger-text">
                      <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                      <span>
                        <span className="sr-only">Incompatible: </span>
                        {candidate.reason ?? 'Incompatible con lo elegido.'}
                      </span>
                    </p>
                  )}
                </div>
                <div className="flex shrink-0 flex-col items-end gap-1">
                  <p className="font-semibold text-text tabular-nums">{formatMoney(candidate.price)}</p>
                  <p className={clsx('text-xs', candidate.stock <= 0 ? 'text-danger-text' : 'text-text-muted')}>
                    {candidate.stock <= 0 ? 'Sin stock' : `Stock ${formatQuantity(candidate.stock)}`}
                  </p>
                  <Button variant="subtle" leftIcon={<Plus />} aria-label={`Agregar ${candidate.name}`} onClick={() => onAdd(candidate)}>
                    Agregar
                  </Button>
                </div>
              </li>
            ))}
          </ul>
          {list.length > limit && (
            <Button variant="outline" onClick={() => setLimit((current) => current + CANDIDATE_PAGE)}>
              Mostrar más ({formatNumber(list.length - limit)} restantes)
            </Button>
          )}
        </>
      )}
    </section>
  );
}

// ---------------------------------------------------------------------------------------------------- piezas elegidas

function ChosenParts({ parts, onRemove, onQuantity }: { parts: readonly PartLine[]; onRemove: (part: PartLine) => void; onQuantity: (part: PartLine, delta: number) => void }) {
  if (parts.length === 0) return <p className="text-sm text-text-muted">Todavía no eligió piezas: empiece por el procesador.</p>;
  return (
    <ul className="divide-y divide-border" aria-label="Piezas elegidas">
      {SLOTS.flatMap((info) =>
        slotParts(parts, info.code).map((part) => (
          <li key={`${part.slot}-${part.sku}`} className="py-2.5" data-sku={part.sku}>
            <p className="text-xs font-semibold tracking-wide text-text-faint uppercase">{info.label}</p>
            <p className="text-sm font-medium break-words text-text">{part.name}</p>
            <div className="mt-1 flex flex-wrap items-center justify-between gap-2">
              <span className="text-sm font-semibold text-text tabular-nums">{formatMoney(part.unitPrice * part.quantity)}</span>
              <span className="flex items-center gap-1">
                {info.multi && (
                  <IconButton label={`Una menos de ${part.name}`} icon={<Minus />} disabled={part.quantity <= 1} onClick={() => onQuantity(part, -1)} />
                )}
                <span className="min-w-10 text-center text-sm text-text tabular-nums" aria-label={`Cantidad: ${part.quantity}`}>
                  × {part.quantity}
                </span>
                {info.multi && (
                  <IconButton label={`Una más de ${part.name}`} icon={<Plus />} disabled={part.quantity >= MAX_QUANTITY} onClick={() => onQuantity(part, 1)} />
                )}
                <IconButton label={`Quitar ${part.name}`} icon={<X />} onClick={() => onRemove(part)} />
              </span>
            </div>
            {part.quantity > part.stock && (
              <p className="mt-0.5 text-xs text-danger-text">{part.stock <= 0 ? 'Sin stock en la sucursal' : `Stock ${formatQuantity(part.stock)} en la sucursal`}</p>
            )}
          </li>
        )),
      )}
    </ul>
  );
}

// ---------------------------------------------------------------------------------------------------- armado congelado

function FrozenDetail({ data, now }: { data: BuildDetailData; now: Date }) {
  const build = data.build;
  const reserved = build.status === 'Reserved';
  const showAvailability = build.status === 'Quoted' || reserved;
  const history = data.history ?? [];
  const contact = build.contactName || build.contactPhone || build.contactEmail;
  return (
    <div className="min-w-0 space-y-4">
      <Section title="Piezas de la cotización" description={`Precios congelados · ${piecesText(data.quotedItems.reduce((sum, item) => sum + item.quantity, 0))}`}>
        <ul className="divide-y divide-border" aria-label="Piezas de la cotización">
          {data.quotedItems.map((item, index) => {
            const availability = availabilityText(item, reserved);
            return (
              <li key={`${item.slot ?? ''}-${item.sku}-${index}`} className="flex flex-wrap justify-between gap-3 py-3">
                <span className="min-w-0 flex-1 basis-56">
                  <span className="block text-xs font-semibold tracking-wide text-text-faint uppercase">{slotInfo(item.slot).label}</span>
                  <span className="block font-medium break-words text-text">{item.name}</span>
                  <span className="block text-xs text-text-muted">
                    {item.sku} · {formatQuantity(item.quantity)} × {formatMoney(item.unitPrice)}
                    {item.keySpecs.length > 0 && ` · ${item.keySpecs.join(' · ')}`}
                  </span>
                  {showAvailability && <span className={clsx('mt-0.5 block text-xs font-medium', AVAILABILITY_TONES[availability.tone])}>{availability.text}</span>}
                </span>
                <span className="shrink-0 font-semibold text-text tabular-nums">{formatMoney(item.subtotal)}</span>
              </li>
            );
          })}
        </ul>
      </Section>

      {contact && (
        <Section title={build.channel === 'Web' ? 'Reserva de la tienda web' : 'Contacto'} description={buildNote(build, now)}>
          <DetailList
            items={[
              { label: 'Nombre', value: build.contactName },
              { label: 'Teléfono', value: build.contactPhone },
              { label: 'Correo', value: build.contactEmail },
              { label: 'Reservado hasta', value: build.reservedUntil ? formatDateTime(build.reservedUntil) : null },
              { label: 'Notas del cliente', value: build.notes, wide: true },
            ]}
          />
        </Section>
      )}

      <Section title="Bitácora" description="Cada cambio de estado del armado, con quién lo hizo.">
        {history.length === 0 ? (
          <EmptyState size="sm" icon={<History />} title="Sin hechos registrados" />
        ) : (
          <ol className="space-y-2" aria-label="Bitácora del armado">
            {history.map((event, index) => (
              <li key={`${event.occurredAt}-${index}`} className="rounded-xl border border-border bg-surface-2/60 p-3 text-sm">
                <p className="flex flex-wrap items-baseline justify-between gap-x-3">
                  <span className="font-semibold text-text">{historyAction(event.action)}</span>
                  <span className="text-xs text-text-muted tabular-nums">{formatDateTime(event.occurredAt)}</span>
                </p>
                {event.detail && <p className="mt-0.5 text-text">{event.detail}</p>}
                <p className="mt-0.5 text-xs text-text-muted">{event.user}</p>
              </li>
            ))}
          </ol>
        )}
      </Section>
    </div>
  );
}
