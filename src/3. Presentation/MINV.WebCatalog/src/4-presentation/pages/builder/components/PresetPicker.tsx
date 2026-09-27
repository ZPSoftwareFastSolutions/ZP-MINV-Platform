// Selector modal de armados sugeridos: los seis armados con nivel, piezas clave y total; cargar uno reemplaza el armado.

import type { PresetDetail } from '@/2-application';
import { BuilderDialog } from './BuilderDialog';
import { PresetCard } from './PresetCard';

export interface PresetPickerProps {
  open: boolean;
  onClose: () => void;
  presets: readonly PresetDetail[];
  currentCount: number;
  onLoad: (id: string) => void;
}

export function PresetPicker({ open, onClose, presets, currentCount, onLoad }: PresetPickerProps) {
  return (
    <BuilderDialog
      open={open}
      onClose={onClose}
      size="lg"
      title="Empezar desde un armado sugerido"
      description={
        currentCount > 0
          ? 'Al cargar un armado se reemplazan las piezas que ya elegiste; después podés cambiar lo que quieras.'
          : 'Elegí un punto de partida y después cambiá las piezas que quieras.'
      }
    >
      <ul className="space-y-3" aria-label="Armados sugeridos">
        {presets.map((detail) => (
          <PresetCard
            key={detail.preset.id}
            detail={detail}
            layout="row"
            currentCount={currentCount}
            onLoad={(id) => {
              onLoad(id);
              onClose();
            }}
          />
        ))}
      </ul>
    </BuilderDialog>
  );
}
