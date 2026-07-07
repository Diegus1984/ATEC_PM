import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, DashboardData } from "@/lib/api/types"

export async function fetchDashboard(): Promise<DashboardData> {
  const response = await apiGet<ApiResponse<DashboardData>>("/api/dashboard")
  return unwrapApi(response)
}
