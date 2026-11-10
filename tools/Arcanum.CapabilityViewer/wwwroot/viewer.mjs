import { parseCapability, affectedNodes, layoutGraph } from "./model.mjs";

const $ = id => document.getElementById(id);
const NS = "http://www.w3.org/2000/svg";
const escapeHtml = value => String(value ?? "").replace(/[&<>"']/g, c =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const clipped = (value, max = 28) => {
  const t = String(value ?? "—");
  return t.length > max ? t.slice(0, max - 1) + "…" : t;
};
const documents = new Map();
const state = {
  active: null, processContracts: {}, selectedNode: "__contract__",
  highlightedData: null, highlightedResource: null, tab: "data",
  zoom: 1, x: 0, y: 0, layout: null, dragging: null, importSeq: 0
};
const current = () => documents.get(state.active)?.capability || null;
const world = $("graph-world");
const svg = $("graph-svg");
const viewport = $("canvas-viewport");
let toastTimeout;

function toast(message) {
  const element = $("toast");
  element.textContent = message;
  element.hidden = false;
  clearTimeout(toastTimeout);
  toastTimeout = setTimeout(() => { element.hidden = true; }, 4500);
}

function loadDocument(id, name, source, sourceLabel = "Pasta local") {
  const capability = parseCapability(source, { fileName: name, processContracts: state.processContracts });
  documents.set(id, { id, name, source: sourceLabel, original: source, capability });
  return capability;
}

function choose(id) {
  if (!documents.has(id)) return;
  state.active = id;
  state.selectedNode = "__contract__";
  state.highlightedData = null;
  state.highlightedResource = null;
  state.layout = layoutGraph(current());
  render();
  requestAnimationFrame(fitView);
}

function closeFile(id) {
  if (!documents.has(id)) return;
  const keys = [...documents.keys()];
  const index = keys.indexOf(id);
  documents.delete(id);
  if (state.active === id) {
    const rest = [...documents.keys()];
    if (rest.length) choose(rest[Math.min(index, rest.length - 1)]);
    else {
      state.active = null;
      state.layout = null;
    }
  }
  render();
}
function closeAll() {
  documents.clear();
  state.active = null;
  state.selectedNode = null;
  state.layout = null;
  state.highlightedData = null;
  state.highlightedResource = null;
  render();
}

async function importFiles(files, fromFolder = false) {
  const items = [...files].filter(f => f.name.toLowerCase().endsWith(".json"));
  if (!items.length) { toast("Não foram encontrados ficheiros JSON."); return; }
  let accepted = 0;
  let errors = 0;
  let first = null;
  // Parsing is local-only; selected folder contents are not uploaded to any server.
  for (const file of items) {
    const name = file.webkitRelativePath || file.name;
    const id = "local:" + name;
    try {
      const value = await file.text();
      loadDocument(id, name, value, fromFolder ? "Pasta local" : "Ficheiro local");
      accepted++;
      if (!first) first = id;
    } catch (error) {
      errors++;
      console.warn("Capability ignorada:", name, error.message);
    }
  }
  if (accepted) choose(first);
  toast(accepted + " Capability(s) carregada(s)" + (errors ? " · " + errors + " ficheiro(s) ignorado(s)" : "") + ".");
}
async function preloadDemo() {
  try {
    const response = await fetch("./api/snapshot", { cache: "no-store" });
    if (!response.ok) return;
    const payload = await response.json();
    state.processContracts = payload.processContracts || {};
    for (const file of payload.files || []) {
      try { loadDocument("demo:" + file.name, file.name, file.document, "Demo API"); }
      catch (error) { console.warn("Exemplo inválido:", file.name, error.message); }
    }
    if (documents.size) choose([...documents.keys()][0]);
    else render();
  } catch {
    // Static HTTP hosting has no Demo API; directory picker remains fully functional.
  }
}

function renderFileList() {
  const filter = $("search-files").value.trim().toLocaleLowerCase("pt");
  const list = $("file-list");
  list.textContent = "";
  const docs = [...documents.values()].filter(d =>
    (d.name + " " + d.capability.name).toLocaleLowerCase("pt").includes(filter));
  $("file-count").textContent = String(documents.size);
  if (!docs.length) {
    const empty = document.createElement("div");
    empty.className = "list-empty";
    empty.textContent = documents.size ? "Sem ficheiros correspondentes." :
      "Ainda não há ficheiros. Carrega uma pasta para começar.";
    list.append(empty);
  }
  for (const doc of docs) {
    const row = document.createElement("div");
    row.className = "file-item" + (doc.id === state.active ? " active" : "");
    row.setAttribute("role", "option");
    row.setAttribute("aria-selected", String(doc.id === state.active));
    const symbol = document.createElement("span");
    symbol.className = "file-glyph";
    symbol.textContent = doc.capability.kind === "0.4" ? "◈" : "◇";
    const open = document.createElement("button");
    open.type = "button";
    open.className = "file-open";
    open.title = doc.name;
    const title = document.createElement("strong");
    title.textContent = doc.capability.name;
    const sub = document.createElement("small");
    sub.textContent = doc.source + " · " + (doc.capability.kind === "0.4" ? "Draft 0.4" : "Legacy");
    open.append(title, sub);
    open.addEventListener("click", () => choose(doc.id));
    const close = document.createElement("button");
    close.type = "button";
    close.className = "file-close";
    close.textContent = "✕";
    close.title = "Fechar " + doc.capability.name;
    close.setAttribute("aria-label", close.title);
    close.addEventListener("click", () => closeFile(doc.id));
    row.append(symbol, open, close);
    list.append(row);
  }
  $("source-hint").textContent = documents.size ?
    documents.size + " documento(s) disponíveis" : "À espera de ficheiros";
  $("close-all").disabled = !documents.size;
}

function svgElement(tag, attrs = {}, text = null) {
  const el = document.createElementNS(NS, tag);
  for (const [name, value] of Object.entries(attrs)) {
    if (value !== undefined && value !== null) el.setAttribute(name, String(value));
  }
  if (text !== null) el.textContent = text;
  return el;
}
function updateTransform() {
  world.setAttribute("transform", "translate(" + state.x + " " + state.y + ") scale(" + state.zoom + ")");
  $("zoom-label").textContent = Math.round(state.zoom * 100) + "%";
}
function nodeHighlights() {
  const capability = current();
  if (!capability) return new Set();
  return affectedNodes(capability, {
    variable: state.highlightedData, resource: state.highlightedResource
  });
}

function renderGraph() {
  world.replaceChildren();
  const c = current();
  if (!c || !state.layout) return;
  const selectedIds = nodeHighlights();
  const highlighting = !!(state.highlightedData || state.highlightedResource);
  const p = state.layout.positions;

  for (const edge of c.edges) {
    const from = p.get(edge.from);
    const to = p.get(edge.to);
    if (!from || !to) continue;
    const back = to.x <= from.x;
    const x1 = from.x + from.width, y1 = from.y + from.height / 2;
    const x2 = to.x, y2 = to.y + to.height / 2;
    const bend = Math.max(65, Math.min(145, Math.abs(x2 - x1) * .42));
    const d = back ?
      "M " + x1 + " " + y1 + " C " + (x1 + 95) + " " + (y1 + 110) +
        " " + (x2 - 95) + " " + (y2 + 110) + " " + x2 + " " + y2 :
      "M " + x1 + " " + y1 + " C " + (x1 + bend) + " " + y1 +
        " " + (x2 - bend) + " " + y2 + " " + x2 + " " + y2;
    const related = selectedIds.has(edge.from) || selectedIds.has(edge.to);
    const className = "edge-path" + (edge.spawn ? " spawn" : "") +
      (highlighting && !related ? " dim" : "") + (highlighting && related ? " related" : "");
    world.append(svgElement("path", { d, class: className }));

    const mx = (x1 + x2) / 2;
    const my = (y1 + y2) / 2 + (back ? 95 : 0);
    const label = clipped(edge.label, 24);
    const width = Math.max(48, label.length * 6.8 + 19);
    const g = svgElement("g", {
      class: "edge-badge" + (edge.spawn ? " spawn" : "") +
        (highlighting && !related ? " dim" : ""),
      transform: "translate(" + mx + " " + my + ")"
    });
    g.append(svgElement("rect", { x: -width / 2, y: -12, width, height: 24, rx: 7 }));
    g.append(svgElement("text", { x: 0, y: 1 }, label));
    g.append(svgElement("title", {}, edge.label));
    world.append(g);
  }

  for (const node of c.nodes) {
    const pos = p.get(node.id);
    if (!pos) continue;
    const matched = selectedIds.has(node.id);
    const classes = ["graph-node", node.kind,
      node.id === state.selectedNode ? "selected" : "",
      highlighting && matched ? "match" : "",
      highlighting && !matched && node.kind !== "contract" ? "dim" : ""
    ].filter(Boolean).join(" ");
    const g = svgElement("g", {
      class: classes, transform: "translate(" + pos.x + " " + pos.y + ")",
      "data-node": node.id, role: "button", tabindex: "0",
      "aria-label": "Nó " + node.label + ", " + (node.process || node.className)
    });
    g.append(svgElement("rect", { class: "node-bg", x: 0, y: 0,
      width: pos.width, height: pos.height, rx: 12 }));
    g.append(svgElement("rect", { class: "node-accent", x: 0, y: 16, width: 4,
      height: pos.height - 32, rx: 2 }));
    const kind = node.kind === "contract" ? "CAPABILITY INPUT" :
      node.kind === "terminal" ? "TERMINAL" : (node.templateId ? "NODE · " + node.templateId : "NODE · LEGACY");
    g.append(svgElement("text", { x: 19, y: 25, class: "node-kind" }, clipped(kind, 31)));
    g.append(svgElement("text", { x: 19, y: 58, class: "node-name" }, clipped(node.label, 25)));
    const info = node.kind === "contract"
      ? clipped(c.displayName, 35)
      : node.kind === "terminal" ? "Resposta declarada" : clipped(node.className, 33);
    g.append(svgElement("text", { x: 19, y: 79, class: "node-class" }, info));
    const footer = node.kind === "contract"
      ? pairsText(c.contract.inputs).length + " entrada(s) · " + (c.response?.name || "respostas condicionais")
      : node.kind === "terminal" ? "Fim do percurso" :
      node.inputs.length + " in  ·  " + node.outputs.length + " out  ·  " + node.resources.length + " DI";
    g.append(svgElement("text", { x: 19, y: pos.height - 15, class: "node-footer" }, clipped(footer, 42)));
    g.append(svgElement("title", {}, node.label + " — " + (node.process || node.description)));
    g.addEventListener("click", event => {
      event.stopPropagation();
      state.selectedNode = node.id;
      renderGraph();
      renderDetail();
    });
    g.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        state.selectedNode = node.id;
        renderGraph();
        renderDetail();
      }
    });
    world.append(g);
  }
  updateTransform();
}
const pairsText = value => Object.entries(value && typeof value === "object" ? value : {});

function detailRow(heading, list, empty) {
  return '<div class="detail-section"><strong>' + escapeHtml(heading) + '</strong>' +
    (list.length ? list.map(item => '<span class="detail-pill">' + escapeHtml(item) + '</span>').join("") :
      '<span class="detail-muted">' + escapeHtml(empty) + '</span>') + "</div>";
}
function renderDetail() {
  const area = $("node-details");
  const c = current();
  if (!c) {
    area.innerHTML = '<div class="details-empty">Seleciona uma Capability para explorar os seus contratos.</div>';
    return;
  }
  const node = c.nodes.find(n => n.id === state.selectedNode);
  if (!node) {
    area.innerHTML = '<div class="details-empty">Seleciona um nó para examinar o Process, contratos e bindings.</div>';
    return;
  }
  let html = '<div class="detail-card"><div class="detail-overline">' +
    escapeHtml(node.kind === "contract" ? "Ponto de entrada" : node.kind === "terminal" ?
      "Resposta terminal" : "Process / Node") + '</div><h3>' +
    escapeHtml(node.label) + '</h3><div class="detail-class">' +
    escapeHtml(node.process || c.name) + '</div>';
  if (node.description)
    html += '<p class="detail-desc">' + escapeHtml(node.description) + '</p>';
  if (node.kind === "contract") {
    html += detailRow("Entradas da Capability", pairsText(c.contract.inputs)
      .map(([name, spec]) => name + ": " + (spec.model || spec.type || "—")), "Sem entradas");
    const responses = c.response?.legacy ?
      c.response.variants.map(v => v.terminal + " → " + v.name) :
      c.response ? [c.response.name + ": " + c.response.model] : [];
    html += detailRow("Contrato de resposta", responses, "Não declarado");
    html += detailRow("Políticas", Object.entries(c.policies).map(([k, v]) => k + ": " + v),
      "Defaults da framework");
  } else if (node.kind === "process") {
    if (node.templateId) html += '<p class="detail-desc">Template: <b>' +
      escapeHtml(node.templateId) + '</b></p>';
    html += detailRow("Inputs", node.inputs.map(v => v.parameter + " ← " + v.name + " · " +
      (v.model || clipped(v.type, 22))), "Sem inputs declarados");
    html += detailRow("Outputs", node.outputs.map(v => v.parameter + " → " + v.name + " · " +
      (v.model || clipped(v.type, 22))), "Sem outputs identificados");
    html += detailRow("Recursos de DI", node.resources.map(v => v.name + ": " + clipped(v.type, 34)),
      "Nenhum recurso identificado");
  }
  area.innerHTML = html + "</div>";
}

function renderContext() {
  const c = current();
  $("data-count").textContent = String(c?.data.length || 0);
  $("di-count").textContent = String(c?.resources.length || 0);
  for (const tab of ["data", "di"]) {
    $("tab-" + tab).classList.toggle("active", state.tab === tab);
    $("tab-" + tab).setAttribute("aria-selected", String(state.tab === tab));
  }
  const box = $("context-list");
  box.textContent = "";
  if (!c) {
    box.innerHTML = '<div class="list-empty">Carrega uma Capability para visualizar as estruturas.</div>';
    return;
  }
  const note = document.createElement("div");
  note.className = "context-note";
  note.textContent = state.tab === "data"
    ? "Contexto estático: nomes, tipos e bindings. Não são valores da execução."
    : "Recursos declarados ou inferidos pelos construtores C# quando o assembly está disponível. ILogger<T> é omitido.";
  box.append(note);
  const items = state.tab === "data" ? c.data : c.resources;
  if (!items.length) {
    const empty = document.createElement("div");
    empty.className = "list-empty";
    empty.textContent = state.tab === "data" ? "Sem variáveis identificadas." :
      "Sem recursos DI declarados ou inferíveis para estes Processes.";
    box.append(empty);
  }
  for (const item of items) {
    const isData = state.tab === "data";
    const active = isData ? state.highlightedData === item.name :
      state.highlightedResource === item.key;
    const row = document.createElement("button");
    row.type = "button";
    row.className = "context-item" + (active ? " active" : "");
    row.setAttribute("aria-pressed", String(active));
    row.title = "Destacar os nós associados a " + item.name;
    const meta = isData ? [
      item.fromContract ? "entrada" : null,
      item.isResponse ? "resposta" : null,
      item.reads.size ? "lido por " + item.reads.size : null,
      item.writes.size ? "escrito por " + item.writes.size : null,
      item.source ? "fonte " + item.source : null,
      item.defaultValue !== undefined ? "default: " + JSON.stringify(item.defaultValue) : null
    ] : ["utilizado em " + item.nodes.size + " nó(s)", item.inferred ? "inferido do C#" : "declarado"];
    row.innerHTML = '<div class="context-item-head"><span class="context-symbol">' +
      (isData ? "{ }" : "◇") + '</span><span class="context-main"><strong>' +
      escapeHtml(item.name) + '</strong><small title="' + escapeHtml(item.type) + '">' +
      escapeHtml(clipped(item.type, 39)) + '</small></span><span class="context-toggle">' +
      (active ? "✓" : "") + '</span></div><div class="context-meta">' +
      meta.filter(Boolean).map(v => "<span>" + escapeHtml(v) + "</span>").join("") + "</div>";
    row.addEventListener("click", () => {
      if (isData) state.highlightedData = active ? null : item.name;
      else state.highlightedResource = active ? null : item.key;
      renderGraph();
      renderContext();
    });
    box.append(row);
  }
}

function render() {
  const c = current();
  renderFileList();
  $("active-title").textContent = c?.displayName || "Explorador de Capabilities";
  $("active-version").textContent = c ? "v" + c.version : "";
  $("graph-name").textContent = c?.name || "Sem Capability selecionada";
  $("empty-canvas").hidden = !!c;
  $("canvas-legend").hidden = !c;
  $("canvas-info").hidden = !c;
  $("node-stat").textContent = c ? c.nodes.filter(n => n.kind === "process").length + " nodes" : "0 nodes";
  $("edge-stat").textContent = c ? c.edges.filter(e => !e.synthetic).length + " edges" : "0 edges";
  $("format-stat").textContent = c ? (c.kind === "0.4" ? "Draft 0.4" : "Legacy") : "JSON";
  $("inspect-footer").textContent = c && c.warnings.length
    ? c.warnings.length + " aviso(s) de integridade. Consulta a consola do browser."
    : "Análise estática; valores em runtime não disponíveis.";
  renderGraph();
  renderDetail();
  renderContext();
  if (c?.warnings.length) console.warn("Capability Atlas:", c.name, c.warnings);
}

function fitView() {
  const c = current();
  if (!c || !state.layout) return;
  const rect = viewport.getBoundingClientRect();
  if (!rect.width || !rect.height) return;
  const { minX, minY, maxX, maxY } = state.layout.bounds;
  const w = Math.max(1, maxX - minX);
  const h = Math.max(1, maxY - minY);
  const fitScale = Math.min((rect.width - 110) / w, (rect.height - 125) / h, 1.18);
  state.zoom = Math.max(.17, Math.min(2.5, fitScale));
  state.x = (rect.width - (minX + maxX) * state.zoom) / 2;
  state.y = (rect.height - (minY + maxY) * state.zoom) / 2;
  updateTransform();
}
function zoomTo(nextScale, clientX, clientY) {
  const rect = svg.getBoundingClientRect();
  const px = clientX - rect.left, py = clientY - rect.top;
  const next = Math.max(.16, Math.min(2.7, nextScale));
  if (next === state.zoom) return;
  const worldX = (px - state.x) / state.zoom;
  const worldY = (py - state.y) / state.zoom;
  state.zoom = next;
  state.x = px - worldX * next;
  state.y = py - worldY * next;
  updateTransform();
}
function centerZoom(factor) {
  const r = svg.getBoundingClientRect();
  zoomTo(state.zoom * factor, r.left + r.width / 2, r.top + r.height / 2);
}

svg.addEventListener("wheel", event => {
  if (!current()) return;
  event.preventDefault();
  zoomTo(state.zoom * Math.exp(-event.deltaY * .0012), event.clientX, event.clientY);
}, { passive: false });
svg.addEventListener("pointerdown", event => {
  if (!current() || event.button !== 0 || event.target.closest("[data-node]")) return;
  state.dragging = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
    startX: state.x, startY: state.y, moved: false };
  svg.setPointerCapture(event.pointerId);
  svg.classList.add("dragging");
});
svg.addEventListener("pointermove", event => {
  const d = state.dragging;
  if (!d || d.pointerId !== event.pointerId) return;
  state.x = d.startX + event.clientX - d.x;
  state.y = d.startY + event.clientY - d.y;
  if (Math.abs(event.clientX - d.x) + Math.abs(event.clientY - d.y) > 3) d.moved = true;
  updateTransform();
});
function endDrag(event) {
  const d = state.dragging;
  if (!d || d.pointerId !== event.pointerId) return;
  if (!d.moved) {
    state.selectedNode = null;
    renderDetail();
    renderGraph();
  }
  state.dragging = null;
  svg.classList.remove("dragging");
  if (svg.hasPointerCapture(event.pointerId)) svg.releasePointerCapture(event.pointerId);
}
svg.addEventListener("pointerup", endDrag);
svg.addEventListener("pointercancel", endDrag);

$("zoom-in").addEventListener("click", () => centerZoom(1.23));
$("zoom-out").addEventListener("click", () => centerZoom(1 / 1.23));
$("fit-view").addEventListener("click", fitView);
$("search-files").addEventListener("input", renderFileList);
$("open-folder").addEventListener("click", () => $("folder-input").click());
$("empty-open-folder").addEventListener("click", () => $("folder-input").click());
$("open-files").addEventListener("click", () => $("files-input").click());
$("folder-input").addEventListener("change", event => {
  void importFiles(event.target.files, true); event.target.value = "";
});
$("files-input").addEventListener("change", event => {
  void importFiles(event.target.files, false); event.target.value = "";
});
$("close-all").addEventListener("click", closeAll);
$("clear-highlight").addEventListener("click", () => {
  state.highlightedData = null;
  state.highlightedResource = null;
  renderGraph(); renderContext();
});
for (const tab of document.querySelectorAll("[data-tab]"))
  tab.addEventListener("click", () => {
    state.tab = tab.dataset.tab;
    renderContext();
  });
document.addEventListener("keydown", event => {
  const tag = document.activeElement?.tagName;
  if (tag === "INPUT" || tag === "TEXTAREA") return;
  if (event.key === "Escape") {
    state.highlightedData = null; state.highlightedResource = null; state.selectedNode = null;
    renderGraph(); renderDetail(); renderContext();
  }
  if (event.key === "+" || event.key === "=") centerZoom(1.2);
  if (event.key === "-") centerZoom(1 / 1.2);
  if (event.key === "0") fitView();
});
window.addEventListener("resize", () => { if (current()) fitView(); });
render();
void preloadDemo();
