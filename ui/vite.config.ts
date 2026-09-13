import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// base './' so the built bundle resolves against the virtual host root
// (https://vibealarm.app/index.html) rather than a dev-server absolute path.
export default defineConfig({
  plugins: [react()],
  base: './',
})
