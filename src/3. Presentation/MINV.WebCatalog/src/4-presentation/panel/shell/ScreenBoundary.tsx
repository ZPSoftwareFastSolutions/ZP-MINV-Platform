// Límite de errores de una pantalla del panel: si la pantalla de un módulo falla al dibujarse (o no se pudo descargar
// su fragmento), el menú y la barra superior siguen funcionando y se ofrece reintentar o recargar. El esqueleto le pone
// una `key` por pantalla: al navegar a otra, empieza limpio.

import { RotateCcw, TriangleAlert } from 'lucide-react';
import { Component, type ReactNode } from 'react';
import { Button } from '@/4-presentation/components/ui/Button';

interface ScreenBoundaryProps {
  children: ReactNode;
  /** Qué no se pudo mostrar («esta pantalla», «esta estadística»). */
  what?: string;
  /** Versión compacta (dentro de una tarjeta del tablero). */
  compact?: boolean;
}

interface ScreenBoundaryState {
  failed: boolean;
}

export class ScreenBoundary extends Component<ScreenBoundaryProps, ScreenBoundaryState> {
  state: ScreenBoundaryState = { failed: false };

  static getDerivedStateFromError(): ScreenBoundaryState {
    return { failed: true };
  }

  render() {
    if (!this.state.failed) return this.props.children;
    const what = this.props.what ?? 'esta pantalla';
    return (
      <div
        role="alert"
        className={
          this.props.compact
            ? 'flex flex-col items-start gap-2 text-sm'
            : 'mx-auto flex max-w-xl flex-col items-center gap-3 rounded-card border border-danger/40 bg-danger-soft/60 px-6 py-10 text-center'
        }
        data-testid="pantalla-con-error"
      >
        {!this.props.compact && (
          <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-danger-soft text-danger-text">
            <TriangleAlert className="size-7" />
          </span>
        )}
        <p className="font-display text-lg font-semibold text-text">No se pudo mostrar {what}</p>
        <p className="text-sm text-text-muted">Puede ser una falla de la conexión. Reintente; si sigue igual, recargue la página.</p>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" leftIcon={<RotateCcw />} onClick={() => this.setState({ failed: false })}>
            Reintentar
          </Button>
          <Button variant="ghost" onClick={() => window.location.reload()}>
            Recargar la página
          </Button>
        </div>
      </div>
    );
  }
}
