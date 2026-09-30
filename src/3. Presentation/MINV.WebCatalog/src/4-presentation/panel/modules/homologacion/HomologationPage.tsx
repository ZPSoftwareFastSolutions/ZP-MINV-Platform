// Facturación › Homologación (HomologationView del escritorio): cada producto, unidad y medio de pago de M-INV con su
// código del SIN (`GetHomologationQuery`). Sin homologar no se factura. Tres pestañas (`?pestana=`): «Productos»
// (asignar con búsqueda en el catálogo del SIN o «Sugerir homologación» y aceptar en lote), «Unidades» y «Medios de pago».
// El resumen (lo que el escritorio mostraba en tarjetas) va PLEGADO en «Ver resumen de la homologación» (P-10). Guardar
// exige «configurar la facturación»; quien solo ve la facturación ve todo sin los botones.

import { BarChart3, CircleCheckBig, CreditCard, Ruler, Tags, TriangleAlert } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Collapsible, Page, StatCard, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { MethodsTab, UnitsTab } from './CodesTabs';
import { homologationSummary } from './homologation';
import { ProductsTab } from './ProductsTab';

const TABS = ['productos', 'unidades', 'medios'] as const;
type TabId = (typeof TABS)[number];

function tabOf(value: string | null): TabId {
  return TABS.includes(value as TabId) ? (value as TabId) : 'productos';
}

export function HomologationPage() {
  const { can } = usePermissions();
  const [params, setParams] = useSearchParams();
  const tab = tabOf(params.get('pestana'));
  const homologation = useRpcQuery('GetHomologationQuery', {});
  const view = homologation.data;
  const summary = view ? homologationSummary(view) : null;
  const shared = { view, loading: homologation.loading, fetching: homologation.fetching, error: homologation.error, reload: homologation.reload };

  const setTab = (next: TabId) =>
    setParams(
      (previous) => {
        const updated = new URLSearchParams(previous);
        if (next === 'productos') updated.delete('pestana');
        else updated.set('pestana', next);
        return updated;
      },
      { replace: true },
    );

  return (
    <Page
      title="Homologación"
      description="Cada producto, unidad y medio de pago con su código del SIN. Lo que no está homologado no se puede facturar."
      actions={
        can('billing.configure') && (
          <Button variant="outline" to={ROUTES.panelModule('configuracion?pestana=facturacion')}>
            Configurar la facturación
          </Button>
        )
      }
    >
      {view && view.activities.filter((activity) => activity.isCurrent).length === 0 && (
        <Alert tone="warning" title="La empresa todavía no tiene actividades económicas sincronizadas">
          Sin el catálogo del SIN no se pueden homologar productos. Sincronice los catálogos (Administración › Configuración › Facturación).
        </Alert>
      )}
      {summary && summary.pending > 0 && (
        <Alert tone="warning" title={`${formatNumber(summary.pending)} productos activos sin homologar`}>
          No se facturan hasta homologarlos: la caja rechaza la venta con un mensaje claro.
        </Alert>
      )}

      {summary && (
        <Collapsible label="Ver resumen de la homologación" openLabel="Ocultar resumen de la homologación" icon={<BarChart3 />} description="Productos, unidades y medios de pago homologados.">
          <div className="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3" data-testid="resumen-homologacion">
            <StatCard
              label="Productos sin homologar"
              value={formatNumber(summary.pending)}
              hint={summary.pending === 0 ? 'Todos los productos activos se pueden facturar' : 'No se facturan hasta homologarlos'}
              tone={summary.pending > 0 ? 'warning' : 'success'}
              icon={<TriangleAlert />}
            />
            <StatCard label="Productos homologados" value={formatNumber(summary.homologatedProducts)} hint={`de ${formatNumber(summary.activeProducts)} productos activos`} tone="success" icon={<CircleCheckBig />} />
            <StatCard label="Unidades" value={`${summary.unitsDone} de ${summary.units}`} hint={summary.unitsDone < summary.units ? `${summary.units - summary.unitsDone} sin unidad del SIN` : 'Todas homologadas'} icon={<Ruler />} />
            <StatCard
              label="Medios de pago"
              value={`${summary.methodsDone} de ${summary.methods}`}
              hint={summary.methodsDone < summary.methods ? 'Hay medios de pago sin código del SIN' : 'Todos homologados'}
              icon={<CreditCard />}
            />
          </div>
        </Collapsible>
      )}

      <Tabs
        label="Qué homologar"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: 'productos', label: 'Productos', icon: <Tags />, count: summary?.pending },
          { id: 'unidades', label: 'Unidades', icon: <Ruler />, count: summary ? summary.units - summary.unitsDone : undefined },
          { id: 'medios', label: 'Medios de pago', icon: <CreditCard />, count: summary ? summary.methods - summary.methodsDone : undefined },
        ]}
      >
        <TabPanel id="productos">
          <ProductsTab {...shared} />
        </TabPanel>
        <TabPanel id="unidades">
          <UnitsTab {...shared} />
        </TabPanel>
        <TabPanel id="medios">
          <MethodsTab {...shared} />
        </TabPanel>
      </Tabs>
    </Page>
  );
}
