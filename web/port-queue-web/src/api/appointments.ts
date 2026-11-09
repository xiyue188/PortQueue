// 只管理预约 HTTP 请求与协议校验，不保存页面状态，不提供 Mock 兜底。
import { appointmentStatuses, type AppointmentItem, type AppointmentPage, type AppointmentQuery } from '../types/appointment'

const statusCodes = new Set<string>(appointmentStatuses.map(item => item.code))
const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null
const nullableString = (value: unknown) => value === null || typeof value === 'string'
const isoTime = (value: unknown) => typeof value === 'string'
  && /(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value))

function isItem(value: unknown): value is AppointmentItem {
  return isObject(value) && typeof value.appointmentId === 'string' && /^\d+$/.test(value.appointmentId)
    && typeof value.appointmentNo === 'string' && typeof value.shipName === 'string'
    && nullableString(value.imoNo) && nullableString(value.mmsi)
    && isoTime(value.requestedArrivalAt) && isoTime(value.requestedDepartureAt)
    && typeof value.appointmentStatusCode === 'string' && statusCodes.has(value.appointmentStatusCode)
    && nullableString(value.reviewerName) && nullableString(value.reviewComment)
    && (value.reviewedAt === null || isoTime(value.reviewedAt))
}

/** 验证真实响应，避免把 Vite 回退 HTML 或不匹配的 API 结构当成空列表。 */
function isPage(value: unknown): value is AppointmentPage {
  return isObject(value) && Array.isArray(value.items) && value.items.every(isItem)
    && Number.isSafeInteger(value.page) && Number(value.page) > 0
    && Number.isSafeInteger(value.pageSize) && Number(value.pageSize) >= 1 && Number(value.pageSize) <= 100
    && Number.isSafeInteger(value.totalCount) && Number(value.totalCount) >= 0
    && value.items.length <= Number(value.pageSize) && value.items.length <= Number(value.totalCount)
}

export async function fetchAppointments(query: AppointmentQuery, signal: AbortSignal): Promise<AppointmentPage> {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
  if (query.status) params.set('status', query.status)
  if (query.keyword) params.set('keyword', query.keyword)

  const response = await fetch(`/api/appointments?${params}`, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) {
    if (response.status === 400) throw new Error('查询条件无效，请检查关键词和筛选条件。')
    if (response.status === 503) throw new Error('预约数据暂时无法读取，请稍后重试。')
    throw new Error('查询服务暂时不可用，请稍后重试。')
  }
  let payload: unknown
  try { payload = await response.json() }
  catch { throw new Error('查询响应无法识别，请稍后重试。') }
  if (!isPage(payload) || payload.page !== query.page || payload.pageSize !== query.pageSize)
    throw new Error('查询响应格式不正确，请稍后重试。')
  return payload
}

/** 精确读取详情，不把主列表改成按编号筛选；保持 bigint 字符串精度。 */
export async function fetchAppointment(appointmentId: string, signal: AbortSignal): Promise<AppointmentItem> {
  if (!/^[1-9]\d*$/.test(appointmentId)) throw new Error('预约标识无效。')
  const response = await fetch(`/api/appointments/${encodeURIComponent(appointmentId)}`, { signal, headers: { Accept: 'application/json' } })
  if (response.status === 404) throw new Error('该预约不存在，请重新选择。')
  if (!response.ok) throw new Error('预约详情暂时无法读取，请重试。')
  const item: unknown = await response.json()
  if (!isItem(item) || item.appointmentId !== appointmentId) throw new Error('预约详情响应不正确，请重试。')
  return item
}
