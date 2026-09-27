// Galería de la ficha: la ilustración grande sobre la loseta `bg-tile` (mismo fondo que la imagen) con un zoom suave al
// pasar el mouse. El catálogo trae una ilustración por producto, así que no hay miniaturas.

import { isOnSale } from '@/1-domain/catalog/money';
import { savingLabel } from '@/4-presentation/i18n/priceLabels';
import type { Product } from '@/1-domain/catalog/types';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { ProductImage } from '@/4-presentation/components/ui/ProductImage';

export function ProductGallery({ product }: { product: Product }) {
  return (
    <figure className="group relative">
      <ProductImage
        src={product.image}
        alt={product.name}
        priority
        padding="lg"
        className="w-full shadow-card"
        imgClassName="transition-transform duration-500 ease-out group-hover:scale-105 motion-reduce:transition-none motion-reduce:group-hover:scale-100"
      />
      {isOnSale(product.price, product.listPrice) && (
        <Badge tone="oferta" variant="solid" className="absolute top-4 left-4 shadow-card">
          {savingLabel(product.price, product.listPrice)}
        </Badge>
      )}
      <figcaption className="mt-2 text-center text-xs text-text-faint">Ilustración referencial del producto.</figcaption>
    </figure>
  );
}
