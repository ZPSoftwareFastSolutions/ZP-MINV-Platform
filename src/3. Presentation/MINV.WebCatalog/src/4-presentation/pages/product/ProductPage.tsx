// PLACEHOLDER: la ficha completa del producto la construye el agente de Catálogo+Producto.

import { Plus } from 'lucide-react';
import { useParams } from 'react-router-dom';
import { isBuildable } from '@/1-domain/builder/slots';
import { isAvailable } from '@/1-domain/catalog/stock';
import { ROUTES } from '@/4-presentation/app/routes';
import { ProductBadges } from '@/4-presentation/components/product/ProductBadges';
import { SpecTable } from '@/4-presentation/components/product/SpecTable';
import { StockIndicator } from '@/4-presentation/components/product/StockIndicator';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { PriceTag } from '@/4-presentation/components/ui/PriceTag';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { NotFoundPage } from '../NotFoundPage';

export function ProductPage() {
  const { catalog } = useServices();
  const { add } = useBuilder();
  const { slug = '' } = useParams();
  const product = catalog.getProduct(slug);
  useDocumentTitle(product?.shortName);
  if (!product) return <NotFoundPage />;
  const path = catalog.getCategoryPath(product.category);

  return (
    <Container className="py-8">
      <Breadcrumbs
        items={[
          { label: 'Catálogo', to: ROUTES.catalog },
          ...path.map((category) => ({ label: category.name, to: ROUTES.category(category.slug) })),
          { label: product.shortName },
        ]}
      />
      <div className="mt-6 grid gap-8 lg:grid-cols-2">
        <ProductImage src={product.image} alt={product.name} priority padding="lg" />
        <div>
          <ProductBadges product={product} variant="soft" max={4} />
          <p className="mt-3 text-sm font-semibold uppercase tracking-wide text-text-faint">{product.brand}</p>
          <h1 className="mt-1 font-display text-3xl font-semibold text-text">{product.name}</h1>
          <p className="mt-4 text-base text-text-muted">{product.description}</p>
          <div className="mt-6 flex flex-wrap items-end gap-4">
            <PriceTag price={product.price} listPrice={product.listPrice} size="xl" showTax />
            <StockIndicator product={product} className="pb-2" />
          </div>
          {isBuildable(product) && (
            <Button className="mt-6" size="lg" variant="brand" leftIcon={<Plus />} disabled={!isAvailable(product)} onClick={() => add(product)}>
              {isAvailable(product) ? 'Agregar al armado' : 'Agotado'}
            </Button>
          )}
          <SpecTable className="mt-8" specs={product.specs} caption={`Especificaciones de ${product.shortName}`} />
        </div>
      </div>
    </Container>
  );
}
