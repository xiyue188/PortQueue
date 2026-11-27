// 明确校验锚地HTTP响应，失败不退回原型或缓存样例。
import type { AnchorageCandidates, AnchorageDetail, AnchorageList, AnchorageQueue, AnchorageStay, AnchorageWriteResult } from '../types/anchorage'
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
  const response = await fetch(url, { signal: AbortSignal.any([signal, AbortSignal.timeout(15_000)]), headers: { Accept: 'application/json' } })
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

export const fetchAnchorageCandidates = (keyword: string, page: number, signal: AbortSignal) => read<AnchorageCandidates>(
  `/api/anchorages/entry-candidates?${new URLSearchParams({ keyword, page: String(page), pageSize: '8' })}`, signal,
  v => object(v) && v.page === page && v.pageSize === 8 && Number.isInteger(v.totalCount) && Array.isArray(v.items)
    && v.items.every(c => object(c) && id(c.appointmentId) && typeof c.shipName === 'string' && typeof c.callNo === 'string' && time(c.requestedArrivalAt)))

/** 不重试写入；连接中断或未知响应可能发生在事务提交之后。 */
export class AnchorageWriteError extends Error {
  constructor(message: string, readonly status: number, readonly uncertain: boolean, readonly code = '') { super(message) }
}
export async function writeAnchorage(path: string, method: 'POST' | 'PUT', body: object, csrfToken: string): Promise<AnchorageWriteResult> {
  const controller = new AbortController(), timer = setTimeout(() => controller.abort(), 25_000)
  try {
    const response = await fetch(path, { method, signal: controller.signal, credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken }, body: JSON.stringify(body) })
    const value: unknown = await response.json().catch(() => null)
    if (!response.ok) {
      const message = response.status === 401 ? '登录已失效，请重新登录后继续。' : response.status === 403 ? '当前账号没有办理权限。'
        : object(value) && object(value.errors) ? Object.values(value.errors).flat().join(' ')
        : object(value) && typeof value.title === 'string' ? value.title : '暂时无法完成保存，请读取最新记录核对。'
      throw new AnchorageWriteError(message, response.status, response.status >= 500, object(value) && typeof value.code === 'string' ? value.code : '')
    }
    if (!object(value) || !id(value.queueEntryId)) throw new AnchorageWriteError('尚未确认保存结果，请先核对。', 0, true)
    return value as unknown as AnchorageWriteResult
  } catch (error) {
    if (error instanceof AnchorageWriteError) throw error
    throw new AnchorageWriteError('连接中断，尚未确认保存结果。请先核对原操作，勿重复提交。', 0, true)
  } finally { clearTimeout(timer) }
}
