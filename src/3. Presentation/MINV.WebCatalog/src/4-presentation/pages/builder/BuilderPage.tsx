// Página «Armá tu PC»: pasos numerados por ranura (acordeón), resumen fijo a la derecha en escritorio y hoja inferior
// en móvil, armados sugeridos (#armados), el diálogo «Reservar armado» (V6) y la vista imprimible.
// Estado del armado: exclusivamente useBuilder (React en memoria). El estado de la página (paso abierto, pasos
// omitidos, diálogos, última reserva aceptada) también vive en memoria: lo único que persiste es la RESERVA en la tienda.

import './builder.css';
import { ListChecks } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import type { Reservation, StockShortage } from '@/1-domain/storefront/types';
import { Container } from '@/4-presentation/components/ui/Container';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { useToast } from '@/4-presentation/hooks/useToast';
import { pluralize } from '@/shared/format';
import { initialOpenSlot, nextOpenSlot, stepStatus } from './builderSteps';
import { BuilderDialog } from './components/BuilderDialog';
import { BuilderHeader } from './components/BuilderHeader';
import { BuildSummaryPanel, SummaryTotals } from './components/BuildSummaryPanel';
import { MobileSummaryBar } from './components/MobileSummaryBar';
import { PresetGallery } from './components/PresetGallery';
import { PresetPicker } from './components/PresetPicker';
import { PrintSummary } from './components/PrintSummary';
import { ReserveDialog } from './components/ReserveDialog';
import { SlotStep } from './components/SlotStep';

interface FocusRequest {
  /** Cabecera de un paso, o la cabecera de la página («top») tras cargar un armado desde la galería. */
  target: SlotKey | 'top';
  /** Espera (ms) antes de enfocar: al cerrar la hoja de móvil, el diálogo devuelve el foco a su disparador primero. */
  delay: number;
  tick: number;
}

const PRINT_ATTRIBUTE = 'data-print';
/** Mientras el armador está montado reserva el alto de la barra inferior de móvil (builder.css). */
const BAR_ATTRIBUTE = 'data-builder-bar';

export function BuilderPage() {
  useDocumentTitle('Armá tu PC');
  const { catalog, refresh } = useServices();
  const store = useStore();
  const toast = useToast();
  const builder = useBuilder();
  const { lines, summary } = builder;

  const slots = catalog.getBuildSlots();
  const presets = useMemo(() => catalog.getPresets(), [catalog]);

  const [expanded, setExpanded] = useState<SlotKey | null>(() => initialOpenSlot(lines));
  const [skipped, setSkipped] = useState<ReadonlySet<SlotKey>>(() => new Set());
  const [pickerOpen, setPickerOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [reserveOpen, setReserveOpen] = useState(false);
  const [lastReservation, setLastReservation] = useState<Reservation | null>(null);
  const [issuedAt, setIssuedAt] = useState(() => new Date());
  const [announcement, setAnnouncement] = useState('');
  const [focusRequest, setFocusRequest] = useState<FocusRequest | null>(null);

  const headerRefs = useRef(new Map<SlotKey, HTMLButtonElement>());
  const topRef = useRef<HTMLDivElement>(null);
  const barButtonRef = useRef<HTMLButtonElement>(null);

  // Mueve el foco (y la vista) a la cabecera del paso pedido después de que React lo dibuje.
  useEffect(() => {
    if (!focusRequest) return;
    const timer = setTimeout(() => {
      const element = focusRequest.target === 'top' ? topRef.current : headerRefs.current.get(focusRequest.target);
      if (!element) return;
      element.focus({ preventScroll: true });
      (element.closest('li') ?? element).scrollIntoView?.({ block: 'start' });
    }, focusRequest.delay);
    return () => clearTimeout(timer);
  }, [focusRequest]);

  // Reserva el alto de la barra de móvil mientras la página existe; al desmontar (navegación) tampoco debe quedar el
  // modo de impresión activo.
  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute(BAR_ATTRIBUTE, '');
    return () => {
      root.removeAttribute(BAR_ATTRIBUTE);
      root.removeAttribute(PRINT_ATTRIBUTE);
    };
  }, []);

  const requestFocus = (target: SlotKey | 'top', delay = 0) => setFocusRequest((current) => ({ target, delay, tick: (current?.tick ?? 0) + 1 }));
  const focusBarButton = () => barButtonRef.current;

  const goToStep = (key: SlotKey) => {
    setExpanded(key);
    const fromSheet = sheetOpen;
    setSheetOpen(false);
    requestFocus(key, fromSheet ? 300 : 0);
  };

  const advanceFrom = (slot: BuildSlot, predictedLines: typeof lines, nextSkipped: ReadonlySet<SlotKey>, prefix: string) => {
    const next = nextOpenSlot(slot.key, predictedLines, nextSkipped);
    setExpanded(next?.key ?? null);
    requestFocus(next?.key ?? slot.key);
    setAnnouncement(next ? `${prefix} Siguiente paso: ${next.order}, ${next.label}.` : `${prefix} No quedan pasos pendientes.`);
  };

  const chooseIn = (slot: BuildSlot, product: Product) => {
    if (!builder.add(product, slot.key)) return;
    if (skipped.has(slot.key)) {
      const nextSkipped = new Set(skipped);
      nextSkipped.delete(slot.key);
      setSkipped(nextSkipped);
    }
    if (slot.multiple) {
      setAnnouncement(`${product.shortName} agregado a ${slot.label}.`);
      return;
    }
    const predicted = [...lines.filter((line) => line.slot !== slot.key), { slot: slot.key, product, quantity: 1 }];
    advanceFrom(slot, predicted, skipped, `${slot.label}: ${product.shortName}.`);
  };

  const skip = (slot: BuildSlot) => {
    const nextSkipped = new Set(skipped).add(slot.key);
    setSkipped(nextSkipped);
    advanceFrom(slot, lines, nextSkipped, `Omitiste ${slot.label}.`);
  };

  const unskip = (slot: BuildSlot) => {
    const nextSkipped = new Set(skipped);
    nextSkipped.delete(slot.key);
    setSkipped(nextSkipped);
    setAnnouncement(`${slot.label} vuelve a estar pendiente.`);
  };

  const clear = () => {
    builder.clear();
    setSkipped(new Set());
    setExpanded(slots[0]?.key ?? null);
    setAnnouncement('Armado vaciado.');
  };

  const loadPreset = (id: string, scrollToTop = false) => {
    if (!builder.loadPreset(id)) return;
    setSkipped(new Set());
    setExpanded(null);
    setAnnouncement('Armado sugerido cargado. Revisá cada paso y cambiá lo que quieras.');
    // Desde la galería (al pie de la página) el foco y la vista suben a la cabecera con el progreso y el total.
    if (scrollToTop) requestFocus('top');
  };

  const openReserve = () => {
    setIssuedAt(new Date());
    setSheetOpen(false);
    // Los avisos («Armado cargado») no deben tapar el formulario de la reserva.
    toast.dismissAll();
    setReserveOpen(true);
  };

  /** La tienda aceptó la reserva: el armado se vacía (ya vive en la base) y el catálogo se refresca con lo reservado. */
  const reserved = (reservation: Reservation) => {
    setLastReservation(reservation);
    builder.clear();
    setSkipped(new Set());
    setExpanded(slots[0]?.key ?? null);
    setAnnouncement(`Reserva ${reservation.number} registrada. Las piezas quedaron guardadas a tu nombre.`);
    void refresh();
  };

  /** 409: baja cada pieza afectada a lo disponible (o la quita si no queda nada) y pide la instantánea fresca. */
  const adjustToAvailable = (shortages: readonly StockShortage[]) => {
    for (const shortage of shortages) {
      const available = Math.trunc(shortage.available);
      if (available <= 0) builder.remove(shortage.sku);
      else builder.setQuantity(shortage.sku, available);
    }
    setAnnouncement(`Armado ajustado a lo disponible en ${pluralize(shortages.length, 'pieza', 'piezas')}.`);
    void refresh();
  };

  const print = () => {
    if (typeof window.print !== 'function') return;
    const root = document.documentElement;
    root.setAttribute(PRINT_ATTRIBUTE, 'armado');
    const restore = () => root.removeAttribute(PRINT_ATTRIBUTE);
    window.addEventListener('afterprint', restore, { once: true });
    window.print();
  };

  const canPrint = summary.lines.length > 0 || lastReservation !== null;

  return (
    <>
      <Container className="py-8 lg:py-10">
        <div ref={topRef} tabIndex={-1} className="scroll-mt-32 outline-none">
          <BuilderHeader
            summary={summary}
            branchName={store.branch.name}
            hasPresets={presets.length > 0}
            canPrint={canPrint}
            lastReservation={lastReservation}
            onPickPreset={() => setPickerOpen(true)}
            onClear={clear}
            onPrint={print}
            onGoToStep={goToStep}
          />
        </div>

        <p role="status" aria-live="polite" className="sr-only">
          {announcement}
        </p>

        <div className="mt-10 grid gap-8 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start xl:grid-cols-[minmax(0,1fr)_25rem]">
          <section aria-labelledby="pasos-titulo" className="min-w-0">
            <div className="mb-4 flex flex-wrap items-end justify-between gap-2">
              <h2 id="pasos-titulo" className="flex items-center gap-2 text-2xl">
                <ListChecks aria-hidden="true" className="size-6 text-accent" />
                Elegí tus piezas
              </h2>
              <p className="text-sm text-text-muted">
                {pluralize(slots.length, 'paso', 'pasos')} · {pluralize(summary.progress.required, 'esencial', 'esenciales')} · abrí un paso para ver sus opciones
              </p>
            </div>
            <ol className="space-y-3" aria-label="Pasos del armado">
              {summary.slots.map(({ slot, lines: slotLines, subtotal }) => (
                <SlotStep
                  key={slot.key}
                  slot={slot}
                  lines={slotLines}
                  subtotal={subtotal}
                  status={stepStatus(slot, lines, skipped)}
                  expanded={expanded === slot.key}
                  onToggle={() => setExpanded((current) => (current === slot.key ? null : slot.key))}
                  onSkip={() => skip(slot)}
                  onUnskip={() => unskip(slot)}
                  onChoose={(product) => chooseIn(slot, product)}
                  onRemove={builder.remove}
                  onQuantity={builder.setQuantity}
                  headerRef={(element) => {
                    if (element) headerRefs.current.set(slot.key, element);
                    else headerRefs.current.delete(slot.key);
                  }}
                />
              ))}
            </ol>
          </section>

          <aside
            aria-label="Resumen del armado"
            className="max-lg:hidden lg:sticky lg:top-32 lg:max-h-[calc(100dvh-9rem)] lg:overflow-y-auto lg:overscroll-contain rounded-card border border-border bg-surface p-5 shadow-card"
          >
            <BuildSummaryPanel
              summary={summary}
              hasPresets={presets.length > 0}
              onRemove={builder.remove}
              onGoToStep={goToStep}
              onFinish={openReserve}
              onPickPreset={() => setPickerOpen(true)}
            />
          </aside>
        </div>

        <div className="mt-16">
          <PresetGallery presets={presets} currentCount={summary.count} onLoad={(id) => loadPreset(id, true)} />
        </div>
      </Container>

      <MobileSummaryBar count={summary.count} total={summary.total} progress={summary.progress} onOpen={() => setSheetOpen(true)} buttonRef={barButtonRef} />

      <BuilderDialog
        variant="sheet"
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Tu armado"
        description={summary.count > 0 ? pluralize(summary.count, 'pieza elegida', 'piezas elegidas') : 'Todavía no elegiste piezas'}
        footer={summary.lines.length > 0 ? <SummaryTotals compact total={summary.total} savings={summary.savings} count={summary.count} onFinish={openReserve} /> : undefined}
      >
        <BuildSummaryPanel
          summary={summary}
          hasPresets={presets.length > 0}
          withHeading={false}
          withTotals={false}
          onRemove={builder.remove}
          onGoToStep={goToStep}
          onFinish={openReserve}
          onPickPreset={() => {
            setSheetOpen(false);
            setPickerOpen(true);
          }}
        />
      </BuilderDialog>

      <PresetPicker
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        presets={presets}
        currentCount={summary.count}
        onLoad={(id) => loadPreset(id)}
        fallbackFocus={focusBarButton}
      />

      <ReserveDialog
        open={reserveOpen}
        onClose={() => setReserveOpen(false)}
        summary={summary}
        onReserved={reserved}
        onAdjust={adjustToAvailable}
        onPrint={print}
        fallbackFocus={focusBarButton}
      />

      <PrintSummary summary={summary} reservation={lastReservation} store={store} issuedAt={issuedAt} />
    </>
  );
}
