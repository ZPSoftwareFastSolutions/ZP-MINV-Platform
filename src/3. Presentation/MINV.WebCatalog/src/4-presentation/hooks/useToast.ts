import { useContext } from 'react';
import { ToastContext, type ToastApi } from '@/4-presentation/components/feedback/ToastContext';

/** `const toast = useToast(); toast.notify({ title: 'Agregado al armado', description: product.shortName })`. */
export function useToast(): ToastApi {
  const api = useContext(ToastContext);
  if (!api) throw new Error('useToast() debe usarse dentro de <ToastProvider>.');
  return api;
}
