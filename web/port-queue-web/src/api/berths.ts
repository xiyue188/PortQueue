// 泊位读取只接受真实 API 响应；标识和带时区时间在边界校验，失败不回退到原型样例。
import type { BerthSchedule, BerthVisitDetail, BerthVisitPage } from '../types/berth'
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object'
const id = (v: unknown) => typeof v === 'string' && /^[1-9]\d*$/.test(v)
const time = (v: unknown) => typeof v === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/.test(v) && Number.isFinite(Date.parse(v))
const nullableTime = (v: unknown) => v === null || time(v)
function revision(v: unknown) {
  return object(v) && ['revisionId','berthPlanId','appointmentId','berthId'].every(k => id(v[k]))
    && ['shipName','callNo','planStatusCode','revisionReason','revisedByName'].every(k => typeof v[k] === 'string')
    && ['plannedBerthingAt','plannedUnberthingAt','revisedAt'].every(k => time(v[k]))
    && nullableTime(v.confirmedAt) && typeof v.isCurrent === 'boolean' && Number.isInteger(v.versionNo)
}
function visit(v: unknown) {
  return object(v) && ['appointmentId','portCallId','shipId','shipTypeItemId'].every(k => id(v[k]))
    && ['appointmentNo','callNo','shipName','shipTypeName','appointmentStatusCode'].every(k => typeof v[k] === 'string')
    && time(v.requestedArrivalAt) && nullableTime(v.manualEstimatedBerthingAt)
    && ['lengthM','declaredDraftM','estimatedOperationHours'].every(k => typeof v[k] === 'number')
}
async function read<T>(url: string, signal: AbortSignal, valid: (value: unknown) => boolean): Promise<T> {
  const response = await fetch(url, { signal: AbortSignal.any([signal, AbortSignal.timeout(15_000)]), headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(response.status === 401 ? '请登录后查看泊位业务。' : response.status === 404 ? '该到访不存在或不属于本港。' : response.status === 400 ? '查询日期或条件无效，请调整后重试。' : '泊位数据暂时无法读取，请重试。')
  const value: unknown = await response.json()
  if (!valid(value)) throw new Error('泊位响应格式异常，请重新读取。')
  return value as T
}
export const fetchBerthSchedule = (from: string, to: string, signal: AbortSignal) => read<BerthSchedule>(
  `/api/berths/schedule?${new URLSearchParams({from,to})}`, signal,
  v => object(v) && time(v.observedAt) && time(v.from) && time(v.to) && Array.isArray(v.resources)
    && v.resources.every(r => object(r) && id(r.berthId) && ['code','name','terminalName','statusCode','terminalStatusCode','portStatusCode'].every(k => typeof r[k] === 'string') && typeof r.lengthM === 'number' && typeof r.maxDraftM === 'number' && Array.isArray(r.shipTypes))
    && Array.isArray(v.revisions) && v.revisions.every(revision) && Array.isArray(v.occupancies)
    && v.occupancies.every(o => object(o) && ['appointmentId','berthId','executedRevisionId'].every(k => id(o[k])) && time(o.actualBerthingAt) && nullableTime(o.actualUnberthingAt)))
export const fetchBerthVisits = (scope: 'waiting' | 'all', keyword: string, page: number, signal: AbortSignal) => read<BerthVisitPage>(
  `/api/berths/visits?${new URLSearchParams({scope,keyword,page:String(page),pageSize:'8'})}`, signal,
  v => object(v) && v.page === page && v.pageSize === 8 && Number.isInteger(v.totalCount) && Array.isArray(v.items) && v.items.every(visit))
export const fetchBerthVisit = (appointmentId: string, signal: AbortSignal) => read<BerthVisitDetail>(
  `/api/berths/visits/${encodeURIComponent(appointmentId)}`, signal,
  v => visit(v) && object(v) && v.appointmentId === appointmentId && ['submittedAt','reviewedAt','enteredAt','leftAt'].every(k => nullableTime(v[k]))
    && Array.isArray(v.versions) && v.versions.every(revision) && Array.isArray(v.events)
    && v.events.every(e => object(e) && id(e.eventId) && time(e.eventTime) && typeof e.eventTypeCode === 'string'))
