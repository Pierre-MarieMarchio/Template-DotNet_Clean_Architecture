import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { discoverApps, findProjectRoot, standInProvider } from '../src/apps.ts';

describe('the apps the smoke drives', () => {
  let root = '';

  beforeEach(() => {
    root = mkdtempSync(join(tmpdir(), 'browser-smoke-'));
  });

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  function project(apps: string[], env?: string): void {
    const recorded = { web: { scope: 'sample', apps: apps.map((name) => ({ name })), packages: [] } };
    writeFileSync(join(root, 'cleanarch.json'), JSON.stringify(recorded));
    if (env !== undefined) {
      writeFileSync(join(root, '.env.example'), env);
    }
  }

  it('are the ones cleanarch.json records, each at the port its variable names', () => {
    project(['Storefront', 'BackOffice'], 'WEB_STOREFRONT_HOST_PORT=4301\nWEB_BACK_OFFICE_HOST_PORT=8082\nAPI_HOST_PORT=8080\n');

    expect(discoverApps(root, {})).toEqual([
      { name: 'Storefront', origin: 'http://127.0.0.1:4301', signsInFromThePage: false },
      { name: 'BackOffice', origin: 'http://127.0.0.1:8082', signsInFromThePage: false },
    ]);
  });

  it('take the port the environment overrides', () => {
    project(['BackOffice'], 'WEB_BACK_OFFICE_HOST_PORT=8082\n');

    expect(discoverApps(root, { WEB_BACK_OFFICE_HOST_PORT: '9000' })[0]?.origin).toBe('http://127.0.0.1:9000');
  });

  it('read the file the stack was started from, ahead of .env.example', () => {
    project(['BackOffice'], 'WEB_BACK_OFFICE_HOST_PORT=8082\n');
    writeFileSync(join(root, 'stack.env'), 'WEB_BACK_OFFICE_HOST_PORT=7000\n');

    expect(discoverApps(root, { SMOKE_ENV_FILE: 'stack.env' })[0]?.origin).toBe('http://127.0.0.1:7000');
  });

  it('name the app whose port is missing', () => {
    project(['BackOffice'], 'API_HOST_PORT=8080\n');

    expect(() => discoverApps(root, {})).toThrow('BackOffice');
  });

  it('are none when the project records no web part', () => {
    writeFileSync(join(root, 'cleanarch.json'), '{}');

    expect(discoverApps(root, {})).toEqual([]);
  });

  it('are found from a folder below the project file', () => {
    project([]);

    expect(findProjectRoot(join(root, 'src', 'Web'))).toBe(root);
  });

  it('sign in from the page only on WebAssembly', () => {
    const recorded = { web: { apps: [{ name: 'Wasm', framework: 'blazor-wasm' }, { name: 'Spa', framework: 'vite-react' }] } };
    writeFileSync(join(root, 'cleanarch.json'), JSON.stringify(recorded));
    writeFileSync(join(root, '.env.example'), 'WEB_WASM_HOST_PORT=4302\nWEB_SPA_HOST_PORT=4303\n');

    expect(discoverApps(root, {}).map((app) => app.signsInFromThePage)).toEqual([true, false]);
  });
});

describe('the provider the smoke stands in for', () => {
  let root = '';

  beforeEach(() => {
    root = mkdtempSync(join(tmpdir(), 'browser-smoke-'));
  });

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  it('is the authority of the stack when its host can never resolve', () => {
    writeFileSync(join(root, '.env'), 'EXTERNAL_AUTH_AUTHORITY=\nEXTERNAL_AUTH_AUTHORITY=https://identity-provider.invalid/\n');

    expect(standInProvider(root, {})).toEqual({
      authority: 'https://identity-provider.invalid/',
      origin: 'https://identity-provider.invalid',
      discovery: 'https://identity-provider.invalid/.well-known/openid-configuration',
    });
  });

  it('keeps the path of an authority that has one', () => {
    writeFileSync(join(root, '.env'), 'EXTERNAL_AUTH_AUTHORITY=https://provider.invalid/realms/app\n');

    expect(standInProvider(root, {})?.discovery).toBe('https://provider.invalid/realms/app/.well-known/openid-configuration');
  });

  it('takes the authority the environment overrides', () => {
    writeFileSync(join(root, '.env.example'), 'EXTERNAL_AUTH_AUTHORITY=\n');

    expect(standInProvider(root, { EXTERNAL_AUTH_AUTHORITY: 'https://identity-provider.invalid/' })?.origin).toBe('https://identity-provider.invalid');
  });

  it('is none for a provider that resolves, or for no provider', () => {
    writeFileSync(join(root, '.env'), 'EXTERNAL_AUTH_AUTHORITY=https://login.example.com/\n');

    expect(standInProvider(root, {})).toBeUndefined();
    expect(standInProvider(root, { EXTERNAL_AUTH_AUTHORITY: '' })).toBeUndefined();
  });
});
