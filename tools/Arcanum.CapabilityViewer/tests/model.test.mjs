import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { resolve, dirname } from "node:path";
import { parseCapability, affectedNodes, layoutGraph } from "../wwwroot/model.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const sample = name => JSON.parse(readFileSync(resolve(root,
  "src/Arcanum.Demo.Api/capabilities/" + name + ".json"), "utf8"));

test("v0.4 projects a contract entry, typed variables and two occurrences of one template", () => {
  const cap = parseCapability(sample("demo.capability-json"), { fileName: "demo.capability-json.json" });
  assert.equal(cap.kind, "0.4");
  assert.equal(cap.nodes[0].id, "__contract__");
  assert.equal(cap.edges[0].from, "__contract__");
  assert.equal(cap.edges[0].to, "first");
  assert.equal(cap.nodes.find(n => n.id === "first").process,
    cap.nodes.find(n => n.id === "second").process);
  assert.equal(cap.nodes.find(n => n.id === "second").outputs[0].name, "finalEcho");
  assert.equal(cap.response.name, "finalEcho");
  assert.equal(cap.data.find(v => v.name === "request").type, "Arcanum.Demo.Api.DemoInput");
  assert.deepEqual([...affectedNodes(cap, { variable: "firstEcho" })].sort(),
    ["first", "second"]);
  assert.equal(cap.data.find(v => v.name === "firstEcho").reads.has("second"), true);
  const layout = layoutGraph(cap);
  assert.ok(layout.positions.get("first").x > layout.positions.get("__contract__").x);
  assert.ok(layout.positions.get("second").x > layout.positions.get("first").x);
});

test("parallel graph preserves all spawn branches, join and data participation", () => {
  const cap = parseCapability(sample("demo.capability-json-parallel"));
  assert.equal(cap.edges.filter(e => e.spawn).length, 2);
  const participants = affectedNodes(cap, { variable: "validated" });
  assert.ok(participants.has("fork"));
  assert.ok(participants.has("alpha"));
  assert.ok(participants.has("beta"));
  assert.ok(participants.has("gamma"));
  assert.ok(!participants.has("join"));
  const alpha = cap.data.find(d => d.name === "alpha");
  assert.ok(alpha.writes.has("alpha"));
  assert.ok(alpha.reads.has("join"));
  const layout = layoutGraph(cap);
  assert.equal(layout.positions.size, cap.nodes.length);
  assert.ok(layout.bounds.maxX > layout.bounds.minX);
});

test("DI resource participation is merged across nodes using CLR metadata", () => {
  const cap = parseCapability(sample("demo.capability-json-parallel"), {
    processContracts: {
      "Arcanum.Demo.Api.ValidateProcess": {
        inputs: [{ name: "request", type: "Arcanum.Demo.Api.DemoInput" }],
        resources: [{ name: "probe", type: "Arcanum.Demo.Api.IExecutionProbe", inferred: true }]
      },
      "Arcanum.Demo.Api.ParallelStartProcess": { resources: [] },
      "Arcanum.Demo.Api.ParallelAlphaProcess": { resources: [] },
      "Arcanum.Demo.Api.ParallelBetaProcess": {
        resources: [{ name: "dependency", type: "Demo.SharedService", inferred: true }]
      },
      "Arcanum.Demo.Api.ParallelJoinProcess": { resources: [] }
    }
  });
  const value = cap.resources.find(item => item.type === "Demo.SharedService");
  assert.ok(value);
  assert.deepEqual([...value.nodes].sort(), ["beta", "gamma"]);
  assert.deepEqual([...affectedNodes(cap, { resource: value.key })].sort(), ["beta", "gamma"]);
});

test("legacy graph renders terminal destinations and contract responses", () => {
  const cap = parseCapability(sample("demo.publish"), {
    processContracts: {
      "Arcanum.Demo.Api.ValidateProcess": {
        inputs: [{ name: "request", type: "Arcanum.Demo.Api.DemoInput" }],
        outputType: "Arcanum.Demo.Api.ValidationData", returnName: "validated",
        resources: [{ name: "probe", type: "Arcanum.Demo.Api.IExecutionProbe" }]
      },
      "Arcanum.Demo.Api.EnrichProcess": {
        inputs: [{ name: "validated", type: "Arcanum.Demo.Api.ValidationData" }],
        outputType: "Arcanum.Demo.Api.EnrichedData", returnName: "enriched"
      },
      "Arcanum.Demo.Api.FinishProcess": {
        inputs: [{ name: "enriched", type: "Arcanum.Demo.Api.EnrichedData" }],
        outputType: "Arcanum.Demo.Api.CompletedData", returnName: "final"
      }
    }
  });
  assert.equal(cap.kind, "legacy");
  assert.ok(cap.nodes.some(n => n.kind === "terminal" && n.label === "@complete"));
  assert.ok(cap.nodes.some(n => n.kind === "terminal" && n.label === "@stop"));
  assert.ok(cap.edges.some(e => e.label === "invalid" && e.to === "__terminal__:@stop"));
  assert.equal(cap.response.variants.length, 2);
  assert.deepEqual([...affectedNodes(cap, { variable: "validated" })].sort(),
    ["enrich", "validate"]);
  assert.ok(cap.resources.find(item => item.name === "probe").nodes.has("validate"));
});

test("cycles have bounded layout and unknown destinations create diagnostics", () => {
  const raw = sample("demo.capability-json");
  raw.graph.edges.push({ from: "second", intention: "again", to: "first" });
  raw.graph.edges.push({ from: "second", intention: "broken", to: "unknown" });
  const cap = parseCapability(raw);
  assert.ok(cap.warnings.some(w => w.includes("unknown")));
  const layout = layoutGraph(cap);
  assert.equal(layout.positions.size, 3);
  assert.ok(Number.isFinite(layout.bounds.maxX));
});

test("invalid files cannot be treated as executable JavaScript or capabilities", () => {
  assert.throws(() => parseCapability("{invalid"), /JSON inválido/);
  assert.throws(() => parseCapability({ name: "no.graph" }), /Capability válida/);
  const cap = parseCapability(sample("demo.capability-json"));
  assert.equal(cap.nodes.find(n => n.id === "first").raw.template, "echo");
});

test("viewer provides directory/file management, viewport and structure panels", () => {
  const html = readFileSync(resolve(root, "tools/Arcanum.CapabilityViewer/wwwroot/index.html"), "utf8");
  for (const token of [
    "webkitdirectory", 'id="file-list"', 'id="graph-svg"', 'id="node-details"',
    'id="tab-data"', 'id="tab-di"', 'id="fit-view"', 'id="close-all"'
  ]) assert.ok(html.includes(token), "Missing viewer control " + token);
});
