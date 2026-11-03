import { execFileSync } from 'node:child_process';
import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isStale, schemaFrom } from './schema.ts';

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const schema = join(packageRoot, 'src', 'generated', 'schema.ts');
const checkIndex = process.argv.indexOf('--check');

if (checkIndex >= 0) {
  const checked = process.argv[checkIndex + 1];

  if (checked === undefined) {
    throw new Error('--check takes the OpenAPI document to compare the schema with.');
  }

  if (isStale(readFileSync(schema, 'utf8'), await schemaFrom(checked))) {
    console.error(
      'src/generated/schema.ts differs from what openapi-typescript writes from the OpenAPI reference. ' +
        'Run `pnpm api` in Src/Web and commit what it writes.',
    );
    process.exit(1);
  }

  console.log('src/generated/schema.ts is what openapi-typescript writes from the OpenAPI reference.');
  process.exit(0);
}

const presentation = join(packageRoot, '..', '..', '..', 'Presentation');
const apiFolder = readdirSync(presentation).find((entry) => entry.endsWith('.Api'));
const apiProjectFile =
  apiFolder === undefined ? undefined : readdirSync(join(presentation, apiFolder)).find((entry) => entry.endsWith('.Api.csproj'));

if (apiProjectFile === undefined) {
  throw new Error(`${presentation} holds no <Initials>.Api folder with its <Name>.Api.csproj, the API the client describes.`);
}

const apiName = apiProjectFile.slice(0, -'.csproj'.length);
const apiProject = join(presentation, apiFolder);
const document = join(packageRoot, 'openapi', 'document.json');

rmSync(join(apiProject, 'obj', `${apiName}.OpenApiFiles.cache`), { force: true });
rmSync(document, { force: true });

const project = join(apiProject, `${apiName}.csproj`);
const fromLastBuild = process.argv.includes('--no-build');

execFileSync(
  'dotnet',
  fromLastBuild
    ? ['msbuild', project, '-nologo', '-verbosity:quiet', '-target:GenerateOpenApiDocuments']
    : ['build', project, '--nologo', '--verbosity', 'quiet', '-p:OpenApiGenerateDocumentsOnBuild=true'],
  {
    stdio: 'inherit',
    env: {
      Authentication__External__Authority: 'https://identity-provider.invalid/',
      Authentication__External__Audience: 'openapi-document',
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
    },
  },
);

mkdirSync(dirname(schema), { recursive: true });
writeFileSync(schema, await schemaFrom(document));

console.log(`Wrote ${schema}`);
