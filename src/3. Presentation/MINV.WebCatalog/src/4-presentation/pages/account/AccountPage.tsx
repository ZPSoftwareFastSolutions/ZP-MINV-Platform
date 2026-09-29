// «Mi cuenta» (/mi-cuenta y /mi-cuenta/:seccion), solo para una sesión de cliente (la guarda está en el enrutador).
// Tres secciones en pestañas: «Mis reservas», «Mis datos» y «Cambiar contraseña». La pestaña activa sale de la URL, así
// cada sección tiene su enlace y el botón «atrás» del navegador funciona. Cada sección carga sus datos al abrirse.

import { KeyRound, TicketCheck, UserRound } from 'lucide-react';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { ACCOUNT_SECTIONS, ROUTES, type AccountSection } from '@/4-presentation/app/routes';
import { Breadcrumbs } from '@/4-presentation/components/ui/Breadcrumbs';
import { Container } from '@/4-presentation/components/ui/Container';
import { TabPanel, Tabs, type TabItem } from '@/4-presentation/components/ui/Tabs';
import { useDocumentMeta } from '@/4-presentation/hooks/useDocumentMeta';
import { useSession } from '@/4-presentation/hooks/useSession';
import { PasswordSection } from './PasswordSection';
import { ProfileSection } from './ProfileSection';
import { ReservationsSection } from './ReservationsSection';

const TABS: readonly TabItem<AccountSection>[] = [
  // Los íconos se ocultan en pantallas angostas: las tres pestañas entran en 360 px sin desplazarse.
  { id: 'reservas', label: 'Mis reservas', shortLabel: 'Reservas', icon: <TicketCheck className="max-sm:hidden" /> },
  { id: 'datos', label: 'Mis datos', shortLabel: 'Datos', icon: <UserRound className="max-sm:hidden" /> },
  { id: 'contrasena', label: 'Cambiar contraseña', shortLabel: 'Contraseña', icon: <KeyRound className="max-sm:hidden" /> },
];

const TITLES: Record<AccountSection, string> = {
  reservas: 'Mis reservas',
  datos: 'Mis datos',
  contrasena: 'Cambiar contraseña',
};

function isSection(value: string): value is AccountSection {
  return (ACCOUNT_SECTIONS as readonly string[]).includes(value);
}

export function AccountPage() {
  const { seccion } = useParams();
  const navigate = useNavigate();
  const { session } = useSession();
  const section: AccountSection = seccion && isSection(seccion) ? seccion : 'reservas';
  useDocumentMeta({ title: `${TITLES[section]} · Mi cuenta` });

  // Una sección que no existe lleva a la primera (en vez de una página vacía).
  if (seccion && !isSection(seccion)) return <Navigate to={ROUTES.account} replace />;

  return (
    <Container className="py-8 lg:py-10">
      <Breadcrumbs items={[{ label: 'Mi cuenta', to: ROUTES.account }, { label: TITLES[section] }]} compactOnMobile />
      <header className="mt-6 animate-fade-up">
        <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-accent">Mi cuenta</p>
        <h1 className="text-3xl sm:text-4xl">Hola, {session?.displayName ?? 'cliente'}</h1>
        <p className="mt-3 max-w-2xl text-base text-text-muted">Seguí tus reservas, mantené tus datos al día y cuidá tu contraseña.</p>
      </header>

      <Tabs tabs={TABS} value={section} onChange={(id) => navigate(ROUTES.accountSection(id))} label="Secciones de mi cuenta" stretch className="mt-8" />

      <TabPanel id="reservas" activeId={section} className="mt-6 rounded-card">
        {section === 'reservas' && <ReservationsSection />}
      </TabPanel>
      <TabPanel id="datos" activeId={section} className="mt-6 rounded-card">
        {section === 'datos' && <ProfileSection />}
      </TabPanel>
      <TabPanel id="contrasena" activeId={section} className="mt-6 rounded-card">
        {section === 'contrasena' && <PasswordSection />}
      </TabPanel>
    </Container>
  );
}
