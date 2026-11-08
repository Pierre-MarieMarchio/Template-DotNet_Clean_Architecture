import { execFileSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isStale, schemaFrom } from './schema.ts';

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const schema = join(packageRoot, 'src', 'generated', 'schema.ts');
const [first, second] = process.argv.slice(2);

async function check(reference) {
  if (reference === undefined) {
    throw new Error('--check takes the OpenAPI reference to compare the schema with.');
  }

  if (isStale(readFileSync(schema, 'utf8'), await schemaFrom(reference))) {
    console.error(
      'src/generated/schema.ts differs from what openapi-typescript writes from the OpenAPI reference. ' +
        'Run `pnpm api` in src/Web and commit what it writes.',
    );
    process.exit(1);
  }

  console.log('src/generated/schema.ts is what openapi-typescript writes from the OpenAPI reference.');
}

async function write(reference) {
  mkdirSync(dirname(schema), { recursive: true });
  writeFileSync(schema, await schemaFrom(reference));
  console.log(`Wrote ${schema}`);
}

function askTheTool(noBuild) {
  execFileSync('dotnet', ['cleanarch', 'openapi', ...(noBuild ? ['--no-build'] : [])], { stdio: 'inherit' });
}

if (first === '--check') {
  await check(second);
} else if (first === undefined || first === '--no-build') {
  askTheTool(first === '--no-build');
} else {
  await write(first);
}
