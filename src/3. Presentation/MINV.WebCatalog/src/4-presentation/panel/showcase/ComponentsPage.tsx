// PÁGINA INTERNA `/panel/_componentes` · SOLO EN DESARROLLO (`import.meta.env.DEV`: la ruta no existe en la versión
// publicada). Muestra todos los componentes del panel con datos de ejemplo para revisarlos de un vistazo, en el mismo
// orden en que se arma una pantalla. Requiere una sesión del personal (la guarda de `/panel`).

import { ArrowLeft } from 'lucide-react';
import type { ReactNode } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { Container } from '@/4-presentation/components/ui/Container';
import { Button, Page, Section, StatusBadge } from '../kit';
import { SalesDemo } from './SalesDemo';
import { BadgesDemo, DashboardDemo, DialogsDemo, FormDemo, PermissionsDemo, RpcDemo, StatesDemo, TableStatesDemo } from './ShowcaseDemos';

interface Block {
  id: string;
  title: string;
  description: string;
  content: ReactNode;
}

const BLOCKS: readonly Block[] = [
  {
    id: 'tablero',
    title: 'Tablero',
    description: 'ActionButton para las funciones del rol y estadísticas plegadas detrás de «Ver» (Collapsible, StatCard, BarList, MiniBars).',
    content: <DashboardDemo />,
  },
  {
    id: 'lista',
    title: 'Pantalla de lista',
    description:
      'FilterBar, SearchField, SelectField, ComboBox, DateRangeField, Toolbar, DataTable (orden, páginas, selección, fila desplegable, totales, acciones), RowActions, SidePanel, Tabs, DetailList, ConfirmDialog y exportar CSV. Los filtros quedan en la dirección de la página.',
    content: <SalesDemo />,
  },
  { id: 'estados-tabla', title: 'Estados de la tabla', description: 'Cargando, vacía, error con «Reintentar» y lista corta sin páginas.', content: <TableStatesDemo /> },
  { id: 'estados', title: 'Estados sueltos', description: 'EmptyState, ErrorState (con el permiso que falta) y Skeleton.', content: <StatesDemo /> },
  {
    id: 'formulario',
    title: 'Formulario',
    description: 'Form, FormGrid, TextField, NumberField, MoneyField, ComboBox asíncrono, SelectField sin «Todos», RadioGroup, TextArea, Checkbox y Switch. Pulse «Guardar» vacío para ver los errores.',
    content: <FormDemo />,
  },
  { id: 'dialogos', title: 'Diálogos y avisos', description: 'Dialog con formulario, ConfirmDialog que falla la primera vez y los avisos de useNotify.', content: <DialogsDemo /> },
  { id: 'distintivos', title: 'Distintivos de estado', description: 'StatusBadge con los seis tonos y con los estados de un módulo.', content: <BadgesDemo /> },
  { id: 'permisos', title: 'Permisos', description: 'PermissionGate y AccessDenied con los permisos de la sesión actual.', content: <PermissionsDemo /> },
  { id: 'servidor', title: 'Servidor', description: 'useRpcQuery dentro de un plegable (consulta recién al abrir) y useRpcCommand con el aviso de permiso faltante.', content: <RpcDemo /> },
];

export function ComponentsPage() {
  return (
    <div className="min-h-dvh">
      <a href="#contenido" className="skip-link">
        Saltar al contenido
      </a>
      <main id="contenido" tabIndex={-1} className="outline-none">
        <Container className="py-8">
          <Page
            title="Componentes del panel"
            description="Muestra interna con datos de ejemplo. Los módulos del panel se construyen solo con estos componentes."
            breadcrumbs={[{ label: 'Panel', to: ROUTES.panel }, { label: 'Componentes' }]}
            badge={<StatusBadge tone="warning">Solo en desarrollo</StatusBadge>}
            actions={
              <Button variant="outline" to={ROUTES.panel} leftIcon={<ArrowLeft />}>
                Volver al panel
              </Button>
            }
          >
            <nav aria-label="Secciones de la muestra" className="flex flex-wrap gap-2">
              {BLOCKS.map((block) => (
                <a
                  key={block.id}
                  href={`#${block.id}`}
                  className="inline-flex min-h-11 items-center rounded-full border border-border bg-surface-2 px-4 text-sm font-medium text-text-muted transition-colors duration-200 hover:border-border-strong hover:text-text"
                >
                  {block.title}
                </a>
              ))}
            </nav>
            {BLOCKS.map((block) => (
              <Section key={block.id} id={block.id} title={block.title} description={block.description}>
                {block.content}
              </Section>
            ))}
          </Page>
        </Container>
      </main>
    </div>
  );
}
