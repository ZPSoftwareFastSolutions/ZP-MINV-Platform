// Estructura del panel (esqueleto): menú lateral por secciones (desde 1024 px; en el teléfono y la tableta, un cajón que
// se abre con el botón de menú), barra superior (migas, sucursal activa y usuario) y el contenido, con los márgenes
// laterales (las pantallas no los ponen: `Page` no agrega márgenes).
//
// Al cambiar de pantalla: la sección de la pantalla abierta se despliega, la vista vuelve arriba y el foco pasa al
// contenido (lectores de pantalla y teclado empiezan por la pantalla nueva). En desarrollo, si algún módulo no se pudo
// cargar, se avisa arriba con el motivo.

import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Drawer } from '@/4-presentation/components/ui/Drawer';
import { useSession } from '@/4-presentation/hooks/useSession';
import { locate, usePanelRegistry } from '../registry';
import { PanelBrand } from './PanelBrand';
import { PanelTopBar } from './PanelTopBar';
import { SideNav } from './SideNav';

const SHOWCASE_PATH = '/panel/_componentes';

export function PanelLayout() {
  const registry = usePanelRegistry();
  const { session } = useSession();
  const { pathname } = useLocation();
  const [menuOpen, setMenuOpen] = useState(false);
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => new Set());
  const mainRef = useRef<HTMLElement>(null);
  const firstPath = useRef(pathname);

  const openMenu = useCallback(() => setMenuOpen(true), []);
  const closeMenu = useCallback(() => setMenuOpen(false), []);
  const toggleSection = useCallback((key: string) => {
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);

  // La sección de la pantalla abierta siempre queda desplegada al llegar a ella.
  const [lastPath, setLastPath] = useState(pathname);
  if (lastPath !== pathname) {
    setLastPath(pathname);
    const section = locate(registry, pathname)?.module.section;
    if (section && collapsed.has(section)) {
      const next = new Set(collapsed);
      next.delete(section);
      setCollapsed(next);
    }
  }

  // Pantalla nueva: arriba de todo y el foco en el contenido (no en la primera carga).
  useEffect(() => {
    if (pathname === firstPath.current) return;
    firstPath.current = '';
    document.documentElement.scrollTop = 0;
    mainRef.current?.focus({ preventScroll: true });
  }, [pathname]);

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[17rem_minmax(0,1fr)]" data-testid="panel-esqueleto">
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>

      <div className="hidden border-r border-border bg-surface/40 lg:sticky lg:top-0 lg:flex lg:h-dvh lg:flex-col">
        <div className="flex h-16 shrink-0 items-center border-b border-border px-5">
          <PanelBrand />
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-3 py-4">
          <SideNav collapsed={collapsed} onToggleSection={toggleSection} />
        </div>
        <SidebarFooter company={session?.company} />
      </div>

      <div className="flex min-w-0 flex-col">
        <PanelTopBar menuOpen={menuOpen} onOpenMenu={openMenu} />
        <main id="contenido" ref={mainRef} tabIndex={-1} className="w-full min-w-0 flex-1 px-4 py-6 outline-none sm:px-6 lg:px-8">
          <div className="mx-auto w-full max-w-[96rem] space-y-4">
            {import.meta.env.DEV && registry.problems.length > 0 && (
              <Alert tone="warning" title="Hay módulos del panel que no se cargaron (aviso de desarrollo)">
                <ul className="list-disc space-y-1 pl-5">
                  {registry.problems.map((problem) => (
                    <li key={problem}>{problem}</li>
                  ))}
                </ul>
              </Alert>
            )}
            <Outlet />
          </div>
        </main>
      </div>

      <Drawer open={menuOpen} onClose={closeMenu} title="Menú del panel" side="left" size="sm" closeLabel="Cerrar el menú">
        <SideNav collapsed={collapsed} onToggleSection={toggleSection} />
      </Drawer>
    </div>
  );
}

function SidebarFooter({ company }: { company?: string }) {
  return (
    <div className="shrink-0 space-y-1 border-t border-border px-5 py-3 text-xs text-text-faint">
      {company && <p className="truncate">{company}</p>}
      {import.meta.env.DEV && (
        <Link to={SHOWCASE_PATH} className="inline-flex min-h-11 items-center font-medium text-accent-hover underline underline-offset-4 hover:text-accent">
          Muestra de componentes (desarrollo)
        </Link>
      )}
    </div>
  );
}
