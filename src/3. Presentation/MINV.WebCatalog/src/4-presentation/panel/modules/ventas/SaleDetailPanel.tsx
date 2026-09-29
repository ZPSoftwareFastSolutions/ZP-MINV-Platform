// Módulo «Ventas» · detalle lateral de una venta (como el panel de la derecha del escritorio): datos, pago, factura del SIN
// y su estado, los productos con sus series (`GetSaleLinesQuery`, se piden recién al abrir el detalle), las devoluciones y
// los botones de las acciones que el rol puede usar.

import { Ban, Filter, Mail, Printer, Undo2, Users } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, DetailList, ErrorState, SidePanel, Skeleton, StatusBadge } from '@/4-presentation/panel/kit';
import { formatDate, formatDateTime, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import {
  FISCAL_STATES,
  NOTE_STATES,
  OFFLINE_EMISSION,
  SALE_STATES,
  fiscalHelp,
  fiscalStatusLabel,
  formatFiscalDateTime,
  type SaleActions,
  type SaleItem,
} from './sales';

export interface SaleDetailPanelProps {
  item: SaleItem | null;
  open: boolean;
  onClose: () => void;
  fiscalEnabled: boolean;
  actions: SaleActions | null;
  /** «CM» → «CM · Casa matriz La Paz». */
  branchLabel: (code: string | null) => string;
  onReprint: (item: SaleItem) => void;
  onEmail: (item: SaleItem) => void;
  onReturn: (item: SaleItem) => void;
  onVoid: (item: SaleItem) => void;
  onOnlyCustomer: (item: SaleItem) => void;
  /** ¿La lista ya muestra solo las ventas de este cliente? */
  filteredByCustomer: boolean;
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-2">
      <h3 id={id} className="text-sm font-semibold text-text">
        {title}
      </h3>
      {children}
    </section>
  );
}

export function SaleDetailPanel({
  item,
  open,
  onClose,
  fiscalEnabled,
  actions,
  branchLabel,
  onReprint,
  onEmail,
  onReturn,
  onVoid,
  onOnlyCustomer,
  filteredByCustomer,
}: SaleDetailPanelProps) {
  const { can } = usePermissions();
  // Las líneas se piden recién con el detalle abierto.
  const lines = useRpcQuery('GetSaleLinesQuery', { invoiceNumber: item?.key ?? '' }, { enabled: open && item !== null });
  const canSeeCustomers = can('inventory.stock.view') && (can('sales.customers.manage') || can('sales.view'));

  const footer =
    item && actions ? (
      <div className="flex flex-col gap-2">
        {actions.reprint.visible && (
          <Button leftIcon={<Printer />} fullWidth onClick={() => onReprint(item)}>
            Reimprimir la factura
          </Button>
        )}
        {actions.email.visible && !actions.email.blocked && (
          <Button variant="outline" leftIcon={<Mail />} fullWidth onClick={() => onEmail(item)}>
            Enviar la factura por correo
          </Button>
        )}
        {actions.returns.visible && !actions.returns.blocked && (
          <Button variant="outline" leftIcon={<Undo2 />} fullWidth onClick={() => onReturn(item)}>
            Devolver productos
          </Button>
        )}
        {!filteredByCustomer && (
          <Button variant="ghost" leftIcon={<Filter />} fullWidth onClick={() => onOnlyCustomer(item)}>
            Ver solo las ventas de {item.row.customer}
          </Button>
        )}
        {canSeeCustomers && (
          <Button variant="ghost" leftIcon={<Users />} fullWidth to={ROUTES.panelModule(`clientes?q=${encodeURIComponent(item.row.customerCode)}`)}>
            Ver el cliente
          </Button>
        )}
        {actions.void.visible && !actions.void.blocked && (
          <Button variant="danger" leftIcon={<Ban />} fullWidth onClick={() => onVoid(item)}>
            Anular la venta
          </Button>
        )}
        {[actions.email, actions.returns, actions.void]
          .filter((action) => action.visible && action.blocked)
          .map((action) => (
            <p key={action.blocked} className="text-xs text-text-muted">
              {action.blocked}
            </p>
          ))}
      </div>
    ) : undefined;

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={item ? `Venta ${item.key}` : 'Venta'}
      description={item ? formatDateTime(item.row.issuedAt) : undefined}
      headerExtra={item && <StatusBadge status={item.state} statuses={SALE_STATES} />}
      footer={footer}
    >
      {item && (
        <div className="space-y-6" data-testid="detalle-venta">
          {item.state === 'anulada' && (
            <Alert tone="danger" title="Venta anulada">
              {item.row.voidReason ?? 'Sin motivo registrado.'}
            </Alert>
          )}
          <DetailList
            items={[
              { label: 'Pedido', value: item.row.orderNumber },
              { label: 'Sucursal', value: branchLabel(item.branch) },
              {
                label: 'Cliente',
                value: (
                  <span className="inline-flex flex-wrap items-center gap-2">
                    <span>
                      {item.row.customer} <span className="text-text-muted">({item.row.customerCode})</span>
                    </span>
                    {item.webCustomer && <StatusBadge tone="info">Cliente web</StatusBadge>}
                  </span>
                ),
              },
              { label: 'Atendió', value: item.row.cashier },
            ]}
          />

          <Block title="Pago">
            <DetailList
              items={[
                { label: 'Medio de pago', value: item.row.paymentMethod },
                { label: 'Total', value: formatMoney(item.row.total) },
                { label: 'IVA incluido', value: formatMoney(item.row.tax) },
                { label: 'Reembolsado en devoluciones', value: item.refunded > 0 ? formatMoney(item.refunded) : 'Nada' },
              ]}
            />
          </Block>

          {fiscalEnabled && (
            <Block title="Factura del SIN">
              {item.fiscal ? (
                <DetailList
                  items={[
                    { label: 'Número', value: `N° ${item.fiscal.number}` },
                    {
                      label: 'Estado',
                      value: (
                        <span className="block space-y-1">
                          <StatusBadge status={item.fiscal.status} statuses={FISCAL_STATES} />
                          {item.fiscal.isReverted && <span className="block text-xs text-text-muted">{fiscalStatusLabel(item.fiscal)}</span>}
                          <span className="block text-xs text-text-muted">{fiscalHelp(item.fiscal.status)}</span>
                        </span>
                      ),
                    },
                    { label: 'Emisión', value: item.fiscal.emissionType === OFFLINE_EMISSION ? 'Fuera de línea' : 'En línea' },
                    { label: 'Se puede anular hasta', value: formatFiscalDateTime(item.fiscal.voidDeadline) },
                    { label: 'CUF', value: <span className="font-mono text-xs break-all">{item.fiscal.cuf}</span>, wide: true },
                  ]}
                />
              ) : (
                <p className="text-sm text-text-muted">Esta venta no tiene factura del SIN.</p>
              )}
            </Block>
          )}

          <Block title={`Productos (${formatNumber(item.row.items)})`}>
            {lines.error ? (
              <ErrorState error={lines.error} title="No se pudieron leer los productos" operation="GetSaleLinesQuery" onRetry={lines.reload} retrying={lines.fetching} />
            ) : !lines.data ? (
              <div className="space-y-2" role="status" aria-label="Cargando los productos…">
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-2/3" />
              </div>
            ) : (
              <ul className="divide-y divide-border text-sm" data-testid="lineas-venta">
                {lines.data.map((line, index) => (
                  <li key={`${line.sku}-${index}`} className="flex items-start justify-between gap-3 py-2">
                    <span className="min-w-0">
                      <span className="block text-text">{line.name}</span>
                      <span className="block text-xs text-text-muted">
                        {line.sku} · {formatQuantity(line.quantity)} {line.unit} × {formatMoney(line.unitPrice)}
                        {line.discountPercent > 0 ? ` · descuento ${formatQuantity(line.discountPercent)} %` : ''}
                      </span>
                      {line.serials && line.serials.length > 0 && (
                        <span className="block text-xs break-words text-text-muted">
                          {line.serials.length === 1 ? 'Serie' : 'Series'}: {line.serials.join(', ')}
                        </span>
                      )}
                    </span>
                    <span className="shrink-0 font-semibold tabular-nums">{formatMoney(line.amount)}</span>
                  </li>
                ))}
              </ul>
            )}
          </Block>

          {item.returns.length > 0 && (
            <Block title="Devoluciones">
              <ul className="divide-y divide-border text-sm" data-testid="devoluciones-venta">
                {item.returns.map((row) => (
                  <li key={row.number} className="space-y-1 py-2">
                    <div className="flex items-start justify-between gap-3">
                      <span className="font-medium text-text">{row.number}</span>
                      <span className="shrink-0 font-semibold tabular-nums">{formatMoney(row.refund)}</span>
                    </div>
                    <p className="text-xs text-text-muted">
                      {formatDate(row.returnedAt)} · {row.reason}
                    </p>
                    {row.creditNote && (
                      <p className="flex flex-wrap items-center gap-2 text-xs text-text-muted">
                        {row.creditNote}
                        {row.creditNoteStatus && <StatusBadge status={row.creditNoteStatus} statuses={NOTE_STATES} />}
                      </p>
                    )}
                  </li>
                ))}
              </ul>
            </Block>
          )}
        </div>
      )}
    </SidePanel>
  );
}
