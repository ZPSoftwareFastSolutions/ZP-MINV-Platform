import { Compass, Cpu, Home, Search } from 'lucide-react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';

export function NotFoundPage() {
  useDocumentTitle('Página no encontrada');
  return (
    <Container className="flex min-h-[60vh] flex-col items-center justify-center py-16 text-center animate-fade-up">
      <span aria-hidden="true" className="flex size-20 items-center justify-center rounded-3xl bg-surface-2 text-accent shadow-glow-accent">
        <Compass className="size-10" />
      </span>
      <p className="mt-8 font-display text-7xl font-bold leading-none text-gradient-brand sm:text-8xl">404</p>
      <h1 className="mt-4 font-display text-3xl font-semibold text-text sm:text-4xl">Esta página no existe</h1>
      <p className="mt-3 max-w-md text-base text-text-muted">
        Puede que el enlace esté mal escrito o que el producto ya no forme parte del catálogo. Te llevamos de vuelta.
      </p>
      <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
        <Button to={ROUTES.home} variant="brand" leftIcon={<Home />}>
          Volver al inicio
        </Button>
        <Button to={ROUTES.catalog} variant="outline" leftIcon={<Search />}>
          Ver el catálogo
        </Button>
        <Button to={ROUTES.builder} variant="ghost" leftIcon={<Cpu />}>
          Armá tu PC
        </Button>
      </div>
    </Container>
  );
}
