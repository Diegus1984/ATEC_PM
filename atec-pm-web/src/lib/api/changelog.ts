import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse } from "@/lib/api/types"

export interface ChangelogSegnalazione {
  id: number
  title: string
}

/** Una versione pubblicata: build, data del deploy, commit e segnalazioni chiuse. */
export interface ChangelogVoce {
  build: string
  data: string
  modifiche: string[]
  segnalazioni: ChangelogSegnalazione[]
}

export async function fetchChangelog(): Promise<ChangelogVoce[]> {
  const response = await apiGet<ApiResponse<ChangelogVoce[]>>("/api/changelog")
  return unwrapApi(response)
}
