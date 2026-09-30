// Módulo «Órdenes de compra» · detalle lateral de una orden (el panel derecho del escritorio): datos, líneas con lo
// pedido, lo recibido y lo pendiente (`GetPurchaseOrderQuery`, se pide recién al abrir el detalle), las recepciones (con
// «Registrar factura», que lleva a la pestaña de facturas) y los botones de lo que el rol puede hacer con la orden.

import { Ban, CircleCheck, FileText, Filter, PackageCheck, Truck } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, DetailList, ErrorState, LoadingState, SidePanel, StatusBadge } from '@/4-presentation/panel/kit';
import { formatDate, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { ORDER_STATES, pendingOf, type OrderItem } from './purchasing';

/** Lo que la sesión puede hacer (permisos): el estado de la orden decide el resto. */
export interface OrderPermissions {
  approve: boolean;
  receive: boolean;
  cancel: boolean;
  invoices: boolean;
  suppliers: boolean;
}

export interface OrderDetailPanelProps {
  item: OrderItem | null;
  open: boolean;
  onClose: () => void;
  allowed: OrderPermissions;
  onApprove: (item: OrderItem) => void;
  onReceive: (item: OrderItem) => void;
  onCancel: (item: OrderItem) => void;
  onOnlySupplier: (item: OrderItem) => void;
  /** ¿La lista ya muestra solo este proveedor? */
  filteredBySupplier: boolean;
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

export function OrderDetailPanel({ item, open, onClose, allowed, onApprove, onReceive, onCancel, onOnlySupplier, filteredBySupplier }: OrderDetailPanelProps) {
  const detail = useRpcQuery('GetPurchaseOrderQuery', { id: item?.key ?? '' }, { enabled: open && item !== null });
  const order = detail.data?.order ?? item?.row;
  const abilities = item?.abilities;

  const footer = item && abilities && (
    <div className="flex flex-col gap-2">
      {allowed.receive && abilities.receive && (
        <Button leftIcon={<PackageCheck />} fullWidth onClick={() => onReceive(item)}>
          Recibir mercadería
        </Button>
      )}
      {allowed.approve && abilities.approve && (
        <Button leftIcon={<CircleCheck />} fullWidth onClick={() => onApprove(item)}>
          Aprobar la orden
        </Button>
      )}
      {!filteredBySupplier && (
        <Button variant="outline" leftIcon={<Filter />} fullWidth onClick={() => onOnlySupplier(item)}>
          Ver solo las órdenes de este proveedor
        </Button>
      )}
      {allowed.suppliers && (
        <Button variant="ghost" leftIcon={<Truck />} fullWidth to={ROUTES.panelModule(`proveedores?q=${encodeURIComponent(item.row.supplierCode)}`)}>
          Ver el proveedor
        </Button>
      )}
      {allowed.cancel && abilities.cancel && (
        <Button variant="danger" leftIcon={<Ban />} fullWidth onClick={() => onCancel(item)}>
          Anular la orden
        </Button>
      )}
    </div>
  );

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={item ? `Orden ${item.row.number}` : 'Orden de compra'}
      description={item ? `${item.row.supplier} · ${formatMoney(item.row.total)}` : undefined}
      headerExtra={order && <StatusBadge status={order.status} statuses={ORDER_STATES} />}
      footer={footer}
    >
      {item && order && (
        <div className="space-y-5" data-testid="detalle-orden">
          {item.overdue && (
            <Alert tone="warning" title="Entrega vencida">
              La entrega esperada era el {formatDate(order.expectedDate)} y todavía hay mercadería por recibir.
            </Alert>
          )}
          <DetailList
            items={[
              { label: 'Proveedor', value: `${order.supplier} (${order.supplierCode})` },
              { label: 'Sucursal', value: item.branchCode },
              { label: 'Pedida el', value: formatDate(order.orderDate) },
              { label: 'Entrega esperada', value: order.expectedDate ? formatDate(order.expectedDate) : null },
              { label: 'Total', value: formatMoney(order.total) },
              { label: 'Recibido', value: `${formatNumber(order.receivedPercent)} %` },
              { label: 'Notas', value: order.notes, wide: true },
            ]}
          />
          {detail.error && <ErrorState error={detail.error} operation="GetPurchaseOrderQuery" title="No se pudieron cargar las líneas" onRetry={detail.reload} retrying={detail.fetching} />}
          {!detail.data && !detail.error && <LoadingState label="Cargando las líneas de la orden…" rows={2} />}
          {detail.data && (
            <Block title={detail.data.lines.length === 1 ? '1 producto' : `${formatNumber(detail.data.lines.length)} productos`}>
              <ul className="divide-y divide-border rounded-xl border border-border" data-testid="lineas-orden">
                {detail.data.lines.map((line) => {
                  const pending = pendingOf(line);
                  return (
                    <li key={line.lineId} className="space-y-1 px-3 py-2 text-sm">
                      <div className="flex flex-wrap items-baseline justify-between gap-2">
                        <span className="min-w-0 font-medium break-words text-text">{line.name}</span>
                        <span className="tabular-nums text-text">{formatMoney(line.subtotal)}</span>
                      </div>
                      <p className="text-xs text-text-muted">
                        {line.sku} · pedido {formatQuantity(line.quantity, { unit: line.unit })} × {formatMoney(line.unitCost)} · recibido{' '}
                        {formatQuantity(line.received)}
                        {pending > 0 ? ` · pendiente ${formatQuantity(pending)}` : ' · completo'}
                      </p>
                    </li>
                  );
                })}
              </ul>
            </Block>
          )}
          {detail.data && (
            <Block title="Recepciones">
              {detail.data.receipts.length === 0 ? (
                <p className="text-sm text-text-muted">Todavía no se recibió mercadería de esta orden.</p>
              ) : (
                <ul className="space-y-2" data-testid="recepciones-orden">
                  {detail.data.receipts.map((receipt) => (
                    <li key={receipt} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border px-3 py-1 text-sm">
                      <span className="font-mono">{receipt}</span>
                      {allowed.invoices && (
                        <Button variant="ghost" leftIcon={<FileText />} to={ROUTES.panelModule(`compras?pestana=facturas&f_q=${encodeURIComponent(receipt)}`)}>
                          Factura del proveedor
                        </Button>
                      )}
                    </li>
                  ))}
                </ul>
              )}
            </Block>
          )}
        </div>
      )}
    </SidePanel>
  );
}
