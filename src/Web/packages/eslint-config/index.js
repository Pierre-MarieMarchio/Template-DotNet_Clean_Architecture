import js from '@eslint/js';
import { defineConfig, globalIgnores } from 'eslint/config';
import globals from 'globals';
import sonarjs from 'eslint-plugin-sonarjs';
import tseslint from 'typescript-eslint';

export function typescriptConfig(tsconfigRootDir) {
  return defineConfig(
    globalIgnores(['dist/', 'coverage/', 'src/generated/']),
    js.configs.recommended,
    tseslint.configs.strictTypeChecked,
    tseslint.configs.stylisticTypeChecked,
    {
      languageOptions: {
        parserOptions: {
          projectService: true,
          tsconfigRootDir,
        },
      },
    },
    {
      plugins: { sonarjs },
      rules: {
        'sonarjs/cognitive-complexity': ['error', 15],
      },
    },
    {
      files: ['**/*.js', '**/*.mjs'],
      extends: [tseslint.configs.disableTypeChecked],
      languageOptions: {
        globals: globals.node,
      },
    },
  );
}
