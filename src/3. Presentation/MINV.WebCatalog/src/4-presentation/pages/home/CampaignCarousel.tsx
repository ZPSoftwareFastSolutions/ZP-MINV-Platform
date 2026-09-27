import clsx from 'clsx';
import { ArrowRight, ChevronLeft, ChevronRight, Gamepad2, Gpu, Pause, Play, Wrench, type LucideIcon } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { usePrefersReducedMotion } from '@/4-presentation/hooks/useMediaQuery';

interface Slide {
  id: string;
  eyebrow: string;
  title: string;
  description: string;
  cta: { label: string; to: string };
  icon: LucideIcon;
  /** Degradado y color del ícono decorativo, siempre con tokens del tema. */
  surface: string;
  glyph: string;
  tone: 'destacado' | 'nuevo' | 'oferta';
}

/** Tres campañas hechas solo con CSS y texto (sin imágenes externas). */
const SLIDES: readonly Slide[] = [
  {
    id: 'rtx',
    eyebrow: 'Semana RTX',
    title: 'Las GeForce RTX serie 50 ya están en Bolivia',
    description: 'DLSS 4, memoria GDDR7 y stock real en las tres sucursales. Elegí la tuya según la resolución y los Hz de tu monitor.',
    cta: { label: 'Ver tarjetas de video', to: ROUTES.category('tarjetas-de-video') },
    icon: Gpu,
    surface: 'from-primary-soft via-surface-2 to-surface',
    glyph: 'text-primary',
    tone: 'destacado',
  },
  {
    id: 'consolas',
    eyebrow: 'Consolas',
    title: 'PS5 y Nintendo Switch 2, listas para jugar',
    description: 'Consolas, mandos y los juegos del momento con garantía oficial de 12 meses y retiro sin costo en sucursal.',
    cta: { label: 'Ver consolas', to: ROUTES.category('consolas') },
    icon: Gamepad2,
    surface: 'from-accent-soft via-surface-2 to-surface',
    glyph: 'text-accent',
    tone: 'nuevo',
  },
  {
    id: 'armado',
    eyebrow: 'Servicio técnico',
    title: 'Armado gratis con tu compra',
    description: 'Elegí las piezas en el armador y nuestro equipo ensambla, prueba y entrega tu PC lista para usar, sin costo adicional.',
    cta: { label: 'Armá tu PC', to: ROUTES.builder },
    icon: Wrench,
    surface: 'from-cta/25 via-surface-2 to-surface',
    glyph: 'text-cta',
    tone: 'oferta',
  },
];

const AUTOPLAY_MS = 6500;

/**
 * Carrusel de campañas accesible: botones anterior/siguiente, puntos con nombre, pausa al pasar el mouse o enfocar,
 * botón de pausa explícito y sin rotación automática si el sistema pide menos movimiento. Los banners que no se ven
 * quedan inertes (fuera del orden de tabulación) y la región avisa el cambio cuando la rotación está detenida.
 */
export function CampaignCarousel() {
  const reducedMotion = usePrefersReducedMotion();
  const [index, setIndex] = useState(0);
  const [hovering, setHovering] = useState(false);
  const [stopped, setStopped] = useState(false);
  const baseId = useId();
  const count = SLIDES.length;
  const autoplay = !reducedMotion && !stopped && !hovering;

  useEffect(() => {
    if (!autoplay) return;
    const timer = setInterval(() => setIndex((current) => (current + 1) % count), AUTOPLAY_MS);
    return () => clearInterval(timer);
  }, [autoplay, count]);

  const go = (next: number) => setIndex(((next % count) + count) % count);

  return (
    <section
      aria-roledescription="carrusel"
      aria-label="Campañas de la semana"
      className="relative"
      onMouseEnter={() => setHovering(true)}
      onMouseLeave={() => setHovering(false)}
      onFocus={() => setHovering(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setHovering(false);
      }}
    >
      <div className="overflow-hidden rounded-card border border-border bg-surface shadow-card">
        <ul
          id={`${baseId}-slides`}
          aria-live={autoplay ? 'off' : 'polite'}
          className="flex transition-transform duration-500 ease-out"
          style={{ transform: `translateX(-${index * 100}%)` }}
        >
          {SLIDES.map((slide, position) => {
            const active = position === index;
            return (
              <li
                key={slide.id}
                role="group"
                aria-roledescription="banner"
                aria-label={`${position + 1} de ${count}: ${slide.eyebrow}`}
                aria-hidden={!active}
                inert={!active}
                className={clsx('relative w-full shrink-0 overflow-hidden bg-linear-to-br', slide.surface)}
              >
                <div aria-hidden="true" className="pointer-events-none absolute inset-y-0 right-0 hidden w-2/5 items-center justify-center sm:flex">
                  <span className="absolute size-72 rounded-full border border-border/70" />
                  <span className="absolute size-52 rounded-full border border-border-strong/60" />
                  <span className={clsx('absolute size-36 rounded-full border-2 opacity-70', slide.glyph, 'border-current')} />
                  <slide.icon className={clsx('relative size-24 opacity-80 drop-shadow-lg', slide.glyph)} />
                </div>
                <div className="relative flex min-h-[17rem] flex-col justify-center gap-4 p-6 sm:max-w-[60%] sm:p-10">
                  <Badge tone={slide.tone} variant="soft" className="self-start">
                    {slide.eyebrow}
                  </Badge>
                  <h2 className="font-display text-2xl font-semibold text-text sm:text-3xl">{slide.title}</h2>
                  <p className="max-w-lg text-base text-text-muted">{slide.description}</p>
                  <Button to={slide.cta.to} variant={slide.tone === 'oferta' ? 'cta' : slide.tone === 'nuevo' ? 'accent' : 'primary'} rightIcon={<ArrowRight />} className="mt-1 self-start">
                    {slide.cta.label}
                  </Button>
                </div>
              </li>
            );
          })}
        </ul>
      </div>

      <div className="mt-3 flex items-center justify-between gap-3">
        <div role="group" aria-label="Elegir banner" className="flex items-center gap-1">
          {SLIDES.map((slide, position) => (
            <button
              key={slide.id}
              type="button"
              aria-label={`Ir al banner ${position + 1}: ${slide.eyebrow}`}
              aria-current={position === index ? 'true' : undefined}
              onClick={() => go(position)}
              className="group flex size-11 cursor-pointer items-center justify-center rounded-full"
            >
              <span
                aria-hidden="true"
                className={clsx(
                  'block h-2 rounded-full transition-[width,background-color] duration-300',
                  position === index ? 'w-8 bg-accent' : 'w-2 bg-border-strong group-hover:bg-text-faint',
                )}
              />
            </button>
          ))}
        </div>
        <div className="flex items-center gap-1">
          {!reducedMotion && (
            <IconButton
              label={stopped ? 'Reanudar la rotación automática' : 'Pausar la rotación automática'}
              icon={stopped ? <Play /> : <Pause />}
              aria-pressed={stopped}
              onClick={() => setStopped((current) => !current)}
            />
          )}
          <IconButton label="Banner anterior" icon={<ChevronLeft />} variant="subtle" onClick={() => go(index - 1)} />
          <IconButton label="Banner siguiente" icon={<ChevronRight />} variant="subtle" onClick={() => go(index + 1)} />
        </div>
      </div>
    </section>
  );
}
