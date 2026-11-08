import { typescriptConfig } from '@app-template/eslint-config';
import { defineConfig } from 'eslint/config';
import { builtinModules } from 'node:module';

export default defineConfig(typescriptConfig(import.meta.dirname), {
  files: ['src/*.ts'],
  ignores: ['src/server.ts'],
  rules: {
    'no-restricted-imports': [
      'error',
      {
        patterns: [
          { group: ['node:*', ...builtinModules, 'openid-client'], message: 'The main entry runs in the browser: a server module goes under src/server.' },
          { group: ['./server.ts', './server/*'], message: 'The main entry runs in the browser: it imports nothing the server entry holds.' },
        ],
      },
    ],
  },
});
