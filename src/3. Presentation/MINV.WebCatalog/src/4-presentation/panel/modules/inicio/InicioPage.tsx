// General › Inicio: el tablero del panel, simplificado como pidió el cliente («Si hay funciones, que sean botones. Nada
// de estadísticas de golpe»).
//   1. Saludo con el nombre, el rol y la sucursal activa.
//   2. «¿Qué quiere hacer?»: los botones grandes que ofrecen los módulos a los que la sesión tiene acceso, agrupados por
//      sección (en el orden del menú).
//   3. «Ver estadísticas» PLEGADO: al abrirlo aparecen las estadísticas que ofrecen los módulos; cada una se descarga y
//      consulta por separado, con su propio estado de carga y de error. Al entrar NO se ve ni se carga ningún indicador.
// Lee lo que ofrecen los demás módulos del registro (nunca los importa: regla P-09).

import { BarChart3, Compass } from 'lucide-react';
import { Suspense, useId, useMemo } from 'react';
import { roleName } from '@/4-presentation/app/contract';
import { usePermissions } from '@/4-presentation/panel/hooks';
import { ActionButton, Collapsible, EmptyState, Page, Skeleton } from '@/4-presentation/panel/kit';
import { dashboardActions, dashboardStats, sectionOf, usePanelRegistry, type DashboardActionGroup, type DashboardStat } from '@/4-presentation/panel/registry';
import { StatBoundary } from './StatBoundary';

export function InicioPage() {
  const registry = usePanelRegistry();
  const { session, permissions } = usePermissions();
  const groups = useMemo(() => dashboardActions(registry, permissions), [registry, permissions]);
  const stats = useMemo(() => dashboardStats(registry, permissions), [registry, permissions]);
  const actionsTitleId = useId();
  if (!session) return null;

  const roles = session.roles.map(roleName).join(', ');
  const active = session.access.branches.find((branch) => branch.id === session.access.activeBranchId);
  const branch = active ? `${active.code} · ${active.name}` : session.access.allBranches ? 'Todas las sucursales (vista consolidada)' : 'Sin sucursal activa';

  return (
    <Page
      title={`Hola, ${session.displayName}`}
      description={
        <p className="flex flex-wrap gap-x-4 gap-y-1" data-testid="saludo">
          <span>
            Rol: <span className="font-medium text-text">{roles}</span>
          </span>
          <span>
            Sucursal activa: <span className="font-medium text-text">{branch}</span>
          </span>
          <span className="max-sm:hidden">{session.company}</span>
        </p>
      }
    >
      <section aria-labelledby={actionsTitleId} className="space-y-4">
        <h2 id={actionsTitleId} className="text-xl">
          ¿Qué quiere hacer?
        </h2>
        {groups.length === 0 ? (
          <EmptyState
            icon={<Compass />}
            title="Todavía no hay funciones para su rol"
            description="Cuando se habiliten pantallas para su rol aparecerán aquí como botones. Mientras tanto, use el menú."
          />
        ) : (
          groups.map((group) => <ActionGroup key={group.section.key} group={group} />)
        )}
      </section>

      {stats.length > 0 && (
        <Collapsible
          label="Ver estadísticas"
          openLabel="Ocultar estadísticas"
          icon={<BarChart3 />}
          description="Indicadores de los módulos a los que tiene acceso. Se cargan recién al abrir."
        >
          <div className="grid gap-4 lg:grid-cols-2">
            {stats.map((item) => (
              <StatPanel key={`${item.module.key}/${item.stat.key}`} item={item} />
            ))}
          </div>
        </Collapsible>
      )}
    </Page>
  );
}

function ActionGroup({ group }: { group: DashboardActionGroup }) {
  const titleId = useId();
  return (
    <section aria-labelledby={titleId} className="space-y-2" data-testid="grupo-de-acciones">
      <h3 id={titleId} className="text-sm font-semibold uppercase tracking-wider text-text-faint">
        {group.section.title}
      </h3>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {group.actions.map(({ module, action, to }) => {
          const Icon = action.icon;
          return <ActionButton key={`${module.key}/${action.key}`} icon={<Icon />} title={action.label} description={action.description} to={to} />;
        })}
      </div>
    </section>
  );
}

/** Una estadística: su título y su contenido, que se descarga y consulta por separado (con su propio límite de errores). */
function StatPanel({ item }: { item: DashboardStat }) {
  const titleId = useId();
  const Content = item.stat.component;
  const section = sectionOf(item.module.section)?.title;
  return (
    <section aria-labelledby={titleId} className="min-w-0 rounded-card border border-border bg-surface-2/40 p-4" data-testid="estadistica">
      <h3 id={titleId} className="text-base">
        {item.stat.title}
      </h3>
      <p className="text-xs text-text-faint">
        {section} › {item.module.title}
      </p>
      <div className="mt-3">
        <StatBoundary>
          <Suspense fallback={<StatLoading title={item.stat.title} />}>
            <Content />
          </Suspense>
        </StatBoundary>
      </div>
    </section>
  );
}

function StatLoading({ title }: { title: string }) {
  return (
    <div role="status" aria-live="polite">
      <span className="sr-only">Cargando {title.toLowerCase()}…</span>
      <div aria-hidden="true" className="grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-3">
        <Skeleton className="h-24 w-full rounded-card" />
        <Skeleton className="h-24 w-full rounded-card" />
      </div>
    </div>
  );
}
