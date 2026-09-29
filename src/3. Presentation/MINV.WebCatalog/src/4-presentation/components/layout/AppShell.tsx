import { useEffect, useRef } from 'react';
import { Outlet, ScrollRestoration, useLocation } from 'react-router-dom';
import { Container } from '@/4-presentation/components/ui/Container';
import { useOptionalServices } from '@/4-presentation/hooks/useServices';
import { BuildDrawer } from './BuildDrawer';
import { Footer } from './Footer';
import { Header } from './Header';
import { Logo } from './Logo';
import { TopBar } from './TopBar';
import { UserMenu } from './UserMenu';

/**
 * Lleva la vista al ancla de la URL (`/arma-tu-pc#armados`) cuando el enlace se abre directamente: en la carga inicial
 * el navegador no encuentra el elemento (React lo dibuja después). Al navegar dentro del sitio ya lo hace
 * ScrollRestoration, así que solo actúa una vez, en el primer render, y sin animación. Se monta recién con el catálogo
 * listo (V7 · W3b): antes, la página de la tienda todavía no está dibujada.
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

/**
 * V7 · W3b: cabecera mientras el catálogo no está (cargando o caído): el logotipo y el acceso («Ingresar» o el menú de la
 * sesión). La barra superior, el buscador, las categorías y el pie necesitan el catálogo; esta no.
 */
function LiteHeader() {
  return (
    <header className="border-b border-border bg-bg/85" data-testid="cabecera-liviana">
      <Container className="flex h-16 items-center justify-between gap-3">
        <Logo compactOnNarrow />
        <UserMenu />
      </Container>
    </header>
  );
}

/**
 * Estructura común de todas las páginas: barra superior, cabecera fija, contenido, pie y cajón del armado.
 *
 * V7 · W3b: se dibuja también sin el catálogo de la tienda (con la cabecera liviana). Las páginas de la tienda esperan el
 * catálogo con `CatalogGate` (tabla de rutas); las de la sesión («Ingresar», «Registrarse», «Cambiar contraseña», «Mi
 * cuenta») no lo necesitan. El `<main>` ocupa SIEMPRE el mismo lugar del árbol: cuando llega el catálogo cambia la
 * cabecera, pero la página abierta (por ejemplo el formulario de ingreso a medio escribir) no se vuelve a montar.
 */
export function AppShell() {
  const ready = useOptionalServices() !== null;
  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      {ready ? (
        <>
          <TopBar />
          <Header />
        </>
      ) : (
        <LiteHeader />
      )}
      <main id="contenido" tabIndex={-1} className="flex-1 outline-none">
        <Outlet />
      </main>
      {ready ? <Footer /> : null}
      {ready ? <BuildDrawer /> : null}
      <ScrollRestoration />
      {ready ? <ScrollToHashOnLoad /> : null}
    </div>
  );
}
