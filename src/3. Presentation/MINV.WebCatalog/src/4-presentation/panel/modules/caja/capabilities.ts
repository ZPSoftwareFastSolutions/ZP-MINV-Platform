// Módulo «Caja» · qué puede hacer la sesión en la caja, con los permisos que el contrato declara para cada operación
// (`RPC_META`). Es comodidad: lo que la sesión no puede hacer no se muestra, y el servidor igual decide en cada pedido
// (regla P-01).

import type { RpcOperationName } from '@/4-presentation/app/contract';

export interface PosCapabilities {
  /** Ver los productos a la venta (`GetSellableProductsQuery`). */
  products: boolean;
  /** Ficha técnica: serie o IMEI, plataformas (`SearchTechProductsQuery`). */
  tech: boolean;
  /** Lo reservado de cada producto (`GetStockReservationsQuery`). */
  reservedStock: boolean;
  /** Opciones de plataforma y condición (`GetSpecDefinitionsQuery`). */
  specs: boolean;
  /** Explicar un código que no está a la venta (`GetProductLookupQuery`). */
  lookup: boolean;
  open: boolean;
  close: boolean;
  checkout: boolean;
  /** Cargar y vender una reserva o cotización (`GetPcBuildQuery` + `SellPcBuildCommand`). */
  sellBuild: boolean;
  /** Elegir la reserva de una lista (`GetPcBuildsQuery`). */
  pickBuild: boolean;
  serials: boolean;
  findBuyer: boolean;
  verifyNit: boolean;
  dispatch: boolean;
  printModel: boolean;
  pdf: boolean;
  email: boolean;
  /** Ir al documento en Facturación › Documentos. */
  documents: boolean;
  /** Ir a la venta en Ventas. */
  sales: boolean;
}

export function posCapabilities(canRun: (operation: RpcOperationName) => boolean): PosCapabilities {
  return {
    products: canRun('GetSellableProductsQuery'),
    tech: canRun('SearchTechProductsQuery'),
    reservedStock: canRun('GetStockReservationsQuery'),
    specs: canRun('GetSpecDefinitionsQuery'),
    lookup: canRun('GetProductLookupQuery'),
    open: canRun('OpenPosSessionCommand'),
    close: canRun('ClosePosSessionCommand'),
    checkout: canRun('CheckoutCommand'),
    sellBuild: canRun('GetPcBuildQuery') && canRun('SellPcBuildCommand'),
    pickBuild: canRun('GetPcBuildsQuery'),
    serials: canRun('GetAvailableSerialsQuery'),
    findBuyer: canRun('FindFiscalBuyerQuery'),
    verifyNit: canRun('VerifyNitCommand'),
    dispatch: canRun('DispatchFiscalDocumentsCommand'),
    printModel: canRun('GetFiscalPrintModelQuery'),
    pdf: canRun('RenderFiscalDocumentQuery'),
    email: canRun('SendFiscalDocumentEmailCommand'),
    documents: canRun('GetFiscalDocumentsQuery'),
    sales: canRun('GetSalesQuery'),
  };
}
