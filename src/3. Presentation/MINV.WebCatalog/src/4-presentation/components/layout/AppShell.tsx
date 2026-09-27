import { Outlet, ScrollRestoration } from 'react-router-dom';
import { BuildDrawer } from './BuildDrawer';
import { Footer } from './Footer';
import { Header } from './Header';
import { TopBar } from './TopBar';

/** Estructura común de todas las páginas: barra superior, cabecera fija, contenido, pie y cajón del armado. */
export function AppShell() {
  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      <TopBar />
      <Header />
      <main id="contenido" tabIndex={-1} className="flex-1 outline-none">
        <Outlet />
      </main>
      <Footer />
      <BuildDrawer />
      <ScrollRestoration />
    </div>
  );
}
