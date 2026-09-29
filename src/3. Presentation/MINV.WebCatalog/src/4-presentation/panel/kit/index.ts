// CONJUNTO DE COMPONENTES DEL PANEL (paquete W3a). Los módulos del panel construyen sus pantallas SOLO con esto (más los
// hooks de `panel/hooks` y las funciones de `panel/lib`), así todas se ven y se usan igual. Un módulo NO modifica estos
// archivos (regla P-09): si falta algo, se agrega aquí para todos.
//
//   import { Page, FilterBar, SearchField, SelectField, DataTable, type DataTableColumn } from '@/4-presentation/panel/kit';
//
// Cada componente trae su comentario de uso al principio del archivo. La página interna `/panel/_componentes` (solo
// en desarrollo) los muestra todos con datos de ejemplo.

// Estructura de la pantalla
export { Page, type PageCrumb, type PageProps } from './Page';
export { Toolbar, type ToolbarProps } from './Toolbar';
export { Section, DetailList, type SectionProps, type DetailItem, type DetailListProps } from './Section';

// Filtros
export { FilterBar, type FilterBarProps } from './FilterBar';
export { SearchField, SEARCH_DELAY_MS, type SearchFieldProps } from './SearchField';
export { SelectField, type SelectFieldProps, type SelectOption } from './SelectField';
export { ComboBox, type ComboBoxProps, type ComboOption } from './ComboBox';
export { DateRangeField, type DateRangeFieldProps } from './DateRangeField';

// Tabla, acciones y detalle
export { DataTable, type DataTableEmpty, type DataTableProps } from './DataTable';
export { type DataTableColumn } from './tableColumns';
export { RowActions, type RowActionItem, type RowActionsProps } from './RowActions';
export { SidePanel, type SidePanelProps } from './SidePanel';
export { Dialog, ConfirmDialog, type DialogProps, type ConfirmDialogProps } from './Dialog';

// Formularios
export { Form, FormGrid, type FormProps } from './Form';
export { Field, FieldGroup, type FieldControlProps, type FieldFrameProps, type FieldProps, type FieldGroupProps } from './Field';
export { TextField, TextArea, type TextFieldProps, type TextAreaProps } from './TextField';
export { NumberField, MoneyField, type NumberFieldProps, type MoneyFieldProps } from './NumberField';
export { Checkbox, Switch, RadioGroup, type CheckboxProps, type SwitchProps, type RadioGroupProps, type RadioOption } from './Choice';

// Navegación, estados y datos
export { Tabs, TabPanel, type TabItem, type TabsProps, type TabPanelProps } from './Tabs';
export { StatusBadge, type StatusBadgeProps } from './StatusBadge';
export { defineStatuses, statusOf, statusOptions, type StatusDefinition, type StatusMap, type StatusTone } from './statuses';
export { Collapsible, type CollapsibleProps } from './Collapsible';
export { StatCard, type StatCardProps, type StatTrend } from './StatCard';
export { BarList, MiniBars, type BarListItem, type BarListProps, type ChartTone, type MiniBarsPoint, type MiniBarsProps } from './Charts';
export { ActionButton, type ActionButtonProps } from './ActionButton';
export { ErrorState, type ErrorStateProps } from './States';
export { EmptyState, type EmptyStateProps } from '@/4-presentation/components/ui/EmptyState';
export { Skeleton, type SkeletonProps } from '@/4-presentation/components/ui/Skeleton';
export { LoadingState, type LoadingStateProps } from '@/4-presentation/components/feedback/AsyncState';

// Avisos y permisos
export { useNotify, type Notify } from './notify';
export { PermissionGate, AccessDenied, type PermissionGateProps, type AccessDeniedProps } from './PermissionGate';

// Botones y avisos en línea del sistema visual (los mismos de la tienda).
export { Button, type ButtonProps } from '@/4-presentation/components/ui/Button';
export { IconButton, type IconButtonProps } from '@/4-presentation/components/ui/IconButton';
export { Alert, type AlertProps, type AlertTone } from '@/4-presentation/components/ui/Alert';
