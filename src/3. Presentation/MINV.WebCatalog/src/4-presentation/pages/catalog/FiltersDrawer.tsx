// Cajón «Filtros» para móvil y tablet. Los cambios se acumulan en un borrador (con conteos y total en vivo) y se
// aplican con «Ver N productos»: así el cajón no se cierra con cada casilla (el cajón se cierra al navegar).

import { RotateCcw } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Badge } from '@/4-presentation/components/ui/Badge';
import { Button } from '@/4-presentation/components/ui/Button';
import { Drawer } from '@/4-presentation/components/ui/Drawer';
import { useServices } from '@/4-presentation/hooks/useServices';
import { formatNumber } from '@/shared/format';
import { activeFilterCount, catalogQuery, clearedFilters, type CatalogFilters } from './catalogFilters';
import { buildCategoryFilterTree } from './categoryFilterTree';
import { FilterPanel } from './FilterPanel';

export interface FiltersDrawerProps {
  open: boolean;
  onClose: () => void;
  filters: CatalogFilters;
  onApply: (next: CatalogFilters) => void;
}

export function FiltersDrawer({ open, onClose, filters, onApply }: FiltersDrawerProps) {
  const { catalog } = useServices();
  const [draft, setDraft] = useState(filters);
  // Cada vez que se abre, el borrador arranca desde los filtros aplicados (ajuste de estado durante el render).
  const [wasOpen, setWasOpen] = useState(open);
  if (wasOpen !== open) {
    setWasOpen(open);
    if (open) setDraft(filters);
  }

  const preview = useMemo(() => catalog.searchCatalog({ ...catalogQuery(draft, 1), page: 1 }), [catalog, draft]);
  const tree = useMemo(() => buildCategoryFilterTree(catalog, draft), [catalog, draft]);
  const active = activeFilterCount(draft);

  const apply = () => {
    onApply({ ...draft, page: 1 });
    onClose();
  };

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title="Filtros"
      description={`${formatNumber(preview.total)} ${preview.total === 1 ? 'producto coincide' : 'productos coinciden'}`}
      side="left"
      size="sm"
      headerExtra={active > 0 ? <Badge tone="nuevo">{active}</Badge> : undefined}
      footer={
        <div className="flex gap-2">
          <Button variant="ghost" leftIcon={<RotateCcw />} disabled={active === 0 && !draft.q} onClick={() => setDraft(clearedFilters(draft))}>
            Limpiar
          </Button>
          <Button variant="brand" fullWidth onClick={apply}>
            Ver {formatNumber(preview.total)} {preview.total === 1 ? 'producto' : 'productos'}
          </Button>
        </div>
      }
    >
      <FilterPanel filters={draft} facets={preview.facets} tree={tree} onChange={(patch) => setDraft((current) => ({ ...current, ...patch, page: 1 }))} />
    </Drawer>
  );
}
