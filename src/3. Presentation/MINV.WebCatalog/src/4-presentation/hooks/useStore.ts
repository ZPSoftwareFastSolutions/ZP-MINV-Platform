import type { StoreInfo } from '@/1-domain/storefront/types';
import { useOptionalServices, useServices } from './useServices';

/** Empresa y sucursal de la tienda (de la instantánea): `const { branch, company } = useStore()`. */
export function useStore(): StoreInfo {
  return useServices().catalog.getStore();
}

/** Lo mismo, o undefined si el catálogo todavía no llegó (pantallas que no dependen de la tienda, como «Mi cuenta»). */
export function useOptionalStore(): StoreInfo | undefined {
  return useOptionalServices()?.catalog.getStore();
}
