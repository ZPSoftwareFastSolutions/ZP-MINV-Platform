// Módulo «Integraciones» · pestaña «Correos de reservas» (`GetOutgoingMailsQuery`): la cola de las confirmaciones de
// reserva (tienda web, cuenta del cliente y mostrador), con su estado, intentos, último error y próximo intento. El
// estado, el número de la reserva y cuántos revisar se piden al servidor; la sucursal, el tipo de reserva, las fechas y
// la búsqueda se aplican en la página. Todo en la dirección; detalle lateral, exportar CSV, «Reenviar el correo»
// (`ResendReservationMailCommand`) y un enlace a la reserva en Ventas › Reservas.

import { Download, Eye, Filter, MailCheck, RefreshCw, Send, ShoppingBag } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  DataTable,
  DateRangeField,
  DetailList,
  FilterBar,
  SearchField,
  SelectField,
  SidePanel,
  StatusBadge,
  Toolbar,
  statusOptions,
  useNotify,
  type DataTableColumn,
} from '@/4-presentation/panel/kit';
import { exportCsv, formatDateTime, formatNumber } from '@/4-presentation/panel/lib';
import { ResendMailDialog, type ResendTarget } from './ResendMailDialog';
import {
  MAIL_CSV_COLUMNS,
  MAIL_FILTERS,
  MAIL_STATUSES,
  MAIL_TAKE_OPTIONS,
  RESERVATION_KINDS,
  branchOptions,
  filterMails,
  mailNextText,
  mailStatusLabel,
  mailsQueryOf,
  reservationKindText,
  type MailRecord,
} from './integrations';

const COLUMNS: DataTableColumn<MailRecord>[] = [
  { id: 'solicitado', header: 'Solicitado', value: (mail) => new Date(mail.requestedAt), cell: (mail) => formatDateTime(mail.requestedAt), className: 'whitespace-nowrap' },
  {
    id: 'reserva',
    header: 'Reserva',
    value: (mail) => mail.reservation,
    card: 'title',
    cell: (mail) => (
      <span className="block min-w-36">
        <span className="block font-mono">{mail.reservation}</span>
        <span className="block text-xs font-normal text-text-muted">
          {reservationKindText(mail.reservationKind)} · {mail.branchCode}
        </span>
      </span>
    ),
  },
  { id: 'destinatario', header: 'Destinatario', value: (mail) => mail.recipient, cell: (mail) => <span className="break-all">{mail.recipient}</span> },
  { id: 'estado', header: 'Estado', value: (mail) => mailStatusLabel(mail), cell: (mail) => <StatusBadge status={mail.status} statuses={MAIL_STATUSES} /> },
  { id: 'intentos', header: 'Intentos', align: 'end', value: (mail) => mail.attempts, cell: (mail) => `${formatNumber(mail.attempts)} de ${formatNumber(mail.maxAttempts)}` },
  {
    id: 'seguimiento',
    header: 'Seguimiento',
    value: (mail) => mail.lastError ?? mailNextText(mail),
    sortable: false,
    cell: (mail) => (
      <span className="block max-w-sm min-w-44">
        {mailNextText(mail) && <span className="block text-text-muted">{mailNextText(mail)}</span>}
        {mail.lastError && <span className="line-clamp-2 block text-xs text-danger-text">{mail.lastError}</span>}
      </span>
    ),
  },
];

export function MailsTab() {
  const notify = useNotify();
  const navigate = useNavigate();
  const { canRun } = usePermissions();
  const canResend = canRun('ResendReservationMailCommand');
  const table = useTableState({ filters: MAIL_FILTERS, sort: { column: 'solicitado', direction: 'desc' } });
  const filters = table.filters;
  const mails = useRpcQuery('GetOutgoingMailsQuery', mailsQueryOf(filters));
  const all = useMemo(() => mails.data ?? [], [mails.data]);
  const rows = useMemo(() => filterMails(all, filters), [all, filters]);
  const [detail, setDetail] = useState<{ mail: MailRecord; open: boolean } | null>(null);
  const [resending, setResending] = useState<ResendTarget | null>(null);
  const reservationOf = (number: string) => ROUTES.panelModule(`reservas?q=${encodeURIComponent(number)}`);
  const current = detail ? (all.find((mail) => mail.id === detail.mail.id) ?? detail.mail) : null;

  const askResend = (target: ResendTarget) => {
    setDetail((value) => (value?.open ? { ...value, open: false } : value));
    setResending(target);
  };

  const exportRows = () => {
    const file = exportCsv('correos-de-reservas', MAIL_CSV_COLUMNS, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  const pending = rows.filter((mail) => mail.status === 'Pending').length;
  const exhausted = rows.filter((mail) => mail.status === 'Exhausted').length;

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Reserva, destinatario o error" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(MAIL_STATUSES)} />
        <SearchField label="Número de reserva" placeholder="ARM-WEB-000001" value={filters.reserva} onChange={(value) => table.setFilter('reserva', value)} />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchOptions(all.map((mail) => mail.branchCode), '')} />
        <SelectField
          label="Tipo de reserva"
          allLabel="Armados y carritos"
          value={filters.tipo}
          onChange={(value) => table.setFilter('tipo', value)}
          options={Object.entries(RESERVATION_KINDS).map(([value, label]) => ({ value, label }))}
        />
        <SelectField
          label="Correos a revisar"
          allLabel={false}
          value={filters.registros}
          onChange={(value) => table.setFilter('registros', value || MAIL_FILTERS.registros)}
          options={MAIL_TAKE_OPTIONS}
          hint="Los más recientes; los filtros de la página se aplican sobre ellos."
        />
        <DateRangeField label="Solicitados" value={table.dateRange()} onChange={(range) => table.setDateRange(range)} />
      </FilterBar>

      <Toolbar
        end={
          <>
            {canResend && (
              <Button leftIcon={<Send />} onClick={() => askResend({ number: '' })}>
                Reenviar una confirmación
              </Button>
            )}
            <Button variant="outline" leftIcon={<RefreshCw />} loading={mails.fetching && !mails.loading} onClick={mails.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {mails.data && (
          <span className="text-sm text-text-muted" data-testid="correos-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} correos · {formatNumber(pending)} pendientes · {formatNumber(exhausted)} agotados
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="Correos de las reservas"
        columns={COLUMNS}
        rows={mails.data ? rows : undefined}
        rowKey={(mail) => mail.id}
        rowLabel={(mail) => `el correo de la reserva ${mail.reservation} a ${mail.recipient}`}
        loading={mails.loading}
        refreshing={mails.fetching && !mails.loading}
        error={mails.error}
        onRetry={mails.reload}
        operation="GetOutgoingMailsQuery"
        {...table.tableProps}
        onRowOpen={(mail) => setDetail({ mail, open: true })}
        activeRowKey={detail?.open ? detail.mail.id : null}
        rowActions={(mail) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => setDetail({ mail, open: true }) },
          { label: 'Reenviar el correo', icon: <Send />, onSelect: () => askResend({ number: mail.reservation, recipient: mail.recipient }), hidden: !canResend },
          { label: 'Ver la reserva', icon: <ShoppingBag />, onSelect: () => navigate(reservationOf(mail.reservation)) },
        ]}
        empty={{
          title: all.length === 0 ? 'Todavía no hay correos en la cola' : 'No hay correos con estos filtros',
          description: all.length === 0 ? 'Aparecen cuando se reserva un armado o un carrito con un correo de contacto.' : 'Pruebe con otro estado, otras fechas o revise más correos.',
          icon: <MailCheck />,
          action:
            all.length === 0 ? undefined : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((value) => (value?.open ? { ...value, open: false } : value))}
        title={current ? `Reserva ${current.reservation}` : 'Correo'}
        description={current ? `${current.kindText} · ${current.recipient}` : undefined}
        headerExtra={current && <StatusBadge status={current.status} statuses={MAIL_STATUSES} />}
        footer={
          current && (
            <div className="flex flex-col gap-2">
              {canResend && (
                <Button leftIcon={<Send />} fullWidth onClick={() => askResend({ number: current.reservation, recipient: current.recipient })}>
                  Reenviar el correo
                </Button>
              )}
              <Button variant="outline" leftIcon={<ShoppingBag />} fullWidth to={reservationOf(current.reservation)}>
                Ver la reserva
              </Button>
            </div>
          )
        }
      >
        {current && (
          <DetailList
            items={[
              { label: 'Reserva', value: current.reservation },
              { label: 'Tipo de reserva', value: reservationKindText(current.reservationKind) },
              { label: 'Sucursal', value: current.branchCode },
              { label: 'Correo', value: current.kindText },
              { label: 'Destinatario', value: current.recipient, wide: true },
              { label: 'Estado', value: mailStatusLabel(current) },
              { label: 'Intentos', value: `${formatNumber(current.attempts)} de ${formatNumber(current.maxAttempts)}` },
              { label: 'Solicitado', value: formatDateTime(current.requestedAt) },
              { label: 'Último intento', value: current.lastAttemptAt ? formatDateTime(current.lastAttemptAt) : null },
              { label: 'Próximo intento', value: current.nextAttemptAt ? formatDateTime(current.nextAttemptAt) : null },
              { label: 'Terminado', value: current.completedAt ? formatDateTime(current.completedAt) : null },
              { label: 'Último error', value: current.lastError, wide: true },
            ]}
          />
        )}
      </SidePanel>

      <ResendMailDialog target={resending} onClose={() => setResending(null)} onDone={mails.reload} />
    </div>
  );
}
