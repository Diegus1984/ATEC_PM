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
      // Ambiente multi-reparto (richiesta esplicita 14/07/2026): un dato inserito da
      // chiunque deve comparire SUBITO. Quindi: ogni ingresso in una pagina rilegge
      // sempre dal server (staleTime 0) e il ritorno sulla finestra riallinea
      // (refetchOnWindowFocus). La cache (gcTime) resta solo come placeholder mentre
      // arriva il dato fresco — la navigazione non mostra mai pagine vuote.
      // A pagina aperta la diretta la fanno gli hub SignalR dei moduli collaborativi.
      refetchOnWindowFocus: true,
      retry: 1,
      staleTime: 0,
      gcTime: 30 * 60 * 1000, // cache mantenuta 30 min (solo come placeholder)
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
