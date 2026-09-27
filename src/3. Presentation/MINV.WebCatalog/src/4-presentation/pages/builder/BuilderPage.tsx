// Página «Armá tu PC»: pasos numerados por ranura (acordeón), resumen fijo a la derecha en escritorio y hoja inferior
// en móvil, armados sugeridos (#armados), modal de resumen final y vista imprimible.
// Estado del armado: exclusivamente useBuilder (React en memoria). El estado de la página (paso abierto, pasos
// omitidos, diálogos) también vive en memoria: nada persiste.

import './builder.css';
import { ListChecks } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { BuildSlot, SlotKey } from '@/1-domain/builder/types';
import type { Product } from '@/1-domain/catalog/types';
import { Container } from '@/4-presentation/components/ui/Container';
import { useBuilder } from '@/4-presentation/hooks/useBuilder';
import { useDocumentTitle } from '@/4-presentation/hooks/useDocumentTitle';
import { useServices } from '@/4-presentation/hooks/useServices';
import { pluralize } from '@/shared/format';
import { initialOpenSlot, nextOpenSlot, randomBuildNumber, stepStatus } from './builderSteps';
import { BuilderDialog } from './components/BuilderDialog';
import { BuilderHeader } from './components/BuilderHeader';
import { BuildSummaryPanel, SummaryTotals } from './components/BuildSummaryPanel';
import { FinalSummaryDialog } from './components/FinalSummaryDialog';
import { MobileSummaryBar } from './components/MobileSummaryBar';
import { PresetGallery } from './components/PresetGallery';
import { PresetPicker } from './components/PresetPicker';
import { PrintSummary } from './components/PrintSummary';
import { SlotStep } from './components/SlotStep';

interface FocusRequest {
  key: SlotKey;
  /** Espera (ms) antes de enfocar: al cerrar la hoja de móvil, el diálogo devuelve el foco a su disparador primero. */
  delay: number;
  tick: number;
}

const PRINT_ATTRIBUTE = 'data-print';

export function BuilderPage() {
  useDocumentTitle('Armá tu PC');
  const { catalog } = useServices();
  const builder = useBuilder();
  const { lines, summary } = builder;

  const slots = catalog.getBuildSlots();
  const presets = useMemo(() => catalog.getPresets(), [catalog]);

  const [expanded, setExpanded] = useState<SlotKey | null>(() => initialOpenSlot(lines));
  const [skipped, setSkipped] = useState<ReadonlySet<SlotKey>>(() => new Set());
  const [pickerOpen, setPickerOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [finalOpen, setFinalOpen] = useState(false);
  const [buildNumber, setBuildNumber] = useState(() => randomBuildNumber());
  const [issuedAt, setIssuedAt] = useState(() => new Date());
  const [announcement, setAnnouncement] = useState('');
  const [focusRequest, setFocusRequest] = useState<FocusRequest | null>(null);

  const headerRefs = useRef(new Map<SlotKey, HTMLButtonElement>());
  const topRef = useRef<HTMLDivElement>(null);

  // Mueve el foco (y la vista) a la cabecera del paso pedido después de que React lo dibuje.
  useEffect(() => {
    if (!focusRequest) return;
    const timer = setTimeout(() => {
      const element = headerRefs.current.get(focusRequest.key);
      if (!element) return;
      element.focus({ preventScroll: true });
      element.closest('li')?.scrollIntoView?.({ block: 'start' });
    }, focusRequest.delay);
    return () => clearTimeout(timer);
  }, [focusRequest]);

  // Al desmontar (navegación) no debe quedar el modo de impresión activo.
  useEffect(() => () => document.documentElement.removeAttribute(PRINT_ATTRIBUTE), []);

  const focusStep = (key: SlotKey, delay = 0) => setFocusRequest((current) => ({ key, delay, tick: (current?.tick ?? 0) + 1 }));

  const goToStep = (key: SlotKey) => {
    setExpanded(key);
    const fromSheet = sheetOpen;
    setSheetOpen(false);
    focusStep(key, fromSheet ? 300 : 0);
  };

  const advanceFrom = (slot: BuildSlot, predictedLines: typeof lines, nextSkipped: ReadonlySet<SlotKey>, prefix: string) => {
    const next = nextOpenSlot(slot.key, predictedLines, nextSkipped);
    setExpanded(next?.key ?? null);
    focusStep(next?.key ?? slot.key);
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

  const clear = () => {
    builder.clear();
    setSkipped(new Set());
    setExpanded(slots[0]?.key ?? null);
    setBuildNumber(randomBuildNumber());
    setAnnouncement('Armado vaciado.');
  };

  const loadPreset = (id: string, scrollToTop = false) => {
    if (!builder.loadPreset(id)) return;
    setSkipped(new Set());
    setExpanded(null);
    setAnnouncement('Armado sugerido cargado. Revisá cada paso y cambiá lo que quieras.');
    if (scrollToTop) topRef.current?.scrollIntoView?.({ block: 'start' });
  };

  const openFinal = () => {
    setIssuedAt(new Date());
    setSheetOpen(false);
    setFinalOpen(true);
  };

  const print = () => {
    if (typeof window.print !== 'function') return;
    const root = document.documentElement;
    root.setAttribute(PRINT_ATTRIBUTE, 'armado');
    const restore = () => root.removeAttribute(PRINT_ATTRIBUTE);
    window.addEventListener('afterprint', restore, { once: true });
    window.print();
  };

  return (
    <>
      <Container className="py-8 pb-32 lg:py-10 lg:pb-16">
        <div ref={topRef} className="scroll-mt-32">
          <BuilderHeader summary={summary} onPickPreset={() => setPickerOpen(true)} onClear={clear} onPrint={print} onGoToStep={goToStep} />
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
            <BuildSummaryPanel summary={summary} onRemove={builder.remove} onGoToStep={goToStep} onFinish={openFinal} onPickPreset={() => setPickerOpen(true)} />
          </aside>
        </div>

        <div className="mt-16">
          <PresetGallery presets={presets} currentCount={summary.count} onLoad={(id) => loadPreset(id, true)} />
        </div>
      </Container>

      <MobileSummaryBar count={summary.count} total={summary.total} progress={summary.progress} onOpen={() => setSheetOpen(true)} />

      <BuilderDialog
        variant="sheet"
        open={sheetOpen}
        onClose={() => setSheetOpen(false)}
        title="Tu armado"
        description={summary.count > 0 ? pluralize(summary.count, 'pieza elegida', 'piezas elegidas') : 'Todavía no elegiste piezas'}
        footer={summary.lines.length > 0 ? <SummaryTotals compact total={summary.total} savings={summary.savings} count={summary.count} onFinish={openFinal} /> : undefined}
      >
        <BuildSummaryPanel
          summary={summary}
          withHeading={false}
          withTotals={false}
          onRemove={builder.remove}
          onGoToStep={goToStep}
          onFinish={openFinal}
          onPickPreset={() => {
            setSheetOpen(false);
            setPickerOpen(true);
          }}
        />
      </BuilderDialog>

      <PresetPicker open={pickerOpen} onClose={() => setPickerOpen(false)} presets={presets} currentCount={summary.count} onLoad={(id) => loadPreset(id)} />

      <FinalSummaryDialog open={finalOpen} onClose={() => setFinalOpen(false)} summary={summary} buildNumber={buildNumber} issuedAt={issuedAt} onPrint={print} />

      <PrintSummary summary={summary} buildNumber={buildNumber} issuedAt={issuedAt} />
    </>
  );
}
