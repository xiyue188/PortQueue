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
