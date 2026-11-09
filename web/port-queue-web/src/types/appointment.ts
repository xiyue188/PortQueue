// 预约查询 API 契约。bigint 主键用字符串；时间为带时区的 ISO 8601。
export const appointmentStatuses = [
  { code: 'DRAFT', label: '草稿' },
  { code: 'PENDING', label: '待审核' },
  { code: 'APPROVED', label: '已批准' },
  { code: 'REJECTED', label: '已驳回' },
  { code: 'CANCELLED', label: '已撤销' },
  { code: 'COMPLETED', label: '已完成' },
] as const

export type AppointmentStatus = (typeof appointmentStatuses)[number]['code']

export interface AppointmentItem {
  appointmentId: string
  appointmentNo: string
  shipName: string
  imoNo: string | null
  mmsi: string | null
  requestedArrivalAt: string
  requestedDepartureAt: string
  appointmentStatusCode: AppointmentStatus
  reviewerName: string | null
  reviewedAt: string | null
  reviewComment: string | null
}

export interface AppointmentQuery {
  page: number
  pageSize: number
  status: AppointmentStatus | ''
  keyword: string
}

export interface AppointmentPage {
  items: AppointmentItem[]
  page: number
  pageSize: number
  totalCount: number
}

/** 审核资料独立读取，任何字段未加载都不能开放审核。 */
export interface AppointmentDetail extends AppointmentItem {
  shipId: string; previousPortId: string | null; nextPortId: string | null; editVersion: string
  shipType: string; companyName: string; lengthM: number; breadthM: number; maxDraftM: number
  grossTonnage: number | null; deadweightT: number | null; callNo: string
  plannedArrivalAt: string; plannedDepartureAt: string | null
  previousPortName: string | null; nextPortName: string | null; cargoTeu: number | null; cargoWeightT: number | null
  estimatedOperationHours: number; declaredDraftM: number; priorityLevel: number; dangerousGoodsFlag: boolean
  specialRequirement: string | null; submittedAt: string | null
}
