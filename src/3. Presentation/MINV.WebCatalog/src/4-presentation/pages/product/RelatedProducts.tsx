// Carrusel horizontal de productos relacionados: desplazamiento nativo con scroll-snap (táctil y con rueda) y botones
// anterior/siguiente que se deshabilitan en los extremos. Sin barra visible; las tarjetas son enfocables por teclado.

import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { Product } from '@/1-domain/catalog/types';
import { ProductCard } from '@/4-presentation/components/product/ProductCard';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { SectionHeading } from '@/4-presentation/components/ui/SectionHeading';
import { usePrefersReducedMotion } from '@/4-presentation/hooks/useMediaQuery';

export interface RelatedProductsProps {
  products: readonly Product[];
  title?: string;
  className?: string;
}

const EDGE_TOLERANCE = 4;

export function RelatedProducts({ products, title = 'También te puede interesar', className }: RelatedProductsProps) {
  const listRef = useRef<HTMLUListElement>(null);
  const [canScrollBack, setCanScrollBack] = useState(false);
  const [canScrollForward, setCanScrollForward] = useState(false);
  const reducedMotion = usePrefersReducedMotion();

  const updateEdges = useCallback(() => {
    const list = listRef.current;
    if (!list) return;
    setCanScrollBack(list.scrollLeft > EDGE_TOLERANCE);
    setCanScrollForward(list.scrollLeft + list.clientWidth < list.scrollWidth - EDGE_TOLERANCE);
  }, []);

  useEffect(() => {
    updateEdges();
    const list = listRef.current;
    if (!list || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(updateEdges);
    observer.observe(list);
    return () => observer.disconnect();
  }, [updateEdges, products]);

  const scrollByPage = (direction: -1 | 1) => {
    const list = listRef.current;
    if (!list) return;
    list.scrollBy({ left: direction * list.clientWidth * 0.9, behavior: reducedMotion ? 'auto' : 'smooth' });
  };

  if (products.length === 0) return null;

  return (
    <section aria-labelledby="relacionados-titulo" className={className}>
      <div className="flex items-end justify-between gap-4">
        <SectionHeading id="relacionados-titulo" eyebrow="Relacionados" title={title} />
        <div className="flex shrink-0 gap-2 max-sm:hidden">
          <IconButton variant="subtle" label="Ver productos anteriores" icon={<ChevronLeft />} disabled={!canScrollBack} onClick={() => scrollByPage(-1)} />
          <IconButton variant="subtle" label="Ver más productos" icon={<ChevronRight />} disabled={!canScrollForward} onClick={() => scrollByPage(1)} />
        </div>
      </div>
      <ul
        ref={listRef}
        onScroll={updateEdges}
        aria-label={title}
        className="-mx-4 mt-6 flex snap-x snap-mandatory gap-3 overflow-x-auto scroll-px-4 px-4 pb-2 scrollbar-none sm:mx-0 sm:gap-4 sm:scroll-px-0 sm:px-0"
      >
        {products.map((product) => (
          <li key={product.sku} className="w-60 shrink-0 snap-start sm:w-64 lg:w-72">
            <ProductCard product={product} showCategory={false} />
          </li>
        ))}
      </ul>
    </section>
  );
}
