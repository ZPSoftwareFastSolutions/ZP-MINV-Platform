// Análisis › Contabilidad (`/panel/contabilidad`; en el escritorio: AccountingView + AccountingViewModel). Tres pestañas
// en la dirección (`?vista=diario`): Estado de resultados, Libro diario y Plan de cuentas, cada una con sus filtros,
// exportar CSV y detalle; el período se conserva al cambiar de pestaña. Botones arriba: «Nuevo asiento» (asiento manual
// con Debe = Haber en vivo) y «Nueva cuenta», solo para quien puede (`canRun`); también se abren con `?nuevo=asiento` o
// `?nuevo=cuenta` (botones del tablero). Después de registrar un asiento, todo se vuelve a consultar.

import { FilePlus2, FolderPlus, NotebookText, ListTree, Scale } from 'lucide-react';
import type { ReactNode } from 'react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Page, TabPanel, Tabs } from '@/4-presentation/panel/kit';
import { DEFAULT_VIEW, NEW_PARAM, SHARED_PARAMS, VIEWS, VIEW_PARAM, viewOf, type AccountingView, type EntryPrefill } from './accounting';
import { ChartView } from './ChartView';
import { CreateAccountDialog } from './CreateAccountDialog';
import { IncomeStatementView } from './IncomeStatementView';
import { JournalEntryDialog } from './JournalEntryDialog';
import { JournalView } from './JournalView';

const ICONS: Record<AccountingView, ReactNode> = {
  resultados: <Scale />,
  diario: <NotebookText />,
  cuentas: <ListTree />,
};

export function AccountingPage() {
  const { canRun } = usePermissions();
  const [params, setParams] = useSearchParams();
  const view = viewOf(params.get(VIEW_PARAM));
  const canEntry = canRun('CreateJournalEntryCommand');
  const canAccount = canRun('CreateAccountCommand');

  // El plan de cuentas lo usan el libro (lista «Cuenta»), el plan y los dos diálogos: se carga una vez aquí.
  const chart = useRpcQuery('GetChartOfAccountsQuery', { from: null, to: null });
  const accounts = chart.data ?? [];
  // Cambia después de registrar un asiento: las pestañas vuelven a consultar.
  const [version, setVersion] = useState(0);

  // Diálogos: se abren con `?nuevo=asiento|cuenta` (así también desde el tablero). Cada apertura empieza en limpio.
  const requested = params.get(NEW_PARAM);
  const entryOpen = requested === 'asiento' && canEntry;
  const accountOpen = requested === 'cuenta' && canAccount;
  const [entryDialog, setEntryDialog] = useState<{ key: number; open: boolean; prefill: EntryPrefill | null }>({ key: 0, open: false, prefill: null });
  if (entryOpen !== entryDialog.open) setEntryDialog({ key: entryOpen ? entryDialog.key + 1 : entryDialog.key, open: entryOpen, prefill: entryOpen ? entryDialog.prefill : null });
  const [accountDialog, setAccountDialog] = useState<{ key: number; open: boolean; parent: string | null }>({ key: 0, open: false, parent: null });
  if (accountOpen !== accountDialog.open) setAccountDialog({ key: accountOpen ? accountDialog.key + 1 : accountDialog.key, open: accountOpen, parent: accountOpen ? accountDialog.parent : null });

  const writeParams = (change: (next: URLSearchParams) => void) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous);
        change(next);
        return next;
      },
      { replace: true },
    );
  const openEntry = (prefill: EntryPrefill | null = null) => {
    setEntryDialog((current) => ({ ...current, prefill }));
    writeParams((next) => next.set(NEW_PARAM, 'asiento'));
  };
  const openAccount = (parent: string | null = null) => {
    setAccountDialog((current) => ({ ...current, parent }));
    writeParams((next) => next.set(NEW_PARAM, 'cuenta'));
  };
  const closeDialogs = () => writeParams((next) => next.delete(NEW_PARAM));

  // Cambiar de pestaña conserva el período y olvida los filtros, el orden y la página de la anterior.
  const choose = (id: AccountingView, extra: Record<string, string> = {}) =>
    setParams(
      (previous) => {
        const next = new URLSearchParams();
        if (id !== DEFAULT_VIEW) next.set(VIEW_PARAM, id);
        for (const key of SHARED_PARAMS) {
          const value = previous.get(key);
          if (key !== VIEW_PARAM && value !== null) next.set(key, value);
        }
        for (const [key, value] of Object.entries(extra)) next.set(key, value);
        return next;
      },
      { replace: true },
    );
  const showAccount = (code: string) => choose('diario', { cuenta: code });

  const entryCreated = () => {
    setVersion((current) => current + 1);
    chart.reload();
    choose('diario');
  };
  const accountCreated = () => {
    chart.reload();
    closeDialogs();
  };

  return (
    <Page
      title="Contabilidad"
      description="Estado de resultados, libro diario y plan de cuentas. Los asientos de ventas, compras, anulaciones y ajustes se generan solos; registre aquí gastos, pagos y depósitos."
      actions={
        <>
          {canAccount && (
            <Button variant="outline" leftIcon={<FolderPlus />} onClick={() => openAccount(null)} disabled={!chart.data}>
              Nueva cuenta
            </Button>
          )}
          {canEntry && (
            <Button leftIcon={<FilePlus2 />} onClick={() => openEntry(null)} disabled={!chart.data}>
              Nuevo asiento
            </Button>
          )}
        </>
      }
    >
      <Tabs label="Secciones de la contabilidad" tabs={VIEWS.map((item) => ({ ...item, icon: ICONS[item.id] }))} value={view} onChange={(id) => choose(id)}>
        <TabPanel id="resultados">
          <IncomeStatementView version={version} onShowAccount={showAccount} />
        </TabPanel>
        <TabPanel id="diario">
          <JournalView accounts={accounts} version={version} onUseAsTemplate={canEntry ? (prefill) => openEntry(prefill) : null} />
        </TabPanel>
        <TabPanel id="cuentas">
          <ChartView chart={chart} version={version} onShowAccount={showAccount} onCreateUnder={canAccount ? (code) => openAccount(code) : null} />
        </TabPanel>
      </Tabs>

      {chart.data && (
        <>
          <JournalEntryDialog key={`asiento-${entryDialog.key}`} open={entryDialog.open} accounts={accounts} prefill={entryDialog.prefill} onClose={closeDialogs} onCreated={entryCreated} />
          <CreateAccountDialog key={`cuenta-${accountDialog.key}`} open={accountDialog.open} parentCode={accountDialog.parent} accounts={accounts} onClose={closeDialogs} onCreated={accountCreated} />
        </>
      )}
    </Page>
  );
}
