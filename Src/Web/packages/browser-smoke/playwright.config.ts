import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: 'smoke',
  reporter: 'list',
  use: { ...devices['Desktop Chrome'] },
  projects: [{ name: 'chromium' }],
});
