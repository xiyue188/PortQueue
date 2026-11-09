// 总览独立读取全港工作量，预约筛选不会改变这些指标；失败不保留过期数字。
import { onScopeDispose, ref } from 'vue'
import { appointmentStatuses, type AppointmentStatus } from '../types/appointment'

export interface RecentPortEvent {
  eventId: string
  eventTypeCode: string
  eventTime: string
  shipName: string
  appointmentId: string
  appointmentNo: string
}
export interface PortOverview {
  pendingAppointments: number
  anchorageVessels: number
  confirmedPlans: number
  waitingQueue: number
  assignedQueue: number
  draftPlans: number
  executingPlans: number
  portEntries: number
  berthings: number
  unberthings: number
  appointmentStates: { code: AppointmentStatus; count: number }[]
  totalEvents: number
  eventsPage: number
  eventsPageSize: number
  recentEvents: RecentPortEvent[]
}
export const eventLabels: Record<string, string> = {
  ANCHORAGE_ENTER: '进入锚地', ANCHORAGE_LEAVE: '离开锚地',
  PORT_ENTER: '实际进港', BERTHING: '靠泊', UNBERTHING: '离泊',
}
function validOverview(value: unknown): value is PortOverview {
  if (!value || typeof value !== 'object') return false
  const item = value as Record<string, unknown>
  return ['pendingAppointments', 'anchorageVessels', 'confirmedPlans', 'totalEvents', 'waitingQueue', 'assignedQueue', 'draftPlans', 'executingPlans', 'portEntries', 'berthings', 'unberthings'].every(key => Number.isSafeInteger(item[key]) && Number(item[key]) >= 0)
    && Array.isArray(item.appointmentStates) && item.appointmentStates.length === 6
    && new Set(item.appointmentStates.map(state => state?.code)).size === 6
    && item.appointmentStates.every(state => state && appointmentStatuses.some(status => status.code === state.code) && Number.isSafeInteger(state.count) && state.count >= 0)
    && Number.isSafeInteger(item.eventsPage) && Number(item.eventsPage) >= 1 && item.eventsPageSize === 12
    && Array.isArray(item.recentEvents) && item.recentEvents.length <= 12 && item.recentEvents.every(event =>
      event && typeof event.eventId === 'string' && /^\d+$/.test(event.eventId)
      && typeof event.appointmentId === 'string' && /^\d+$/.test(event.appointmentId)
      && typeof event.shipName === 'string' && typeof event.appointmentNo === 'string'
      && typeof event.eventTypeCode === 'string' && Object.hasOwn(eventLabels, event.eventTypeCode)
      && typeof event.eventTime === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/.test(event.eventTime) && Number.isFinite(Date.parse(event.eventTime)))
}
export function useOverview() {
  const overview = ref<PortOverview | null>(null)
  const overviewError = ref('')
  const overviewLoading = ref(false)
  let controller: AbortController | undefined
  let sequence = 0
  async function refreshOverview(eventsPage = 1) {
    controller?.abort()
    const request = new AbortController()
    controller = request
    const current = ++sequence
    overviewError.value = ''; overviewLoading.value = true
    const timer = window.setTimeout(() => request.abort(), 15_000)
    try {
      const response = await fetch(`/api/overview?eventsPage=${eventsPage}`, { signal: request.signal, headers: { Accept: 'application/json' } })
      if (!response.ok) throw new Error('总览暂时无法读取，请重试。')
      const data: unknown = await response.json()
      if (!validOverview(data)) throw new Error('总览响应格式不正确，请重试。')
      if (current === sequence) overview.value = data
    } catch {
      if (current === sequence) {
        overview.value = null
        overviewError.value = '总览暂时无法读取，请重试。'
      }
    } finally {
      window.clearTimeout(timer)
      if (current === sequence) overviewLoading.value = false
    }
  }
  onScopeDispose(() => { sequence++; controller?.abort() })
  return { overview, overviewError, overviewLoading, refreshOverview }
}
