// Galería «PC armadas» (ancla #armados): los armados sugeridos como tarjetas para cargarlos en el armador.

import type { PresetDetail } from '@/2-application';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { PresetCard } from './PresetCard';

export interface PresetGalleryProps {
  presets: readonly PresetDetail[];
  currentCount: number;
  onLoad: (id: string) => void;
}

export function PresetGallery({ presets, currentCount, onLoad }: PresetGalleryProps) {
  return (
    <section id="armados" aria-labelledby="armados-titulo" className="scroll-mt-32">
      <SectionHeading
        id="armados-titulo"
        eyebrow="PC armadas"
        title="Armados sugeridos"
        subtitle="Configuraciones equilibradas para cada presupuesto, listas para cargar en tu armado y ajustar pieza por pieza."
      />
      <ul className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-3" aria-label="Armados sugeridos">
        {presets.map((detail, index) => (
          <PresetCard key={detail.preset.id} detail={detail} currentCount={currentCount} onLoad={onLoad} priority={index < 3} />
        ))}
      </ul>
    </section>
  );
}
