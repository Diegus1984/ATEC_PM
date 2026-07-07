import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr"

import { getApiBaseUrl } from "@/lib/api/client"
import { getSession } from "@/lib/auth/session"

export type HubName = "project" | "resource-planner" | "codex"

function hubPath(name: HubName): string {
  if (name === "project") return "/hubs/project"
  if (name === "codex") return "/hubs/codex"
  return "/hubs/resource-planner"
}

/** Crea una connessione SignalR verso gli hub ASP.NET esistenti. */
export function createHubConnection(name: HubName): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${getApiBaseUrl()}${hubPath(name)}`, {
      accessTokenFactory: () => getSession()?.token ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(
      import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning
    )
    .build()
}

export async function startHub(connection: HubConnection): Promise<boolean> {
  if (connection.state === HubConnectionState.Connected) {
    return true
  }

  try {
    await connection.start()
    return true
  } catch {
    // Real-time opzionale: la pagina resta usabile senza hub.
    return false
  }
}

export async function stopHub(connection: HubConnection | null) {
  if (!connection) {
    return
  }

  try {
    await connection.stop()
  } catch {
    /* chiusura best-effort */
  }
}
