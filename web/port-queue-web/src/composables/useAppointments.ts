// 预约页面的请求状态机：表单草稿由页面持有，这里仅保存已提交的查询条件。
import { computed, onScopeDispose, ref } from 'vue'
import { fetchAppointments } from '../api/appointments'
import type { AppointmentItem, AppointmentQuery, AppointmentStatus } from '../types/appointment'

export function useAppointments() {
  const query = ref<AppointmentQuery>({ page: 1, pageSize: 20, status: '', keyword: '' })
  const rows = ref<AppointmentItem[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref('')
  const lastUpdated = ref<Date | null>(null)
  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / query.value.pageSize)))
  let controller: AbortController | undefined
  let requestId = 0

  async function load(next: AppointmentQuery = { ...query.value }) {
    controller?.abort()
    const current = ++requestId
    const requestController = new AbortController()
    controller = requestController
    query.value = { ...next }
    loading.value = true
    error.value = ''
    rows.value = []
    totalCount.value = 0
    lastUpdated.value = null
    let timedOut = false
    const timeout = window.setTimeout(() => { timedOut = true; requestController.abort() }, 15000)
    try {
      const page = await fetchAppointments(next, requestController.signal)
      if (current !== requestId) return
      rows.value = page.items
      totalCount.value = page.totalCount
      lastUpdated.value = new Date()
    } catch (failure: unknown) {
      if (current !== requestId) return
      error.value = timedOut ? '查询超时，请检查连接后重试。'
        : failure instanceof TypeError ? '无法连接查询服务，请检查连接后重试。'
          : failure instanceof Error ? failure.message : '查询失败，请稍后重试。'
    } finally {
      window.clearTimeout(timeout)
      if (current === requestId) loading.value = false
    }
  }

  function search(keyword: string, status: AppointmentStatus | '') {
    return load({ ...query.value, keyword: keyword.trim(), status, page: 1 })
  }

  onScopeDispose(() => { requestId++; controller?.abort() })
  return { query, rows, totalCount, totalPages, loading, error, lastUpdated, load, search }
}
