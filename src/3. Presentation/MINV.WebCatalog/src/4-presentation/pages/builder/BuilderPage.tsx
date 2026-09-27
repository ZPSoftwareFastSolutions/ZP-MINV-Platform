// PLACEHOLDER: el armador paso a paso lo construye el agente de «Armá tu PC». Esta vista mínima muestra las
// ranuras, los armados sugeridos (#armados) y abre el cajón «Mi armado».

import { PcCase, Sparkles } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Card } from '@/4-presentation/components/ui/Card';
import { CategoryIcon } from '@/4-presentation/components/ui/CategoryIcon';
import { Container } from '@/4-presentation/components/ui/Container';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { formatMoney, pluralize } from '@/shared/format';

export function BuilderPage() {
  useDocumentTitle('Armá tu PC');
  const { catalog } = useServices();
  const { count, total, openDrawer, loadPreset } = useBuilder();
  const slots = catalog.getBuildSlots();
  const presets = catalog.getPresets();

  return (
    <Container className="space-y-12 py-8">
      <SectionHeading as="h1" eyebrow="Paso a paso" title="Armá tu PC" subtitle="Elegí una pieza por ranura y mirá el total al instante. Solo presentación: nada se guarda." />
      <div className="flex flex-wrap items-center gap-3">
        <Button variant="brand" leftIcon={<PcCase />} onClick={openDrawer}>
          Mi armado ({pluralize(count, 'pieza', 'piezas')} · {formatMoney(total)})
        </Button>
        <Button to={ROUTES.catalog} variant="outline">
          Ir al catálogo
        </Button>
      </div>
      <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {slots.map((slot) => (
          <Card as="li" key={slot.key} className="flex gap-3">
            <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-surface-2 text-accent">
              <CategoryIcon name={slot.icon} />
            </span>
            <div>
              <p className="font-semibold text-text">
                {slot.order}. {slot.label} {slot.required && <span className="text-xs font-medium text-cta-hover">· obligatoria</span>}
              </p>
              <p className="mt-1 text-sm text-text-muted">{slot.hint}</p>
            </div>
          </Card>
        ))}
      </ul>
      <section id="armados" aria-labelledby="armados-titulo" className="scroll-mt-32">
        <SectionHeading id="armados-titulo" eyebrow="PC armadas" title="Armados sugeridos" subtitle="Configuraciones listas para cargar en tu armado y ajustar a tu gusto." />
        <ul className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {presets.map((detail) => (
            <Card as="li" key={detail.preset.id} interactive className="flex flex-col gap-3">
              <p className="font-display text-lg font-semibold text-text">{detail.preset.name}</p>
              <p className="text-sm text-text-muted">{pluralize(detail.summary.count, 'pieza', 'piezas')}</p>
              <p className="mt-auto font-display text-2xl font-semibold text-text">{formatMoney(detail.summary.total)}</p>
              <Button variant="accent" leftIcon={<Sparkles />} onClick={() => loadPreset(detail.preset.id)}>
                Cargar este armado
              </Button>
            </Card>
          ))}
        </ul>
      </section>
    </Container>
  );
}
