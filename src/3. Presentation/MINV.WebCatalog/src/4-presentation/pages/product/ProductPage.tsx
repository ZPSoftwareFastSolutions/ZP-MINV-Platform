// Ficha de producto: /producto/:slug. Migas, galería, marca (enlace al catálogo filtrado), nombre, SKU e insignias;
// bloque de compra; pestañas (descripción, especificaciones, compatibilidad informativa) y relacionados.
// Un slug inexistente muestra la página 404 del sitio.

import { ScanBarcode } from 'lucide-react';
import { useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import type { Product } from '@/1-domain/catalog/types';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductBadges } from '@/4-presentation/components/product/ProductBadges';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { catalogHref, EMPTY_FILTERS } from '../catalog/catalogFilters';
import { NotFoundPage } from '../NotFoundPage';
import { ProductGallery } from './ProductGallery';
import { ProductTabs } from './ProductTabs';
import { PurchaseBox } from './PurchaseBox';
import { RelatedProducts } from './RelatedProducts';

const RELATED_LIMIT = 8;

export function ProductPage() {
  const { catalog } = useServices();
  const { slug = '' } = useParams();
  const product = catalog.getProduct(slug);
  if (!product) return <NotFoundPage />;
  // La clave reinicia el estado interno (pestaña activa, cantidad) al pasar de un producto a otro.
  return <ProductDetail key={product.slug} product={product} />;
}

function ProductDetail({ product }: { product: Product }) {
  const { catalog } = useServices();
  useDocumentTitle(product.name);

  const path = useMemo(() => catalog.getCategoryPath(product.category), [catalog, product.category]);
  const related = useMemo(() => catalog.getRelatedProducts(product.slug, RELATED_LIMIT), [catalog, product.slug]);
  const brand = useMemo(() => catalog.getBrands().find((candidate) => candidate.name === product.brand), [catalog, product.brand]);

  const brandHref = brand ? catalogHref({ ...EMPTY_FILTERS, brands: [brand.code] }) : ROUTES.search(product.brand);
  const category = path[path.length - 1];
  const categoryHref = category ? ROUTES.category(category.slug) : ROUTES.catalog;

  return (
    <Container className="py-6 sm:py-8">
      <Breadcrumbs
        items={[
          { label: 'Catálogo', to: ROUTES.catalog },
          ...path.map((crumb) => ({ label: crumb.name, to: ROUTES.category(crumb.slug) })),
          { label: product.shortName },
        ]}
      />

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)] lg:gap-x-10 xl:grid-cols-[minmax(0,28rem)_minmax(0,1fr)]">
        <header className="animate-fade-up lg:col-start-2 lg:row-start-1">
          <div className="flex flex-wrap items-center gap-1.5">
            <ProductBadges product={product} variant="soft" max={4} />
            {product.serialized && (
              <Badge tone="neutral" variant="soft" size="sm" icon={<ScanBarcode />}>
                Con número de serie
              </Badge>
            )}
          </div>
          <p className="mt-3 text-sm">
            <Link
              to={brandHref}
              className="rounded-sm font-semibold uppercase tracking-wide text-accent transition-colors duration-200 hover:text-accent-hover"
              aria-label={`Ver todos los productos de ${product.brand}`}
            >
              {product.brand}
            </Link>
            <span className="text-text-faint"> · {product.categoryName}</span>
          </p>
          <h1 className="mt-2 font-display text-2xl font-semibold tracking-tight text-text sm:text-3xl">{product.name}</h1>
          <p className="mt-2 text-sm text-text-faint">
            SKU <span className="font-mono text-text-muted">{product.sku}</span>
          </p>
        </header>

        <div className="lg:col-start-1 lg:row-span-2 lg:row-start-1 lg:self-start">
          <ProductGallery product={product} />
        </div>

        <div className="lg:col-start-2 lg:row-start-2">
          <PurchaseBox product={product} categoryHref={categoryHref} />
        </div>
      </div>

      <div className="mt-10 lg:mt-14">
        <ProductTabs product={product} />
      </div>

      <RelatedProducts className="mt-14" products={related} />
    </Container>
  );
}
