// 预约页面的请求状态机：表单草稿由页面持有，这里仅保存已提交的查询条件。
import { computed, onScopeDispose, ref } from 'vue'
import { fetchAppointments } from '../api/appointments'
import type { AppointmentItem, AppointmentQuery, AppointmentStatus } from '../types/appointment'

export function useAppointments(pageSize = 20) {
  const query = ref<AppointmentQuery>({ page: 1, pageSize, status: '', keyword: '' })
  const pendingQuery = ref<AppointmentQuery | null>(null)
  const rows = ref<AppointmentItem[]>([])
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref('')
  const lastUpdated = ref<Date | null>(null)
  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / query.value.pageSize)))
  let controller: AbortController | undefined
  let requestId = 0

  async function load(next: AppointmentQuery = { ...(pendingQuery.value ?? query.value) }) {
    controller?.abort()
    const current = ++requestId
    const requestController = new AbortController()
    controller = requestController
    // 提交条件和已展示条件分开：慢请求期间旧数据仍对应旧页码，不冒充新结果。
    pendingQuery.value = { ...next }
    loading.value = true
    error.value = ''
    let timedOut = false
    const timeout = window.setTimeout(() => { timedOut = true; requestController.abort() }, 15000)
    try {
      const page = await fetchAppointments(next, requestController.signal)
      if (current !== requestId) return
      query.value = { ...next }
      pendingQuery.value = null
      rows.value = page.items
      totalCount.value = page.totalCount
      lastUpdated.value = new Date()
    } catch (failure: unknown) {
      if (current !== requestId) return
      rows.value = []
      totalCount.value = 0
      lastUpdated.value = null
      error.value = timedOut ? '查询超时，请检查连接后重试。'
        : failure instanceof TypeError ? '无法连接查询服务，请检查连接后重试。'
          : failure instanceof Error ? failure.message : '查询失败，请稍后重试。'
    } finally {
      window.clearTimeout(timeout)
      if (current === requestId) loading.value = false
    }
  }

  function search(keyword: string, status: AppointmentStatus | '') {
    return load({ ...(pendingQuery.value ?? query.value), keyword: keyword.trim(), status, page: 1 })
  }

  onScopeDispose(() => { requestId++; controller?.abort() })
  return { query, pendingQuery, rows, totalCount, totalPages, loading, error, lastUpdated, load, search }
}
