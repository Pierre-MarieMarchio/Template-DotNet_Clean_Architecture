import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { isStale, schemaFrom } from '../scripts/schema.ts';

const document = (path: string) => ({
  openapi: '3.0.1',
  info: { title: 'Api', version: 'v1' },
  paths: { [path]: { get: { responses: { 200: { description: 'OK' } } } } },
});

describe('isStale', () => {
  it('accepts the same text', () => {
    expect(isStale('a\nb\n', 'a\nb\n')).toBe(false);
  });

  it('ignores the line endings a checkout converted', () => {
    expect(isStale('a\r\nb\r\n', 'a\nb\n')).toBe(false);
  });

  it('refuses a different text', () => {
    expect(isStale('a\n', 'b\n')).toBe(true);
  });
});

describe('schemaFrom', () => {
  let folder = '';

  beforeEach(() => {
    folder = mkdtempSync(join(tmpdir(), 'schema-'));
  });

  afterEach(() => {
    rmSync(folder, { recursive: true, force: true });
  });

  it('differs once a route differs', async () => {
    const before = join(folder, 'before.json');
    const after = join(folder, 'after.json');
    writeFileSync(before, JSON.stringify(document('/api/v1/notes')));
    writeFileSync(after, JSON.stringify(document('/api/v1/notes/archived')));

    expect(isStale(await schemaFrom(before), await schemaFrom(after))).toBe(true);
    expect(isStale(await schemaFrom(before), await schemaFrom(before))).toBe(false);
  });
});
