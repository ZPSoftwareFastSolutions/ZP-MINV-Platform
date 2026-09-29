// Módulo «Integraciones» · pestaña «API Keys» (`GetApiKeysQuery`): las llaves del API Gateway con su prefijo (el token no
// se vuelve a ver nunca), alcances, sucursal, último uso, vencimiento y estado. Filtros por estado, alcance y sucursal
// con listas desplegables y búsqueda (en la dirección), detalle lateral, exportar CSV y «Revocar» con confirmación
// (`RevokeApiKeyCommand`: la llave deja de funcionar de inmediato; no se borra, queda en la auditoría).

import { Ban, Download, Eye, Filter, KeyRound, Plus, RefreshCw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import {
  Button,
  ConfirmDialog,
  DataTable,
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
import {
  KEY_FILTERS,
  KEY_STATUSES,
  branchOptions,
  filterKeys,
  keyCsvColumns,
  keyPrefixText,
  keyStatusOf,
  nameOf,
  optionsOf,
  plainMessage,
  type ApiKeyRecord,
  type NamedOption,
} from './integrations';

export interface ApiKeysTabProps {
  scopes: readonly NamedOption[];
  canCreate: boolean;
  onCreate: () => void;
}

export function ApiKeysTab({ scopes, canCreate, onCreate }: ApiKeysTabProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const canRevoke = canRun('RevokeApiKeyCommand');
  const keys = useRpcQuery('GetApiKeysQuery', {});
  const table = useTableState({ filters: KEY_FILTERS, sort: { column: 'creada', direction: 'desc' } });
  const filters = table.filters;
  const all = useMemo(() => keys.data ?? [], [keys.data]);
  const rows = useMemo(() => filterKeys(all, filters, scopes), [all, filters, scopes]);
  const [detail, setDetail] = useState<{ key: ApiKeyRecord; open: boolean } | null>(null);
  const [revoking, setRevoking] = useState<{ key: ApiKeyRecord; open: boolean } | null>(null);
  const revoke = useRpcCommand('RevokeApiKeyCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Llave revocada' });

  const columns = useMemo<DataTableColumn<ApiKeyRecord>[]>(
    () => [
      {
        id: 'nombre',
        header: 'Llave',
        value: (key) => key.name,
        card: 'title',
        cell: (key) => (
          <span className="block min-w-40">
            <span className="block">{key.name}</span>
            <span className="block font-mono text-xs font-normal text-text-muted">{keyPrefixText(key.prefix)}</span>
          </span>
        ),
      },
      {
        id: 'alcances',
        header: 'Alcances',
        value: (key) => key.scopes.map((scope) => nameOf(scopes, scope)).join(' · '),
        sortable: false,
        cell: (key) => <span className="line-clamp-2 block max-w-sm min-w-48 text-text-muted">{key.scopes.map((scope) => nameOf(scopes, scope)).join(' · ')}</span>,
      },
      { id: 'sucursal', header: 'Sucursal', value: (key) => key.branch ?? 'Todas sus sucursales' },
      { id: 'uso', header: 'Último uso', value: (key) => (key.lastUsedAt ? new Date(key.lastUsedAt) : null), cell: (key) => (key.lastUsedAt ? formatDateTime(key.lastUsedAt) : 'Nunca'), className: 'whitespace-nowrap' },
      { id: 'creada', header: 'Creada', value: (key) => new Date(key.createdAt), cell: (key) => formatDateTime(key.createdAt), className: 'whitespace-nowrap' },
      {
        id: 'estado',
        header: 'Estado',
        value: (key) => KEY_STATUSES[keyStatusOf(key)].label,
        cell: (key) => <StatusBadge status={keyStatusOf(key)} statuses={KEY_STATUSES} />,
      },
    ],
    [scopes],
  );

  const current = detail ? (all.find((key) => key.id === detail.key.id) ?? detail.key) : null;
  const target = revoking?.key ?? null;
  const askRevoke = (key: ApiKeyRecord) => {
    revoke.reset();
    setDetail((value) => (value?.open ? { ...value, open: false } : value));
    setRevoking({ key, open: true });
  };

  const exportRows = () => {
    const file = exportCsv('api-keys', keyCsvColumns(scopes), rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-5">
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SearchField label="Buscar" placeholder="Nombre, prefijo o quién la creó" value={filters.q} onChange={(q) => table.setFilter('q', q)} />
        <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(KEY_STATUSES)} />
        <SelectField label="Alcance" allLabel="Todos los alcances" value={filters.alcance} onChange={(value) => table.setFilter('alcance', value)} options={optionsOf(scopes)} />
        <SelectField
          label="Sucursal"
          allLabel="Todas"
          value={filters.sucursal}
          onChange={(value) => table.setFilter('sucursal', value)}
          options={branchOptions(all.map((key) => key.branch), 'Todas sus sucursales (sin límite)')}
        />
      </FilterBar>

      <Toolbar
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={keys.fetching && !keys.loading} onClick={keys.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      >
        {keys.data && (
          <span className="text-sm text-text-muted" data-testid="llaves-resumen">
            {formatNumber(rows.length)} de {formatNumber(all.length)} llaves · {formatNumber(all.filter((key) => key.isUsable).length)} activas
          </span>
        )}
      </Toolbar>

      <DataTable
        caption="API Keys"
        columns={columns}
        rows={keys.data ? rows : undefined}
        rowKey={(key) => key.id}
        rowLabel={(key) => `la llave «${key.name}»`}
        loading={keys.loading}
        refreshing={keys.fetching && !keys.loading}
        error={keys.error}
        onRetry={keys.reload}
        operation="GetApiKeysQuery"
        {...table.tableProps}
        onRowOpen={(key) => setDetail({ key, open: true })}
        activeRowKey={detail?.open ? detail.key.id : null}
        rowActions={(key) => [
          { label: 'Ver detalle', icon: <Eye />, onSelect: () => setDetail({ key, open: true }) },
          { label: 'Revocar', icon: <Ban />, tone: 'danger', onSelect: () => askRevoke(key), hidden: !canRevoke || !key.isUsable },
        ]}
        empty={{
          title: all.length === 0 ? 'Todavía no hay API Keys' : 'No hay llaves con estos filtros',
          description: all.length === 0 ? 'Cree una para conectar su tienda en línea o su ERP.' : 'Pruebe con otro estado, alcance o sucursal.',
          icon: <KeyRound />,
          action:
            all.length === 0 ? (
              canCreate && (
                <Button leftIcon={<Plus />} onClick={onCreate}>
                  Nueva API Key
                </Button>
              )
            ) : (
              <Button variant="outline" leftIcon={<Filter />} onClick={table.clearFilters}>
                Limpiar filtros
              </Button>
            ),
        }}
      />

      <SidePanel
        open={detail?.open ?? false}
        onClose={() => setDetail((value) => (value?.open ? { ...value, open: false } : value))}
        title={current?.name ?? 'API Key'}
        description={current ? keyPrefixText(current.prefix) : undefined}
        headerExtra={current && <StatusBadge status={keyStatusOf(current)} statuses={KEY_STATUSES} />}
        footer={
          current &&
          canRevoke &&
          current.isUsable && (
            <Button variant="danger" leftIcon={<Ban />} fullWidth onClick={() => askRevoke(current)}>
              Revocar la llave
            </Button>
          )
        }
      >
        {current && (
          <DetailList
            items={[
              { label: 'Llave', value: <span className="font-mono">{keyPrefixText(current.prefix)}</span> },
              { label: 'Estado', value: KEY_STATUSES[keyStatusOf(current)].label },
              { label: 'Sucursal', value: current.branch ?? 'Todas las sucursales de quien la creó' },
              { label: 'Creada por', value: current.owner },
              { label: 'Creada', value: formatDateTime(current.createdAt) },
              { label: 'Vence', value: current.expiresAt ? formatDateTime(current.expiresAt) : 'Sin vencimiento' },
              { label: 'Revocada', value: current.revokedAt ? formatDateTime(current.revokedAt) : null },
              { label: 'Último uso', value: current.lastUsedAt ? formatDateTime(current.lastUsedAt) : 'Nunca' },
              {
                label: 'Alcances',
                wide: true,
                value: (
                  <ul className="list-disc space-y-1 pl-5">
                    {current.scopes.map((scope) => (
                      <li key={scope}>{nameOf(scopes, scope)}</li>
                    ))}
                  </ul>
                ),
              },
            ]}
          />
        )}
      </SidePanel>

      <ConfirmDialog
        open={revoking?.open ?? false}
        onClose={() => setRevoking((value) => (value ? { ...value, open: false } : value))}
        tone="danger"
        title={target ? `¿Revocar «${target.name}»?` : ''}
        message={
          target
            ? `La llave ${keyPrefixText(target.prefix)} deja de funcionar de inmediato en el API Gateway: el sistema que la usa ya no podrá conectarse. No se borra: queda en la auditoría.`
            : ''
        }
        confirmLabel="Revocar"
        onConfirm={async () => {
          if (!target) return false;
          const outcome = await revoke.run({ id: target.id });
          if (outcome.ok) keys.reload();
          return outcome;
        }}
        error={revoke.errorText}
      />
    </div>
  );
}
