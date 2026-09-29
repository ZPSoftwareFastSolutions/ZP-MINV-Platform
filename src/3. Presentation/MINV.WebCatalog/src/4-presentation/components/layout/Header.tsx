import { Cpu, Menu, PcCase, Search, TicketCheck, X } from 'lucide-react';
import { useId, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { pluralize } from '@/shared/format';
import { CategoryNav } from './CategoryNav';
import { Logo } from './Logo';
import { MobileMenu } from './MobileMenu';
import { SearchBox } from './SearchBox';
import { UserMenu } from './UserMenu';

/**
 * Cabecera fija: logotipo, buscador, «Armá tu PC» con contador, «Mi armado» (cajón), «Mi reserva», el acceso («Ingresar»
 * o el menú de la sesión, V7) y la navegación por categorías.
 */
export function Header() {
  const { count, openDrawer } = useBuilder();
  const [menuOpen, setMenuOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const mobileSearchId = useId();

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-bg/85 backdrop-blur-md">
      <Container className="flex h-16 items-center gap-2 sm:gap-4">
        <IconButton label="Abrir menú" icon={<Menu />} className="lg:hidden" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)} />
        <Logo compactOnNarrow />
        <SearchBox className="mx-auto max-w-2xl flex-1 max-md:hidden" />
        <div className="ml-auto flex items-center gap-1 sm:gap-2">
          <IconButton
            label={searchOpen ? 'Cerrar búsqueda' : 'Abrir búsqueda'}
            icon={searchOpen ? <X /> : <Search />}
            className="md:hidden"
            aria-expanded={searchOpen}
            aria-controls={mobileSearchId}
            onClick={() => setSearchOpen((current) => !current)}
          />
          <Button to={ROUTES.builder} variant="brand" leftIcon={<Cpu />} className="max-sm:hidden">
            Armá tu PC
            {count > 0 && (
              <span className="ml-1 rounded-full bg-white/20 px-1.5 font-display text-xs font-bold">
                {count}
                <span className="sr-only">{count === 1 ? ' pieza en tu armado' : ' piezas en tu armado'}</span>
              </span>
            )}
          </Button>
          <IconButton
            label={count > 0 ? `Mi armado, ${pluralize(count, 'pieza', 'piezas')}` : 'Mi armado'}
            icon={<PcCase />}
            variant="subtle"
            badge={count}
            onClick={openDrawer}
          />
          <Button to={ROUTES.reservations} variant="ghost" leftIcon={<TicketCheck />} className="max-lg:hidden" aria-label="Consultar mi reserva">
            Mi reserva
          </Button>
          <UserMenu />
        </div>
      </Container>
      {searchOpen && (
        <div id={mobileSearchId} className="border-t border-border/60 px-4 py-2 md:hidden">
          <SearchBox autoFocus onSubmitted={() => setSearchOpen(false)} />
        </div>
      )}
      <CategoryNav />
      <MobileMenu open={menuOpen} onClose={() => setMenuOpen(false)} />
    </header>
  );
}
