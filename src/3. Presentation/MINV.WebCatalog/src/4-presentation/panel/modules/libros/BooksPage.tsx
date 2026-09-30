// Facturación › Libros fiscales (FiscalBooksView del escritorio): el mes elegido (`?mes=2026-09`, el actual por defecto) en
// cuatro pestañas (`?pestana=`): «Libro de ventas» (`GetSalesBookQuery`), «Libro de compras» (`GetPurchasesBookQuery`),
// «Resumen IVA / IT» (`GetTaxSummaryQuery`) y, para quien gestiona compras, «Recepciones sin factura»
// (`GetReceiptsWithoutInvoiceQuery` + `RegisterSupplierInvoiceCommand`). Filtros en la dirección: mes, sucursal, búsqueda y,
// en el libro de ventas, estado y tipo; totales al pie; descarga del libro (`ExportFiscalBookQuery`). Los indicadores del
// mes (lo que el escritorio mostraba en tarjetas) van PLEGADOS en «Ver resumen del mes» (P-10).

import { BarChart3, BookOpen, Calculator, PackageCheck, ShoppingBag, TrendingDown, TrendingUp } from 'lucide-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcQuery, useTableState } from '@/4-presentation/panel/hooks';
import { Collapsible, ErrorState, FilterBar, Page, SearchField, SelectField, StatCard, TabPanel, Tabs, statusOptions } from '@/4-presentation/panel/kit';
import { formatMoney, laPazToday } from '@/4-presentation/panel/lib';
import { KIND_OPTIONS, SALES_STATUSES, bookFilters, monthLabel, monthOf, parseMonth, recentMonths, type MonthValue } from './books';
import { PendingReceiptsTab } from './PendingReceiptsTab';
import { PurchasesBookTab } from './PurchasesBookTab';
import { SalesBookTab } from './SalesBookTab';
import { TaxSummaryTab } from './TaxSummaryTab';

const TABS = ['ventas', 'compras', 'resumen', 'recepciones'] as const;
type TabId = (typeof TABS)[number];

function tabOf(value: string | null, canPending: boolean): TabId {
  const tab = TABS.includes(value as TabId) ? (value as TabId) : 'ventas';
  return tab === 'recepciones' && !canPending ? 'ventas' : tab;
}

/** Indicadores del mes (se consultan recién al abrir «Ver resumen del mes»). */
function MonthKpis({ month }: { month: MonthValue }) {
  const summary = useRpcQuery('GetTaxSummaryQuery', { year: month.year, month: month.month });
  if (summary.error) return <ErrorState error={summary.error} operation="GetTaxSummaryQuery" onRetry={summary.reload} retrying={summary.fetching} />;
  const data = summary.data;
  const loading = !data;
  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-mes">
      <StatCard
        label="Ventas del mes (libro)"
        value={formatMoney(data?.grossSales ?? 0)}
        hint={data ? `${data.invoices} facturas · ${data.voidedInvoices} anuladas · ${data.notes} notas` : undefined}
        tone="success"
        icon={<ShoppingBag />}
        loading={loading}
      />
      <StatCard label="Débito fiscal IVA" value={formatMoney(data?.taxDebit ?? 0)} hint="13 % de la base de las facturas válidas" tone="warning" icon={<TrendingUp />} loading={loading} />
      <StatCard
        label="Crédito fiscal IVA"
        value={formatMoney((data?.taxCreditPurchases ?? 0) + (data?.taxCreditNotes ?? 0))}
        hint={data ? `Compras ${formatMoney(data.taxCreditPurchases)} · notas ${formatMoney(data.taxCreditNotes)}` : undefined}
        icon={<TrendingDown />}
        loading={loading}
      />
      <StatCard
        label="IVA a pagar"
        value={formatMoney(Math.max(0, data?.vatPayable ?? 0))}
        hint={data ? (data.vatCarryForward > 0 ? `Saldo a favor ${formatMoney(data.vatCarryForward)}` : `IT ${formatMoney(data.transactionTax)}`) : undefined}
        icon={<Calculator />}
        loading={loading}
      />
    </div>
  );
}

export function BooksPage() {
  const { canRun, session } = usePermissions();
  const [today] = useState(() => laPazToday());
  const table = useTableState({ filters: bookFilters(today) });
  const filters = table.filters;
  const [params, setParams] = useSearchParams();
  const canPending = canRun('GetReceiptsWithoutInvoiceQuery');
  const tab = tabOf(params.get('pestana'), canPending);
  const month = parseMonth(filters.mes) ?? parseMonth(monthOf(today))!;
  const months = recentMonths(today);
  const monthOptions = months.some((option) => option.value === filters.mes) ? months : [{ value: filters.mes, label: monthLabel(month) }, ...months];
  const branchOptions = (session?.access.branches ?? []).map((branch) => ({ value: branch.code, label: `${branch.code} · ${branch.name}` }));

  const setTab = (next: TabId) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === 'ventas') updated.delete('pestana');
        else updated.set('pestana', next);
        return updated;
      },
      { replace: true },
    );

  return (
    <Page title="Libros fiscales" description={`${monthLabel(month)} · libro de ventas IVA, libro de compras y resumen IVA / IT para el contador.`}>
      <FilterBar activeCount={table.activeFilterCount} onClear={table.clearFilters}>
        <SelectField label="Mes" allLabel={false} value={filters.mes} onChange={(value) => table.setFilter('mes', value || monthOf(today))} options={monthOptions} />
        <SelectField label="Sucursal" allLabel="Todas las sucursales" value={filters.sucursal} onChange={(value) => table.setFilter('sucursal', value)} options={branchOptions} />
        {(tab === 'ventas' || tab === 'compras' || tab === 'recepciones') && (
          <SearchField
            label="Buscar"
            placeholder={tab === 'ventas' ? 'Número, CUF, NIT/CI o nombre' : tab === 'compras' ? 'N° de factura, autorización, NIT o proveedor' : 'Recepción o proveedor'}
            value={filters.q}
            onChange={(q) => table.setFilter('q', q)}
          />
        )}
        {tab === 'ventas' && (
          <>
            <SelectField label="Estado" allLabel="Todos los estados" value={filters.estado} onChange={(value) => table.setFilter('estado', value)} options={statusOptions(SALES_STATUSES)} />
            <SelectField label="Tipo" allLabel="Facturas y notas" value={filters.tipo} onChange={(value) => table.setFilter('tipo', value)} options={KIND_OPTIONS} />
          </>
        )}
      </FilterBar>

      <Collapsible label="Ver resumen del mes" openLabel="Ocultar resumen del mes" icon={<BarChart3 />} description="Ventas del libro, débito y crédito fiscal e IVA a pagar del mes elegido.">
        <MonthKpis month={month} />
      </Collapsible>

      <Tabs
        label="Libros del mes"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: 'ventas', label: 'Libro de ventas', icon: <BookOpen /> },
          { id: 'compras', label: 'Libro de compras', icon: <ShoppingBag /> },
          { id: 'resumen', label: 'Resumen IVA / IT', icon: <Calculator /> },
          ...(canPending ? [{ id: 'recepciones' as const, label: 'Recepciones sin factura', icon: <PackageCheck /> }] : []),
        ]}
      >
        <TabPanel id="ventas">
          <SalesBookTab month={month} filters={filters} />
        </TabPanel>
        <TabPanel id="compras">
          <PurchasesBookTab month={month} filters={filters} />
        </TabPanel>
        <TabPanel id="resumen">
          <TaxSummaryTab month={month} />
        </TabPanel>
        {canPending && (
          <TabPanel id="recepciones">
            <PendingReceiptsTab filters={filters} />
          </TabPanel>
        )}
      </Tabs>
    </Page>
  );
}
