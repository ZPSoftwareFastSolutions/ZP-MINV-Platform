// ============================================================================================================
// M-INV V2 · Bloque común de los Office Scripts · Z&P Software Fast Solutions
// Fuente única: src/office-scripts/lib/comun.ts. Office Scripts no permite importar módulos, así que
// tools/office_scripts.py copia este bloque dentro de cada script (sync) y verifica que no se desvíe (check).
// ============================================================================================================

/** Contraseña de las hojas protegidas: tools/office_scripts.py la inyecta al generar la versión instalable. */
const CLAVE = "__MINV_PASSWORD__";
const HOJA_SESION = "92_SESION";
const PREFIJO_OK = "✔";
const ESTADO_OK = "✔ Consolidado";
const SEPARADOR = " · ";
const MAX_DOCUMENTO = 30;
const MAX_OBSERVACIONES = 250;

type Celda = string | number | boolean;

interface Identidad {
  correo: string;
  nombre: string;
}

interface Usuario {
  correo: string;
  nombre: string;
  rol: string;
  activo: boolean;
}

interface Producto {
  indice: number;
  sku: string;
  nombre: string;
  categoria: string;
  proveedor: string;
  unidad: string;
  minimo: number;
  maximo: number;
  costo: number;
  activo: boolean;
}

interface TablaLeida {
  tabla: ExcelScript.Table;
  encabezados: string[];
  filas: Celda[][];
}

interface Movimiento {
  tipo: string;
  factor: number;
  fecha: number;
  producto: Producto;
  cantidad: number;
  documento: string;
  observaciones: string;
}

// ---------------------------------------------------------------------------------------------- utilidades
function texto(v: Celda): string {
  return v === undefined || v === null ? "" : String(v).trim();
}

function numero(v: Celda): number {
  if (typeof v === "number") {
    return v;
  }
  const t = texto(v);
  return t === "" || typeof v === "boolean" ? NaN : Number(t.replace(",", "."));
}

function r6(x: number): number {
  return Math.round(x * 1e6) / 1e6;
}

function dos(n: number): string {
  return (n < 10 ? "0" : "") + n;
}

/** Fecha y hora local como número de serie de Excel (lo que se ve en la celda con formato de fecha). */
function serialDe(d: Date): number {
  return (d.getTime() - d.getTimezoneOffset() * 60000) / 86400000 + 25569;
}

function horaDe(d: Date): string {
  return dos(d.getHours()) + ":" + dos(d.getMinutes());
}

/** ID sin coordinación: fragmento-fecha-hora-aleatorio. Dos registros simultáneos nunca comparten ID. */
function nuevoId(prefijo: string, d: Date): string {
  const aleatorio = ("0000" + Math.floor(Math.random() * 65536).toString(16).toUpperCase()).slice(-4);
  return prefijo + "-" + d.getFullYear() + dos(d.getMonth() + 1) + dos(d.getDate()) + "-" +
    dos(d.getHours()) + dos(d.getMinutes()) + dos(d.getSeconds()) + "-" + aleatorio;
}

function mensajeDe(error: Error | string): string {
  const m = typeof error === "string" ? error : error.message;
  return (m || String(error)).replace(/^Error:\s*/, "");
}

function hoja(workbook: ExcelScript.Workbook, nombre: string): ExcelScript.Worksheet {
  const h = workbook.getWorksheet(nombre);
  if (!h) {
    throw new Error("Falta la hoja " + nombre + ": ¿es el libro M-INV V2?");
  }
  return h;
}

function leerTabla(workbook: ExcelScript.Workbook, nombre: string): TablaLeida {
  const tabla = workbook.getTable(nombre);
  if (!tabla) {
    throw new Error("Falta la tabla " + nombre + ": ¿es el libro M-INV V2?");
  }
  const encabezados = tabla.getHeaderRowRange().getValues()[0].map((v: Celda) => texto(v));
  const filas = tabla.getRowCount() > 0 ? tabla.getRangeBetweenHeaderAndTotal().getValues() : [];
  return { tabla, encabezados, filas };
}

function columna(t: TablaLeida, nombre: string): number {
  const i = t.encabezados.indexOf(nombre);
  if (i < 0) {
    throw new Error("Falta la columna " + nombre + " en " + t.tabla.getName() + ".");
  }
  return i;
}

/** Lee solo algunas columnas de una tabla grande (menos datos viajan desde el servidor). */
function leerColumnas(workbook: ExcelScript.Workbook, nombre: string, columnas: string[]): Celda[][] {
  const tabla = workbook.getTable(nombre);
  if (!tabla) {
    throw new Error("Falta la tabla " + nombre + ": ¿es el libro M-INV V2?");
  }
  if (tabla.getRowCount() === 0) {
    return columnas.map(() => []);
  }
  return columnas.map((c: string) => {
    const col = tabla.getColumnByName(c);
    if (!col) {
      throw new Error("Falta la columna " + c + " en " + nombre + ".");
    }
    return col.getRangeBetweenHeaderAndTotal().getValues().map((f: Celda[]) => f[0]);
  });
}

function valorNombre(workbook: ExcelScript.Workbook, nombre: string): Celda {
  const n = workbook.getNamedItem(nombre);
  if (!n) {
    throw new Error("Falta el nombre definido " + nombre + ".");
  }
  return n.getRange().getValue() as Celda;
}

function escribirNombre(workbook: ExcelScript.Workbook, nombre: string, valor: Celda): void {
  const n = workbook.getNamedItem(nombre);
  if (!n) {
    throw new Error("Falta el nombre definido " + nombre + ".");
  }
  n.getRange().setValue(valor);
}

// ---------------------------------------------------------------------------------------------- identidad
/**
 * Correo de Microsoft 365 de quien ejecuta el script. La API de Office Scripts no lo expone directamente:
 * se crea un comentario temporal en la hoja técnica 92_SESION (su autor es el usuario actual), se lee
 * getAuthorEmail() y se borra. Se usa una celda aleatoria para que dos sesiones simultáneas no choquen.
 */
function identificarUsuario(workbook: ExcelScript.Workbook): Identidad {
  hoja(workbook, HOJA_SESION);
  for (let intento = 0; intento < 3; intento++) {
    const celda = "'" + HOJA_SESION + "'!C" + (2 + Math.floor(Math.random() * 4000));
    try {
      const comentario = workbook.addComment(celda, "M-INV · identificación de sesión (se borra sola)");
      const correo = texto(comentario.getAuthorEmail()).toLowerCase();
      const nombre = texto(comentario.getAuthorName());
      comentario.delete();
      if (correo.indexOf("@") > 0) {
        return { correo, nombre };
      }
    } catch (error) {
      console.log("Identificación: reintento (" + mensajeDe(error as Error) + ")");
    }
  }
  throw new Error("No se pudo identificar su cuenta de Microsoft 365. Abra el libro con su cuenta de trabajo " +
    "(no como invitado anónimo) y vuelva a intentar.");
}

function buscarUsuario(workbook: ExcelScript.Workbook, correo: string): Usuario | undefined {
  const t = leerTabla(workbook, "tblUsuarios");
  const cC = columna(t, "Correo"), cN = columna(t, "Nombre"), cR = columna(t, "Rol"), cA = columna(t, "Activo");
  for (const f of t.filas) {
    if (texto(f[cC]).toLowerCase() === correo) {
      return {
        correo, nombre: texto(f[cN]), rol: texto(f[cR]).toUpperCase(), activo: texto(f[cA]).toUpperCase() !== "NO"
      };
    }
  }
  return undefined;
}

/** RBAC: la cuenta debe estar activa en 02_USUARIOS con uno de los roles del fragmento. */
function autorizar(usuario: Usuario | undefined, correo: string, roles: string[], accion: string): Usuario {
  if (!usuario) {
    throw new Error("su cuenta (" + correo + ") no está autorizada en 02_USUARIOS: pida acceso al administrador");
  }
  if (!usuario.activo) {
    throw new Error("su usuario está inactivo en 02_USUARIOS");
  }
  if (roles.indexOf(usuario.rol) < 0) {
    throw new Error("su rol (" + usuario.rol + ") no permite " + accion);
  }
  return usuario;
}

// ---------------------------------------------------------------------------------------------- protección
/**
 * Ejecuta `accion` con las hojas desbloqueadas solo para esta sesión (pauseProtection) o, si el anfitrión no lo
 * permite, desprotegiendo y volviendo a proteger con las mismas opciones. Siempre restaura (finally).
 */
function conHojasDesbloqueadas<T>(hojas: ExcelScript.Worksheet[], accion: () => T): T {
  const pausadas: ExcelScript.Worksheet[] = [];
  const desprotegidas: ExcelScript.Worksheet[] = [];
  const opciones: ExcelScript.WorksheetProtectionOptions[] = [];
  try {
    for (const h of hojas) {
      const p = h.getProtection();
      if (!p.getProtected()) {
        continue;
      }
      let pausada = false;
      try {
        p.pauseProtection(CLAVE);
        pausada = p.getIsPaused();
      } catch (error) {
        pausada = false;
      }
      if (pausada) {
        pausadas.push(h);
        continue;
      }
      const o = p.getOptions();
      try {
        p.unprotect(CLAVE);
      } catch (error) {
        throw new Error("no se pudo desbloquear " + h.getName() + ": la contraseña del script no coincide con la " +
          "del libro (reinstale los scripts con tools/office_scripts.py)");
      }
      desprotegidas.push(h);
      opciones.push(o);
    }
    return accion();
  } finally {
    for (const h of pausadas) {
      try {
        h.getProtection().resumeProtection();
      } catch (error) {
        console.log("Aviso: no se pudo reanudar la protección de " + h.getName());
      }
    }
    for (let i = 0; i < desprotegidas.length; i++) {
      try {
        desprotegidas[i].getProtection().protect(opciones[i], CLAVE);
      } catch (error) {
        console.log("Aviso: no se pudo volver a proteger " + desprotegidas[i].getName());
      }
    }
  }
}

// ---------------------------------------------------------------------------------------------- catálogo
function leerProductos(workbook: ExcelScript.Workbook): Producto[] {
  const t = leerTabla(workbook, "tblProductos");
  const c = (n: string): number => columna(t, n);
  const cS = c("SKU"), cP = c("Producto"), cC = c("Categoría"), cV = c("Proveedor"), cU = c("Unidad");
  const cMin = c("StockMin"), cMax = c("StockMax"), cCo = c("CostoUnitario"), cA = c("Activo");
  const lista: Producto[] = [];
  t.filas.forEach((f: Celda[], i: number) => {
    const sku = texto(f[cS]);
    if (sku !== "") {
      lista.push({
        indice: i, sku, nombre: texto(f[cP]), categoria: texto(f[cC]), proveedor: texto(f[cV]),
        unidad: texto(f[cU]), minimo: numero(f[cMin]) || 0, maximo: numero(f[cMax]) || 0,
        costo: numero(f[cCo]) || 0, activo: texto(f[cA]).toUpperCase() !== "NO"
      });
    }
  });
  return lista;
}

function admiteDecimales(workbook: ExcelScript.Workbook, unidad: string): boolean {
  const t = leerTabla(workbook, "tblUnidades");
  const cC = columna(t, "Código"), cD = columna(t, "Decimales");
  for (const f of t.filas) {
    if (texto(f[cC]) === unidad) {
      return texto(f[cD]).toUpperCase() === "SI";
    }
  }
  return true;
}

function skuDeEtiqueta(etiqueta: string): string {
  const p = etiqueta.indexOf(SEPARADOR);
  return (p >= 0 ? etiqueta.substring(0, p) : etiqueta).trim();
}

/** Disponible exacto: suma de CantidadNeta consolidada (✔) de las dos bitácoras oficiales. */
function disponible(workbook: ExcelScript.Workbook, sku: string): number {
  let total = 0;
  for (const nombre of ["tblEntradas", "tblSalidas"]) {
    const [skus, netas, estados] = leerColumnas(workbook, nombre, ["SKU", "CantidadNeta", "Estado"]);
    for (let i = 0; i < skus.length; i++) {
      if (texto(skus[i]) === sku && texto(estados[i]).indexOf(PREFIJO_OK) === 0) {
        total += numero(netas[i]) || 0;
      }
    }
  }
  return r6(total);
}

// ---------------------------------------------------------------------------------------------- captura
interface FilaCaptura {
  leida: TablaLeida;
  indice: number;
  valores: Celda[];
}

/** La fila de captura del usuario (la columna oculta Correo la asigna 02_USUARIOS). */
function filaDeCaptura(workbook: ExcelScript.Workbook, tabla: string, correo: string, hojaNombre: string): FilaCaptura {
  const t = leerTabla(workbook, tabla);
  const cC = columna(t, "Correo");
  for (let i = 0; i < t.filas.length; i++) {
    if (texto(t.filas[i][cC]).toLowerCase() === correo) {
      return { leida: t, indice: i, valores: t.filas[i] };
    }
  }
  throw new Error("Su cuenta (" + correo + ") no tiene fila de captura en " + hojaNombre + ": el administrador debe " +
    "registrarla en 02_USUARIOS con el rol correspondiente.");
}

function escribirCaptura(c: FilaCaptura, cambios: Map<string, Celda>): void {
  const cuerpo = c.leida.tabla.getRangeBetweenHeaderAndTotal();
  cambios.forEach((valor: Celda, col: string) => {
    cuerpo.getCell(c.indice, columna(c.leida, col)).setValue(valor);
  });
}

/** Valida la fila de captura con las mismas reglas que la columna Validación (el script no confía en fórmulas). */
function validarCaptura(workbook: ExcelScript.Workbook, c: FilaCaptura, dominio: string, hoy: number): Movimiento {
  const v = (n: string): Celda => c.valores[columna(c.leida, n)];
  const tipo = dominio === "VENTAS" ? "SALIDA" : texto(v("Tipo")).toUpperCase();
  if (tipo === "") {
    throw new Error("elija el tipo de movimiento");
  }
  const tipos = leerTabla(workbook, "tblTiposMov");
  const cT = columna(tipos, "Tipo"), cF = columna(tipos, "FactorStock"), cD = columna(tipos, "Dominio");
  let factor = 0;
  for (const f of tipos.filas) {
    if (texto(f[cT]).toUpperCase() === tipo && texto(f[cD]).toUpperCase() === dominio) {
      factor = numero(f[cF]);
    }
  }
  if (factor !== 1 && factor !== -1) {
    throw new Error("el tipo " + tipo + " no es válido en este fragmento");
  }
  const etiqueta = texto(v("Producto"));
  if (etiqueta === "") {
    throw new Error("elija el producto");
  }
  const sku = skuDeEtiqueta(etiqueta);
  const producto = leerProductos(workbook).filter((p: Producto) => p.sku === sku)[0];
  if (!producto) {
    throw new Error("el producto " + sku + " no existe en el catálogo");
  }
  if (!producto.activo) {
    throw new Error("el producto " + sku + " está inactivo");
  }
  const cantidad = numero(v("Cantidad"));
  if (!(cantidad > 0)) {
    throw new Error("la cantidad debe ser un número mayor que 0");
  }
  if (cantidad !== Math.floor(cantidad) && !admiteDecimales(workbook, producto.unidad)) {
    throw new Error("la unidad " + producto.unidad + " no admite decimales");
  }
  let fecha = hoy;
  const f0 = v("Fecha");
  if (texto(f0) !== "") {
    fecha = Math.floor(numero(f0));
    const minima = Math.floor(numero(valorNombre(workbook, "cfgFechaMin")) || 0);
    if (!(fecha > 0) || fecha > hoy || fecha < minima) {
      throw new Error("la fecha no es válida (futura o anterior a la mínima)");
    }
  }
  const documento = texto(v("Documento"));
  const observaciones = texto(v("Observaciones"));
  if (documento.length > MAX_DOCUMENTO) {
    throw new Error("el documento supera " + MAX_DOCUMENTO + " caracteres");
  }
  if (observaciones.length > MAX_OBSERVACIONES) {
    throw new Error("las observaciones superan " + MAX_OBSERVACIONES + " caracteres");
  }
  if (tipo.indexOf("AJUSTE") === 0 && observaciones === "") {
    throw new Error("los ajustes exigen una observación (motivo)");
  }
  if (tipo === "SALDO INICIAL") {
    const [skus, tiposE, estados] = leerColumnas(workbook, "tblEntradas", ["SKU", "Tipo", "Estado"]);
    for (let i = 0; i < skus.length; i++) {
      if (texto(skus[i]) === sku && texto(tiposE[i]) === "SALDO INICIAL" && texto(estados[i]).indexOf(PREFIJO_OK) === 0) {
        throw new Error(sku + " ya tiene SALDO INICIAL: use un AJUSTE");
      }
    }
  }
  return { tipo, factor, fecha, producto, cantidad, documento, observaciones };
}

// ---------------------------------------------------------------------------------------------- bitácora
/** Agrega un registro al final de la bitácora oficial (inserción atómica del servidor). */
function agregarFila(tabla: ExcelScript.Table, valores: Celda[]): void {
  if (tabla.getRowCount() === 1) {
    const unica = tabla.getRangeBetweenHeaderAndTotal();
    if (unica.getValues()[0].every((x: Celda) => texto(x) === "")) {
      unica.setValues([valores]);
      return;
    }
  }
  tabla.addRow(-1, valores);
}

function marcarRechazo(tabla: ExcelScript.Table, id: string, motivo: string): void {
  const t: TablaLeida = {
    tabla, encabezados: tabla.getHeaderRowRange().getValues()[0].map((x: Celda) => texto(x)),
    filas: tabla.getRangeBetweenHeaderAndTotal().getValues()
  };
  const cId = columna(t, "ID"), cE = columna(t, "Estado");
  for (let i = t.filas.length - 1; i >= 0; i--) {
    if (texto(t.filas[i][cId]) === id) {
      tabla.getRangeBetweenHeaderAndTotal().getCell(i, cE).setValue("✖ Rechazado: " + motivo);
      return;
    }
  }
}

/**
 * Comando común de registro: identifica al usuario, autoriza, valida su fila de captura, agrega el movimiento a la
 * bitácora oficial con Usuario_O365 y Timestamp, vuelve a verificar el stock (registros simultáneos) y deja el
 * resultado en la fila de captura. Devuelve el mensaje mostrado.
 */
function registrar(workbook: ExcelScript.Workbook, dominio: string, hojaNombre: string, tablaCaptura: string,
  tablaBitacora: string, prefijo: string, roles: string[], limpiar: string[]): string {
  const ahora = new Date();
  const hojaMov = hoja(workbook, hojaNombre);
  const yo = identificarUsuario(workbook);
  const captura = filaDeCaptura(workbook, tablaCaptura, yo.correo, hojaNombre);
  let mensaje = "";
  let cambios = new Map<string, Celda>();
  try {
    const usuario = autorizar(buscarUsuario(workbook, yo.correo), yo.correo, roles,
      dominio === "VENTAS" ? "registrar salidas" : "registrar movimientos de bodega");
    const mov = validarCaptura(workbook, captura, dominio, Math.floor(serialDe(ahora)));
    const antes = disponible(workbook, mov.producto.sku);
    const neta = r6(mov.cantidad * mov.factor);
    if (antes + neta < 0) {
      throw new Error("stock insuficiente: disponible " + antes + " " + mov.producto.unidad);
    }
    const bitacora = workbook.getTable(tablaBitacora);
    if (!bitacora) {
      throw new Error("falta la tabla " + tablaBitacora);
    }
    const id = nuevoId(prefijo, ahora);
    const valores = new Map<string, Celda>([
      ["ID", id], ["Tipo", mov.tipo], ["Fecha", mov.fecha], ["Producto", mov.producto.sku + SEPARADOR + mov.producto.nombre],
      ["Cantidad", mov.cantidad], ["Documento", mov.documento], ["Observaciones", mov.observaciones],
      ["CantidadNeta", neta], ["Estado", ESTADO_OK], ["Registró", usuario.nombre || yo.nombre],
      ["Unidad", mov.producto.unidad], ["FactorStock", mov.factor], ["Usuario_O365", yo.correo],
      ["SKU", mov.producto.sku], ["Timestamp", serialDe(ahora)]
    ]);
    const encabezados = bitacora.getHeaderRowRange().getValues()[0].map((x: Celda) => texto(x));
    const fila: Celda[] = encabezados.map((e: string) => (valores.has(e) ? valores.get(e) as Celda : ""));
    mensaje = conHojasDesbloqueadas([hojaMov], () => {
      agregarFila(bitacora, fila);
      if (neta < 0) {
        const despues = disponible(workbook, mov.producto.sku);
        if (despues < 0) {
          marcarRechazo(bitacora, id, "stock insuficiente por un registro simultáneo");
          escribirCaptura(captura, new Map<string, Celda>([["Resultado",
            "✖ Bloqueado: otra persona registró antes y el stock no alcanza (disponible " + r6(despues - neta) + ")"]]));
          return "✖ Rechazado " + id + ": stock insuficiente por un registro simultáneo";
        }
      }
      const saldo = r6(antes + neta);
      const ok = "✔ Registrado " + id + " · " + horaDe(ahora) + " · stock " + saldo + " " + mov.producto.unidad;
      const limpieza = new Map<string, Celda>([["Resultado", ok]]);
      for (const c of limpiar) {
        limpieza.set(c, "");
      }
      escribirCaptura(captura, limpieza);
      return ok;
    });
  } catch (error) {
    mensaje = "✖ Bloqueado: " + mensajeDe(error as Error);
    cambios = new Map<string, Celda>([["Resultado", mensaje]]);
    conHojasDesbloqueadas([hojaMov], () => escribirCaptura(captura, cambios));
  }
  console.log(mensaje);
  return mensaje;
}
