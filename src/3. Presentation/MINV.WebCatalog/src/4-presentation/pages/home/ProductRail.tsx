import type { ReactNode } from 'react';
import type { Product } from '@/1-domain/catalog/types';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { Button } from '@/4-presentation/components/ui/Button';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { ROUTES } from '@/4-presentation/app/routes';

export interface ProductRailProps {
  id: string;
  eyebrow?: string;
  title: string;
  subtitle?: string;
  action?: { label: string; to: string };
  products: Product[];
  /** Carga sin lazy las primeras tarjetas (secciones sobre el pliegue). */
  priority?: boolean;
  /** Cinta o aviso entre el título y la grilla (p. ej. el porcentaje de ahorro de las ofertas). */
  ribbon?: ReactNode;
  emptyTitle?: string;
  emptyDescription?: string;
}

/** Sección de productos: título con «Ver todo», cinta opcional y grilla responsive de 2/3/4 columnas. */
export function ProductRail({
  id,
  eyebrow,
  title,
  subtitle,
  action,
  products,
  priority = false,
  ribbon,
  emptyTitle = 'No hay productos para mostrar',
  emptyDescription = 'Explorá el catálogo completo mientras actualizamos esta sección.',
}: ProductRailProps) {
  return (
    <section aria-labelledby={id}>
      <SectionHeading id={id} eyebrow={eyebrow} title={title} subtitle={subtitle} action={action} />
      {ribbon && <div className="mt-5">{ribbon}</div>}
      {products.length === 0 ? (
        <EmptyState className="mt-6" title={emptyTitle} description={emptyDescription}>
          <Button to={ROUTES.catalog} variant="outline">
            Ir al catálogo
          </Button>
        </EmptyState>
      ) : (
        <ul className="mt-6 grid grid-cols-2 gap-3 sm:gap-4 md:grid-cols-3 xl:grid-cols-4">
          {products.map((product, index) => (
            <li key={product.sku} className="min-w-0">
              <ProductCard product={product} priority={priority && index < 4} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
