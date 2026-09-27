import clsx from 'clsx';
import { ChevronDown, PcCase, Sparkles } from 'lucide-react';
import { useId, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { PresetDetail } from '@/2-application';
import { ROUTES } from '@/4-presentation/app/routes';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { formatMoney, pluralize } from '@/shared/format';
import { PRESET_HIGHLIGHT_SLOTS, TIER_META, presetLineForSlot } from './homeSelectors';

export interface PresetsSectionProps {
  presets: PresetDetail[];
}

function PresetCard({ detail }: { detail: PresetDetail }) {
  const { loadPreset } = useBuilder();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const partsId = useId();
  const meta = TIER_META[detail.preset.tier];
  const { summary } = detail;

  const load = () => {
    if (loadPreset(detail.preset.id)) navigate(ROUTES.builder);
  };

  return (
    <Card as="li" padding="none" interactive className="flex h-full flex-col">
      <div className="flex flex-1 flex-col gap-4 p-5">
        <div className="flex items-center justify-between gap-3">
          <Badge tone={meta.tone} variant="soft">
            {meta.label}
          </Badge>
          <span className="text-xs text-text-faint tabular-nums">{pluralize(summary.count, 'pieza', 'piezas')}</span>
        </div>
        <div>
          <h3 className="line-clamp-2 font-display text-lg font-semibold leading-snug text-text">{detail.preset.name}</h3>
          <p className="mt-1 text-sm text-text-muted">{meta.blurb}</p>
        </div>

        <ul className="space-y-2 rounded-xl border border-border bg-surface-2 p-3">
          {PRESET_HIGHLIGHT_SLOTS.map((slotKey) => {
            const entry = summary.slots.find((candidate) => candidate.slot.key === slotKey);
            if (!entry) return null;
            const line = presetLineForSlot(detail, slotKey);
            return (
              <li key={slotKey} className="flex items-start gap-3">
                <span aria-hidden="true" className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg bg-surface-3 text-accent">
                  <CategoryIcon name={entry.slot.icon} className="size-4" />
                </span>
                <span className="min-w-0">
                  <span className="block text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{entry.slot.label}</span>
                  <span className={clsx('block truncate text-sm', line ? 'font-medium text-text' : 'text-text-muted')}>
                    {line ? line.product.shortName : 'Gráficos integrados en el procesador'}
                  </span>
                </span>
              </li>
            );
          })}
        </ul>

        <div className="mt-auto flex flex-wrap items-end justify-between gap-x-4 gap-y-1">
          <div>
            <p className="text-xs text-text-faint">Total del armado</p>
            <PriceTag price={summary.total} size="lg" showSaving={false} />
          </div>
          {summary.savings > 0 && (
            <p className="text-xs font-semibold text-success-text">
              Ahorrás {formatMoney(summary.savings)} frente al precio de lista
            </p>
          )}
        </div>

        <div className="grid grid-cols-1 gap-2 sm:grid-cols-[1fr_auto]">
          <Button type="button" variant="primary" leftIcon={<PcCase />} onClick={load} aria-label={`Cargar este armado: ${detail.preset.name}`}>
            Cargar este armado
          </Button>
          <Button
            type="button"
            variant="outline"
            aria-expanded={open}
            aria-controls={partsId}
            rightIcon={<ChevronDown className={clsx('transition-transform duration-200', open && 'rotate-180')} />}
            onClick={() => setOpen((current) => !current)}
          >
            {open ? 'Ocultar piezas' : 'Ver piezas'}
          </Button>
        </div>
      </div>

      {open && (
        <div id={partsId} className="border-t border-border bg-surface-2/60 p-5 animate-fade-up">
          <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">Piezas del armado</p>
          <ul className="mt-2 divide-y divide-border/60">
            {summary.lines.map((line) => (
              <li key={`${line.slot}-${line.product.sku}`} className="flex items-center justify-between gap-3 py-2 text-sm">
                <span className="min-w-0">
                  <span className="block text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">
                    {summary.slots.find((entry) => entry.slot.key === line.slot)?.slot.label ?? line.slot}
                    {line.quantity > 1 && ` × ${line.quantity}`}
                  </span>
                  <span className="block truncate text-text">{line.product.shortName}</span>
                </span>
                <span className="shrink-0 font-medium text-text tabular-nums">{formatMoney(line.product.price * line.quantity)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}

/** «PC armadas listas»: un armado sugerido por nivel, con sus piezas principales, el total y la carga al armador. */
export function PresetsSection({ presets }: PresetsSectionProps) {
  return (
    <section aria-labelledby="armados-titulo">
      <SectionHeading
        id="armados-titulo"
        eyebrow="Listas para cargar"
        title="PC armadas por nuestros técnicos"
        subtitle="Configuraciones probadas para cada nivel. Cargá una en el armador y cambiá lo que quieras antes de pedirla."
        action={{ label: 'Ver todos los armados', to: ROUTES.presets }}
      />
      {presets.length === 0 ? (
        <EmptyState className="mt-6" icon={<Sparkles />} title="Todavía no hay armados sugeridos" description="Podés armar tu PC desde cero con el armador paso a paso.">
          <Button to={ROUTES.builder} variant="primary" leftIcon={<PcCase />}>
            Armá tu PC
          </Button>
        </EmptyState>
      ) : (
        <ul className="mt-6 grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {presets.map((detail) => (
            <PresetCard key={detail.preset.id} detail={detail} />
          ))}
        </ul>
      )}
    </section>
  );
}
