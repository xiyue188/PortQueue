// 排期、清单和所选到访独立缓存；选择不改变资源范围，过期或身份变化才重读。
import { onScopeDispose, ref } from 'vue'
import { fetchBerthSchedule, fetchBerthVisit, fetchBerthVisits } from '../api/berths'
import type { BerthSchedule, BerthVisitDetail, BerthVisitPage } from '../types/berth'
export function useBerths() {
  const schedule = ref<BerthSchedule | null>(null), visits = ref<BerthVisitPage | null>(null), detail = ref<BerthVisitDetail | null>(null)
  const selectedId = ref(''), date = ref(new Date(Date.now() + 8 * 3600_000).toISOString().slice(0,10)), hours = ref(24)
  const scope = ref<'waiting' | 'all'>('waiting'), keyword = ref(''), page = ref(1)
  const loading = ref(false), visitsLoading = ref(false), detailLoading = ref(false)
  const error = ref(''), visitsError = ref(''), detailError = ref('')
  let chartRequest: AbortController | undefined, listRequest: AbortController | undefined, detailRequest: AbortController | undefined
  let chartRead = 0, listRead = 0, detailRead = 0
  let chartKey = '', listKey = ''
  const scheduleKey = () => `${date.value}|${hours.value}`
  const visitsKey = () => `${scope.value}|${keyword.value}|${page.value}`
  async function loadSchedule() {
    chartRequest?.abort(); const request = new AbortController(); chartRequest = request; loading.value = true; error.value = ''
    const key = scheduleKey()
    try {
      const start = new Date(`${date.value}T00:00:00+08:00`)
      const result = await fetchBerthSchedule(start.toISOString(), new Date(start.getTime() + hours.value * 3600_000).toISOString(), request.signal)
      if (chartRequest === request) { schedule.value = result; chartRead = Date.now(); chartKey = key }
    } catch (failure) { if (!request.signal.aborted) { schedule.value = null; error.value = failure instanceof Error ? failure.message : '排期读取失败。' } }
    finally { if (chartRequest === request) loading.value = false }
  }
  async function loadVisits() {
    listRequest?.abort(); const request = new AbortController(); listRequest = request; visitsLoading.value = true; visitsError.value = ''
    const key = visitsKey()
    try {
      const result = await fetchBerthVisits(scope.value, keyword.value, page.value, request.signal)
      if (listRequest === request) { visits.value = result; listRead = Date.now(); listKey = key }
    } catch (failure) { if (!request.signal.aborted) { visits.value = null; visitsError.value = failure instanceof Error ? failure.message : '到访读取失败。' } }
    finally { if (listRequest === request) visitsLoading.value = false }
  }
  async function select(id: string) {
    detailRequest?.abort(); const request = new AbortController(); detailRequest = request
    if (id !== selectedId.value) detail.value = null
    selectedId.value = id; detailLoading.value = true; detailError.value = ''
    try { const result = await fetchBerthVisit(id,request.signal); if (detailRequest === request) { detail.value = result; detailRead = Date.now() } }
    catch (failure) { if (!request.signal.aborted) { detail.value = null; detailError.value = failure instanceof Error ? failure.message : '到访详情读取失败。' } }
    finally { if (detailRequest === request) detailLoading.value = false }
  }
  async function refresh(force = true) {
    const tasks: Promise<void>[] = []
    if (force || !schedule.value || chartKey !== scheduleKey() || Date.now() - chartRead > 30_000) tasks.push(loadSchedule())
    if (force || !visits.value || listKey !== visitsKey() || Date.now() - listRead > 30_000) tasks.push(loadVisits())
    if (selectedId.value && (force || !detail.value || Date.now() - detailRead > 30_000)) tasks.push(select(selectedId.value))
    await Promise.all(tasks)
    if (!selectedId.value && !chartRequest?.signal.aborted) {
      const first = schedule.value?.revisions[0]?.appointmentId ?? visits.value?.items[0]?.appointmentId
      if (first) await select(first)
    }
  }
  function suspend() { chartRequest?.abort(); listRequest?.abort(); detailRequest?.abort(); loading.value = false; visitsLoading.value = false; detailLoading.value = false }
  function invalidate() { suspend(); chartRequest = listRequest = detailRequest = undefined; chartRead = listRead = detailRead = 0 }
  function clear() { suspend(); chartRequest = listRequest = detailRequest = undefined; schedule.value = null; visits.value = null; detail.value = null; selectedId.value = ''; error.value = visitsError.value = detailError.value = ''; chartRead = listRead = detailRead = 0; chartKey = listKey = '' }
  onScopeDispose(clear)
  return { schedule, visits, detail, selectedId, date, hours, scope, keyword, page, loading, visitsLoading, detailLoading, error, visitsError, detailError, loadSchedule, loadVisits, select, refresh, suspend, clear, invalidate }
}
