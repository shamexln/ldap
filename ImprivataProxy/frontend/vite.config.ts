import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../src/ImprivataProxy/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/admin': {
        target: 'http://127.0.0.1:80',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://127.0.0.1:80',
        changeOrigin: true,
      },
    },
  },
})
