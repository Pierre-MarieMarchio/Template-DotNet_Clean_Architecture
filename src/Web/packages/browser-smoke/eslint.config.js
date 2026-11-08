import { typescriptConfig } from '@app-template/eslint-config';
import { globalIgnores } from 'eslint/config';

export default [globalIgnores(['test-results/']), ...typescriptConfig(import.meta.dirname)];
