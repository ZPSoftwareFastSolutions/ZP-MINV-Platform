// Módulo «Estado del SIAT» · la tarjeta de un punto de venta del SIN (la grilla del escritorio): sucursal, punto, caja,
// modo de conexión (desde cuándo), CUIS y CUFD con su vigencia, documentos por enviar y fuera de línea, evento abierto,
// último error y sus acciones como botones (solo las que el rol puede y el modo admite).

import { FileClock, KeyRound, Plug, Power, PowerOff, RadioTower, RotateCcw, WifiOff } from 'lucide-react';
import { useId } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button, DetailList, StatusBadge } from '@/4-presentation/panel/kit';
import { formatDateTime } from '@/4-presentation/panel/lib';
import {
  CUFD_MARGIN_MS,
  CUIS_MARGIN_MS,
  EVENT_KINDS,
  EVENT_STATUSES,
  MODES,
  branchText,
  offlineDocumentsLink,
  pointAbilities,
  pointMode,
  pointTitle,
  validity,
  validityTone,
  type PointData,
} from './siat';

export interface PointCardAbilities {
  check: boolean;
  requestCufd: boolean;
  contingency: boolean;
}

export interface PointCardProps {
  point: PointData;
  now: Date;
  can: PointCardAbilities;
  busy: boolean;
  onCheck: () => void;
  onRequestCufd: () => void;
  onGoOffline: () => void;
  onStartManual: () => void;
  onEnd: () => void;
  onRecover: () => void;
}

export function PointCard({ point, now, can, busy, onCheck, onRequestCufd, onGoOffline, onStartManual, onEnd, onRecover }: PointCardProps) {
  const titleId = useId();
  const abilities = pointAbilities(point);
  const cuis = validity(point.cuisValidUntil, now, CUIS_MARGIN_MS);
  const cufd = validity(point.cufdValidUntil, now, CUFD_MARGIN_MS);
  const open = point.openEvent;
  const pending = point.pendingDocuments;
  const offline = point.offlineDocuments;

  return (
    <article aria-labelledby={titleId} className="flex min-w-0 flex-col gap-3 rounded-card border border-border bg-surface p-4 shadow-card" data-testid={`punto-${point.branchCode}-${point.code}`}>
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 id={titleId} className="font-display text-lg font-semibold">
            {pointTitle(point)}
          </h3>
          <p className="text-sm text-text-muted">
            {branchText(point)} · sucursal {point.siatBranchCode} del Padrón · {point.registerCode ? `caja ${point.registerCode}` : 'sin caja'}
          </p>
        </div>
        <StatusBadge status={pointMode(point)} statuses={MODES} />
      </header>

      <DetailList
        items={[
          { label: 'Modo', value: point.mode === 'Online' || point.isClosed ? null : `desde ${formatDateTime(point.modeSince)}` },
          {
            label: 'CUIS',
            value: <StatusBadge tone={validityTone(cuis)}>{point.cuisValidUntil ? `hasta ${formatDateTime(point.cuisValidUntil)}` : 'Sin CUIS'}</StatusBadge>,
          },
          {
            label: 'CUFD',
            value: <StatusBadge tone={validityTone(cufd)}>{point.cufdValidUntil ? `hasta ${formatDateTime(point.cufdValidUntil)}` : 'Sin CUFD'}</StatusBadge>,
          },
          { label: 'Último contacto con el SIN', value: point.lastContactAt ? formatDateTime(point.lastContactAt) : null },
          { label: 'Por enviar', value: pending + offline === 0 ? 'Nada' : `${pending} por enviar${offline > 0 ? ` · ${offline} fuera de línea` : ''}` },
          { label: 'Próximo reintento', value: point.retryAt ? formatDateTime(point.retryAt) : null },
          {
            label: 'Evento abierto',
            value: open ? (
              <span className="flex flex-wrap items-center gap-1">
                <StatusBadge status={open.kind} statuses={EVENT_KINDS} />
                <StatusBadge status={open.status} statuses={EVENT_STATUSES} />
              </span>
            ) : null,
            wide: true,
          },
          { label: 'Último error', value: point.lastError, wide: true },
        ]}
      />

      {offline > 0 && (
        <Button variant="ghost" leftIcon={<FileClock />} to={ROUTES.panelModule(offlineDocumentsLink(point))} className="self-start">
          Ver los documentos fuera de línea
        </Button>
      )}

      {!point.isClosed && (
        <div className="mt-auto flex flex-wrap gap-2 border-t border-border pt-3" role="group" aria-label={`Acciones del ${pointTitle(point).toLocaleLowerCase('es')}`}>
          {can.check && (
            <Button variant="outline" leftIcon={<RadioTower />} disabled={busy} onClick={onCheck}>
              Verificar comunicación
            </Button>
          )}
          {can.requestCufd && (
            <Button variant="outline" leftIcon={<KeyRound />} disabled={busy} onClick={onRequestCufd}>
              Pedir CUFD nuevo
            </Button>
          )}
          {can.contingency && abilities.offline && (
            <Button variant="outline" leftIcon={<RotateCcw />} disabled={busy} onClick={onRecover}>
              Recuperar ahora
            </Button>
          )}
          {can.contingency && abilities.canEnd && (
            <Button leftIcon={<Plug />} disabled={busy} onClick={onEnd}>
              Terminar contingencia
            </Button>
          )}
          {can.contingency && abilities.online && (
            <Button variant="outline" leftIcon={<WifiOff />} disabled={busy} onClick={onGoOffline}>
              Pasar a fuera de línea
            </Button>
          )}
          {can.contingency && abilities.online && (
            <Button variant="danger" leftIcon={<Power />} disabled={busy} onClick={onStartManual}>
              Contingencia manual
            </Button>
          )}
        </div>
      )}
      {point.isClosed && (
        <p className="flex items-center gap-2 text-sm text-text-muted">
          <PowerOff aria-hidden="true" className="size-4" /> Cerrado en el SIN: sus documentos siguen consultables.
        </p>
      )}
    </article>
  );
}
