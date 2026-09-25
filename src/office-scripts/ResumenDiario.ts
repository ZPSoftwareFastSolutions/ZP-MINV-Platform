/**
 * M-INV V2.1 · ResumenDiario.ts — resumen del día para Power Automate (SOLO LECTURA)
 * Z&P Software Fast Solutions
 *
 * Pensado para un flujo programado de Power Automate («Ejecutar script» de Excel Online (Business) + «Enviar un
 * correo (V2)»), aunque también se puede ejecutar a mano desde Automatizar. No modifica el libro, así que se puede
 * programar sin riesgo. Calcula con el stock EXACTO de ese momento (las dos bitácoras, no la instantánea):
 *   - movimientos registrados hoy (entradas y ajustes, salidas y unidades vendidas) y registros rechazados;
 *   - intentos bloqueados hoy (14_ACTIVIDAD);
 *   - productos que requieren acción (agotados, críticos y bajos) con la cantidad a pedir y el total estimado;
 *   - frescura de la instantánea (cuándo se recalculó y cuántos movimientos llegaron después).
 * Devuelve el asunto, el cuerpo en HTML y en texto y los indicadores (campos dinámicos del flujo).
 */
function main(workbook: ExcelScript.Workbook): ResumenDelDia {
  const ahora = new Date();
  const hoy = Math.floor(serialDe(ahora));
  const empresa = texto(valorOpcional(workbook, "cfgEmpresa"));
  const margen = numero(valorNombre(workbook, "cfgMargenAlerta")) || 0;
  const calculado = numero(valorOpcional(workbook, "stkActualizado")) || 0;

  // Movimientos registrados hoy (Timestamp) y los que llegaron después del último recálculo
  let entradasHoy = 0, salidasHoy = 0, unidadesHoy = 0, rechazosHoy = 0, nuevos = 0;
  for (const nombre of ["tblEntradas", "tblSalidas"]) {
    const [marcas, netas, estados] = leerColumnas(workbook, nombre, ["Timestamp", "CantidadNeta", "Estado"]);
    for (let i = 0; i < marcas.length; i++) {
      const ts = numero(marcas[i]);
      if (isNaN(ts)) {
        continue;
      }
      if (ts > calculado) {
        nuevos++;
      }
      if (Math.floor(ts) !== hoy) {
        continue;
      }
      const estado = texto(estados[i]);
      if (estado.indexOf(PREFIJO_OK) !== 0) {
        rechazosHoy++;
      } else if (nombre === "tblSalidas") {
        salidasHoy++;
        unidadesHoy += -(numero(netas[i]) || 0);
      } else {
        entradasHoy++;
      }
    }
  }

  // Intentos bloqueados hoy (14_ACTIVIDAD)
  const bloqueos: string[][] = [];
  if (workbook.getTable("tblActividad")) {
    const [marcas, nombres, resultados, detalles] = leerColumnas(workbook, "tblActividad",
      ["Timestamp", "Nombre", "Resultado", "Detalle"]);
    for (let i = 0; i < marcas.length; i++) {
      const ts = numero(marcas[i]);
      if (!isNaN(ts) && Math.floor(ts) === hoy && texto(resultados[i]).indexOf("✖") === 0) {
        bloqueos.push([horaDeSerial(ts), texto(nombres[i]), texto(detalles[i])]);
      }
    }
  }

  // Productos que requieren acción con el stock exacto de este momento
  const actuales = saldos(workbook);
  const reponer: Reponer[] = [];
  let totalPedido = 0;
  for (const p of leerProductos(workbook)) {
    const s = actuales.get(p.sku);
    const stock = s ? s.stock : 0;
    const estado = estadoDe(stock, p.minimo, p.maximo, p.activo, margen);
    if (!p.activo || REPONER.indexOf(estado) < 0) {
      continue;
    }
    const tope = p.maximo > 0 ? p.maximo : 2 * p.minimo;
    const apedir = r6(Math.max(0, tope - Math.max(0, stock)));
    totalPedido += r6(apedir * p.costo);
    reponer.push({ orden: [REPONER.indexOf(estado), p.indice], estado, producto: p, stock, apedir });
  }
  reponer.sort((a: Reponer, b: Reponer) => a.orden[0] - b.orden[0] || a.orden[1] - b.orden[1]);
  const cuenta = (e: string): number => reponer.filter((r: Reponer) => r.estado === e).length;
  const agotados = cuenta("AGOTADO"), criticos = cuenta("CRÍTICO"), bajos = cuenta("BAJO");
  totalPedido = Math.round(totalPedido);

  // Correo
  const fecha = fechaTexto(hoy);
  const asunto = "M-INV" + (empresa ? " · " + empresa : "") + " · Resumen del " + fecha + " · " +
    (reponer.length === 0 ? "inventario sin alertas de reposición" : reponer.length + " producto(s) por reponer");
  const frescura = calculado > 0 ?
    "Instantánea de stock calculada el " + fechaTexto(calculado) + " a las " + horaDeSerial(calculado) +
    (nuevos > 0 ? " · " + nuevos + " movimiento(s) nuevos desde entonces: pulse «Recalcular stock»." : " · al día.") :
    "La instantánea de stock aún no se calcula: pulse «Recalcular stock».";
  const indicadores: string[][] = [
    ["Entradas y ajustes registrados hoy", miles(entradasHoy)],
    ["Salidas registradas hoy", miles(salidasHoy) + " (" + cantidadTexto(unidadesHoy) + " unidades)"],
    ["Registros rechazados hoy", miles(rechazosHoy)],
    ["Intentos bloqueados hoy", miles(bloqueos.length)],
    ["Productos por reponer", agotados + " agotado(s) · " + criticos + " crítico(s) · " + bajos + " bajo(s)"],
    ["Pedido sugerido estimado", "$ " + miles(totalPedido)],
  ];
  const lineas = reponer.slice(0, MAX_LINEAS_RESUMEN);
  const estilo = "font-family:Segoe UI,Arial,sans-serif;font-size:13px;color:#1E293B";
  const celda = "padding:4px 8px;border-bottom:1px solid #E2E8F0";
  let html = "<div style=\"" + estilo + "\"><h2 style=\"margin:0 0 4px\">M-INV · Resumen del " + fecha + "</h2>" +
    "<p style=\"margin:0 0 12px;color:#64748B\">" + escapar(empresa || "Inventario colaborativo") + " · generado a las " +
    horaDe(ahora) + "</p><table style=\"border-collapse:collapse;margin-bottom:12px\">" +
    indicadores.map((f: string[]) => "<tr><td style=\"" + celda + "\">" + escapar(f[0]) + "</td><td style=\"" + celda +
      ";font-weight:bold\">" + escapar(f[1]) + "</td></tr>").join("") + "</table>";
  if (lineas.length > 0) {
    html += "<h3 style=\"margin:12px 0 4px\">Productos por reponer (stock exacto)</h3>" +
      "<table style=\"border-collapse:collapse\"><tr style=\"background:#0F172A;color:#FFFFFF\">" +
      ["Estado", "SKU", "Producto", "Stock", "A pedir", "Proveedor"].map((h: string) =>
        "<th style=\"padding:4px 8px;text-align:left\">" + h + "</th>").join("") + "</tr>" +
      lineas.map((r: Reponer) => "<tr><td style=\"" + celda + ";color:" + (r.estado === "BAJO" ? "#B45309" : "#B91C1C") +
        ";font-weight:bold\">" + r.estado + "</td><td style=\"" + celda + "\">" + escapar(r.producto.sku) +
        "</td><td style=\"" + celda + "\">" + escapar(r.producto.nombre) + "</td><td style=\"" + celda + "\">" +
        cantidadTexto(r.stock) + " " + escapar(r.producto.unidad) + "</td><td style=\"" + celda + "\">" +
        cantidadTexto(r.apedir) + "</td><td style=\"" + celda + "\">" + escapar(r.producto.proveedor || SIN_PROVEEDOR) +
        "</td></tr>").join("") + "</table>";
    if (reponer.length > lineas.length) {
      html += "<p>… y " + (reponer.length - lineas.length) + " producto(s) más en 18_PEDIDO.</p>";
    }
  }
  if (bloqueos.length > 0) {
    html += "<h3 style=\"margin:12px 0 4px\">Intentos bloqueados hoy</h3><ul>" +
      bloqueos.slice(0, MAX_LINEAS_RESUMEN).map((b: string[]) => "<li>" + escapar(b[0] + " · " + b[1] + " · " + b[2]) +
        "</li>").join("") + "</ul>";
  }
  html += "<p style=\"color:#64748B\">" + escapar(frescura) + "</p></div>";
  const textoPlano = ["M-INV · Resumen del " + fecha + (empresa ? " · " + empresa : "")]
    .concat(indicadores.map((f: string[]) => "- " + f[0] + ": " + f[1]))
    .concat(lineas.map((r: Reponer) => "  " + r.estado + " · " + r.producto.sku + " · " + r.producto.nombre +
      " · stock " + cantidadTexto(r.stock) + " · pedir " + cantidadTexto(r.apedir)))
    .concat([frescura]).join("\n");
  console.log(textoPlano);
  return {
    asunto, html, texto: textoPlano, fecha, movimientosHoy: entradasHoy + salidasHoy, unidadesVendidasHoy: r6(unidadesHoy),
    rechazosHoy, bloqueosHoy: bloqueos.length, agotados, criticos, bajos, totalPedido
  };
}

/** Resultado para Power Automate (cada campo aparece como contenido dinámico del flujo). */
interface ResumenDelDia {
  asunto: string;
  html: string;
  texto: string;
  fecha: string;
  movimientosHoy: number;
  unidadesVendidasHoy: number;
  rechazosHoy: number;
  bloqueosHoy: number;
  agotados: number;
  criticos: number;
  bajos: number;
  totalPedido: number;
}

interface Reponer {
  orden: number[];
  estado: string;
  producto: Producto;
  stock: number;
  apedir: number;
}

const MAX_LINEAS_RESUMEN = 25;

function valorOpcional(workbook: ExcelScript.Workbook, nombre: string): Celda {
  const n = workbook.getNamedItem(nombre);
  return n ? n.getRange().getValue() as Celda : "";
}

function horaDeSerial(serial: number): string {
  const minutos = Math.round((serial - Math.floor(serial)) * 1440);
  return dos(Math.floor(minutos / 60) % 24) + ":" + dos(minutos % 60);
}

/** Entero con separador de miles (1.234.567). */
function miles(n: number): string {
  return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
}

function cantidadTexto(n: number): string {
  const x = r6(n);
  return x === Math.floor(x) ? miles(x) : String(x).replace(".", ",");
}

function escapar(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

// >>> M-INV · BLOQUE COMÚN (generado desde lib/comun.ts con tools/office_scripts.py: no editar aquí)
// ============================================================================================================
// M-INV V2.1 · Bloque común de los Office Scripts · Z&P Software Fast Solutions
// Fuente única: src/office-scripts/lib/comun.ts. Office Scripts no permite importar módulos, así que
// tools/office_scripts.py copia este bloque dentro de cada script (sync) y verifica que no se desvíe (check).
// ============================================================================================================

/** Contraseña de las hojas protegidas: tools/office_scripts.py la inyecta al generar la versión instalable. */
const CLAVE = "__MINV_PASSWORD__";
const HOJA_SESION = "92_SESION";
const HOJA_ACTIVIDAD = "14_ACTIVIDAD";
const PREFIJO_OK = "✔";
const ESTADO_OK = "✔ Consolidado";
const SEPARADOR = " · ";
const MAX_DOCUMENTO = 30;
const MAX_OBSERVACIONES = 250;
const MAX_DETALLE = 250;
const VENTANA_DIAS = 30;
const ALERTAS = ["INCONSISTENTE", "AGOTADO", "CRÍTICO", "BAJO", "SOBRESTOCK"];
const REPONER = ["AGOTADO", "CRÍTICO", "BAJO"];
const SIN_PROVEEDOR = "(Sin proveedor)";

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

interface Saldo {
  stock: number;
  movimientos: number;
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

/** Número de serie de Excel (solo la parte de fecha) como dd/mm/aaaa. */
function fechaTexto(serial: number): string {
  const d = new Date(Math.round((Math.floor(serial) - 25569) * 86400000));
  return dos(d.getUTCDate()) + "/" + dos(d.getUTCMonth() + 1) + "/" + d.getUTCFullYear();
}

/** Número de serie de Excel como AAAAMMDD (documentos generados, ej. CF-20260925). */
function fechaCompacta(serial: number): string {
  const d = new Date(Math.round((Math.floor(serial) - 25569) * 86400000));
  return d.getUTCFullYear() + dos(d.getUTCMonth() + 1) + dos(d.getUTCDate());
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

/** Nombre visible de la cuenta: el de 02_USUARIOS si está registrada; si no, el de Microsoft 365. */
function nombreVisible(workbook: ExcelScript.Workbook, yo: Identidad): string {
  try {
    const u = buscarUsuario(workbook, yo.correo);
    return u && u.nombre ? u.nombre : yo.nombre;
  } catch (error) {
    return yo.nombre;
  }
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

/** Semáforo: mismas reglas y orden que tblEstados (01_CONFIG) y que el proyector del generador (regla C-11). */
function estadoDe(stock: number, minimo: number, maximo: number, activo: boolean, margen: number): string {
  if (!activo) {
    return "INACTIVO";
  }
  if (stock < 0) {
    return "INCONSISTENTE";
  }
  if (stock === 0) {
    return "AGOTADO";
  }
  if (stock <= minimo) {
    return "CRÍTICO";
  }
  if (stock <= minimo * (1 + margen)) {
    return "BAJO";
  }
  if (maximo > 0 && stock > maximo) {
    return "SOBRESTOCK";
  }
  return "ÓPTIMO";
}

/** Stock exacto de TODOS los productos con una sola lectura de las dos bitácoras (solo registros consolidados ✔). */
function saldos(workbook: ExcelScript.Workbook): Map<string, Saldo> {
  const m = new Map<string, Saldo>();
  for (const nombre of ["tblEntradas", "tblSalidas"]) {
    const [skus, netas, estados] = leerColumnas(workbook, nombre, ["SKU", "CantidadNeta", "Estado"]);
    for (let i = 0; i < skus.length; i++) {
      const sku = texto(skus[i]);
      if (sku === "" || texto(estados[i]).indexOf(PREFIJO_OK) !== 0) {
        continue;
      }
      const s = m.get(sku) || { stock: 0, movimientos: 0 };
      s.stock += numero(netas[i]) || 0;
      s.movimientos++;
      m.set(sku, s);
    }
  }
  m.forEach((s: Saldo) => {
    s.stock = r6(s.stock);
  });
  return m;
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
/** Valores de una fila en el orden de los encabezados de la tabla (las columnas que falten quedan vacías). */
function filaSegunEncabezados(tabla: ExcelScript.Table, valores: Map<string, Celda>): Celda[] {
  const encabezados = tabla.getHeaderRowRange().getValues()[0].map((x: Celda) => texto(x));
  return encabezados.map((e: string) => (valores.has(e) ? valores.get(e) as Celda : ""));
}

/** Agrega varios registros con UNA inserción atómica del servidor (addRows); ocupa la fila en blanco si la hay. */
function agregarFilas(tabla: ExcelScript.Table, filas: Celda[][]): void {
  let resto = filas;
  if (resto.length > 0 && tabla.getRowCount() === 1) {
    const unica = tabla.getRangeBetweenHeaderAndTotal();
    if (unica.getValues()[0].every((x: Celda) => texto(x) === "")) {
      unica.setValues([resto[0]]);
      resto = resto.slice(1);
    }
  }
  if (resto.length > 0) {
    tabla.addRows(-1, resto);
  }
}

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

// ---------------------------------------------------------------------------------------------- auditoría
/**
 * 14_ACTIVIDAD: una fila por ejecución de un script (quién, cuándo, qué script, resultado y detalle), agregada al
 * final con una inserción atómica, igual que las bitácoras. Nunca interrumpe la operación principal: si no puede
 * escribir (libro anterior a la 2.1, contraseña distinta), solo lo informa en la salida del script.
 */
function registrarActividad(workbook: ExcelScript.Workbook, yo: Identidad, nombre: string, script: string,
  resultado: string, detalle: string, momento: Date): void {
  try {
    const tabla = workbook.getTable("tblActividad");
    const h = workbook.getWorksheet(HOJA_ACTIVIDAD);
    if (!tabla || !h) {
      console.log("Actividad: el libro no tiene " + HOJA_ACTIVIDAD + " (versión anterior a la 2.1)");
      return;
    }
    const corto = detalle.length > MAX_DETALLE ? detalle.substring(0, MAX_DETALLE - 1) + "…" : detalle;
    const fila = filaSegunEncabezados(tabla, new Map<string, Celda>([
      ["ID", nuevoId("A", momento)], ["Timestamp", serialDe(momento)], ["Usuario_O365", yo.correo],
      ["Nombre", nombre || yo.nombre], ["Script", script], ["Resultado", resultado], ["Detalle", corto]
    ]));
    conHojasDesbloqueadas([h], () => agregarFila(tabla, fila));
  } catch (error) {
    console.log("Actividad: no se pudo registrar (" + mensajeDe(error as Error) + ")");
  }
}

/** La fila de captura del usuario; si no la tiene, deja el intento en 14_ACTIVIDAD antes de informar el error. */
function capturaAuditada(workbook: ExcelScript.Workbook, tabla: string, yo: Identidad, hojaNombre: string,
  script: string, momento: Date): FilaCaptura {
  try {
    return filaDeCaptura(workbook, tabla, yo.correo, hojaNombre);
  } catch (error) {
    registrarActividad(workbook, yo, nombreVisible(workbook, yo), script, "✖ Bloqueado",
      "✖ Bloqueado: " + mensajeDe(error as Error), momento);
    throw error;
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
  const script = dominio === "VENTAS" ? "RegistrarSalida" : "RegistrarEntrada";
  const hojaMov = hoja(workbook, hojaNombre);
  const yo = identificarUsuario(workbook);
  const captura = capturaAuditada(workbook, tablaCaptura, yo, hojaNombre, script, ahora);
  let nombre = yo.nombre;
  let resultado = "✔ Registrado";
  let mensaje = "";
  let detalle = "";
  try {
    const registro = buscarUsuario(workbook, yo.correo);
    if (registro && registro.nombre) {
      nombre = registro.nombre;
    }
    autorizar(registro, yo.correo, roles, dominio === "VENTAS" ? "registrar salidas" : "registrar movimientos de bodega");
    const mov = validarCaptura(workbook, captura, dominio, Math.floor(serialDe(ahora)));
    const antes = disponible(workbook, mov.producto.sku);
    const neta = r6(mov.cantidad * mov.factor);
    const resumen = mov.tipo + " " + mov.cantidad + " " + mov.producto.unidad + SEPARADOR + mov.producto.sku;
    if (antes + neta < 0) {
      throw new Error("stock insuficiente: disponible " + antes + " " + mov.producto.unidad + " (" + mov.producto.sku +
        ")");
    }
    const bitacora = workbook.getTable(tablaBitacora);
    if (!bitacora) {
      throw new Error("falta la tabla " + tablaBitacora);
    }
    const id = nuevoId(prefijo, ahora);
    const fila = filaSegunEncabezados(bitacora, new Map<string, Celda>([
      ["ID", id], ["Tipo", mov.tipo], ["Fecha", mov.fecha], ["Producto", mov.producto.sku + SEPARADOR + mov.producto.nombre],
      ["Cantidad", mov.cantidad], ["Documento", mov.documento], ["Observaciones", mov.observaciones],
      ["CantidadNeta", neta], ["Estado", ESTADO_OK], ["Registró", nombre],
      ["Unidad", mov.producto.unidad], ["FactorStock", mov.factor], ["Usuario_O365", yo.correo],
      ["SKU", mov.producto.sku], ["Timestamp", serialDe(ahora)]
    ]));
    mensaje = conHojasDesbloqueadas([hojaMov], () => {
      agregarFila(bitacora, fila);
      if (neta < 0) {
        const despues = disponible(workbook, mov.producto.sku);
        if (despues < 0) {
          marcarRechazo(bitacora, id, "stock insuficiente por un registro simultáneo");
          escribirCaptura(captura, new Map<string, Celda>([["Resultado",
            "✖ Bloqueado: otra persona registró antes y el stock no alcanza (disponible " + r6(despues - neta) + ")"]]));
          resultado = "✖ Rechazado";
          detalle = "✖ Rechazado " + id + SEPARADOR + resumen + ": stock insuficiente por un registro simultáneo";
          return "✖ Rechazado " + id + ": stock insuficiente por un registro simultáneo";
        }
      }
      const saldo = r6(antes + neta);
      const ok = "✔ Registrado " + id + " · " + horaDe(ahora) + " · stock " + saldo + " " + mov.producto.unidad;
      detalle = "✔ Registrado " + id + SEPARADOR + resumen + " · stock " + saldo;
      const limpieza = new Map<string, Celda>([["Resultado", ok]]);
      for (const c of limpiar) {
        limpieza.set(c, "");
      }
      escribirCaptura(captura, limpieza);
      return ok;
    });
  } catch (error) {
    resultado = "✖ Bloqueado";
    mensaje = "✖ Bloqueado: " + mensajeDe(error as Error);
    detalle = mensaje;
    conHojasDesbloqueadas([hojaMov], () => escribirCaptura(captura, new Map<string, Celda>([["Resultado", mensaje]])));
  }
  registrarActividad(workbook, yo, nombre, script, resultado, detalle || mensaje, ahora);
  console.log(mensaje);
  return mensaje;
}
// <<< M-INV · FIN DEL BLOQUE COMÚN
