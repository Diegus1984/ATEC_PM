import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  GammaComponentDto,
  GammaDistintaAddRequest,
  GammaDistintaItemDto,
  GammaDistintaUpdateRequest,
  GammaQuadroDto,
  GammaQuadroSaveRequest,
  GammaRobotDto,
  GammaRobotSaveRequest,
  GammaUsageDto,
} from "@/lib/api/types"

export async function fetchGammaRobots(): Promise<GammaRobotDto[]> {
  const response = await apiGet<ApiResponse<GammaRobotDto[]>>("/api/gamma-robot/robots")
  return unwrapApi(response)
}

export async function fetchGammaQuadri(robotId: number): Promise<GammaQuadroDto[]> {
  const response = await apiGet<ApiResponse<GammaQuadroDto[]>>(
    `/api/gamma-robot/robots/${robotId}/quadri`
  )
  return unwrapApi(response)
}

export async function fetchGammaDistinta(
  quadroId: number
): Promise<GammaDistintaItemDto[]> {
  const response = await apiGet<ApiResponse<GammaDistintaItemDto[]>>(
    `/api/gamma-robot/quadri/${quadroId}/distinta`
  )
  return unwrapApi(response)
}

export async function fetchGammaComponents(): Promise<GammaComponentDto[]> {
  const response = await apiGet<ApiResponse<GammaComponentDto[]>>(
    "/api/gamma-robot/components"
  )
  return unwrapApi(response)
}

export async function fetchGammaUsage(productId: number): Promise<GammaUsageDto[]> {
  const response = await apiGet<ApiResponse<GammaUsageDto[]>>(
    `/api/gamma-robot/products/${productId}/usage`
  )
  return unwrapApi(response)
}

export async function createGammaRobot(
  request: GammaRobotSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/gamma-robot/robots", request)
  return unwrapApi(response)
}

export async function updateGammaRobot(
  id: number,
  request: GammaRobotSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/gamma-robot/robots/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteGammaRobot(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/gamma-robot/robots/${id}`
  )
  return unwrapApi(response)
}

export async function createGammaQuadro(
  robotId: number,
  request: GammaQuadroSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/gamma-robot/robots/${robotId}/quadri`,
    request
  )
  return unwrapApi(response)
}

export async function updateGammaQuadro(
  id: number,
  request: GammaQuadroSaveRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/gamma-robot/quadri/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteGammaQuadro(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/gamma-robot/quadri/${id}`
  )
  return unwrapApi(response)
}

export async function addGammaDistinta(
  request: GammaDistintaAddRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/gamma-robot/distinta",
    request
  )
  return unwrapApi(response)
}

export async function updateGammaDistinta(
  id: number,
  request: GammaDistintaUpdateRequest
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/gamma-robot/distinta/${id}`,
    request
  )
  return unwrapApi(response)
}

export async function deleteGammaDistinta(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/gamma-robot/distinta/${id}`
  )
  return unwrapApi(response)
}
