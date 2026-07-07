import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom"

import { AppRoutes } from "@/app/AppRoutes"
import { AppShell } from "@/app/AppShell"
import { AuthBootstrap } from "@/app/AuthBootstrap"
import { ConfirmProvider } from "@/components/shared/confirm"
import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import { LoginPage } from "@/features/auth/LoginPage"

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
      // Evita il refetch completo ad ogni ingresso in una pagina: entro questa
      // finestra i dati arrivano dalla cache. «Aggiorna» e le mutation
      // (invalidateQueries) forzano comunque il ricaricamento; refetchInterval
      // (es. polling sync Codex) resta indipendente.
      staleTime: 5 * 60 * 1000, // 5 min "freschi"
      gcTime: 30 * 60 * 1000, // cache mantenuta 30 min
    },
  },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider delayDuration={0}>
        <ConfirmProvider>
          <BrowserRouter>
            <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              element={
                <AuthBootstrap>
                  <AppShell />
                </AuthBootstrap>
              }
            >
              {AppRoutes()}
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </ConfirmProvider>
        <Toaster closeButton position="bottom-right" richColors />
      </TooltipProvider>
    </QueryClientProvider>
  )
}
