// Módulo «Catálogo» · alta y edición de un producto en un diálogo con pestañas (el editor lateral del escritorio):
//   Datos (SKU, nombre, categoría —o una nueva—, unidad, proveedor, posición, código de barras, descripción, activo) ·
//   Precios (costo, precio con IVA, «Aplicar margen» y el margen como guía) · Imágenes (elegir, reducir y quitar) ·
//   Ficha técnica (serie o IMEI, garantía y las especificaciones de la categoría) · Stock mínimo (mínimo y máximo del
//   almacén de trabajo).
// Se guarda como el escritorio, en orden: `SaveProductCommand` (devuelve el SKU) → `SetProductImageCommand` o
// `RemoveProductImageCommand` → `SaveProductTechCommand` (solo si la ficha cambió y la sesión puede). Si el producto se
// guardó pero la imagen o la ficha no, el diálogo lo dice, queda abierto en esa pestaña y el reintento corrige ESE
// producto (ya no crea otro). Modo «ficha»: solo la ficha técnica (quien tiene `catalog.specs.manage` sin
// `catalog.manage`). Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { Barcode, FolderPlus, ImageOff, ImagePlus, Save, Trash2 } from 'lucide-react';
import { useId, useRef, useState, type ChangeEvent } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import {
  Alert,
  Button,
  Dialog,
  Form,
  FormGrid,
  MoneyField,
  NumberField,
  SelectField,
  Switch,
  TabPanel,
  Tabs,
  TextArea,
  TextField,
  useNotify,
} from '@/4-presentation/panel/kit';
import { formatMoney, formatNumber } from '@/4-presentation/panel/lib';
import {
  FIELD_TAB,
  LIMITS,
  MARGIN_PRESETS,
  draftOf,
  includedTax,
  marginOf,
  marginText,
  netOf,
  newEan13,
  priceForMargin,
  productProblems,
  suggestCategoryCode,
  taxRuleOf,
  toSaveProduct,
  type CatalogOptionsRecord,
  type CatalogRecord,
  type ProductDraft,
  type ProductField,
} from './catalog';
import { ImageProblem, imageDataUrl, prepareImage, type PreparedImage } from './image';
import { hasTechProblems, techDraftOf, techProblems, toSaveTech, type TechDraft } from './specs';
import { TechSheetFields } from './TechSheetFields';

/** Qué se abre: un producto nuevo, uno existente o solo su ficha técnica. */
export interface ProductTarget {
  mode: 'nuevo' | 'editar' | 'ficha';
  row: CatalogRecord | null;
}

export interface ProductDialogProps {
  /** null = cerrado. */
  target: ProductTarget | null;
  options: CatalogOptionsRecord | undefined;
  onClose: () => void;
  onSaved: (sku: string) => void;
  /** Se creó una categoría desde el formulario (la pantalla vuelve a leer las listas). */
  onCategoryCreated: () => void;
}

type DialogTab = 'datos' | 'precios' | 'imagen' | 'ficha' | 'stock';

const TAB_LABELS: Readonly<Record<DialogTab, string>> = {
  datos: 'Datos',
  precios: 'Precios',
  imagen: 'Imágenes',
  ficha: 'Ficha técnica',
  stock: 'Stock mínimo',
};

export function ProductDialog({ target, options, onClose, onSaved, onCategoryCreated }: ProductDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [initial] = useState(target);
  const row = initial?.row ?? null;
  const techOnly = initial?.mode === 'ficha';
  const canTech = canRun('SaveProductTechCommand');
  const canCategory = canRun('SaveCategoryCommand');
  const tax = taxRuleOf(options);

  const [draft, setDraft] = useState<ProductDraft>(() => draftOf(row, options));
  // Si las listas llegan después de abrir un producto nuevo, la unidad toma la de siempre.
  if (!row && !draft.unitCode && options && options.units.length > 0) setDraft((current) => ({ ...current, unitCode: draftOf(null, options).unitCode }));
  const [savedSku, setSavedSku] = useState<string | null>(row?.sku ?? null);
  const isNew = savedSku === null;
  const [tab, setTab] = useState<DialogTab>(techOnly ? 'ficha' : 'datos');
  const [touched, setTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Imagen: la elegida (lista para enviar) o quitar la actual.
  const fileRef = useRef<HTMLInputElement>(null);
  const [pending, setPending] = useState<PreparedImage | null>(null);
  const [imageAction, setImageAction] = useState<'mantener' | 'quitar' | 'quitada'>('mantener');
  const [imageProblem, setImageProblem] = useState<string | null>(null);
  const [reading, setReading] = useState(false);

  // Categoría nueva desde el formulario.
  const [newCategory, setNewCategory] = useState<string | null>(null);
  const [createdCategories, setCreatedCategories] = useState<{ code: string; name: string }[]>([]);

  // Ficha técnica (se arma cuando llega la guardada; un producto nuevo empieza vacía).
  const [techDraft, setTechDraft] = useState<TechDraft | null>(null);
  const [techDirty, setTechDirty] = useState(false);

  const workspace = useRpcQuery('GetWorkspaceQuery', {}, { enabled: !techOnly });
  const warehouseCode = workspace.data?.warehouseCode ?? null;
  const bins = useRpcQuery('GetBinsQuery', { warehouseCode }, { enabled: !techOnly && warehouseCode !== null });
  const currentImage = useRpcQuery('GetProductImagesQuery', { variantIds: row ? [row.variantId] : [] }, { enabled: !techOnly && Boolean(row?.hasImage) });
  const definitionsQuery = useRpcQuery('GetSpecDefinitionsQuery', { categoryCode: draft.categoryCode || null }, { enabled: Boolean(draft.categoryCode), keepPreviousData: false });
  const definitions = draft.categoryCode ? definitionsQuery : null;
  const savedTech = useRpcQuery('GetProductTechQuery', { sku: row?.sku ?? '' }, { enabled: row !== null });
  if (techDraft === null && (row === null || savedTech.data !== undefined)) setTechDraft(techDraftOf(savedTech.data));

  const saveProduct = useRpcCommand('SaveProductCommand', { notifyError: false });
  const setImage = useRpcCommand('SetProductImageCommand', { notifyError: false });
  const removeImage = useRpcCommand('RemoveProductImageCommand', { notifyError: false });
  const saveTech = useRpcCommand('SaveProductTechCommand', { notifyError: false });
  const saveCategory = useRpcCommand('SaveCategoryCommand', { notifyError: false });
  const busy = saveProduct.sending || setImage.sending || removeImage.sending || saveTech.sending;

  const problems = techOnly ? {} : productProblems(draft, isNew);
  const shown = (field: ProductField) => (touched ? problems[field] : undefined);
  const specDefinitions = definitions?.data ?? [];
  const techCheck = canTech && techDirty && techDraft ? techProblems(specDefinitions, techDraft) : null;
  const tabHasError = (id: DialogTab) =>
    touched && (id === 'ficha' ? techCheck !== null && hasTechProblems(techCheck) : (Object.keys(problems) as ProductField[]).some((field) => FIELD_TAB[field] === id));

  const change = <K extends keyof ProductDraft>(field: K, value: ProductDraft[K]) => {
    setDraft((current) => ({ ...current, [field]: value }));
    setError(null);
  };
  const changeTech = (next: TechDraft) => {
    setTechDraft(next);
    setTechDirty(true);
    setError(null);
  };

  // ------------------------------------------------------------------------------------------------ listas
  const categories = [...(options?.categories ?? []), ...createdCategories.filter((created) => !(options?.categories ?? []).some((item) => item.code === created.code))]
    .map((category) => ({ value: category.code, label: category.name }))
    .sort((a, b) => a.label.localeCompare(b.label, 'es'));
  const categoryName = categories.find((category) => category.value === draft.categoryCode)?.label ?? row?.category ?? null;
  const units = (options?.units ?? []).map((unit) => ({ value: unit.code, label: `${unit.name} (${unit.code})` }));
  const unitAllowsDecimals = options?.units.find((unit) => unit.code === draft.unitCode)?.allowsDecimals ?? false;
  const suppliers = (options?.suppliers ?? []).map((supplier) => ({ value: supplier.code, label: supplier.name }));
  const binOptions = (bins.data ?? []).map((bin) => ({ value: bin.code, label: bin.zone ? `${bin.code} · ${bin.zone}` : bin.code }));
  if (draft.binCode && !binOptions.some((bin) => bin.value === draft.binCode)) binOptions.unshift({ value: draft.binCode, label: draft.binCode });

  // ------------------------------------------------------------------------------------------------ categoría nueva
  const existingCodes = categories.map((category) => category.value);
  const newCategoryCode = newCategory && newCategory.trim() ? suggestCategoryCode(newCategory, existingCodes) : '';
  const createCategory = async () => {
    const name = (newCategory ?? '').trim();
    if (!name) return;
    const outcome = await saveCategory.run({ code: newCategoryCode, name, parentCode: null });
    if (!outcome.ok) return;
    setCreatedCategories((current) => [...current, { code: outcome.result, name }]);
    change('categoryCode', outcome.result);
    setNewCategory(null);
    notify.success('Categoría creada', `${name} (${outcome.result})`);
    onCategoryCreated();
  };

  // ------------------------------------------------------------------------------------------------ imagen
  const currentPicture = currentImage.data?.[0];
  const preview = pending ? pending.dataUrl : imageAction === 'mantener' && currentPicture ? imageDataUrl(currentPicture.contentType, currentPicture.content) : null;
  const hasPicture = pending !== null || (imageAction === 'mantener' && Boolean(row?.hasImage));
  const pickFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    setImageProblem(null);
    setReading(true);
    try {
      setPending(await prepareImage(file));
      setError(null);
    } catch (caught) {
      setImageProblem(caught instanceof ImageProblem ? caught.message : 'No se pudo leer la imagen elegida.');
    } finally {
      setReading(false);
    }
  };
  const dropPicture = () => {
    setPending(null);
    if (row?.hasImage && imageAction === 'mantener') setImageAction('quitar');
  };

  // ------------------------------------------------------------------------------------------------ margen (guía)
  const margin = draft.salePrice !== null && draft.unitCost !== null ? marginOf(draft.salePrice, draft.unitCost, tax) : null;
  const net = draft.salePrice !== null && draft.salePrice > 0 ? netOf(draft.salePrice, tax) : null;
  const applyMargin = (value: string) => {
    const percent = Number(value);
    if (!(percent > 0)) return;
    if (!(draft.unitCost !== null && draft.unitCost > 0)) {
      setError('Indique primero el costo: el precio se calcula a partir de él.');
      return;
    }
    change('salePrice', priceForMargin(draft.unitCost, percent / 100, tax));
  };

  // ------------------------------------------------------------------------------------------------ guardar
  const submit = async () => {
    setTouched(true);
    const firstField = (Object.keys(problems) as ProductField[])[0];
    if (firstField) {
      setTab(FIELD_TAB[firstField]);
      return;
    }
    const withTech = canTech && techDirty && techDraft !== null && definitions?.data !== undefined;
    if (withTech && hasTechProblems(techProblems(specDefinitions, techDraft))) {
      setTab('ficha');
      return;
    }
    if (techOnly && !withTech) {
      notify.info('Sin cambios', 'La ficha técnica quedó como estaba.');
      onClose();
      return;
    }
    setError(null);
    let sku = savedSku;
    if (!techOnly) {
      const saved = await saveProduct.run(toSaveProduct(savedSku, draft));
      if (!saved.ok) {
        setError(saved.message);
        return;
      }
      sku = saved.result;
      setSavedSku(saved.result);
      if (pending) {
        const outcome = await setImage.run({ sku: saved.result, content: pending.content, contentType: pending.contentType, fileName: pending.fileName });
        if (!outcome.ok) {
          setError(`El producto ${saved.result} se guardó, pero la imagen no: ${outcome.message}`);
          setTab('imagen');
          return;
        }
        setPending(null);
        setImageAction('mantener');
      } else if (imageAction === 'quitar' && row?.hasImage) {
        const outcome = await removeImage.run({ sku: saved.result });
        if (!outcome.ok) {
          setError(`El producto ${saved.result} se guardó, pero la imagen no se quitó: ${outcome.message}`);
          setTab('imagen');
          return;
        }
        setImageAction('quitada');
      }
    }
    if (withTech && sku) {
      const outcome = await saveTech.run(toSaveTech(sku, specDefinitions, techDraft));
      if (!outcome.ok) {
        setError(techOnly ? outcome.message : `El producto ${sku} se guardó, pero la ficha técnica no: ${outcome.message}`);
        setTab('ficha');
        return;
      }
      setTechDirty(false);
    }
    if (!sku) return;
    notify.success(techOnly ? 'Ficha técnica guardada' : row ? 'Producto actualizado' : 'Producto creado', `${sku} · ${draft.name.trim()}`);
    onSaved(sku);
    onClose();
  };

  const tabs = (techOnly ? (['ficha'] as const) : (['datos', 'precios', 'imagen', 'ficha', 'stock'] as const)).map((id) => ({
    id,
    label: tabHasError(id) ? `${TAB_LABELS[id]} (revisar)` : TAB_LABELS[id],
  }));

  const title = techOnly ? 'Ficha técnica' : row ? 'Editar producto' : 'Nuevo producto';
  const saveLabel = techOnly ? 'Guardar ficha técnica' : row ? 'Guardar cambios' : 'Crear producto';

  const techFields = (
    <>
      {savedTech.error && techDraft === null ? (
        <Alert
          tone="danger"
          title="No se pudo leer la ficha técnica guardada"
          actions={
            <Button variant="outline" onClick={savedTech.reload}>
              Reintentar
            </Button>
          }
        >
          Sin ella no se puede editar: guardar la reemplazaría.
        </Alert>
      ) : (
        <TechSheetFields
          categoryName={categoryName}
          definitions={definitions}
          draft={techDraft}
          saved={savedTech.data}
          onChange={changeTech}
          problems={touched ? techCheck : null}
          canEdit={canTech}
        />
      )}
    </>
  );

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!busy}
      size="xl"
      title={title}
      description={row ? `${row.sku} · ${row.name}` : 'El SKU y la unidad no cambian después de crear el producto.'}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={busy} disabled={reading}>
            {saveLabel}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={error} busy={busy}>
        {techOnly ? (
          techFields
        ) : (
          <Tabs label="Secciones del producto" value={tab} onChange={setTab} tabs={tabs}>
            <TabPanel id="datos">
              <div className="space-y-4">
                <FormGrid>
                  <TextField
                    label="SKU"
                    value={draft.sku}
                    onChange={(value) => change('sku', value.toUpperCase())}
                    error={shown('sku')}
                    required={isNew}
                    disabled={!isNew}
                    maxLength={LIMITS.sku}
                    autoComplete="off"
                    spellCheck={false}
                    hint={isNew ? 'Letras, números y guiones (sin espacios). No cambia después de crear el producto.' : 'No cambia: para otro SKU se crea otro producto.'}
                    data-autofocus={isNew ? true : undefined}
                  />
                  <TextField label="Nombre" value={draft.name} onChange={(value) => change('name', value)} error={shown('name')} required maxLength={LIMITS.name} />
                  <div className="space-y-2">
                    <SelectField
                      label="Categoría"
                      allLabel={false}
                      placeholder="Elija una categoría"
                      value={draft.categoryCode}
                      onChange={(value) => change('categoryCode', value)}
                      options={categories}
                      error={shown('categoryCode')}
                      required
                      hint="De ella salen las especificaciones de la ficha técnica."
                    />
                    {canCategory && newCategory === null && (
                      <Button variant="subtle" leftIcon={<FolderPlus />} onClick={() => setNewCategory('')}>
                        Crear una categoría nueva
                      </Button>
                    )}
                  </div>
                  <SelectField
                    label="Unidad"
                    allLabel={false}
                    placeholder="Elija la unidad"
                    value={draft.unitCode}
                    onChange={(value) => change('unitCode', value)}
                    options={units}
                    error={shown('unitCode')}
                    required={isNew}
                    disabled={!isNew}
                    hint={isNew ? undefined : 'La unidad no cambia después de crear el producto.'}
                  />
                </FormGrid>
                {newCategory !== null && (
                  <div className="space-y-3 rounded-xl border border-border bg-surface-2 p-3" data-testid="categoria-nueva">
                    <TextField
                      label="Nombre de la categoría nueva"
                      value={newCategory}
                      onChange={(value) => {
                        setNewCategory(value);
                        saveCategory.reset();
                      }}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                          event.preventDefault();
                          void createCategory();
                        }
                      }}
                      maxLength={80}
                      hint={newCategoryCode ? `Código: ${newCategoryCode}. Queda disponible para todos los productos.` : 'Queda disponible para todos los productos.'}
                      autoFocus
                    />
                    {saveCategory.errorText && <Alert tone="danger">{saveCategory.errorText}</Alert>}
                    <div className="flex flex-wrap gap-2">
                      <Button variant="outline" onClick={() => setNewCategory(null)} disabled={saveCategory.sending}>
                        Cancelar
                      </Button>
                      <Button leftIcon={<FolderPlus />} loading={saveCategory.sending} disabled={!newCategory.trim()} onClick={() => void createCategory()}>
                        Crear categoría
                      </Button>
                    </div>
                  </div>
                )}
                <FormGrid>
                  <SelectField
                    label="Proveedor preferido"
                    allLabel="Sin proveedor preferido"
                    value={draft.supplierCode}
                    onChange={(value) => change('supplierCode', value)}
                    options={suppliers}
                  />
                  <SelectField
                    label="Posición en el almacén"
                    allLabel={isNew ? 'La posición general del almacén' : 'Sin cambiar la posición'}
                    value={draft.binCode}
                    onChange={(value) => change('binCode', value)}
                    options={binOptions}
                    hint={workspace.data ? `Posiciones de ${workspace.data.warehouseName} (${workspace.data.warehouseCode}).` : undefined}
                  />
                  <div className="space-y-2">
                    <TextField
                      label="Código de barras"
                      value={draft.barcode}
                      onChange={(value) => change('barcode', value)}
                      error={shown('barcode')}
                      optional
                      maxLength={LIMITS.barcode}
                      inputMode="numeric"
                      autoComplete="off"
                      hint="EAN-13 del fabricante o uno interno. El lector de códigos lo escribe solo."
                    />
                    <Button variant="subtle" leftIcon={<Barcode />} onClick={() => change('barcode', newEan13())}>
                      Generar un código interno
                    </Button>
                  </div>
                  <Switch
                    label="Producto activo"
                    description="Se vende en la caja, aparece en el pedido sugerido y en la tienda."
                    checked={draft.isActive}
                    onChange={(value) => change('isActive', value)}
                  />
                </FormGrid>
                <TextArea label="Descripción" value={draft.description} onChange={(value) => change('description', value)} error={shown('description')} maxLength={LIMITS.description} optional rows={3} />
              </div>
            </TabPanel>

            <TabPanel id="precios">
              <div className="space-y-4">
                <FormGrid>
                  <MoneyField
                    label="Costo unitario"
                    value={draft.unitCost}
                    onChange={(value) => change('unitCost', value)}
                    error={shown('unitCost')}
                    required
                    hint="Costo promedio del almacén de trabajo; cada cambio queda en su historial."
                  />
                  <MoneyField
                    label="Precio de venta (IVA incluido)"
                    value={draft.salePrice}
                    onChange={(value) => change('salePrice', value)}
                    error={shown('salePrice')}
                    required
                    hint={net === null ? `El precio incluye el IVA (${formatNumber(tax.rate)} %). Con 0 queda «Sin precio».` : `Sin IVA: ${formatMoney(net)} · IVA ${formatMoney(includedTax(draft.salePrice ?? 0, tax))}`}
                  />
                  <SelectField label="Aplicar un margen" allLabel={false} placeholder="Elija un margen…" value="" onChange={applyMargin} options={MARGIN_PRESETS} hint="Calcula el precio con IVA a partir del costo." />
                </FormGrid>
                <Alert tone={margin !== null && margin < 0.15 ? 'warning' : 'info'} title="Margen (guía)">
                  {margin === null || net === null
                    ? 'Indique costo y precio para ver el margen.'
                    : `Margen ${marginText(margin)} · ganancia ${formatMoney(net - (draft.unitCost ?? 0))} por unidad (sin IVA).`}
                </Alert>
                <p className="text-sm text-text-muted">
                  Lista de precios {options?.priceListName ?? '—'} · IVA {formatNumber(tax.rate)} % incluido en el precio.
                </p>
              </div>
            </TabPanel>

            <TabPanel id="imagen">
              <div className="space-y-4">
                <div className="flex aspect-[4/3] max-w-md items-center justify-center overflow-hidden rounded-card border border-border bg-surface-2">
                  {preview ? (
                    <img src={preview} alt={`Imagen de ${draft.name || 'el producto'}`} className="size-full object-contain" />
                  ) : (
                    <span className="flex flex-col items-center gap-1 text-sm text-text-muted">
                      <ImageOff aria-hidden="true" className="size-8" />
                      {row?.hasImage && imageAction === 'mantener' && currentImage.loading ? 'Cargando la imagen…' : 'Sin imagen'}
                    </span>
                  )}
                </div>
                <input ref={fileRef} type="file" accept="image/png,image/jpeg,image/*" className="sr-only" tabIndex={-1} aria-label="Archivo de la imagen del producto" onChange={(event) => void pickFile(event)} />
                <div className="flex flex-wrap gap-2">
                  <Button variant="outline" leftIcon={<ImagePlus />} loading={reading} onClick={() => fileRef.current?.click()}>
                    {hasPicture ? 'Cambiar imagen…' : 'Elegir imagen…'}
                  </Button>
                  {hasPicture && (
                    <Button variant="ghost" leftIcon={<Trash2 />} onClick={dropPicture}>
                      Quitar imagen
                    </Button>
                  )}
                </div>
                <p className="text-sm text-text-muted">PNG o JPEG. Las fotos grandes se reducen solas antes de enviarlas (máximo 1 MB). Se guarda al guardar el producto.</p>
                {pending && (
                  <p className="text-sm text-text" data-testid="imagen-pendiente">
                    Imagen nueva lista: {pending.fileName} ({formatNumber(Math.max(1, Math.round(pending.bytes / 1024)))} KB).
                  </p>
                )}
                {imageAction === 'quitar' && !pending && <p className="text-sm text-text">La imagen actual se quita al guardar.</p>}
                {imageProblem && <Alert tone="danger">{imageProblem}</Alert>}
              </div>
            </TabPanel>

            <TabPanel id="ficha">{techFields}</TabPanel>

            <TabPanel id="stock">
              <div className="space-y-4">
                <FormGrid>
                  <NumberField
                    label="Stock mínimo"
                    value={draft.minimum}
                    onChange={(value) => change('minimum', value)}
                    decimals={unitAllowsDecimals ? 3 : 0}
                    error={shown('minimum')}
                    required
                    hint="Por debajo, el producto entra en alerta y en el pedido sugerido."
                  />
                  <NumberField
                    label="Stock máximo"
                    value={draft.maximum}
                    onChange={(value) => change('maximum', value)}
                    decimals={unitAllowsDecimals ? 3 : 0}
                    error={shown('maximum')}
                    required
                    hint="Hasta cuánto reponer. Con 0, sin máximo."
                  />
                </FormGrid>
                <p className="text-sm text-text-muted">
                  Se aplican al almacén de trabajo de la sucursal activa{workspace.data ? `: ${workspace.data.warehouseName} (${workspace.data.warehouseCode})` : ''}.
                </p>
              </div>
            </TabPanel>
          </Tabs>
        )}
      </Form>
    </Dialog>
  );
}
