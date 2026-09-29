// CARRITO (`/carrito`) · componente PROVISIONAL del paquete W1.
//
// El paquete W2 reemplaza el contenido de este archivo por el carrito real, conservando el nombre del archivo y la
// exportación `CartPage`: el enrutador ya lo carga con carga diferida dentro de la estructura de la tienda.

import { ShoppingCart } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';

export function CartPage() {
  useDocumentMeta({ title: 'Carrito' });
  return (
    <Container className="py-8 lg:py-10">
      <h1 className="text-3xl sm:text-4xl">Carrito</h1>
      <EmptyState
        className="mt-8"
        icon={<ShoppingCart />}
        title="El carrito está en construcción"
        description="Muy pronto vas a poder reservar cualquier producto desde acá. Mientras tanto, podés reservar tu armado desde «Armá tu PC»."
      >
        <Button to={ROUTES.catalog} variant="brand">
          Ver el catálogo
        </Button>
        <Button to={ROUTES.builder} variant="outline">
          Armá tu PC
        </Button>
      </EmptyState>
    </Container>
  );
}
