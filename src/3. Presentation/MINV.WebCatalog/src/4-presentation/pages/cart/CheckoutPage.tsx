// RESERVA DEL CARRITO (`/reservar`) · componente PROVISIONAL del paquete W1.
//
// El paquete W2 reemplaza el contenido de este archivo por la página de reserva real (datos del cliente, documento
// para la factura y días para recoger), conservando el nombre del archivo y la exportación `CheckoutPage`.

import { ClipboardList } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { EmptyState } from '@/4-presentation/components/ui/EmptyState';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';

export function CheckoutPage() {
  useDocumentMeta({ title: 'Reservar' });
  return (
    <Container className="py-8 lg:py-10">
      <h1 className="text-3xl sm:text-4xl">Reservar</h1>
      <EmptyState
        className="mt-8"
        icon={<ClipboardList />}
        title="La reserva del carrito está en construcción"
        description="Muy pronto vas a poder reservar tu carrito con tus datos. Mientras tanto, podés reservar tu armado desde «Armá tu PC»."
      >
        <Button to={ROUTES.builder} variant="brand">
          Armá tu PC
        </Button>
        <Button to={ROUTES.cart} variant="outline">
          Volver al carrito
        </Button>
      </EmptyState>
    </Container>
  );
}
