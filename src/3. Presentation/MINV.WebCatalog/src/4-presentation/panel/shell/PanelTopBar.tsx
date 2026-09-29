// Barra superior del panel: menú (en el teléfono y la tableta), migas de pan, sucursal activa y el usuario. Por debajo
// de 768 px la sucursal baja a una segunda línea a todo el ancho (sin desplazamiento horizontal a 360 px).

import { Menu } from 'lucide-react';
import { IconButton } from '@/4-presentation/components/ui/IconButton';
import { BranchSwitcher } from './BranchSwitcher';
import { PanelBrand } from './PanelBrand';
import { PanelBreadcrumbs } from './PanelBreadcrumbs';
import { PanelUserMenu } from './PanelUserMenu';

export interface PanelTopBarProps {
  menuOpen: boolean;
  onOpenMenu: () => void;
}

export function PanelTopBar({ menuOpen, onOpenMenu }: PanelTopBarProps) {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-bg/85 backdrop-blur-md">
      <div className="flex min-h-16 flex-wrap items-center gap-x-2 gap-y-1 px-4 py-2 sm:gap-x-3 sm:px-6 lg:px-8">
        <IconButton label="Abrir el menú del panel" icon={<Menu />} className="lg:hidden" aria-expanded={menuOpen} onClick={onOpenMenu} />
        <PanelBrand compact className="lg:hidden" />
        <PanelBreadcrumbs className="flex-1" />
        <BranchSwitcher className="max-md:order-last max-md:basis-full" />
        <PanelUserMenu />
      </div>
    </header>
  );
}
