// USO · Sección plegable «Ver …» con el símbolo ^ que gira (pedido del cliente: nada de estadísticas de golpe; regla
// P-10). Empieza CERRADA y su contenido NO se monta hasta abrirla: las consultas de adentro corren recién ahí. Al
// cerrarla se desmonta (al volver a abrir, los datos se cargan de nuevo) salvo `keepMounted`.
//
//   <Collapsible label="Ver estadísticas" openLabel="Ocultar estadísticas" icon={<BarChart3 />}
//     description="Ventas del mes, productos más vendidos y cajas.">
//     <EstadisticasDelMes />          ← este componente hace su useRpcQuery al montarse, es decir, al abrir
//   </Collapsible>
//
// Botón con aria-expanded y aria-controls; el título es un encabezado (h2 por defecto) para navegar por secciones.

import clsx from 'clsx';
import { ChevronUp } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';

export interface CollapsibleProps {
  /** Texto del botón: «Ver estadísticas». */
  label: string;
  /** Texto con la sección abierta (por defecto el mismo). */
  openLabel?: string;
  description?: ReactNode;
  icon?: ReactNode;
  /** Estado controlado desde afuera (opcional). */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Después de abrirse una vez queda montado (oculto) al cerrar. */
  keepMounted?: boolean;
  /** Nivel del encabezado (2 dentro de una Page). */
  level?: 2 | 3;
  children: ReactNode;
  className?: string;
}

export function Collapsible({ label, openLabel, description, icon, open: controlled, onOpenChange, keepMounted = false, level = 2, children, className }: CollapsibleProps) {
  const contentId = useId();
  const [own, setOwn] = useState(false);
  const open = controlled ?? own;
  const [visited, setVisited] = useState(open);
  if (open && !visited) setVisited(true);
  const Heading = level === 2 ? 'h2' : 'h3';
  const mounted = open || (keepMounted && visited);

  const toggle = () => {
    if (controlled === undefined) setOwn(!open);
    onOpenChange?.(!open);
  };

  return (
    <section className={clsx('min-w-0 rounded-card border border-border bg-surface shadow-card', className)} data-state={open ? 'abierto' : 'cerrado'}>
      <Heading className="text-base font-semibold">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={mounted ? contentId : undefined}
          onClick={toggle}
          className="flex min-h-14 w-full cursor-pointer items-center gap-3 rounded-card px-4 py-3 text-left transition-colors duration-200 hover:bg-surface-2"
        >
          {icon && (
            <span aria-hidden="true" className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-primary-soft text-primary-text [&_svg]:size-5">
              {icon}
            </span>
          )}
          <span className="min-w-0 flex-1">
            <span className="block font-display">{open ? (openLabel ?? label) : label}</span>
            {description && <span className="block text-sm font-normal text-text-muted">{description}</span>}
          </span>
          <ChevronUp aria-hidden="true" data-testid="plegable-simbolo" className={clsx('size-5 shrink-0 text-text-muted transition-transform duration-300', open ? 'rotate-0' : 'rotate-180')} />
        </button>
      </Heading>
      {mounted && (
        <div id={contentId} hidden={!open} className="border-t border-border p-4">
          {children}
        </div>
      )}
    </section>
  );
}
