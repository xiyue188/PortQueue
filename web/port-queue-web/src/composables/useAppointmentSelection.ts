// 独立详情选择：不改清单条件；外部事件精确读取，候选/表格已有记录直接选择。
import { onScopeDispose, ref } from 'vue'
import { fetchAppointment } from '../api/appointments'
import type { AppointmentItem } from '../types/appointment'

export function useAppointmentSelection() {
  const item = ref<AppointmentItem | null>(null)
  const loading = ref(false)
  const error = ref('')
  const requestedId = ref<string>()
  let controller: AbortController | undefined
  let sequence = 0
  function clear() {
    sequence++; controller?.abort(); item.value = null
    loading.value = false; error.value = ''; requestedId.value = undefined
  }
  function select(record: AppointmentItem) { clear(); item.value = record }
  async function open(id: string) {
    clear()
    requestedId.value = id
    loading.value = true
    const current = sequence
    const request = new AbortController()
    controller = request
    const timeout = setTimeout(() => request.abort(), 8_000)
    try {
      const result = await fetchAppointment(id, request.signal)
      if (sequence === current) item.value = result
    } catch (failure) {
      if (sequence === current) error.value = failure instanceof Error && failure.name !== 'AbortError'
        ? failure.message : '预约详情读取超时，请重试。'
    } finally {
      clearTimeout(timeout)
      if (sequence === current) loading.value = false
    }
  }
  function retry() { if (requestedId.value) void open(requestedId.value) }
  onScopeDispose(clear)
  return { item, loading, error, clear, select, open, retry }
}
