// 明确校验锚地HTTP响应，失败不退回原型或缓存样例。
import type { AnchorageDetail, AnchorageList, AnchorageQueue, AnchorageStay } from '../types/anchorage'
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object'
const id = (v: unknown) => typeof v === 'string' && /^[1-9]\d*$/.test(v)
const time = (v: unknown) => typeof v === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/.test(v) && Number.isFinite(Date.parse(v))
const nullableTime = (v: unknown) => v === null || time(v)
function stay(v: unknown): v is AnchorageStay {
  return object(v) && ['queueEntryId','anchorageId','appointmentId','portCallId','shipId'].every(k => id(v[k]))
    && ['callNo','appointmentNo','shipName','statusCode','estimateVersion'].every(k => typeof v[k] === 'string')
    && ['enteredAt','leftAt','legacyEstimatedBerthingAt','manualEstimatedBerthingAt','confirmedBerthingAt','confirmedUnberthingAt'].every(k => nullableTime(v[k]))
    && (v.queuePosition === null || (Number.isInteger(v.queuePosition) && Number(v.queuePosition) > 0))
}
async function read<T>(url: string, signal: AbortSignal, valid: (value: unknown) => boolean): Promise<T> {
  const response = await fetch(url, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(response.status === 401 ? '请登录后查看锚地业务。' : response.status === 404 ? '该锚地或停留记录不存在。' : '锚地数据暂时无法读取，请重试。')
  const value: unknown = await response.json()
  if (!valid(value)) throw new Error('锚地响应格式异常，请刷新后重试。')
  return value as T
}
export const fetchAnchorages = (signal: AbortSignal, appointmentId?: string) => read<AnchorageList>(
  `/api/anchorages${appointmentId ? `?appointmentId=${encodeURIComponent(appointmentId)}` : ''}`, signal,
  v => object(v) && time(v.observedAt) && typeof v.portName === 'string' && Array.isArray(v.items)
    && v.items.every(a => object(a) && id(a.anchorageId) && typeof a.name === 'string' && Number.isInteger(a.occupiedCount) && Number.isInteger(a.queueCount)))
export const fetchAnchorageQueue = (anchorageId: string, signal: AbortSignal) => read<AnchorageQueue>(`/api/anchorages/${encodeURIComponent(anchorageId)}/queue`, signal,
  v => object(v) && v.anchorageId === anchorageId && time(v.observedAt) && typeof v.snapshot === 'string'
    && Number.isInteger(v.occupiedCount) && (v.capacity === null || Number.isInteger(v.capacity)) && typeof v.statusCode === 'string'
    && Array.isArray(v.items) && v.items.every(stay) && Array.isArray(v.cancelled) && v.cancelled.every(stay))
export const fetchAnchorageStay = (stayId: string, signal: AbortSignal) => read<AnchorageDetail>(`/api/anchorage-stays/${encodeURIComponent(stayId)}`, signal,
  v => stay(v) && v.queueEntryId === stayId && object(v) && Array.isArray(v.events)
    && v.events.every(e => object(e) && id(e.eventId) && time(e.eventTime) && typeof e.eventTypeCode === 'string'))
