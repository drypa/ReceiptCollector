import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const API_PORT = 5039;

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: `http://localhost:${API_PORT}`,
        changeOrigin: true,
        secure: false,
      },
    },
  },
  preview: {
    proxy: {
      '/api': {
        target: `http://localhost:${API_PORT}`,
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    outDir: '../src/ReceiptCollector.Analytics.Api/wwwroot',
    emptyOutDir: true,
    assetsDir: 'assets',
    sourcemap: true,
  },
});
