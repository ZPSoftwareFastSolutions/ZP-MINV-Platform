// Módulo «Reportes» · piezas comunes de las pestañas: la barra de acciones (Actualizar, Exportar CSV, Imprimir) y el
// bloque «Totales del período» (texto: lo que manda el servidor, con formato; los gráficos van aparte, plegados).

import { Download, Printer, RefreshCw } from 'lucide-react';
import type { ReactNode } from 'react';
import { Button, DetailList, Section, Skeleton, Toolbar } from '@/4-presentation/panel/kit';

export interface ReportToolbarProps {
  /** Resumen a la izquierda («12 de 40 filas»). */
  summary?: ReactNode;
  onReload: () => void;
  reloading: boolean;
  /** ¿Hay filas para exportar e imprimir? */
  hasRows: boolean;
  onExport: () => void;
  onPrint: () => void;
}

export function ReportToolbar({ summary, onReload, reloading, hasRows, onExport, onPrint }: ReportToolbarProps) {
  return (
    <Toolbar
      label="Acciones del reporte"
      end={
        <>
          <Button variant="outline" leftIcon={<RefreshCw />} loading={reloading} onClick={onReload}>
            Actualizar
          </Button>
          <Button variant="outline" leftIcon={<Download />} disabled={!hasRows} onClick={onExport}>
            Exportar CSV
          </Button>
          <Button variant="outline" leftIcon={<Printer />} disabled={!hasRows} onClick={onPrint}>
            Imprimir
          </Button>
        </>
      }
    >
      {summary && <span className="text-sm text-text-muted">{summary}</span>}
    </Toolbar>
  );
}

export interface ReportTotalsProps {
  title?: string;
  description?: ReactNode;
  items: readonly { label: string; value: string }[] | null;
  testId: string;
}

export function ReportTotals({ title = 'Totales del período', description, items, testId }: ReportTotalsProps) {
  return (
    <Section title={title} description={description}>
      {items ? (
        <div data-testid={testId}>
          <DetailList items={items} className="lg:grid-cols-3" />
        </div>
      ) : (
        <div aria-hidden="true" className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </div>
      )}
    </Section>
  );
}
