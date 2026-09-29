// USO · Detalle lateral de una fila (se abre desde la tabla sin perder la lista). Cajón modal a la derecha: foco
// atrapado, Escape y el fondo cierran, se cierra al navegar y devuelve el foco (components/ui/Drawer).
//
//   const detalle = useRpcQuery('GetSaleQuery', { number: abierta ?? '' }, { enabled: abierta !== null });
//   <SidePanel open={abierta !== null} onClose={() => setAbierta(null)} title={`Venta ${abierta}`}
//     loading={detalle.loading} error={detalle.error} onRetry={detalle.reload}
//     footer={<Button variant="danger" onClick={…}>Anular</Button>}>
//     <DetailList items={[{ label: 'Cliente', value: … }, …]} />
//   </SidePanel>
//
// Mientras `loading` muestra un esqueleto; con `error`, el aviso con «Reintentar» (si hay `onRetry`). `onClose` puede
// ser una función nueva en cada dibujo: el panel la llama UNA vez por cierre (y nunca si ya está cerrado).

import type { ReactNode } from 'react';
import { LoadingState } from '@/4-presentation/components/feedback/AsyncState';
import { Drawer } from '@/4-presentation/components/ui/Drawer';
import { useStableClose } from './stableClose';
import { ErrorState } from './States';

export interface SidePanelProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: string;
  /** Botones fijos al pie (acciones sobre el registro). */
  footer?: ReactNode;
  /** Algo junto al título (un StatusBadge). */
  headerExtra?: ReactNode;
  /** md 448 px · lg 512 px (por defecto). En el teléfono ocupa toda la pantalla. */
  size?: 'md' | 'lg';
  loading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  children?: ReactNode;
}

export function SidePanel({ open, onClose, title, description, footer, headerExtra, size = 'lg', loading = false, error, onRetry, children }: SidePanelProps) {
  const close = useStableClose(open, onClose);
  let content = children;
  if (loading) content = <LoadingState label={`Cargando ${title.toLowerCase()}…`} rows={2} />;
  else if (error) content = <ErrorState error={error} onRetry={onRetry} />;
  return (
    <Drawer open={open} onClose={close} title={title} description={description} side="right" size={size} footer={footer} headerExtra={headerExtra}>
      {content}
    </Drawer>
  );
}
