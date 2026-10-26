import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vite';

const api = process.env.API_UPSTREAM ?? 'http://localhost:5187';

export default defineConfig({
  plugins: [svelte()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api/': api, '/health': api },
  },
});
