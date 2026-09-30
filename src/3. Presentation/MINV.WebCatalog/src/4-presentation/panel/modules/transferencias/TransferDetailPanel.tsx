// Módulo «Transferencias» · detalle lateral (el panel derecho del escritorio): ruta, almacenes, fechas, valor al costo del
// origen, líneas con lo despachado, lo recibido, el faltante y su motivo, los lotes y las series que viajan, y la
// BITÁCORA (`GetTransferQuery`, se pide recién al abrir). Los botones siguen el lado de la sesión (regla B-03): el origen
// despacha y anula; el destino recibe.

import { Ban, PackageCheck, Truck } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, ErrorState, LoadingState, SidePanel, StatusBadge } from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney, formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { TRANSFER_STATES, type TransferItem } from './transfers';

export interface TransferDetailPanelProps {
  item: TransferItem | null;
  open: boolean;
  onClose: () => void;
  /** La sesión puede ejecutar los comandos de transferencias. */
  canManage: boolean;
  onDispatch: (item: TransferItem) => void;
  onReceive: (item: TransferItem) => void;
  onCancel: (item: TransferItem) => void;
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

export function TransferDetailPanel({ item, open, onClose, canManage, onDispatch, onReceive, onCancel }: TransferDetailPanelProps) {
  const detail = useRpcQuery('GetTransferQuery', { id: item?.key ?? '' }, { enabled: open && item !== null });
  const header = detail.data?.header ?? item?.row;
  const hasActions = Boolean(item && canManage && (item.row.canDispatch || item.row.canReceive || item.row.canCancel));

  const footer =
    item && hasActions ? (
      <div className="flex flex-col gap-2">
        {item.row.canReceive && (
          <Button leftIcon={<PackageCheck />} fullWidth onClick={() => onReceive(item)}>
            Recibir
          </Button>
        )}
        {item.row.canDispatch && (
          <Button leftIcon={<Truck />} fullWidth onClick={() => onDispatch(item)}>
            Despachar
          </Button>
        )}
        {item.row.canCancel && (
          <Button variant="danger" leftIcon={<Ban />} fullWidth onClick={() => onCancel(item)}>
            Anular
          </Button>
        )}
      </div>
    ) : undefined;

  return (
    <SidePanel
      open={open}
      onClose={onClose}
      title={item ? `Transferencia ${item.row.number}` : 'Transferencia'}
      description={item ? `${item.row.fromBranch} → ${item.row.toBranch}` : undefined}
      headerExtra={header && <StatusBadge status={header.status} statuses={TRANSFER_STATES} />}
      footer={footer}
    >
      {item && header && (
        <div className="space-y-5" data-testid="detalle-transferencia">
          <DetailList
            items={[
              { label: 'Sale de', value: `${header.fromBranchCode} · ${header.fromBranch} (almacén ${header.fromWarehouse})`, wide: true },
              { label: 'Llega a', value: `${header.toBranchCode} · ${header.toBranch} (almacén ${header.toWarehouse})`, wide: true },
              { label: 'Solicitada', value: formatDateTime(header.requestedAt) },
              { label: 'Despachada', value: header.dispatchedAt ? formatDateTime(header.dispatchedAt) : null },
              { label: 'Recibida', value: header.receivedAt ? formatDateTime(header.receivedAt) : null },
              { label: 'Valor al costo del origen', value: header.status === 'Pending' ? 'Se calcula al despachar' : formatMoney(header.value) },
              { label: 'Faltante', value: header.shortage > 0 ? formatQuantity(header.shortage) : null },
              { label: 'Notas', value: header.notes, wide: true },
            ]}
          />
          {detail.error && <ErrorState error={detail.error} operation="GetTransferQuery" title="No se pudo cargar el detalle" onRetry={detail.reload} retrying={detail.fetching} />}
          {!detail.data && !detail.error && <LoadingState label="Cargando los productos y la bitácora…" rows={2} />}
          {detail.data && (
            <Block title={detail.data.lines.length === 1 ? '1 producto' : `${formatNumber(detail.data.lines.length)} productos`}>
              <ul className="divide-y divide-border rounded-xl border border-border text-sm" data-testid="lineas-transferencia">
                {detail.data.lines.map((line) => (
                  <li key={line.lineId} className="space-y-1 px-3 py-2">
                    <span className="flex flex-wrap justify-between gap-2">
                      <span className="min-w-0 font-medium break-words text-text">{line.name}</span>
                      <span className="tabular-nums">{formatQuantity(line.quantity, { unit: line.unit })}</span>
                    </span>
                    <span className="block text-xs text-text-muted">
                      {line.sku}
                      {header.status === 'Received' && ` · recibido ${formatQuantity(line.received)}`}
                      {line.shortage > 0 && ` · faltante ${formatQuantity(line.shortage)}${line.shortageReason ? ` (${line.shortageReason})` : ''}`}
                      {line.unitCost !== null && ` · costo ${formatMoney(line.unitCost)}`}
                    </span>
                    {line.lots.length > 0 && <span className="block text-xs text-text-muted">Lotes: {line.lots.join(', ')}</span>}
                    {line.serials && line.serials.length > 0 && <span className="block font-mono text-xs text-text-muted">Series: {line.serials.join(', ')}</span>}
                  </li>
                ))}
              </ul>
            </Block>
          )}
          {detail.data && (
            <Block title="Bitácora">
              <ol className="space-y-2" data-testid="bitacora-transferencia">
                {detail.data.history.map((event, index) => (
                  <li key={`${event.occurredAt}-${index}`} className="rounded-xl border border-border px-3 py-2 text-sm">
                    <span className="flex flex-wrap justify-between gap-2">
                      <span className="font-medium text-text">{event.statusLabel}</span>
                      <span className="text-xs text-text-muted">{formatDateTime(event.occurredAt)}</span>
                    </span>
                    <span className="block text-xs text-text-muted">
                      {event.user}
                      {event.detail ? ` · ${event.detail}` : ''}
                    </span>
                  </li>
                ))}
              </ol>
            </Block>
          )}
        </div>
      )}
    </SidePanel>
  );
}
