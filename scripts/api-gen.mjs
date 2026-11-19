// pnpm api:gen — exports the API's OpenAPI document and regenerates the TypeScript types in @astryx/api-client (T-010).
// Needs the API running in Development (pnpm stack:up). CI fails when either output is stale: OpenApiDriftTests compares
// apps/api/openapi.json with the running API, and the mobile job regenerates the types from it and diffs them.
import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const apiUrl = process.env.API_URL ?? 'http://localhost:5080';
const documentPath = join(root, 'apps/api/openapi.json');
const typesPath = join(root, 'packages/api-client/src/generated/schema.ts');

const response = await fetch(`${apiUrl}/openapi/v1.json`);
if (!response.ok) {
  console.error(`GET ${apiUrl}/openapi/v1.json → HTTP ${response.status}. Is the API running in Development? (pnpm stack:up)`);
  process.exit(1);
}

const document = await response.json();
delete document.servers; // host-specific; keeps the committed document stable
writeFileSync(documentPath, `${JSON.stringify(document, null, 2)}\n`);
mkdirSync(dirname(typesPath), { recursive: true });
execFileSync('pnpm', ['--filter', '@astryx/api-client', 'exec', 'openapi-typescript', documentPath, '--output', typesPath], { stdio: 'inherit' });
console.log(`Wrote ${documentPath} and ${typesPath}`);
