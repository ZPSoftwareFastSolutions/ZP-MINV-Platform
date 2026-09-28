// Galería «PC armadas» (ancla #armados): los armados publicados desde el escritorio como tarjetas para cargarlos en el
// armador. Si la tienda no publicó ninguno, la sección se reduce a un aviso discreto.

import { Sparkles } from 'lucide-react';
import type { PresetDetail } from '@/2-application';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { PresetCard } from './PresetCard';

export interface PresetGalleryProps {
  presets: readonly PresetDetail[];
  currentCount: number;
  onLoad: (id: string) => void;
}

export function PresetGallery({ presets, currentCount, onLoad }: PresetGalleryProps) {
  if (presets.length === 0) {
    return (
      <section id="armados" aria-labelledby="armados-titulo" className="scroll-mt-32">
        <div className="flex items-start gap-3 rounded-card border border-dashed border-border-strong bg-surface/60 p-4 text-sm text-text-muted">
          <Sparkles aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-accent" />
          <p>
            <span id="armados-titulo" className="font-semibold text-text">
              Armados sugeridos
            </span>
            : la tienda todavía no publicó ninguno. Armá tu PC pieza por pieza desde los pasos de arriba.
          </p>
        </div>
      </section>
    );
  }
  return (
    <section id="armados" aria-labelledby="armados-titulo" className="scroll-mt-32">
      <SectionHeading
        id="armados-titulo"
        eyebrow="PC armadas"
        title="Armados sugeridos"
        subtitle="Configuraciones publicadas por los técnicos de la tienda, listas para cargar en tu armado y ajustar pieza por pieza."
      />
      <ul className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Armados sugeridos">
        {presets.map((detail, index) => (
          <PresetCard key={detail.preset.id} detail={detail} currentCount={currentCount} onLoad={onLoad} priority={index < 3} />
        ))}
      </ul>
    </section>
  );
}
