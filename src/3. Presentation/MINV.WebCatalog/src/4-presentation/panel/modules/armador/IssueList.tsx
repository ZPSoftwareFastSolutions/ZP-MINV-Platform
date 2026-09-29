// Módulo «Armador de PC» · el panel de COMPATIBILIDAD con lo que respondió el servidor (`CheckPcBuildQuery`): título
// (compatible, con avisos o con errores), cada error y aviso con su mensaje, y la energía (consumo estimado, fuente
// recomendada, potencia de la fuente y a qué porcentaje trabajaría). Aquí no se decide nada: se muestra el informe
// (regla T-06). El título se anuncia al cambiar (compatibilidad en vivo).

import clsx from 'clsx';
import { CircleAlert, CircleCheck, Info, LoaderCircle, TriangleAlert } from 'lucide-react';
import { formatNumber } from '@/4-presentation/panel/lib';
import { checkSummary, psuLoad, sortedIssues, type CheckData } from './builder';

export interface IssueListProps {
  check: CheckData | undefined;
  /** ¿Hay piezas elegidas? (sin piezas: «Elija las piezas»). */
  hasParts?: boolean;
  /** Hay una revisión en curso. */
  checking?: boolean;
  /** La cotización se emitió aceptando los errores. */
  accepted?: boolean;
}

const BOX = {
  info: 'border-accent/40 bg-accent-soft',
  danger: 'border-danger/40 bg-danger-soft',
  warning: 'border-warning/40 bg-warning-soft',
  success: 'border-success/40 bg-success-soft',
} as const;
const TEXT = { info: 'text-accent-hover', danger: 'text-danger-text', warning: 'text-warning-text', success: 'text-success-text' } as const;
const ICONS = { info: Info, danger: CircleAlert, warning: TriangleAlert, success: CircleCheck } as const;

export function IssueList({ check, hasParts = true, checking = false, accepted = false }: IssueListProps) {
  const summary = checkSummary(check, hasParts);
  const issues = hasParts ? sortedIssues(check) : [];
  const load = psuLoad(check);
  const Icon = checking ? LoaderCircle : ICONS[summary.tone];
  return (
    <section aria-label="Compatibilidad" className="space-y-3" data-testid="compatibilidad">
      <div className={clsx('flex items-start gap-3 rounded-xl border p-3', BOX[summary.tone])}>
        <Icon aria-hidden="true" className={clsx('mt-0.5 size-5 shrink-0', TEXT[summary.tone], checking && 'animate-spin')} />
        <div className="min-w-0">
          <p role="status" className={clsx('font-semibold', TEXT[summary.tone])} data-testid="compatibilidad-titulo">
            {summary.title}
          </p>
          <p className="text-xs text-text-muted">
            {checking ? 'Revisando con las fichas técnicas…' : 'Revisión en vivo con las fichas técnicas de cada pieza.'}
            {accepted && ' Se cotizó aceptando los errores.'}
          </p>
        </div>
      </div>
      {issues.length > 0 && (
        <ul className="space-y-1.5" aria-label="Errores y avisos de compatibilidad">
          {issues.map((issue, index) => (
            <li key={`${issue.code}-${index}`} className="flex items-start gap-2 text-sm text-text">
              {issue.isError ? (
                <CircleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-danger-text" />
              ) : (
                <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning-text" />
              )}
              <span>
                <span className="sr-only">{issue.isError ? 'Error: ' : 'Aviso: '}</span>
                {issue.message}
              </span>
            </li>
          ))}
        </ul>
      )}
      {check && hasParts && (
        <div className="rounded-xl border border-border p-3">
          <h4 className="text-xs font-semibold tracking-wide text-text-faint uppercase">Energía</h4>
          <dl className="mt-2 grid grid-cols-3 gap-2 text-sm">
            <div>
              <dt className="text-xs text-text-muted">Consumo</dt>
              <dd className="font-semibold text-text tabular-nums">{formatNumber(check.estimatedDrawW)} W</dd>
            </div>
            <div>
              <dt className="text-xs text-text-muted">Recomendada</dt>
              <dd className="font-semibold text-text tabular-nums">{formatNumber(check.recommendedPsuW)} W</dd>
            </div>
            <div>
              <dt className="text-xs text-text-muted">Fuente</dt>
              <dd className="font-semibold text-text tabular-nums">{check.psuW ? `${formatNumber(check.psuW)} W` : 'Sin fuente'}</dd>
            </div>
          </dl>
          {load !== null && (
            <>
              <div aria-hidden="true" className="mt-2 h-1.5 overflow-hidden rounded-full bg-surface-3">
                {/* Color según los números del servidor: la fuente no alcanza, queda por debajo de la recomendada o sobra. */}
                <div
                  className={clsx(
                    'h-full rounded-full',
                    (check.psuW ?? 0) < check.estimatedDrawW ? 'bg-danger' : (check.psuW ?? 0) < check.recommendedPsuW ? 'bg-warning' : 'bg-accent',
                  )}
                  style={{ width: `${Math.min(100, load)}%` }}
                />
              </div>
              <p className="mt-1 text-xs text-text-muted">Con el consumo estimado, la fuente trabajaría al {formatNumber(load)} % de su potencia.</p>
            </>
          )}
        </div>
      )}
    </section>
  );
}
