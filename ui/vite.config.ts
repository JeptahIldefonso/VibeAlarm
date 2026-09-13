import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// base './' so the built bundle resolves against the virtual host root
// (https://vibealarm.app/index.html) rather than a dev-server absolute path.
export default defineConfig({
  plugins: [react()],
  base: './',
  build: {
    // CSS minification is OFF on purpose. Vite 8's LightningCSS pipeline
    // rewrites `backdrop-filter: blur(...)` to its -webkit- form ONLY (dropping
    // the standard declaration), which WebView2's Chromium ignores — silently
    // losing every glass blur in the UI. The app is served from local disk, so
    // the ~12 KB the minifier would save buys nothing. The host is evergreen
    // Chromium; cssTarget keeps any future minifier honest.
    cssMinify: false,
    cssTarget: 'chrome120',
  },
})
