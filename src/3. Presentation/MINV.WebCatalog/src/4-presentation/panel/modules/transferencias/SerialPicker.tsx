// Módulo «Transferencias» · las series (o IMEI) que viajan en una línea de un producto serializado (regla T-02), como el
// escritorio: se marcan de las disponibles en el almacén de origen (`GetAvailableSerialsQuery`) o, si no se pueden
// consultar (sin permiso o sin respuesta), se escanean o pegan una por renglón. El servidor vuelve a validarlas al
// solicitar y al despachar.

import { useState } from 'react';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Checkbox, FieldGroup, TextArea } from '@/4-presentation/panel/kit';
import { formatNumber } from '@/4-presentation/panel/lib';
import { parseSerials, serialWord, type TransferDraftLine } from './transfers';

export interface SerialPickerProps {
  line: TransferDraftLine;
  /** Almacén de origen (de donde salen las unidades). */
  warehouseCode: string;
  onChange: (serials: string[]) => void;
  error?: string;
}

export function SerialPicker({ line, warehouseCode, onChange, error }: SerialPickerProps) {
  const { canRun } = usePermissions();
  const canList = canRun('GetAvailableSerialsQuery');
  const available = useRpcQuery('GetAvailableSerialsQuery', { sku: line.sku, warehouseCode: warehouseCode || null }, { enabled: canList && warehouseCode !== '' });
  const [text, setText] = useState(line.serials.join('\n'));
  const word = serialWord(line.serialKind);
  const title = `${line.serialKind === 'Imei' ? 'IMEI' : 'Series'} de ${line.name}`;
  const expected = line.quantity ?? 0;

  const list = available.data ?? [];
  if (canList && !available.error && (available.data === undefined || list.length > 0)) {
    const toggle = (serial: string, checked: boolean) => onChange(checked ? [...line.serials.filter((item) => item !== serial), serial] : line.serials.filter((item) => item !== serial));
    return (
      <FieldGroup
        label={title}
        hint={`${formatNumber(line.serials.length)} de ${formatNumber(expected)} ${word} marcadas (disponibles en ${warehouseCode}).`}
        error={error}
        bodyClassName="grid grid-cols-1 gap-x-4 sm:grid-cols-2"
      >
        {available.data === undefined ? (
          <p role="status" className="py-2 text-sm text-text-muted">
            Buscando las unidades disponibles…
          </p>
        ) : (
          list.map((row) => (
            <Checkbox
              key={row.serial}
              label={<span className="font-mono">{row.serial}</span>}
              checked={line.serials.includes(row.serial)}
              disabled={!line.serials.includes(row.serial) && line.serials.length >= expected}
              onChange={(checked) => toggle(row.serial, checked)}
            />
          ))
        )}
      </FieldGroup>
    );
  }

  return (
    <TextArea
      label={title}
      value={text}
      onChange={(value) => {
        setText(value);
        onChange(parseSerials(value));
      }}
      rows={Math.min(6, Math.max(2, expected))}
      spellCheck={false}
      autoCapitalize="characters"
      hint={`${formatNumber(line.serials.length)} de ${formatNumber(expected)} ${word}: una por renglón (escanee o pegue).${canList && list.length === 0 && available.data ? ` No hay unidades disponibles en ${warehouseCode}.` : ''}`}
      error={error}
      required
    />
  );
}
