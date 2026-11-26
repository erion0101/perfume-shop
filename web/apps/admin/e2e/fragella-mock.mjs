// Stand-in for https://api.fragella.com/api/v1/ during e2e runs (no real API key, no monthly quota used).
// Returns the documented sample, renamed with E2E_RUN_ID so every run imports a brand-new fragrance.
import { readFile } from "node:fs/promises";
import { createServer } from "node:http";

const port = Number(process.env.FRAGELLA_MOCK_PORT ?? 4010);
const runId = process.env.E2E_RUN_ID ?? "local";
const sample = JSON.parse(await readFile(new URL("./fixtures/fragella-search.json", import.meta.url), "utf8"));

createServer((request, response) => {
  const url = new URL(request.url ?? "/", `http://127.0.0.1:${port}`);
  if (url.pathname === "/health") {
    response.writeHead(200).end("ok");
    return;
  }

  if (request.headers["x-api-key"] !== "e2e-fragella-key") {
    response.writeHead(401, { "Content-Type": "application/json" }).end(JSON.stringify({ error: "invalid api key" }));
    return;
  }

  if (url.pathname === "/api/v1/fragrances") {
    const items = sample.slice(0, 1).map((item) => ({ ...item, _id: `E2E-${runId}`, Name: `Khamrah E2E ${runId}` }));
    response.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify(items));
    return;
  }

  response.writeHead(404, { "Content-Type": "application/json" }).end("{}");
}).listen(port, "127.0.0.1", () => console.log(`Fragella mock on http://127.0.0.1:${port} (run ${runId})`));
