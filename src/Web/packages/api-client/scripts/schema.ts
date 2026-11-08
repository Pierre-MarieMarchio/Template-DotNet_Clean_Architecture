import { pathToFileURL } from 'node:url';
import openapiTS, { astToString } from 'openapi-typescript';

export async function schemaFrom(document: string): Promise<string> {
  const ast = await openapiTS(pathToFileURL(document), { alphabetize: true });

  return astToString(ast);
}

export function isStale(committed: string, written: string): boolean {
  return committed.replaceAll('\r\n', '\n') !== written.replaceAll('\r\n', '\n');
}
