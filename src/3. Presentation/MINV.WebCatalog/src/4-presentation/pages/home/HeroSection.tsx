import clsx from 'clsx';
import { BadgePercent, Cpu, MapPin, QrCode, ShieldCheck, Truck } from 'lucide-react';
import { Link } from 'react-router-dom';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { STORE } from '@/shared/constants';
import { formatNumber } from '@/shared/format';

export interface HeroSectionProps {
  /** Dos o tres productos protagonistas (losetas flotantes). */
  products: Product[];
  stats: { products: number; brands: number; presets: number };
}

const TRUST = [
  { icon: Truck, title: 'Envíos a todo el país', detail: 'En 24 a 72 horas hábiles' },
  { icon: ShieldCheck, title: 'Garantía oficial', detail: 'De 12 a 36 meses según el producto' },
  { icon: QrCode, title: 'Pago con QR, tarjeta o transferencia', detail: 'También en cuotas con bancos asociados' },
  { icon: MapPin, title: `${STORE.branches.length} sucursales`, detail: STORE.branches.join(' · ') },
] as const;

/** Posición, tamaño y retraso de aparición de cada loseta del hero (la primera es la protagonista). */
const TILE_LAYOUT = [
  { className: 'row-span-2 lg:-rotate-2', delay: 120 },
  { className: 'lg:translate-x-3 lg:rotate-2', delay: 240 },
  { className: 'lg:-translate-x-2 lg:rotate-1', delay: 360 },
] as const;

function HeroTile({ product, className, delay, priority }: { product: Product; className: string; delay: number; priority: boolean }) {
  return (
    <li className={clsx('min-w-0 animate-fade-up', className)} style={{ animationDelay: `${delay}ms` }}>
      <Link
        to={ROUTES.product(product.slug)}
        className="group block h-full rounded-2xl bg-linear-to-br from-primary to-accent p-px shadow-glow transition-shadow duration-300 hover:shadow-glow-accent"
      >
        <div className="flex h-full flex-col gap-3 rounded-[calc(1rem-1px)] bg-surface p-3">
          <ProductImage src={product.image} alt="" priority={priority} padding="sm" className="border-transparent" imgClassName="transition-transform duration-300 group-hover:scale-[1.04]" />
          <div className="min-w-0">
            <p className="truncate text-[0.6875rem] font-semibold uppercase tracking-wide text-text-faint">{product.categoryName}</p>
            <p className="line-clamp-2 text-sm leading-snug font-medium text-text">{product.shortName}</p>
            <PriceTag price={product.price} listPrice={product.listPrice} size="sm" showSaving={false} className="mt-1" />
          </div>
        </div>
      </Link>
    </li>
  );
}

/**
 * Portada: titular, promesa, dos llamadas a la acción, cifras del catálogo y una composición de losetas con productos
 * reales del mock. Debajo, la cinta de confianza (envíos, garantía, pagos y sucursales).
 */
export function HeroSection({ products, stats }: HeroSectionProps) {
  return (
    <section aria-labelledby="hero-titulo" className="relative overflow-hidden">
      <div aria-hidden="true" className="pointer-events-none absolute inset-0">
        <div className="absolute -top-32 right-[-12%] size-[34rem] rounded-full bg-primary/20 blur-3xl" />
        <div className="absolute top-32 right-[18%] size-[22rem] rounded-full bg-accent/15 blur-3xl" />
        <div className="absolute inset-0 bg-[linear-gradient(to_right,var(--color-border)_1px,transparent_1px),linear-gradient(to_bottom,var(--color-border)_1px,transparent_1px)] bg-[size:3rem_3rem] opacity-25 [mask-image:radial-gradient(70%_60%_at_60%_40%,black,transparent)]" />
      </div>

      <Container className="relative grid gap-10 py-12 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:py-20">
        <div className="animate-fade-up">
          <p className="inline-flex items-center gap-2 rounded-full border border-border bg-surface/80 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-accent">
            <span aria-hidden="true" className="size-1.5 rounded-full bg-accent" />
            {STORE.legalName} · Bolivia
          </p>
          <h1 id="hero-titulo" className="mt-5 max-w-2xl font-display text-4xl font-bold tracking-tight text-text sm:text-5xl lg:text-[3.5rem] lg:leading-[1.05]">
            Armá la PC que querés, <span className="text-gradient-brand">pieza por pieza</span>.
          </h1>
          <p className="mt-5 max-w-xl text-lg text-text-muted">
            Componentes originales, consolas y periféricos con garantía oficial. Elegí cada pieza, mirá el total al instante y recibí tu equipo armado y probado por nuestros técnicos.
          </p>
          <div className="mt-8 flex flex-wrap gap-3">
            <Button to={ROUTES.builder} variant="brand" size="lg" leftIcon={<Cpu />}>
              Armá tu PC
            </Button>
            <Button to={ROUTES.offers} variant="outline" size="lg" leftIcon={<BadgePercent />}>
              Ver ofertas
            </Button>
          </div>
          <dl className="mt-10 grid max-w-md grid-cols-3 gap-4 border-t border-border/70 pt-6">
            <div>
              <dt className="text-xs font-medium uppercase tracking-wide text-text-faint">Productos</dt>
              <dd className="mt-1 font-display text-2xl font-semibold text-text tabular-nums">{formatNumber(stats.products)}</dd>
            </div>
            <div>
              <dt className="text-xs font-medium uppercase tracking-wide text-text-faint">Marcas</dt>
              <dd className="mt-1 font-display text-2xl font-semibold text-text tabular-nums">{formatNumber(stats.brands)}</dd>
            </div>
            <div>
              <dt className="text-xs font-medium uppercase tracking-wide text-text-faint">Armados sugeridos</dt>
              <dd className="mt-1 font-display text-2xl font-semibold text-text tabular-nums">{formatNumber(stats.presets)}</dd>
            </div>
          </dl>
        </div>

        {products.length > 0 && (
          <ul aria-label="Productos protagonistas" className="mx-auto grid w-full max-w-md grid-cols-[1.3fr_1fr] gap-3 sm:gap-4 lg:mr-0 lg:max-w-[34rem] lg:pl-6">
            {products.slice(0, TILE_LAYOUT.length).map((product, index) => (
              <HeroTile key={product.sku} product={product} className={TILE_LAYOUT[index].className} delay={TILE_LAYOUT[index].delay} priority={index === 0} />
            ))}
          </ul>
        )}
      </Container>

      <div className="relative border-y border-border/70 bg-surface/60">
        <Container>
          <ul className="grid grid-cols-1 divide-y divide-border/60 sm:grid-cols-2 sm:divide-y-0 lg:grid-cols-4 lg:divide-x">
            {TRUST.map((item) => (
              <li key={item.title} className="flex items-center gap-3 py-3 sm:pr-4 lg:px-5 lg:first:pl-0 lg:last:pr-0">
                <span aria-hidden="true" className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent-hover">
                  <item.icon className="size-5" />
                </span>
                <span className="min-w-0">
                  <span className="block text-sm font-semibold text-text">{item.title}</span>
                  <span className="block truncate text-xs text-text-muted">{item.detail}</span>
                </span>
              </li>
            ))}
          </ul>
        </Container>
      </div>
    </section>
  );
}
