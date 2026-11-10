// 总览独立读取全港工作量，预约筛选不会改变这些指标；失败不保留过期数字。
import { onMounted, onScopeDispose, ref, watch, type Ref } from 'vue'
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
  port: { name: string; code: string; countryCode: string; timezone: string; terminals: number; anchorages: number; berths: number } | null
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
  const port = item.port as PortOverview['port']
  if (port !== null && (!port || !['name', 'code', 'countryCode', 'timezone'].every(key => typeof port[key as keyof typeof port] === 'string') || !['terminals', 'anchorages', 'berths'].every(key => Number.isSafeInteger(port[key as keyof typeof port]) && Number(port[key as keyof typeof port]) >= 0))) return false
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
export function useOverview(active: Readonly<Ref<boolean>>) {
  const overview = ref<PortOverview | null>(null)
  const overviewError = ref('')
  const overviewLoading = ref(false)
  let controller: AbortController | undefined
  let sequence = 0
  const interval = 30_000
  let lastSettledAt = 0
  let pollTimer: number | undefined
  // 动态页码属于独立查询状态；响应失败时 overview 会暂时清空，不能因此退回第一页。
  let requestedEventsPage = 1
  // 所有查询完成后再计时，分页读取也会更新指标；不与正在进行的请求竞争。
  function schedule() {
    window.clearTimeout(pollTimer)
    if (!active.value || document.hidden || overviewLoading.value) return
    const delay = lastSettledAt ? Math.max(0, interval - (Date.now() - lastSettledAt)) : interval
    pollTimer = window.setTimeout(() => { void refreshOverview(requestedEventsPage) }, delay)
  }
  async function refreshOverview(eventsPage = requestedEventsPage) {
    requestedEventsPage = Math.max(1, eventsPage)
    window.clearTimeout(pollTimer)
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
      if (current === sequence) {
        overview.value = data
        requestedEventsPage = data.eventsPage
      }
    } catch {
      if (current === sequence) {
        overview.value = null
        overviewError.value = '总览暂时无法读取，请重试。'
      }
    } finally {
      window.clearTimeout(timer)
      if (current === sequence) {
        overviewLoading.value = false
        lastSettledAt = Date.now()
        schedule()
      }
    }
  }
  watch(active, schedule)
  onMounted(() => document.addEventListener('visibilitychange', schedule))
  onScopeDispose(() => {
    sequence++; controller?.abort(); window.clearTimeout(pollTimer)
    document.removeEventListener('visibilitychange', schedule)
  })
  return { overview, overviewError, overviewLoading, refreshOverview }
}
