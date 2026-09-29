// Módulo «Ventas» · las series (o IMEI) de un producto serializado en la devolución (regla T-02): se marcan las unidades
// que devuelve el cliente. Solo se pueden marcar las series de ESTA venta que siguen vendidas (`SearchSerialsQuery` con el
// estado «vendida», como el escritorio); sin permiso para consultar series (o si la consulta falla) se ofrecen todas y el
// servidor valida al registrar. Avisa a la pantalla qué series quedaron disponibles (para «Devolver todo»).

import { CheckCheck } from 'lucide-react';
import { useEffect, useMemo } from 'react';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, FieldGroup } from '@/4-presentation/panel/kit';
import type { ReturnDraftLine } from './sales';

/** Series vendidas que se revisan por producto. */
const MAX_SERIALS = 2000;

export interface SerialChoicesProps {
  line: ReturnDraftLine;
  selected: readonly string[];
  onChange: (list: string[]) => void;
  /** Avisa qué series se pueden devolver (null mientras se revisa). */
  onResolved: (sku: string, list: readonly string[] | null) => void;
  error?: string;
}

export function SerialChoices({ line, selected, onChange, onResolved, error }: SerialChoicesProps) {
  const { canRun } = usePermissions();
  const check = canRun('SearchSerialsQuery');
  const sold = useRpcQuery('SearchSerialsQuery', { text: null, status: 'Sold', sku: line.sku, max: MAX_SERIALS }, { enabled: check });

  const returnable = useMemo(() => {
    if (!check || sold.error) return line.serials;
    if (!sold.data) return null;
    const stillSold = new Set(sold.data.map((row) => row.serial));
    return line.serials.filter((serial) => stillSold.has(serial));
  }, [check, sold.error, sold.data, line.serials]);

  useEffect(() => {
    onResolved(line.sku, returnable);
  }, [line.sku, returnable, onResolved]);

  const toggle = (serial: string, checked: boolean) => onChange(checked ? [...selected.filter((item) => item !== serial), serial] : selected.filter((item) => item !== serial));
  const limit = Math.max(0, Math.round(line.available));

  return (
    <FieldGroup label="Series a devolver" hint={`${selected.length} de ${line.serials.length} series marcadas.`} error={error}>
      {returnable === null ? (
        <p role="status" className="py-2 text-sm text-text-muted">
          Revisando qué series siguen vendidas…
        </p>
      ) : (
        <>
          <div className="grid grid-cols-1 gap-x-4 sm:grid-cols-2">
            {line.serials.map((serial) => {
              const allowed = returnable.includes(serial);
              return (
                <Checkbox
                  key={serial}
                  label={<span className="font-mono">{serial}</span>}
                  description={allowed ? undefined : 'Ya no está vendida: se devolvió o está en garantía.'}
                  checked={selected.includes(serial)}
                  disabled={!allowed}
                  onChange={(checked) => toggle(serial, checked)}
                />
              );
            })}
          </div>
          {returnable.length > 1 && (
            <Button variant="ghost" leftIcon={<CheckCheck />} onClick={() => onChange(returnable.slice(0, limit))}>
              Marcar todas
            </Button>
          )}
        </>
      )}
      {sold.error && <p className="mt-1 text-xs text-text-muted">No se pudo revisar qué series siguen vendidas: el servidor lo valida al registrar.</p>}
    </FieldGroup>
  );
}
