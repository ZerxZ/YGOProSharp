import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../YGOProSharp.Web/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks: {
          'vendor-react': ['react', 'react-dom', 'lucide-react'],
          'vendor-codemirror': [
            'codemirror',
            '@codemirror/state',
            '@codemirror/view',
            '@codemirror/language',
            '@codemirror/commands',
            '@codemirror/autocomplete',
            '@codemirror/lint',
            '@codemirror/legacy-modes/mode/lua',
            '@codemirror/theme-one-dark'
          ]
        }
      }
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://127.0.0.1:7912',
      '/healthz': 'http://127.0.0.1:7912',
      '/mcp': 'http://127.0.0.1:7912',
      '/ws': {
        target: 'ws://127.0.0.1:7912',
        ws: true
      }
    }
  }
});
