// Tarjeta de un armado sugerido: nivel, nombre, piezas clave con miniatura, cantidad de piezas y total.
// «Cargar este armado» reemplaza el armado actual; si ya hay piezas pide confirmación inline (el foco pasa a
// «Cancelar» y vuelve al botón si se cancela; al confirmar, quien carga el armado decide adónde va el foco).

import clsx from 'clsx';
import { Check, Sparkles } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { PresetTier } from '@/1-domain/builder/types';
import type { PresetDetail } from '@/2-application';
import { Badge, type BadgeTone } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { formatMoney, pluralize } from '@/shared/format';
import { presetKeyLines, TIER_LABELS } from '../builderSteps';

export interface PresetCardProps {
  detail: PresetDetail;
  /** Piezas del armado actual: si hay, se confirma antes de reemplazarlas. */
  currentCount: number;
  onLoad: (id: string) => void;
  /** Fila compacta para el selector; tarjeta completa para la galería. */
  layout?: 'card' | 'row';
  /** Carga la imagen sin lazy (galería sobre el pliegue). */
  priority?: boolean;
}

const TIER_TONE: Record<PresetTier, BadgeTone> = {
  entrada: 'neutral',
  media: 'nuevo',
  alta: 'destacado',
  entusiasta: 'oferta',
  oficina: 'condicion',
  creador: 'exito',
};

export function PresetCard({ detail, currentCount, onLoad, layout = 'card', priority = false }: PresetCardProps) {
  const [confirming, setConfirming] = useState(false);
  const loadRef = useRef<HTMLButtonElement>(null);
  const cancelRef = useRef<HTMLButtonElement>(null);
  const returnFocus = useRef(false);
  const { preset, lines, summary } = detail;
  const keyLines = presetKeyLines(lines, 3);
  const rest = lines.length - keyLines.length;

  useEffect(() => {
    if (confirming) {
      cancelRef.current?.focus();
    } else if (returnFocus.current) {
      returnFocus.current = false;
      loadRef.current?.focus();
    }
  }, [confirming]);

  const load = () => {
    onLoad(preset.id);
    setConfirming(false);
  };
  const cancel = () => {
    returnFocus.current = true;
    setConfirming(false);
  };
  const request = () => (currentCount > 0 ? setConfirming(true) : load());

  const actions = confirming ? (
    <div role="alert" className="flex flex-col gap-2 rounded-xl border border-warning/40 bg-warning-soft p-3 text-sm">
      <p className="text-text">Reemplaza {pluralize(currentCount, 'pieza que ya elegiste', 'piezas que ya elegiste')}. ¿Continuar?</p>
      <div className="flex flex-wrap gap-2">
        <Button ref={cancelRef} variant="ghost" onClick={cancel}>
          Cancelar
        </Button>
        <Button variant="cta" onClick={load}>
          Sí, reemplazar
        </Button>
      </div>
    </div>
  ) : (
    <Button ref={loadRef} variant="accent" fullWidth={layout === 'card'} leftIcon={<Sparkles aria-hidden="true" />} onClick={request}>
      Cargar este armado
    </Button>
  );

  const pieces = (
    <ul className="space-y-1.5" aria-label="Piezas clave">
      {keyLines.map((line) => (
        <li key={line.product.sku} className="flex items-center gap-2 text-sm text-text-muted">
          <ProductImage src={line.product.image} alt="" padding="sm" priority={priority} className="w-8 shrink-0 rounded-md" />
          <span className="line-clamp-1">{line.product.shortName}</span>
        </li>
      ))}
      {rest > 0 && (
        <li className="flex items-center gap-2 text-xs text-text-faint">
          <Check aria-hidden="true" className="size-4 shrink-0" />
          {pluralize(rest, 'pieza más', 'piezas más')} (fuente, gabinete, sistema…)
        </li>
      )}
    </ul>
  );

  if (layout === 'row') {
    return (
      <li className="grid gap-3 rounded-xl border border-border bg-surface-2 p-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center sm:gap-4">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={TIER_TONE[preset.tier]} size="sm">
              {TIER_LABELS[preset.tier]}
            </Badge>
            <span className="text-xs text-text-faint">{pluralize(summary.count, 'pieza', 'piezas')}</span>
          </div>
          <p className="mt-1 font-display text-base font-semibold text-text">{preset.name}</p>
          <div className="mt-2">{pieces}</div>
        </div>
        <div className="flex flex-col items-stretch gap-2 sm:w-52 sm:items-end">
          <p className="font-display text-xl font-semibold text-text tabular-nums">{formatMoney(summary.total)}</p>
          {actions}
        </div>
      </li>
    );
  }

  return (
    <Card as="li" interactive className={clsx('flex h-full flex-col gap-4')}>
      <div className="flex items-start justify-between gap-3">
        <Badge tone={TIER_TONE[preset.tier]}>{TIER_LABELS[preset.tier]}</Badge>
        <span className="text-xs text-text-faint">{pluralize(summary.count, 'pieza', 'piezas')}</span>
      </div>
      <h3 className="text-xl">{preset.name}</h3>
      {pieces}
      <div className="mt-auto space-y-3 border-t border-border pt-4">
        <div className="flex items-end justify-between gap-3">
          <div>
            <p className="text-xs text-text-muted">Total, IVA incluido</p>
            {summary.savings > 0 && <p className="text-xs font-semibold text-cta-hover">Ahorrás {formatMoney(summary.savings)}</p>}
          </div>
          <p className="font-display text-2xl font-semibold text-text tabular-nums">{formatMoney(summary.total)}</p>
        </div>
        {actions}
      </div>
    </Card>
  );
}
