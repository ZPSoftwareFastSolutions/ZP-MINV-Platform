// Módulo «Caja» · tipos del servidor que usa la caja, DERIVADOS del contrato generado por el nombre de cada operación
// (regla P-07): no se declara a mano ningún tipo del servidor. Los nombres terminan en «Data» o «Payload» para no
// confundirse con los del contrato.

import type { RpcRequestOf, RpcResponseOf } from '@/4-presentation/app/contract';

// ---------------------------------------------------------------------------------------------------- caja y turno
/** Estado de la caja (`GetPosStateQuery`): cajas, medios de pago, clientes, IVA y el turno abierto del usuario. */
export type PosStateData = RpcResponseOf<'GetPosStateQuery'>;
/** Turno de caja abierto. */
export type PosSessionData = NonNullable<PosStateData['session']>;
/** Una opción de la caja (caja, medio de pago o cliente). */
export type PosOptionData = PosStateData['paymentMethods'][number];

// ---------------------------------------------------------------------------------------------------- productos
export type SellableData = RpcResponseOf<'GetSellableProductsQuery'>[number];
export type TechProductData = RpcResponseOf<'SearchTechProductsQuery'>[number];
export type ReservedStockData = RpcResponseOf<'GetStockReservationsQuery'>[number];
export type SpecDefinitionData = RpcResponseOf<'GetSpecDefinitionsQuery'>[number];
export type ProductLookupData = RpcResponseOf<'GetProductLookupQuery'>[number];
/** Serie de fábrica o IMEI (enumeración del servidor). */
export type SerialKindData = TechProductData['serialKind'];
/** Una unidad con serie o IMEI disponible en la sucursal. */
export type SerialData = RpcResponseOf<'GetAvailableSerialsQuery'>[number];

// ---------------------------------------------------------------------------------------------------- reservas
export type BuildDetailData = RpcResponseOf<'GetPcBuildQuery'>;
export type BuildRowData = BuildDetailData['build'];
export type BuildItemData = BuildDetailData['quotedItems'][number];
export type BuildListData = RpcResponseOf<'GetPcBuildsQuery'>[number];

// ---------------------------------------------------------------------------------------------------- cobro y factura
export type CheckoutPayload = RpcRequestOf<'CheckoutCommand'>;
export type SellBuildPayload = RpcRequestOf<'SellPcBuildCommand'>;
/** Datos del comprador para la factura (nominatividad). */
export type BuyerPayload = NonNullable<CheckoutPayload['buyer']>;
export type BuyerLookupPayload = RpcRequestOf<'FindFiscalBuyerQuery'>;
/** Resultado del cobro (el mismo para una venta y para una reserva). */
export type SaleResultData = RpcResponseOf<'CheckoutCommand'>;
export type ReceiptLineData = SaleResultData['lines'][number];
export type FiscalStateData = RpcResponseOf<'GetPosFiscalStateQuery'>;
export type SiatItemData = FiscalStateData['documentTypes'][number];
export type BuyerLookupData = RpcResponseOf<'FindFiscalBuyerQuery'>;
export type NitCheckData = RpcResponseOf<'VerifyNitCommand'>;
export type DispatchData = RpcResponseOf<'DispatchFiscalDocumentsCommand'>;
export type FiscalRowData = DispatchData['documents'][number];
export type PrintModelData = RpcResponseOf<'GetFiscalPrintModelQuery'>;
export type FiscalFileData = RpcResponseOf<'RenderFiscalDocumentQuery'>;
