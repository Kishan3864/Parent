import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dev server proxies only /api; /health is hit directly against the API when needed.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
});
