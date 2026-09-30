// Análisis › Reportes (`/panel/reportes`; en el escritorio: ReportsView + ReportsViewModel). Un selector de reporte en
// pestañas (Ventas, Compras, Movimientos, Inventario, Sucursales y Tecnología) guardado en la dirección
// (`?reporte=compras`); cada pestaña tiene sus filtros (período con atajos, agrupación, búsqueda…), sus totales, su tabla
// con pie de totales, detalle lateral, exportar CSV e imprimir, y sus gráficos PLEGADOS detrás de «Ver gráfico» (P-10).
// Solo se monta la pestaña elegida: las demás no consultan nada hasta abrirse.
//
// El período se conserva al cambiar de reporte; el resto de los filtros, el orden y la página son de cada reporte. Los
// reportes cubren la sucursal activa (o todas, en la vista consolidada de la gerencia): se cambia arriba, en la barra.

import { ArrowLeftRight, Boxes, Building2, Cpu, Receipt, Truck } from 'lucide-react';
import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions } from '@/4-presentation/panel/hooks';
import { Page, StatusBadge, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { BranchReportView } from './BranchReportView';
import { InventoryReportView } from './InventoryReportView';
import { MovementsReportView } from './MovementsReportView';
import { PurchasesReportView } from './PurchasesReportView';
import { DEFAULT_REPORT, REPORT_PARAM, SHARED_PARAMS, reportOf, reportTabs, scopeText, type ReportId } from './reports';
import { SalesReportView } from './SalesReportView';
import { TechReportView } from './TechReportView';

const ICONS: Record<ReportId, ReactNode> = {
  ventas: <Receipt />,
  compras: <Truck />,
  movimientos: <ArrowLeftRight />,
  inventario: <Boxes />,
  sucursales: <Building2 />,
  tecnologia: <Cpu />,
};

export function ReportsPage() {
  const { canRun, session } = usePermissions();
  const [params, setParams] = useSearchParams();
  const access = session?.access;
  const tabs = reportTabs({ inventory: canRun('GetStockProjectionQuery'), branches: Boolean(access && (access.allBranches || access.branches.length > 1)) });
  const current = reportOf(params.get(REPORT_PARAM), tabs);

  // Cambiar de reporte conserva el período y olvida los filtros, el orden y la página del anterior.
  const choose = (id: ReportId) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams();
        if (id !== DEFAULT_REPORT) next.set(REPORT_PARAM, id);
        for (const key of SHARED_PARAMS) {
          const value = previous.get(key);
          if (key !== REPORT_PARAM && value !== null) next.set(key, value);
        }
        return next;
      },
      { replace: true },
    );

  return (
    <Page
      title="Reportes"
      description="Ventas, compras, movimientos, inventario, sucursales y tecnología: elija el reporte y el período, revise los totales y expórtelo o imprímalo."
      badge={<StatusBadge tone="info">{scopeText(access)}</StatusBadge>}
    >
      <Tabs label="Reportes" tabs={tabs.map((tab) => ({ ...tab, icon: ICONS[tab.id] }))} value={current} onChange={choose}>
        <TabPanel id="ventas">
          <SalesReportView />
        </TabPanel>
        <TabPanel id="compras">
          <PurchasesReportView />
        </TabPanel>
        <TabPanel id="movimientos">
          <MovementsReportView />
        </TabPanel>
        <TabPanel id="inventario">
          <InventoryReportView />
        </TabPanel>
        <TabPanel id="sucursales">
          <BranchReportView />
        </TabPanel>
        <TabPanel id="tecnologia">
          <TechReportView />
        </TabPanel>
      </Tabs>
    </Page>
  );
}
