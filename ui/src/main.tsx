import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Self-hosted variable fonts (Outfit display/body, JetBrains Mono technical) —
// bundled by Vite so the app renders identically offline. Must precede the
// stylesheet so the @font-face rules are registered before first layout.
import '@fontsource-variable/outfit'
import '@fontsource-variable/jetbrains-mono'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
