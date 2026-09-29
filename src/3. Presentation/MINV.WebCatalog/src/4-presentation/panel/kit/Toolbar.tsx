// USO · Barra de acciones sobre una lista: a la izquierda el estado o las acciones de la selección; a la derecha las
// acciones de la lista (exportar, actualizar).
//
//   <Toolbar label="Acciones de la lista"
//     end={<><Button variant="outline" leftIcon={<RefreshCw />} onClick={ventas.reload}>Actualizar</Button>
//            <Button variant="outline" leftIcon={<Download />} onClick={exportar}>Exportar CSV</Button></>}>
//     {seleccion.size > 0 && <Button variant="danger" onClick={anularSeleccion}>Anular {seleccion.size}</Button>}
//   </Toolbar>

import clsx from 'clsx';
import type { ReactNode } from 'react';

export interface ToolbarProps {
  /** Nombre del grupo para lectores de pantalla. */
  label?: string;
  /** Lado izquierdo. */
  children?: ReactNode;
  /** Lado derecho. */
  end?: ReactNode;
  className?: string;
}

export function Toolbar({ label = 'Acciones de la lista', children, end, className }: ToolbarProps) {
  return (
    <div role="group" aria-label={label} className={clsx('flex min-w-0 flex-wrap items-center justify-between gap-2', className)}>
      <div className="flex min-w-0 flex-wrap items-center gap-2">{children}</div>
      {end && <div className="ml-auto flex flex-wrap items-center justify-end gap-2">{end}</div>}
    </div>
  );
}
