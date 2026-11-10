// Arcanum Capability Viewer — pure projection and layout, independent of the DOM.
// No executable code from a Capability file is evaluated.
const contractId = "__contract__";
const terminalPrefix = "__terminal__:";
const responseId = "__implicit_response__";
const asObject = value => value && typeof value === "object" && !Array.isArray(value) ? value : {};
const pairs = value => Object.entries(asObject(value));
const shortType = value => String(value || "—").split(".").at(-1);

function modelType(models, aliasOrType) {
  const model = asObject(models)[aliasOrType];
  return model && typeof model.type === "string" ? model.type : (aliasOrType || "Indefinido");
}

export function parseCapability(source, { fileName = "capability.json", processContracts = {} } = {}) {
  let raw;
  if (typeof source === "string") {
    try { raw = JSON.parse(source); } catch (error) {
      throw new Error("JSON inválido em " + fileName + ": " + error.message);
    }
  } else {
    raw = source;
  }
  if (!raw || typeof raw !== "object" || Array.isArray(raw) || !raw.graph || !asObject(raw.graph.nodes) ||
      !raw.graph.entry || !raw.name) {
    throw new Error("O ficheiro " + fileName + " não contém uma Capability válida (name, graph.entry, graph.nodes).");
  }
  const legacy = !Object.keys(asObject(raw.templates)).length;
  const graph = raw.graph;
  const models = asObject(raw.models);
  const templateMap = asObject(raw.templates);
  const processMap = asObject(raw.processes);
  const contract = asObject(raw.contract);
  const warnings = [];
  const nodes = [];
  const dataMap = new Map();
  const resourceMap = new Map();

  function variable(name, type = "Desconhecido", model = "", role = "", nodeId = "", extra = {}) {
    if (typeof name !== "string" || !name.trim()) return;
    let item = dataMap.get(name);
    if (!item) {
      item = { name, type, model: model || "", reads: new Set(), writes: new Set(),
        fromContract: false, isResponse: false, source: "", defaultValue: undefined };
      dataMap.set(name, item);
    }
    if (item.type === "Desconhecido" && type !== "Desconhecido") item.type = type;
    if (!item.model && model) item.model = model;
    if (role === "read" && nodeId) item.reads.add(nodeId);
    if (role === "write" && nodeId) item.writes.add(nodeId);
    if (extra.fromContract) item.fromContract = true;
    if (extra.isResponse) item.isResponse = true;
    if (typeof extra.source === "string") item.source = extra.source;
    if (Object.hasOwn(extra, "defaultValue")) item.defaultValue = extra.defaultValue;
  }

  for (const [name, value] of pairs(contract.inputs)) {
    const input = asObject(value);
    const type = input.model ? modelType(models, input.model) : input.type;
    variable(name, type || "Desconhecido", input.model || "", "", "", {
      fromContract: true, source: input.source || "",
      ...(Object.hasOwn(input, "default") ? { defaultValue: input.default } : {})
    });
  }

  let response = null;
  if (contract.response) {
    const declared = asObject(contract.response);
    response = { name: declared.name || "", model: declared.model || "", type:
      declared.model ? modelType(models, declared.model) : (declared.type || "Desconhecido") };
    variable(response.name, response.type, response.model, "", "", { isResponse: true });
  } else if (Object.keys(asObject(contract.responses)).length) {
    response = { legacy: true, variants: [] };
    for (const [terminal, obj] of pairs(contract.responses)) {
      const value = asObject(obj);
      response.variants.push({ terminal, name: value.name, type: value.type });
      variable(value.name, value.type, "", "", "", { isResponse: true });
    }
  }

  for (const [id, original] of pairs(graph.nodes)) {
    const spec = asObject(original);
    const template = asObject(templateMap[spec.template]);
    const process = legacy ? spec.process : template.process;
    const meta = asObject(processMap[process]);
    const signature = asObject(processContracts[process]);
    const node = {
      id, kind: "process", label: id, templateId: spec.template || "",
      process: process || "Process desconhecido", className: shortType(process),
      description: template.description || meta.description || meta.displayName || "",
      inputs: [], outputs: [], resources: [], raw: original
    };
    if (!process) warnings.push("Nó " + id + " sem classe Process.");
    if (!legacy && !templateMap[spec.template]) warnings.push("Template em falta no nó " + id + ": " + spec.template);
    const bindings = asObject(spec.bindings);
    if (!legacy) {
      for (const [parameter, model] of pairs(template.inputs)) {
        const name = asObject(bindings.inputs)[parameter];
        const type = modelType(models, model);
        node.inputs.push({ parameter, name: name || "—", model, type });
        variable(name, type, model, "read", id);
      }
      for (const [result, model] of pairs(template.outputs)) {
        const name = asObject(bindings.outputs)[result];
        const type = modelType(models, model);
        node.outputs.push({ parameter: result, name: name || "—", model, type });
        variable(name, type, model, "write", id);
      }
    } else {
      const sigInputs = Array.isArray(signature.inputs) ? signature.inputs : [];
      for (const parameter of sigInputs) {
        const name = parameter.dataName || parameter.name;
        const type = parameter.type || "Desconhecido";
        node.inputs.push({ parameter: parameter.name, name, type, inferred: true });
        variable(name, type, "", "read", id);
      }
      const outputName = spec.saveResultAs || meta.saveResultAs || signature.returnName;
      if (outputName) {
        const type = signature.outputType || "Desconhecido";
        node.outputs.push({ parameter: "result", name: outputName, type, inferred: true });
        variable(outputName, type, "", "write", id);
      }
    }
    const declared = pairs(template.resources).map(([name, type]) => ({ name, type, inferred: false }));
    const inferred = Array.isArray(signature.resources) ? signature.resources
      .filter(resource => !resource.automatic) : [];
    const resList = new Map();
    for (const item of [...declared, ...inferred]) {
      if (!item.name || !item.type) continue;
      resList.set(item.type + "|" + item.name, item);
    }
    for (const item of resList.values()) {
      node.resources.push(item);
      const resourceKey = item.type + "|" + item.name;
      if (!resourceMap.has(resourceKey))
        resourceMap.set(resourceKey, { key: resourceKey, name: item.name, type: item.type,
          nodes: new Set(), inferred: !!item.inferred });
      resourceMap.get(resourceKey).nodes.add(id);
    }
    nodes.push(node);
  }

  const originalEdges = Array.isArray(graph.edges) ? graph.edges : [];
  const terminalNames = new Set();
  const edges = [];
  for (let i = 0; i < originalEdges.length; i++) {
    const e = asObject(originalEdges[i]);
    if (typeof e.from !== "string" || typeof e.to !== "string") {
      warnings.push("Edge " + (i + 1) + " sem origem/destino.");
      continue;
    }
    const isTerminal = e.to.startsWith("@");
    if (isTerminal) terminalNames.add(e.to);
    edges.push({ id: "edge-" + i, from: e.from, to: isTerminal ? terminalPrefix + e.to : e.to,
      label: e.intention || "—", spawn: e.intention === "@spawn", synthetic: false });
  }
  for (const terminal of terminalNames)
    nodes.push({ id: terminalPrefix + terminal, kind: "terminal", label: terminal,
      className: "Resposta terminal", process: "", templateId: "", inputs: [], outputs: [],
      resources: [], description: "Destino terminal declarado no grafo." });

  nodes.unshift({ id: contractId, kind: "contract", label: "Contrato", className: raw.name,
    description: "Ponto de entrada da Capability", process: "", templateId: "",
    inputs: [], outputs: [], resources: [], raw: contract });
  edges.unshift({ id: "entry", from: contractId, to: graph.entry,
    label: "entrada", spawn: false, synthetic: true });

  const known = new Set(nodes.map(n => n.id));
  for (const edge of edges)
    if (!known.has(edge.from) || !known.has(edge.to))
      warnings.push("Edge " + edge.label + " aponta para nó desconhecido (" + edge.from + " → " + edge.to + ").");

  const data = [...dataMap.values()].sort((a, b) => a.name.localeCompare(b.name, "pt"));
  const resources = [...resourceMap.values()].sort((a, b) =>
    a.type.localeCompare(b.type, "pt") || a.name.localeCompare(b.name, "pt"));
  return {
    name: raw.name, displayName: raw.displayName || raw.name, description: raw.description || "",
    version: raw.version, fileName, kind: legacy ? "legacy" : "0.4",
    nodes, edges, data, resources, warnings, contract, response,
    graph: graph, models, templates: templateMap, policies: asObject(raw.policies),
    raw
  };
}

export function affectedNodes(capability, { variable = null, resource = null } = {}) {
  const ids = new Set();
  if (variable) {
    const item = capability.data.find(entry => entry.name === variable);
    if (item) for (const id of [...item.reads, ...item.writes]) ids.add(id);
  }
  if (resource) {
    const item = capability.resources.find(entry => entry.key === resource);
    if (item) for (const id of item.nodes) ids.add(id);
  }
  return ids;
}

export function layoutGraph(capability) {
  const nodes = capability.nodes;
  const index = new Map(nodes.map((node, position) => [node.id, position]));
  const incoming = new Map(nodes.map(n => [n.id, []]));
  const outgoing = new Map(nodes.map(n => [n.id, []]));
  for (const edge of capability.edges) {
    if (!outgoing.has(edge.from) || !incoming.has(edge.to)) continue;
    outgoing.get(edge.from).push(edge.to);
    incoming.get(edge.to).push(edge.from);
  }

  // Shortest directed distance from contract; bounded even with back-edges/cycles.
  const levels = new Map([[contractId, 0]]);
  const queue = [contractId];
  while (queue.length) {
    const id = queue.shift();
    for (const to of outgoing.get(id) || []) {
      if (levels.has(to)) continue;
      levels.set(to, levels.get(id) + 1);
      queue.push(to);
    }
  }
  let maxLevel = Math.max(0, ...levels.values());
  for (const node of nodes) if (!levels.has(node.id)) levels.set(node.id, ++maxLevel);
  const groups = new Map();
  for (const node of nodes) {
    const level = levels.get(node.id);
    if (!groups.has(level)) groups.set(level, []);
    groups.get(level).push(node);
  }

  const positions = new Map();
  for (const [level, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
    // Prefer incoming source positions for stable visual ordering at a join.
    group.sort((a, b) => {
      const score = n => {
        const origins = (incoming.get(n.id) || []).filter(p => positions.has(p));
        if (!origins.length) return index.get(n.id) * 10;
        return origins.reduce((sum, p) => sum + positions.get(p).y, 0) / origins.length;
      };
      return score(a) - score(b) || index.get(a.id) - index.get(b.id);
    });
    const height = group.length * 184 - 28;
    group.forEach((node, i) => {
      const width = node.kind === "contract" ? 270 : (node.kind === "terminal" ? 196 : 252);
      const y = i * 184 + 150 - Math.max(0, height - 160) / 2;
      positions.set(node.id, { x: level * 356 + 88, y, width,
        height: node.kind === "contract" ? 130 : (node.kind === "terminal" ? 96 : 120) });
    });
  }
  const bounds = [...positions.values()].reduce((acc, p) => ({
    minX: Math.min(acc.minX, p.x), minY: Math.min(acc.minY, p.y),
    maxX: Math.max(acc.maxX, p.x + p.width), maxY: Math.max(acc.maxY, p.y + p.height)
  }), { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity });
  return { positions, bounds };
}
