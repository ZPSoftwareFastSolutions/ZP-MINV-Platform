import { Cpu, Menu, PcCase, Search, ShoppingCart, TicketCheck, X } from 'lucide-react';
import { useId, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { cartCountLabel } from '@/4-presentation/components/cart/cartText';
import { Button } from '@/4-presentation/components/ui/Button';
import { Container } from '@/4-presentation/components/ui/Container';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useCartState } from '@/4-presentation/hooks/useCart';
import { pluralize } from '@/shared/format';
import { CategoryNav } from './CategoryNav';
import { Logo } from './Logo';
import { MobileMenu } from './MobileMenu';
import { SearchBox } from './SearchBox';
import { UserMenu } from './UserMenu';

/**
 * Cabecera fija: logotipo, buscador, «Armá tu PC» con contador, «Mi armado» (cajón), el carrito con sus unidades (V7, en
 * escritorio y en móvil), «Mi reserva», el acceso («Ingresar» o el menú de la sesión, V7) y la navegación por categorías.
 *
 * El carrito se ve SIEMPRE. Para que entre sin desplazamiento horizontal, el botón «Armá tu PC» aparece desde 1024 px
 * (antes, desde 640 px) y, por debajo de 480 px, «Mi armado» aparece solo con un armado en curso. El armador sigue a
 * un toque en el menú (que existe justamente por debajo de 1024 px).
 */
export function Header() {
  const { count, openDrawer } = useBuilder();
  const { count: cartCount } = useCartState();
  const [menuOpen, setMenuOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const mobileSearchId = useId();

  return (
    <header className="sticky top-0 z-30 border-b border-border bg-bg/85 backdrop-blur-md">
      <Container className="flex h-16 items-center gap-2 sm:gap-4">
        <IconButton label="Abrir menú" icon={<Menu />} className="lg:hidden" aria-expanded={menuOpen} onClick={() => setMenuOpen(true)} />
        <Logo compactOnNarrow />
        <SearchBox className="mx-auto max-w-2xl flex-1 max-md:hidden" />
        <div className="ml-auto flex items-center gap-0.5 min-[30rem]:gap-1 sm:gap-2">
          <IconButton
            label={searchOpen ? 'Cerrar búsqueda' : 'Abrir búsqueda'}
            icon={searchOpen ? <X /> : <Search />}
            className="md:hidden"
            aria-expanded={searchOpen}
            aria-controls={mobileSearchId}
            onClick={() => setSearchOpen((current) => !current)}
          />
          {/* Desde 1024 px: en pantallas más angostas el carrito ocupa su lugar y «Armá tu PC» está en el menú. */}
          <Button to={ROUTES.builder} variant="brand" leftIcon={<Cpu />} className="max-lg:hidden">
            Armá tu PC
            {count > 0 && (
              <span className="ml-1 rounded-full bg-white/20 px-1.5 font-display text-xs font-bold">
                {count}
                <span className="sr-only">{count === 1 ? ' pieza en tu armado' : ' piezas en tu armado'}</span>
              </span>
            )}
          </Button>
          {/* En pantallas angostas (menos de 480 px) «Mi armado» aparece solo cuando hay un armado en curso: vacío no
              aporta nada y el lugar es del carrito; a «Armá tu PC» se llega desde el menú. */}
          <IconButton
            label={count > 0 ? `Mi armado, ${pluralize(count, 'pieza', 'piezas')}` : 'Mi armado'}
            icon={<PcCase />}
            variant="subtle"
            badge={count}
            className={count > 0 ? undefined : 'max-[30rem]:hidden'}
            onClick={openDrawer}
          />
          <IconButton
            to={ROUTES.cart}
            label={cartCount > 0 ? `Carrito, ${cartCountLabel(cartCount)}` : 'Carrito'}
            icon={<ShoppingCart />}
            variant="subtle"
            badge={cartCount}
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
