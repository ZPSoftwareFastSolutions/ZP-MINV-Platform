// Bloques de la página interna de componentes: tablero (botones y estadísticas plegadas), estados de la tabla,
// formulario, diálogos y avisos, distintivos, permisos y una consulta real al servidor. Datos de ejemplo.

import { BarChart3, CalendarClock, FileText, PackageSearch, Receipt, ShoppingCart, TriangleAlert, Users } from 'lucide-react';
import { useEffect, useState } from 'react';
import { WebApiError } from '@/1-domain/auth/errors';
import { PERMISSION_LIST, permissionName } from '@/4-presentation/app/contract';
import { useSession } from '@/4-presentation/hooks/useSession';
import { usePermissions } from '../hooks/usePermissions';
import { useRpcCommand } from '../hooks/useRpcCommand';
import { useRpcQuery } from '../hooks/useRpcQuery';
import { addDays, formatDate, formatMoney, formatTime, laPazToday, toIsoDate } from '../lib';
import {
  AccessDenied,
  ActionButton,
  Alert,
  BarList,
  Button,
  Checkbox,
  Collapsible,
  ComboBox,
  ConfirmDialog,
  DataTable,
  DetailList,
  Dialog,
  EmptyState,
  ErrorState,
  Form,
  FormGrid,
  LoadingState,
  MiniBars,
  MoneyField,
  NumberField,
  PermissionGate,
  RadioGroup,
  SelectField,
  Skeleton,
  StatCard,
  StatusBadge,
  Switch,
  TabPanel,
  Tabs,
  TextArea,
  TextField,
  useNotify,
  type ComboOption,
  type DataTableColumn,
} from '../kit';
import { BRANCHES, SALE_STATUSES, buildSales, searchProducts, type SampleProduct, type SampleSale } from './sampleData';

// ------------------------------------------------------------------------------------------------------------ tablero

export function DashboardDemo() {
  const notify = useNotify();
  const go = (name: string) => notify.info(name, 'En el panel real este botón lleva a su módulo.');
  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <ActionButton icon={<ShoppingCart />} title="Abrir caja" description="Cobre ventas y emita facturas." onClick={() => go('Abrir caja')} />
        <ActionButton
          icon={<PackageSearch />}
          tone="accent"
          title="Consultar stock"
          description="Existencias y reservas por sucursal."
          badge={<StatusBadge tone="warning">7 bajos</StatusBadge>}
          onClick={() => go('Consultar stock')}
        />
        <ActionButton icon={<CalendarClock />} title="Reservas" description="Carritos y armados reservados en la web." onClick={() => go('Reservas')} />
        <ActionButton icon={<FileText />} tone="neutral" title="Reportes" description="Ventas, compras e inventario." onClick={() => go('Reportes')} />
      </div>
      <Collapsible label="Ver estadísticas" openLabel="Ocultar estadísticas" icon={<BarChart3 />} description="Se cargan recién al abrir esta sección (nada de gráficos al entrar).">
        <StatsDemo />
      </Collapsible>
    </div>
  );
}

/** Estadísticas de ejemplo: «cargan» al montarse (es decir, al abrir el plegable). */
function StatsDemo() {
  const [mountedAt] = useState(() => new Date());
  const [ready, setReady] = useState(false);
  useEffect(() => {
    const timer = setTimeout(() => setReady(true), 600);
    return () => clearTimeout(timer);
  }, []);
  const sales = buildSales();
  const today = laPazToday();
  const days = Array.from({ length: 14 }, (_, index) => addDays(today, index - 13)).map((day) => ({
    label: formatDate(day).slice(0, 5),
    value: sales.filter((sale) => sale.status !== 'Voided' && toIsoDate(sale.date) === day).reduce((sum, sale) => sum + sale.total, 0),
  }));
  const units = new Map<string, number>();
  for (const sale of sales) for (const line of sale.lines) units.set(line.name, (units.get(line.name) ?? 0) + line.quantity);
  const top = [...units.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);
  return (
    <div className="space-y-5">
      <p className="text-xs text-text-faint" data-testid="estadisticas-montadas">
        Contenido montado a las {formatTime(mountedAt)} (al abrir la sección).
      </p>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Ventas de hoy" value={formatMoney(8450.5)} hint="23 ventas" icon={<Receipt />} trend={{ direction: 'up', text: '8 % más que ayer' }} loading={!ready} />
        <StatCard label="Ticket promedio" value={formatMoney(367.41)} icon={<BarChart3 />} trend={{ direction: 'down', text: '3 % menos que ayer' }} loading={!ready} />
        <StatCard label="Clientes nuevos" value="12" hint="Este mes" icon={<Users />} trend={{ direction: 'flat', text: 'Igual que el mes pasado' }} loading={!ready} />
        <StatCard label="Productos con stock bajo" value="7" tone="warning" icon={<TriangleAlert />} hint="Revise el pedido sugerido" loading={!ready} />
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <BarList label="Productos más vendidos (unidades)" items={top.map(([name, value]) => ({ label: name, value }))} />
        <div>
          <p className="mb-3 text-sm font-semibold text-text">Ventas de los últimos 14 días</p>
          <MiniBars label="Ventas de los últimos 14 días" points={days} format={formatMoney} height={96} showAxis />
        </div>
      </div>
      <BarList
        label="Ventas por sucursal"
        tone="accent"
        format={formatMoney}
        items={BRANCHES.map((branch) => ({ label: branch.label, value: sales.filter((sale) => sale.branch === branch.value && sale.status !== 'Voided').reduce((sum, sale) => sum + sale.total, 0) }))}
      />
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------------ estados

const MINI_COLUMNS: DataTableColumn<SampleSale>[] = [
  { id: 'numero', header: 'Número', value: (sale) => sale.number, card: 'title' },
  { id: 'cliente', header: 'Cliente', value: (sale) => sale.customer.name },
  { id: 'estado', header: 'Estado', value: (sale) => SALE_STATUSES[sale.status].label, cell: (sale) => <StatusBadge status={sale.status} statuses={SALE_STATUSES} /> },
  { id: 'total', header: 'Total', align: 'end', value: (sale) => sale.total, cell: (sale) => formatMoney(sale.total) },
];

type TableStateDemo = 'cargando' | 'vacia' | 'error' | 'datos';

export function TableStatesDemo() {
  const notify = useNotify();
  const [state, setState] = useState<TableStateDemo>('cargando');
  const [rows] = useState(() => buildSales().slice(0, 5));
  return (
    <Tabs
      label="Estados de la tabla"
      value={state}
      onChange={setState}
      tabs={[
        { id: 'cargando', label: 'Cargando' },
        { id: 'vacia', label: 'Vacía' },
        { id: 'error', label: 'Error' },
        { id: 'datos', label: 'Con datos' },
      ]}
    >
      <TabPanel id="cargando">
        <DataTable caption="Tabla cargando" columns={MINI_COLUMNS} rows={undefined} rowKey={(sale) => sale.number} loading />
      </TabPanel>
      <TabPanel id="vacia">
        <DataTable
          caption="Tabla vacía"
          columns={MINI_COLUMNS}
          rows={[]}
          rowKey={(sale) => sale.number}
          empty={{ title: 'No hay ventas en estas fechas', description: 'Pruebe con otro rango o limpie los filtros.', action: <Button variant="outline">Limpiar filtros</Button> }}
        />
      </TabPanel>
      <TabPanel id="error">
        <DataTable
          caption="Tabla con error"
          columns={MINI_COLUMNS}
          rows={undefined}
          rowKey={(sale) => sale.number}
          error={new WebApiError({ kind: 'network', message: 'Sin respuesta del servidor.' })}
          onRetry={() => notify.info('Reintentando…', 'En una pantalla real se vuelve a consultar al servidor.')}
        />
      </TabPanel>
      <TabPanel id="datos">
        <DataTable caption="Tabla corta sin páginas" columns={MINI_COLUMNS} rows={rows} rowKey={(sale) => sale.number} paginate={false} />
      </TabPanel>
    </Tabs>
  );
}

export function StatesDemo() {
  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <EmptyState size="sm" title="Todavía no hay clientes" description="Cree el primero con «Nuevo cliente».">
        <Button>Nuevo cliente</Button>
      </EmptyState>
      <ErrorState error={new WebApiError({ kind: 'access_denied', status: 403, message: 'Su rol no tiene el permiso reports.view.' })} />
      <div className="space-y-3 rounded-card border border-border bg-surface p-4">
        <p className="text-sm font-semibold">Esqueletos de carga</p>
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
        <LoadingState label="Cargando la sección de ejemplo…" rows={1} />
      </div>
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------------ formulario

interface ProductForm {
  name: string;
  email: string;
  product: ComboOption<SampleProduct> | null;
  branch: string;
  quantity: number | null;
  price: number | null;
  weight: number | null;
  document: 'factura' | 'recibo';
  notes: string;
  invoice: boolean;
  active: boolean;
}

const EMPTY_FORM: ProductForm = { name: '', email: '', product: null, branch: '', quantity: null, price: null, weight: null, document: 'factura', notes: '', invoice: true, active: true };

function validate(form: ProductForm): Partial<Record<keyof ProductForm, string>> {
  const errors: Partial<Record<keyof ProductForm, string>> = {};
  if (form.name.trim().length < 3) errors.name = 'Escriba el nombre (al menos 3 letras).';
  if (form.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email)) errors.email = 'Escriba un correo válido.';
  if (!form.branch) errors.branch = 'Elija una sucursal.';
  if (form.quantity === null || form.quantity < 1) errors.quantity = 'Indique la cantidad (1 o más).';
  if (form.price === null || form.price <= 0) errors.price = 'Indique el precio.';
  return errors;
}

export function FormDemo() {
  const notify = useNotify();
  const [form, setForm] = useState<ProductForm>(EMPTY_FORM);
  const [errors, setErrors] = useState<Partial<Record<keyof ProductForm, string>>>({});
  const [saving, setSaving] = useState(false);
  const set = <K extends keyof ProductForm>(key: K, value: ProductForm[K]) => setForm((current) => ({ ...current, [key]: value }));

  const submit = () => {
    const found = validate(form);
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    setSaving(true);
    return new Promise<void>((resolve) =>
      setTimeout(() => {
        setSaving(false);
        notify.success('Producto guardado', `${form.name} · ${formatMoney(form.price)}`);
        setForm(EMPTY_FORM);
        resolve();
      }, 700),
    );
  };

  return (
    <Form
      onSubmit={submit}
      busy={saving}
      actions={
        <>
          <Button
            variant="outline"
            onClick={() => {
              setForm(EMPTY_FORM);
              setErrors({});
            }}
          >
            Limpiar
          </Button>
          <Button type="submit" loading={saving}>
            Guardar producto
          </Button>
        </>
      }
    >
      <FormGrid>
        <TextField label="Nombre" required value={form.name} onChange={(value) => set('name', value)} error={errors.name} autoComplete="off" />
        <TextField label="Correo del proveedor" type="email" optional value={form.email} onChange={(value) => set('email', value)} error={errors.email} />
        <ComboBox
          label="Producto parecido"
          optional
          hint="Carga asíncrona: escriba dos letras (monitor, ssd, consola…)."
          placeholder="Nombre o SKU"
          value={form.product}
          onChange={(option) => set('product', option)}
          loadOptions={searchProducts}
        />
        <SelectField label="Sucursal" required allLabel={false} placeholder="Elija una sucursal" value={form.branch} onChange={(value) => set('branch', value)} options={BRANCHES} error={errors.branch} />
        <NumberField label="Cantidad" required unit="u." value={form.quantity} onChange={(value) => set('quantity', value)} error={errors.quantity} />
        <MoneyField label="Precio de venta" required hint="Con IVA incluido." value={form.price} onChange={(value) => set('price', value)} error={errors.price} />
        <NumberField label="Peso" optional decimals={3} unit="kg" value={form.weight} onChange={(value) => set('weight', value)} />
        <RadioGroup
          label="Documento al vender"
          orientation="horizontal"
          value={form.document}
          onChange={(value) => set('document', value)}
          options={[
            { value: 'factura', label: 'Factura', description: 'Con crédito fiscal' },
            { value: 'recibo', label: 'Recibo', description: 'Sin crédito fiscal' },
          ]}
        />
        <TextArea label="Notas" optional maxLength={200} value={form.notes} onChange={(value) => set('notes', value)} className="sm:col-span-2" />
        <Checkbox label="Emitir factura al vender" description="Usa el NIT del cliente si lo tiene." checked={form.invoice} onChange={(value) => set('invoice', value)} />
        <Switch label="Producto activo" description="Se vende en la caja y en la tienda web." checked={form.active} onChange={(value) => set('active', value)} />
      </FormGrid>
    </Form>
  );
}

// ------------------------------------------------------------------------------------------------------------ diálogos y avisos

export function DialogsDemo() {
  const notify = useNotify();
  const [dialog, setDialog] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [attempts, setAttempts] = useState(0);
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [nit, setNit] = useState('');

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => setDialog(true)}>Abrir diálogo</Button>
        <Button
          variant="danger"
          onClick={() => {
            setAttempts(0);
            setConfirmError(null);
            setConfirm(true);
          }}
        >
          Confirmación peligrosa
        </Button>
      </div>
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" onClick={() => notify.success('Cambios guardados', 'El cliente quedó actualizado.')}>
          Aviso de éxito
        </Button>
        <Button variant="outline" onClick={() => notify.info('Exportación lista', 'Se descargó ventas-2026-09-28.csv.')}>
          Aviso informativo
        </Button>
        <Button variant="outline" onClick={() => notify.warning('La caja sigue abierta', 'Ciérrela antes de terminar el turno.')}>
          Advertencia
        </Button>
        <Button
          variant="outline"
          onClick={() => notify.error('No se pudo anular', new WebApiError({ kind: 'access_denied', status: 403, message: 'Su rol no tiene el permiso billing.void.' }))}
        >
          Error por permisos
        </Button>
        <Button variant="outline" onClick={() => notify.error('No se pudo guardar', new WebApiError({ kind: 'network', message: 'Sin respuesta.' }))}>
          Error de conexión
        </Button>
      </div>

      <Dialog
        open={dialog}
        onClose={() => setDialog(false)}
        title="Nuevo cliente"
        description="Los datos se usan en la factura."
        footer={
          <>
            <Button variant="outline" onClick={() => setDialog(false)}>
              Cancelar
            </Button>
            <Button type="submit" form="muestra-dialogo">
              Guardar cliente
            </Button>
          </>
        }
      >
        <Form
          id="muestra-dialogo"
          onSubmit={() => {
            notify.success('Cliente guardado', name || 'Sin nombre');
            setDialog(false);
          }}
        >
          <TextField label="Nombre o razón social" data-autofocus value={name} onChange={setName} />
          <TextField label="NIT o CI" inputMode="numeric" value={nit} onChange={setNit} optional />
        </Form>
      </Dialog>

      <ConfirmDialog
        open={confirm}
        onClose={() => setConfirm(false)}
        tone="danger"
        title="¿Eliminar la lista de precios «Mayoristas»?"
        message="Los productos vuelven al precio general. La primera vez esta muestra falla a propósito para ver el error."
        confirmLabel="Eliminar lista"
        error={confirmError}
        onConfirm={() =>
          new Promise((resolve) =>
            setTimeout(() => {
              if (attempts === 0) {
                setAttempts(1);
                setConfirmError('No se pudo completar la operación: sin respuesta del servidor (simulado). Pulse de nuevo.');
                resolve({ ok: false });
              } else {
                notify.success('Lista eliminada', 'Mayoristas');
                resolve({ ok: true });
              }
            }, 600),
          )
        }
      />
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------------ distintivos

export function BadgesDemo() {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        <StatusBadge tone="neutral">Borrador</StatusBadge>
        <StatusBadge tone="info">En tránsito</StatusBadge>
        <StatusBadge tone="success">Pagada</StatusBadge>
        <StatusBadge tone="warning">Por vencer</StatusBadge>
        <StatusBadge tone="danger">Anulada</StatusBadge>
        <StatusBadge tone="accent">Reservada</StatusBadge>
      </div>
      <div className="flex flex-wrap gap-2">
        {(Object.keys(SALE_STATUSES) as (keyof typeof SALE_STATUSES)[]).map((status) => (
          <StatusBadge key={status} status={status} statuses={SALE_STATUSES} />
        ))}
        <StatusBadge status="Desconocido" statuses={SALE_STATUSES} />
      </div>
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------------ permisos

export function PermissionsDemo() {
  const { permissions } = usePermissions();
  const granted = permissions[0];
  const lacking = PERMISSION_LIST.find((permission) => !permissions.includes(permission.code))?.code;
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      {granted ? (
        <PermissionGate permission={granted}>
          <Alert tone="success" title="Visible con permiso">
            Se ve porque la sesión tiene «{permissionName(granted)}».
          </Alert>
        </PermissionGate>
      ) : (
        <Alert tone="info">La sesión no tiene permisos.</Alert>
      )}
      {lacking ? (
        <PermissionGate permission={lacking} fallback={<AccessDenied permissions={[lacking]} />}>
          <Alert tone="info">Contenido protegido.</Alert>
        </PermissionGate>
      ) : (
        <Alert tone="info">La sesión tiene todos los permisos: no hay ejemplo de acceso denegado.</Alert>
      )}
    </div>
  );
}

// ------------------------------------------------------------------------------------------------------------ servidor

export function RpcDemo() {
  const { mode } = useSession();
  return (
    <div className="space-y-4">
      <Collapsible level={3} label="Ver una consulta real al servidor" description="useRpcQuery('GetMyAccountQuery'): se ejecuta recién al abrir.">
        <AccountQueryDemo />
      </Collapsible>
      {mode === 'mock' && <CommandDemo />}
    </div>
  );
}

function AccountQueryDemo() {
  const account = useRpcQuery('GetMyAccountQuery', {});
  if (account.loading) return <LoadingState label="Consultando al servidor…" rows={1} />;
  if (account.error) return <ErrorState error={account.error} operation="GetMyAccountQuery" onRetry={account.reload} />;
  if (!account.data) return null;
  return (
    <DetailList
      items={[
        { label: 'Nombre', value: account.data.name },
        { label: 'Correo', value: account.data.email },
        { label: 'Teléfono', value: account.data.phone },
      ]}
    />
  );
}

/** Solo en el modo de demostración (sin servidor): un comando que el personal no puede ejecutar. */
function CommandDemo() {
  const cancel = useRpcCommand('CancelMyReservationCommand', { success: 'Reserva liberada' });
  return (
    <div className="flex flex-wrap items-center gap-3">
      <Button variant="outline" loading={cancel.sending} onClick={() => void cancel.run({ number: 'RES-WEB-000012' })}>
        Probar un comando sin permiso
      </Button>
      {cancel.errorText && <p className="text-sm text-text-muted">Último error: {cancel.errorText}</p>}
    </div>
  );
}
