// Diccionario ÚNICO de los niveles de armado sugerido (etiqueta, tono de insignia y descripción corta). Lo comparten
// la portada, la galería del armador y el selector de armados: el mismo armado se ve igual en todo el sitio.

import type { PresetTier } from '@/1-domain/builder/types';
import type { BadgeTone } from '@/4-presentation/components/ui/Badge';

export interface PresetTierMeta {
  label: string;
  tone: BadgeTone;
  blurb: string;
}

export const PRESET_TIERS: Record<PresetTier, PresetTierMeta> = {
  entrada: { label: 'Gama de entrada', tone: 'nuevo', blurb: 'Para jugar en 1080p con buena tasa de cuadros sin gastar de más.' },
  media: { label: 'Gama media', tone: 'destacado', blurb: '1440p fluido hoy y margen para actualizar mañana.' },
  alta: { label: 'Gama alta', tone: 'oferta', blurb: '1440p a tope y 4K con trazado de rayos activado.' },
  entusiasta: { label: 'Entusiasta', tone: 'aviso', blurb: 'Lo mejor en cada ranura, sin concesiones.' },
  creador: { label: 'Creadores y streaming', tone: 'exito', blurb: 'Streaming, edición y render sin esperas.' },
  oficina: { label: 'Oficina y estudio', tone: 'neutral', blurb: 'Silenciosa, eficiente y lista para trabajar.' },
};

/** Orden de los niveles al listar armados por gama (de menor a mayor presupuesto, luego los de uso). */
export const PRESET_TIER_ORDER: readonly PresetTier[] = ['entrada', 'media', 'alta', 'entusiasta', 'creador', 'oficina'];
