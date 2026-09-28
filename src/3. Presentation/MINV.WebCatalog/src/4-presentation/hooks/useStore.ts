import type { StoreInfo } from '@/1-domain/storefront/types';
import { useServices } from './useServices';

/** Empresa y sucursal de la tienda (de la instantánea): `const { branch, company } = useStore()`. */
export function useStore(): StoreInfo {
  return useServices().catalog.getStore();
}
