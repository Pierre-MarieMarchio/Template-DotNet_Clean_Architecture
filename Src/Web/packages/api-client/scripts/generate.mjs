import { execFileSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import openapiTS, { astToString } from 'openapi-typescript';

const packageRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const apiProject = join(packageRoot, '..', '..', '..', 'Presentation', 'AppTemplate.Api');
const document = join(packageRoot, 'openapi', 'document.json');
const schema = join(packageRoot, 'src', 'generated', 'schema.ts');

rmSync(join(apiProject, 'obj', 'AppTemplate.Api.OpenApiFiles.cache'), { force: true });
rmSync(document, { force: true });

execFileSync(
  'dotnet',
  ['build', join(apiProject, 'AppTemplate.Api.csproj'), '--nologo', '--verbosity', 'quiet', '-p:OpenApiGenerateDocumentsOnBuild=true'],
  { stdio: 'inherit', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' } },
);

const ast = await openapiTS(pathToFileURL(document), { alphabetize: true });

mkdirSync(dirname(schema), { recursive: true });
writeFileSync(schema, astToString(ast));

console.log(`Wrote ${schema}`);
