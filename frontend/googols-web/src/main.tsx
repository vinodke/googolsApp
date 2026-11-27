import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { router } from './app/router'
import { AuthProvider } from './core/auth/auth'
import '@fontsource-variable/plus-jakarta-sans'
import '@fontsource-variable/noto-sans-malayalam'
import './styles/global.css'

// Apply the saved theme before the first paint to avoid a light flash in dark mode.
try {
  const saved = localStorage.getItem('googols-theme')
  document.documentElement.dataset.theme = saved === 'dark' || saved === 'light' ? saved
    : matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
} catch { /* storage unavailable */ }

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
