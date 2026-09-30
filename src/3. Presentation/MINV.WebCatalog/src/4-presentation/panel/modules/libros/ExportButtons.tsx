// Módulo «Libros fiscales» · descargar el libro del mes que arma el servidor (`ExportFiscalBookQuery`): CSV con el orden de
// columnas de las plantillas del SIN, o Excel. Es el libro COMPLETO del mes (sin los filtros de la página: para eso está
// «Exportar lo filtrado»).

import { FileSpreadsheet, FileText } from 'lucide-react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, useNotify } from '@/4-presentation/panel/kit';
import type { MonthValue } from './books';
import { downloadServerFile } from './files';

export interface ExportButtonsProps {
  month: MonthValue;
  purchases: boolean;
  disabled?: boolean;
}

export function ExportButtons({ month, purchases, disabled = false }: ExportButtonsProps) {
  const notify = useNotify();
  const exporting = useRpcCommand('ExportFiscalBookQuery', { errorTitle: 'No se pudo exportar el libro' });
  const book = purchases ? 'compras' : 'ventas';

  const download = async (format: 'csv' | 'xlsx') => {
    const outcome = await exporting.run({ year: month.year, month: month.month, purchases, format });
    if (!outcome.ok) return;
    const fileName = downloadServerFile(outcome.result, `libro-${book}-${month.year}-${String(month.month).padStart(2, '0')}.${format}`);
    if (!fileName) {
      notify.error('No se pudo descargar el libro', 'El archivo llegó incompleto. Intente de nuevo.');
      return;
    }
    notify.success(purchases ? 'Libro de compras exportado' : 'Libro de ventas exportado', `Se descargó ${fileName}.`);
  };

  return (
    <>
      <Button variant="outline" leftIcon={<FileText />} loading={exporting.sending} disabled={disabled} onClick={() => void download('csv')}>
        CSV para el SIN
      </Button>
      <Button variant="outline" leftIcon={<FileSpreadsheet />} disabled={disabled || exporting.sending} onClick={() => void download('xlsx')}>
        Excel
      </Button>
    </>
  );
}
