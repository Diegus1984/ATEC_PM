import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  DdpFeedbackAcquistiGroup,
  DdpFeedbackMagazzinoGroup,
} from "@/lib/api/types"

export async function fetchDdpFeedbackAcquisti(): Promise<
  DdpFeedbackAcquistiGroup[]
> {
  const response = await apiGet<ApiResponse<DdpFeedbackAcquistiGroup[]>>(
    "/api/ddp-feedback/acquisti"
  )
  return unwrapApi(response)
}

export async function setDdpFeedbackAcquistiNote(
  projectId: number,
  ddpType: string,
  statusKey: string,
  note: string
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/ddp-feedback/acquisti/${projectId}/${ddpType}/${statusKey}/note`,
    { note }
  )
  unwrapApi(response)
}

export async function setDdpFeedbackAcquistiHidden(
  projectId: number,
  ddpType: string,
  statusKey: string,
  hidden: boolean
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/ddp-feedback/acquisti/${projectId}/${ddpType}/${statusKey}/hidden`,
    { hidden }
  )
  unwrapApi(response)
}

export async function resetDdpFeedbackAcquisti(
  projectId: number,
  ddpType: string
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/ddp-feedback/acquisti/${projectId}/${ddpType}/reset`,
    {}
  )
  unwrapApi(response)
}

export async function fetchDdpFeedbackMagazzino(): Promise<
  DdpFeedbackMagazzinoGroup[]
> {
  const response = await apiGet<ApiResponse<DdpFeedbackMagazzinoGroup[]>>(
    "/api/ddp-feedback/magazzino"
  )
  return unwrapApi(response)
}

export async function setDdpFeedbackMagazzinoHidden(
  projectId: number,
  ddpType: string,
  itemId: number,
  hidden: boolean
): Promise<void> {
  const response = await apiPut<ApiResponse<boolean>>(
    `/api/ddp-feedback/magazzino/${projectId}/${ddpType}/${itemId}/hidden`,
    { hidden }
  )
  unwrapApi(response)
}

export async function resetDdpFeedbackMagazzino(
  projectId: number,
  ddpType: string
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    `/api/ddp-feedback/magazzino/${projectId}/${ddpType}/reset`,
    {}
  )
  unwrapApi(response)
}
