// Límite de errores de UNA estadística del tablero: si una falla al dibujarse (o no se pudo descargar), las demás siguen
// funcionando.

import { RotateCcw } from 'lucide-react';
import { Component, type ReactNode } from 'react';
import { Button } from '@/4-presentation/panel/kit';

interface StatBoundaryState {
  failed: boolean;
}

export class StatBoundary extends Component<{ children: ReactNode }, StatBoundaryState> {
  state: StatBoundaryState = { failed: false };

  static getDerivedStateFromError(): StatBoundaryState {
    return { failed: true };
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <div role="alert" className="flex flex-col items-start gap-2 text-sm">
        <p className="font-medium text-text">No se pudo mostrar esta estadística.</p>
        <p className="text-text-muted">Puede ser una falla de la conexión.</p>
        <Button variant="outline" leftIcon={<RotateCcw />} onClick={() => this.setState({ failed: false })}>
          Reintentar
        </Button>
      </div>
    );
  }
}
