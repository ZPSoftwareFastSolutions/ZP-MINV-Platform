// Administración › Integraciones (paquete M10). Lo mismo que «Integraciones» del escritorio (IntegrationsView +
// IntegrationsViewModel) y más, en pestañas para no mezclar: «API Keys» (crear con alcances en casillas y el token
// mostrado UNA vez con «Copiar», revocar), «Webhooks» (registrar con eventos en casillas y el secreto mostrado una vez,
// rotar el secreto, desactivar), «Entregas» (filtros por resultado, evento, webhook y fechas) y «Correos de reservas»
// (la cola de confirmaciones con su estado y reenvío; solo para quien gestiona las reservas). Los indicadores del
// escritorio van plegados en «Ver resumen de las integraciones» (regla P-10).
//
// Botón del tablero «Correos de reservas»: `?pestana=correos`. Cada acción solo se ofrece a quien puede ejecutar su
// comando (`canRun`); el servidor decide igual (regla P-01), también la licencia «API_INTEGRATIONS» al crear.

import { BarChart3, KeyRound, MailCheck, Plus, Radio, RotateCcw, Send, Webhook } from 'lucide-react';
import { useState } from 'react';
import { usePermissions, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Collapsible, Page, TabPanel, Tabs, type TabItem } from '@/4-presentation/panel/kit';
import { ApiKeysTab } from './ApiKeysTab';
import { CreateApiKeyDialog } from './CreateApiKeyDialog';
import { CreateWebhookDialog } from './CreateWebhookDialog';
import { DeliveriesTab } from './DeliveriesTab';
import { IntegrationsStat } from './IntegrationsStat';
import { MailsTab } from './MailsTab';
import { SecretDialog, type SecretNotice } from './SecretDialog';
import { WebhooksTab } from './WebhooksTab';
import { useTabParam } from './params';
import { catalogEvents, catalogScopes, effectivePermissionsText, formBranchOptions, plainMessage, type CreatedHook, type CreatedKey } from './integrations';

type IntegrationTab = 'llaves' | 'webhooks' | 'entregas' | 'correos';

const SIGNATURE_NOTE =
  'Su sistema verifica cada aviso con este secreto: la cabecera X-MINV-Signature lleva la hora y la firma HMAC-SHA256 de «hora.contenido».';

function keyNotice(created: CreatedKey): SecretNotice {
  return {
    title: `API Key «${created.name}»`,
    description: `minv_${created.prefix}_…`,
    message: 'Copie el token y guárdelo en la configuración de su sistema: se muestra UNA sola vez (en M-INV solo queda su huella).',
    label: 'Token de la llave',
    value: created.token,
    notes: [
      created.effectivePermissions.length > 0 ? `Permisos efectivos: ${effectivePermissionsText(created)}.` : 'La llave no recibió permisos: revise los alcances.',
      'Su sistema la envía en la cabecera X-Api-Key de cada pedido al API Gateway (vea la guía del integrador de M-INV).',
    ],
  };
}

function hookNotice(created: CreatedHook, rotated: boolean): SecretNotice {
  return {
    title: rotated ? 'Secreto nuevo del webhook' : 'Secreto del webhook',
    description: created.url,
    message: plainMessage(created.message),
    label: 'Secreto de firma',
    value: created.secret,
    notes: rotated ? ['Durante 24 horas cada aviso lleva las dos firmas: cambie el secreto en su sistema dentro de ese plazo.', SIGNATURE_NOTE] : [SIGNATURE_NOTE],
  };
}

export function IntegrationsPage() {
  const { canRun } = usePermissions();
  const canMails = canRun('GetOutgoingMailsQuery');
  const canCreateKey = canRun('CreateApiKeyCommand');
  const canCreateHook = canRun('CreateWebhookCommand');
  const tabs: IntegrationTab[] = canMails ? ['llaves', 'webhooks', 'entregas', 'correos'] : ['llaves', 'webhooks', 'entregas'];
  const [tab, setTab] = useTabParam<IntegrationTab>(tabs, 'llaves');

  const catalog = useRpcQuery('GetIntegrationCatalogQuery', {});
  const branches = useRpcQuery('GetBranchesQuery', {}, { enabled: canRun('GetBranchesQuery') });
  const scopes = catalogScopes(catalog.data);
  const events = catalogEvents(catalog.data);
  const branchChoices = formBranchOptions(branches.data);

  const [creatingKey, setCreatingKey] = useState(false);
  const [creatingHook, setCreatingHook] = useState(false);
  const [secret, setSecret] = useState<SecretNotice | null>(null);
  // Después de crear, la lista se vuelve a montar (y a leer) con lo nuevo.
  const [version, setVersion] = useState(0);

  const keyCreated = (created: CreatedKey) => {
    setTab('llaves');
    setVersion((value) => value + 1);
    setSecret(keyNotice(created));
  };
  const hookCreated = (created: CreatedHook) => {
    setTab('webhooks');
    setVersion((value) => value + 1);
    setSecret(hookNotice(created, false));
  };

  const tabItems: TabItem<IntegrationTab>[] = [
    { id: 'llaves', label: 'API Keys', icon: <KeyRound /> },
    { id: 'webhooks', label: 'Webhooks', icon: <Webhook /> },
    { id: 'entregas', label: 'Entregas', icon: <Radio /> },
    ...(canMails ? [{ id: 'correos' as const, label: 'Correos de reservas', icon: <MailCheck /> }] : []),
  ];

  return (
    <Page
      title="Integraciones"
      description="Conecte su tienda en línea o su ERP con M-INV: llaves del API Gateway, avisos automáticos (webhooks) y sus entregas, y la cola de correos de las reservas."
      actions={
        <>
          {canCreateKey && (
            <Button variant="outline" leftIcon={<Plus />} disabled={!catalog.data} onClick={() => setCreatingKey(true)}>
              Nueva API Key
            </Button>
          )}
          {canCreateHook && (
            <Button leftIcon={<Webhook />} disabled={!catalog.data} onClick={() => setCreatingHook(true)}>
              Nuevo webhook
            </Button>
          )}
        </>
      }
    >
      {catalog.error && (
        <Alert
          tone="warning"
          title="No se pudieron leer los alcances y los eventos"
          actions={
            <Button variant="outline" leftIcon={<RotateCcw />} onClick={catalog.reload}>
              Reintentar
            </Button>
          }
        >
          Las listas se ven con sus códigos y no se pueden crear llaves ni webhooks hasta leerlos.
        </Alert>
      )}

      <Collapsible
        label="Ver resumen de las integraciones"
        openLabel="Ocultar el resumen"
        icon={<BarChart3 />}
        description="API Keys activas y en uso, webhooks activos y entregas correctas y fallidas."
      >
        <IntegrationsStat />
      </Collapsible>

      <Tabs label="Secciones de las integraciones" value={tab} onChange={(next) => setTab(next)} tabs={tabItems}>
        <TabPanel id="llaves">
          <ApiKeysTab key={`llaves-${version}`} scopes={scopes} canCreate={canCreateKey && Boolean(catalog.data)} onCreate={() => setCreatingKey(true)} />
        </TabPanel>
        <TabPanel id="webhooks">
          <WebhooksTab
            key={`webhooks-${version}`}
            events={events}
            canCreate={canCreateHook && Boolean(catalog.data)}
            onCreate={() => setCreatingHook(true)}
            onShowDeliveries={(id) => setTab('entregas', { webhook: id })}
            onRotated={(rotated) => setSecret(hookNotice(rotated, true))}
          />
        </TabPanel>
        <TabPanel id="entregas">
          <DeliveriesTab events={events} />
        </TabPanel>
        {canMails && (
          <TabPanel id="correos">
            <MailsTab />
          </TabPanel>
        )}
      </Tabs>

      {!canMails && (
        <p className="flex items-center gap-2 text-sm text-text-muted">
          <Send aria-hidden="true" className="size-4 shrink-0" />
          La cola de correos de las reservas la ve quien gestiona las reservas (permiso de Armador de PC y reservas).
        </p>
      )}

      <CreateApiKeyDialog open={creatingKey} scopes={scopes} branches={branchChoices} onClose={() => setCreatingKey(false)} onCreated={keyCreated} />
      <CreateWebhookDialog open={creatingHook} events={events} branches={branchChoices} onClose={() => setCreatingHook(false)} onCreated={hookCreated} />
      <SecretDialog secret={secret} onClose={() => setSecret(null)} />
    </Page>
  );
}
