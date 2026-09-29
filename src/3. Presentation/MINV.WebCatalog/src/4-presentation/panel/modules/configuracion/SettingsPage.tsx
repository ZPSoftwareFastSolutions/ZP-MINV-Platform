// Administración › Configuración (paquete M10), en pestañas para no mezclar:
//   · «Empresa»: datos de la empresa y parámetros del semáforo (la pestaña «Empresa» de Usuarios y roles del escritorio);
//     se ve con `inventory.stock.view` y se cambia con `iam.users.manage`.
//   · «Facturación SIAT»: BillingSettingsView del escritorio más las acciones de configuración de «Estado SIAT»
//     (preparar, catálogos, puntos de venta, CUIS, CUFD); con `billing.configure`.
//   · «Correo de la empresa»: el SMTP de las facturas y de las reservas, con ayuda para Gmail; con `billing.configure`.
// Los secretos (token del SIN y contraseña del correo) nunca se muestran. La configuración de la estación del escritorio
// (impresora, escáner, tema) no aplica a la web.
//
// Botón del tablero «Configurar la facturación»: `?pestana=facturacion`.

import { Building2, Mail, Receipt } from 'lucide-react';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { AccessDenied, Page, TabPanel, Tabs, type TabItem } from '@/4-presentation/panel/kit';
import { BillingTab } from './BillingTab';
import { CompanyTab } from './CompanyTab';
import { MailTab } from './MailTab';
import { useTabParam } from './params';

type SettingsTab = 'empresa' | 'facturacion' | 'correo';

export function SettingsPage() {
  const { can, canRun, session } = usePermissions();
  const showCompany = canRun('GetCompanySettingsQuery');
  const showBilling = can('billing.configure') && canRun('GetSiatSettingsQuery');
  const tabs: SettingsTab[] = [...(showCompany ? (['empresa'] as const) : []), ...(showBilling ? (['facturacion', 'correo'] as const) : [])];
  const [tab, setTab] = useTabParam<SettingsTab>(tabs, tabs[0] ?? 'empresa');
  // La configuración de la facturación la comparten «Facturación SIAT» y «Correo»: se lee al abrir una de las dos.
  const siat = useRpcQuery('GetSiatSettingsQuery', {}, { enabled: showBilling && tab !== 'empresa' });
  const companyName = session?.company ?? '';

  const items: TabItem<SettingsTab>[] = [
    ...(showCompany ? [{ id: 'empresa' as const, label: 'Empresa', icon: <Building2 /> }] : []),
    ...(showBilling
      ? [
          { id: 'facturacion' as const, label: 'Facturación SIAT', icon: <Receipt /> },
          { id: 'correo' as const, label: 'Correo de la empresa', icon: <Mail /> },
        ]
      : []),
  ];

  return (
    <Page title="Configuración" description="Datos de la empresa y semáforo de stock, facturación con el SIN (SIAT) y el correo de la empresa.">
      {items.length === 0 ? (
        <AccessDenied permissions={['inventory.stock.view', 'billing.configure']} title="No tiene acceso a ninguna sección de la configuración" />
      ) : (
        <Tabs label="Secciones de la configuración" value={tab} onChange={(next) => setTab(next)} tabs={items}>
          {showCompany && (
            <TabPanel id="empresa">
              <CompanyTab />
            </TabPanel>
          )}
          {showBilling && (
            <TabPanel id="facturacion">
              <BillingTab siat={siat} companyName={companyName} />
            </TabPanel>
          )}
          {showBilling && (
            <TabPanel id="correo">
              <MailTab siat={siat} companyName={companyName} />
            </TabPanel>
          )}
        </Tabs>
      )}
    </Page>
  );
}
