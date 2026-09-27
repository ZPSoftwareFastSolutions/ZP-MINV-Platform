import { useEffect, useRef } from 'react';
import { Outlet, ScrollRestoration, useLocation } from 'react-router-dom';
import { BuildDrawer } from './BuildDrawer';
import { Footer } from './Footer';
import { Header } from './Header';
import { TopBar } from './TopBar';

/**
 * Lleva la vista al ancla de la URL (`/arma-tu-pc#armados`) cuando el enlace se abre directamente: en la carga inicial
 * el navegador no encuentra el elemento (React lo dibuja después). Al navegar dentro del sitio ya lo hace
 * ScrollRestoration, así que solo actúa una vez, en el primer render, y sin animación.
 */
function ScrollToHashOnLoad() {
  const { hash } = useLocation();
  const done = useRef(false);
  useEffect(() => {
    if (done.current) return;
    done.current = true;
    if (!hash) return;
    const target = document.getElementById(decodeURIComponent(hash.slice(1)));
    target?.scrollIntoView({ behavior: 'instant', block: 'start' });
  }, [hash]);
  return null;
}

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
      <ScrollToHashOnLoad />
    </div>
  );
}
