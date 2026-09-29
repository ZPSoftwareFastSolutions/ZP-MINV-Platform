// Marco de las pantallas de acceso (ingresar, registrarse, cambiar contraseña): una tarjeta angosta y centrada, con el
// título de la página. A 360 px ocupa todo el ancho sin desplazamiento horizontal.

import type { ReactNode } from 'react';
import { Card } from '@/4-presentation/components/ui/Card';
import { Container } from '@/4-presentation/components/ui/Container';

export interface AuthLayoutProps {
  /** Texto corto sobre el título («Tu cuenta»). */
  eyebrow: string;
  title: string;
  description?: ReactNode;
  children: ReactNode;
  /** Enlaces debajo de la tarjeta («¿No tenés cuenta? Registrate»). */
  footer?: ReactNode;
}

export function AuthLayout({ eyebrow, title, description, children, footer }: AuthLayoutProps) {
  return (
    <Container className="py-8 lg:py-12">
      <div className="mx-auto w-full max-w-md animate-fade-up">
        <header className="mb-6">
          <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">{eyebrow}</p>
          <h1 className="text-3xl sm:text-4xl">{title}</h1>
          {description && <p className="mt-3 text-base text-text-muted">{description}</p>}
        </header>
        <Card padding="md">{children}</Card>
        {footer && <div className="mt-5 space-y-2 text-center text-sm text-text-muted">{footer}</div>}
      </div>
    </Container>
  );
}

/** Clases de un enlace de texto dentro de las pantallas de acceso (objetivo táctil de 44 px en móvil). */
export const TEXT_LINK = 'inline-flex min-h-11 items-center font-semibold text-accent-hover underline underline-offset-4 transition-colors duration-200 hover:text-accent sm:min-h-0';
