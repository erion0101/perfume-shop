// Downloads the OpenAPI document from a running API (Development) into openapi.json.
// Usually not needed: the backend test OpenApiSnapshotTests writes it with UPDATE_OPENAPI_SNAPSHOT=1.
import { writeFile } from "node:fs/promises";

const base = process.env.API_URL ?? "http://localhost:5080";
const response = await fetch(`${base}/openapi/v1.json`);
if (!response.ok) {
  throw new Error(`GET ${base}/openapi/v1.json → ${response.status}. Is the API running in Development?`);
}

const document = await response.json();
delete document.servers; // host-specific, same as the snapshot test
await writeFile(new URL("../openapi.json", import.meta.url), `${JSON.stringify(document, null, 2)}\n`);
console.log(`openapi.json updated from ${base}`);
