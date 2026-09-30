// Módulo «Estado del SIAT» · la bitácora técnica del SIN (`GetSiatServiceCallsQuery`, solo para quien configura la
// facturación): cada llamada al SIN de los últimos días con su resultado y duración; en la fila desplegable, lo que se
// envió y lo que respondió (sin el token: el servidor nunca lo guarda). Vive PLEGADA: se consulta recién al abrirla.

import { Download, RefreshCw, ScrollText } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, DataTable, SelectField, StatusBadge, Toolbar, useNotify, type DataTableColumn } from '@/4-presentation/panel/kit';
import { addDays, exportCsv, formatDateTime, formatNumber, laPazToday, type CsvColumn } from '@/4-presentation/panel/lib';
import type { ServiceCallData } from './siat';

/** Días hacia atrás y cuántas llamadas se piden (como el escritorio). */
const DAYS_BACK = 2;
const MAX_CALLS = 300;

interface CallItem {
  key: string;
  row: ServiceCallData;
}

const COLUMNS: DataTableColumn<CallItem>[] = [
  { id: 'fecha', header: 'Fecha y hora', value: (item) => new Date(item.row.occurredAt), cell: (item) => formatDateTime(item.row.occurredAt), className: 'whitespace-nowrap' },
  {
    id: 'operacion',
    header: 'Operación',
    value: (item) => item.row.operation,
    card: 'title',
    cell: (item) => (
      <span className="block">
        {item.row.operation}
        <span className="block text-xs font-normal text-text-muted">
          {item.row.resource}
          {item.row.pointOfSaleCode != null ? ` · punto ${item.row.pointOfSaleCode}` : ''}
        </span>
      </span>
    ),
  },
  {
    id: 'resultado',
    header: 'Resultado',
    value: (item) => (item.row.succeeded ? 'Correcto' : 'Con error'),
    cell: (item) => (
      <span className="flex flex-col items-start gap-1">
        <StatusBadge tone={item.row.succeeded ? 'success' : 'danger'}>{item.row.succeeded ? `Correcto${item.row.siatCode != null ? ` (${item.row.siatCode})` : ''}` : 'Con error'}</StatusBadge>
        {!item.row.succeeded && <span className="text-xs text-danger-text">{item.row.error ?? (item.row.httpStatus != null ? `HTTP ${item.row.httpStatus}` : 'Sin respuesta')}</span>}
      </span>
    ),
  },
  { id: 'duracion', header: 'Duración', align: 'end', value: (item) => item.row.durationMs, cell: (item) => `${formatNumber(item.row.durationMs)} ms` },
];

const CSV: readonly CsvColumn<CallItem>[] = [
  { header: 'Fecha y hora', value: (item) => formatDateTime(item.row.occurredAt) },
  { header: 'Servicio', value: (item) => item.row.resource },
  { header: 'Operación', value: (item) => item.row.operation },
  { header: 'Punto de venta', value: (item) => item.row.pointOfSaleCode },
  { header: 'Resultado', value: (item) => (item.row.succeeded ? 'Correcto' : 'Con error') },
  { header: 'Código del SIN', value: (item) => item.row.siatCode },
  { header: 'Error', value: (item) => item.row.error },
  { header: 'Duración (ms)', value: (item) => item.row.durationMs },
];

export function ServiceCalls() {
  const notify = useNotify();
  const [today] = useState(() => laPazToday());
  const [operation, setOperation] = useState('');
  const [onlyErrors, setOnlyErrors] = useState(false);
  const calls = useRpcQuery('GetSiatServiceCallsQuery', { from: addDays(today, -DAYS_BACK), to: today, operation: operation || null, max: MAX_CALLS });
  const items = useMemo(() => (calls.data ?? []).map((row, index) => ({ key: `${row.occurredAt}-${index}`, row })), [calls.data]);
  const rows = useMemo(() => items.filter((item) => !onlyErrors || !item.row.succeeded), [items, onlyErrors]);
  const [operations, setOperations] = useState<string[]>([]);
  const seen = [...new Set(items.map((item) => item.row.operation))];
  if (seen.some((name) => !operations.includes(name))) setOperations([...new Set([...operations, ...seen])].sort());

  const exportRows = () => {
    const file = exportCsv('bitacora-sin', CSV, rows);
    notify.success('Exportación lista', `Se descargó ${file} (${formatNumber(rows.length)} filas).`);
  };

  return (
    <div className="space-y-3" data-testid="bitacora-tecnica">
      <p className="text-sm text-text-muted">Llamadas al SIN de los últimos {DAYS_BACK + 1} días (hasta {MAX_CALLS}). Sirve de evidencia para la inspección y para soporte.</p>
      <div className="grid grid-cols-1 items-end gap-3 sm:grid-cols-2">
        <SelectField label="Operación del SIN" allLabel="Todas las operaciones" value={operation} onChange={setOperation} options={operations.map((name) => ({ value: name, label: name }))} />
        <Checkbox label="Solo las llamadas con error" checked={onlyErrors} onChange={setOnlyErrors} />
      </div>
      <Toolbar
        label="Acciones de la bitácora"
        end={
          <>
            <Button variant="outline" leftIcon={<RefreshCw />} loading={calls.fetching && !calls.loading} onClick={calls.reload}>
              Actualizar
            </Button>
            <Button variant="outline" leftIcon={<Download />} disabled={rows.length === 0} onClick={exportRows}>
              Exportar CSV
            </Button>
          </>
        }
      />
      <DataTable
        caption="Llamadas al SIN"
        columns={COLUMNS}
        rows={calls.data ? rows : undefined}
        rowKey={(item) => item.key}
        loading={calls.loading}
        refreshing={calls.fetching && !calls.loading}
        error={calls.error}
        onRetry={calls.reload}
        operation="GetSiatServiceCallsQuery"
        defaultSort={{ column: 'fecha', direction: 'desc' }}
        renderExpanded={(item) => (
          <div className="grid gap-3 lg:grid-cols-2">
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">Enviado</p>
              <pre className="mt-1 max-h-60 overflow-auto rounded-lg bg-surface-2 p-2 font-mono text-xs whitespace-pre-wrap break-all">{item.row.requestBody ?? '—'}</pre>
            </div>
            <div className="min-w-0">
              <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">Respuesta</p>
              <pre className="mt-1 max-h-60 overflow-auto rounded-lg bg-surface-2 p-2 font-mono text-xs whitespace-pre-wrap break-all">{item.row.responseBody ?? '—'}</pre>
            </div>
          </div>
        )}
        empty={{ title: 'Sin llamadas al SIN en estos días', description: 'Pruebe con otra operación o sin «Solo las llamadas con error».', icon: <ScrollText /> }}
      />
    </div>
  );
}
