// Biblioteca del panel (funciones puras, sin React): formato en español de Bolivia con la hora de La Paz, rangos de
// fechas, orden y paginación de tablas, exportación a CSV y ayudas del RPC. Los módulos importan de aquí:
//
//   import { formatMoney, formatDateTime, exportCsv } from '@/4-presentation/panel/lib';

export * from './format';
export * from './dates';
export * from './numbers';
export * from './table';
export * from './csv';
export * from './rpc';
