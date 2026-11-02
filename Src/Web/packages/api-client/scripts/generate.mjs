import { execFileSync } from 'node:child_process';
import { mkdirSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import openapiTS, { astToString } from 'openapi-typescript';

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
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
const schema = join(packageRoot, 'src', 'generated', 'schema.ts');

rmSync(join(apiProject, 'obj', `${apiName}.OpenApiFiles.cache`), { force: true });
rmSync(document, { force: true });

execFileSync(
  'dotnet',
  ['build', join(apiProject, `${apiName}.csproj`), '--nologo', '--verbosity', 'quiet', '-p:OpenApiGenerateDocumentsOnBuild=true'],
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

const ast = await openapiTS(pathToFileURL(document), { alphabetize: true });

mkdirSync(dirname(schema), { recursive: true });
writeFileSync(schema, astToString(ast));

console.log(`Wrote ${schema}`);
