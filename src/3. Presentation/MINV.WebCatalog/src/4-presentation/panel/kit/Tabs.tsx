// USO · Pestañas del panel (secciones de un detalle o de una pantalla). Accesibles: role="tablist", flechas ← →, Inicio
// y Fin. Solo se monta el panel de la pestaña elegida: las demás no cargan datos hasta que se abren.
//
//   <Tabs label="Secciones del cliente" value={pestana} onChange={setPestana}
//     tabs={[{ id: 'datos', label: 'Datos' }, { id: 'ventas', label: 'Ventas', count: 12 }, { id: 'reservas', label: 'Reservas' }]}>
//     <TabPanel id="datos">…</TabPanel>
//     <TabPanel id="ventas"><VentasDelCliente /></TabPanel>          ← consulta recién al abrir la pestaña
//     <TabPanel id="reservas" keepMounted>…</TabPanel>                ← sigue montado después de abrirlo una vez
//   </Tabs>
//
// Para guardar la pestaña en la dirección, use `useSearchParams` o un filtro de useTableState como `value`.

import clsx from 'clsx';
import { useContext, useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { TabsContext, tabIds } from './tabsContext';

export interface TabItem<T extends string = string> {
  id: T;
  label: string;
  icon?: ReactNode;
  /** Número a la derecha («Ventas 12»). */
  count?: number;
  disabled?: boolean;
}

export interface TabsProps<T extends string = string> {
  /** Nombre del grupo para lectores de pantalla. */
  label: string;
  tabs: readonly TabItem<T>[];
  value: T;
  onChange: (id: T) => void;
  /** Los TabPanel. */
  children?: ReactNode;
  className?: string;
}

export function Tabs<T extends string = string>({ label, tabs, value, onChange, children, className }: TabsProps<T>) {
  const prefix = useId();

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const enabled = tabs.filter((tab) => !tab.disabled);
    const index = enabled.findIndex((tab) => tab.id === value);
    let next: number;
    if (event.key === 'ArrowRight') next = (index + 1) % enabled.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + enabled.length) % enabled.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = enabled.length - 1;
    else return;
    event.preventDefault();
    const target = enabled[next];
    if (!target) return;
    onChange(target.id);
    document.getElementById(tabIds(prefix, target.id).tab)?.focus();
  };

  return (
    <TabsContext.Provider value={{ prefix, value }}>
      <div className={clsx('min-w-0', className)}>
        <div role="tablist" aria-label={label} onKeyDown={onKeyDown} className="flex gap-1 overflow-x-auto border-b border-border scrollbar-none">
          {tabs.map((tab) => {
            const selected = tab.id === value;
            const ids = tabIds(prefix, tab.id);
            return (
              <button
                key={tab.id}
                id={ids.tab}
                type="button"
                role="tab"
                aria-selected={selected}
                aria-controls={selected ? ids.panel : undefined}
                tabIndex={selected ? 0 : -1}
                disabled={tab.disabled}
                onClick={() => onChange(tab.id)}
                className={clsx(
                  '-mb-px inline-flex h-11 shrink-0 cursor-pointer items-center gap-2 border-b-2 px-4 text-sm font-semibold whitespace-nowrap transition-colors duration-200 [&_svg]:size-4',
                  'disabled:cursor-not-allowed disabled:opacity-50',
                  selected ? 'border-accent text-text' : 'border-transparent text-text-muted hover:text-text',
                )}
              >
                {tab.icon && (
                  <span aria-hidden="true" className="inline-flex">
                    {tab.icon}
                  </span>
                )}
                {tab.label}
                {tab.count != null && <span className="rounded-full bg-surface-3 px-1.5 text-xs tabular-nums text-text-muted">{tab.count}</span>}
              </button>
            );
          })}
        </div>
        {children}
      </div>
    </TabsContext.Provider>
  );
}

export interface TabPanelProps {
  id: string;
  children: ReactNode;
  /** Después de abrirse una vez, queda montado (oculto) al cambiar de pestaña. */
  keepMounted?: boolean;
  className?: string;
}

export function TabPanel({ id, children, keepMounted = false, className }: TabPanelProps) {
  const context = useContext(TabsContext);
  if (!context) throw new Error('<TabPanel> debe ir dentro de <Tabs>.');
  return <TabPanelBody id={id} active={context.value === id} prefix={context.prefix} keepMounted={keepMounted} className={className}>{children}</TabPanelBody>;
}

function TabPanelBody({ id, active, prefix, keepMounted, className, children }: { id: string; active: boolean; prefix: string; keepMounted: boolean; className?: string; children: ReactNode }) {
  const [visited, setVisited] = useState(active);
  if (active && !visited) setVisited(true);
  if (!active && !(keepMounted && visited)) return null;
  const ids = tabIds(prefix, id);
  return (
    <div role="tabpanel" id={ids.panel} aria-labelledby={ids.tab} tabIndex={0} hidden={!active} className={clsx('pt-4', className)}>
      {children}
    </div>
  );
}
