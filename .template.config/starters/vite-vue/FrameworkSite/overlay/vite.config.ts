import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

const api = process.env.API_UPSTREAM ?? 'http://localhost:5187';

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api/': api, '/health': api },
  },
});
