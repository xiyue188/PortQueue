// 队列、设施和所选停留分别读取；身份变化清空，晚到响应不能覆盖新选择。
import { onScopeDispose, ref } from 'vue'
import { fetchAnchorages, fetchAnchorageQueue, fetchAnchorageStay } from '../api/anchorages'
import type { AnchorageDetail, AnchorageList, AnchorageQueue } from '../types/anchorage'
export function useAnchorages() {
  const facilities = ref<AnchorageList | null>(null), queue = ref<AnchorageQueue | null>(null), detail = ref<AnchorageDetail | null>(null)
  const areaId = ref(''), selectedId = ref(''), loading = ref(false), detailLoading = ref(false)
  const error = ref(''), detailError = ref('')
  let request: AbortController | undefined, selection: AbortController | undefined
  let lastRead = 0
  async function select(id: string) {
    selection?.abort(); const controller = new AbortController(); selection = controller
    selectedId.value = id; detail.value = null; detailError.value = ''; detailLoading.value = true
    try { const result = await fetchAnchorageStay(id, controller.signal); if (selection === controller) detail.value = result }
    catch (failure) { if (!controller.signal.aborted) detailError.value = failure instanceof Error ? failure.message : '停留详情读取失败。' }
    finally { if (selection === controller) detailLoading.value = false }
  }
  async function load(anchorageId = areaId.value, appointmentId?: string) {
    request?.abort(); selection?.abort(); const controller = new AbortController(); request = controller
    loading.value = true; error.value = ''; detailError.value = ''
    if (anchorageId !== areaId.value || appointmentId) { queue.value = null; detail.value = null; selectedId.value = '' }
    try {
      const list = await fetchAnchorages(controller.signal, appointmentId)
      if (request !== controller) return
      facilities.value = list
      const chosen = list.locatedStay?.anchorageId ?? (list.items.some(a => a.anchorageId === anchorageId) ? anchorageId : list.items[0]?.anchorageId)
      if (chosen !== areaId.value) { selectedId.value = ''; detail.value = null }
      areaId.value = chosen ?? ''
      if (!chosen) { queue.value = null; detail.value = null; return }
      const result = await fetchAnchorageQueue(chosen, controller.signal)
      if (request !== controller) return
      queue.value = result; lastRead = Date.now()
      if (appointmentId && !list.locatedStay) {
        detail.value = null; selectedId.value = ''; detailLoading.value = false
        detailError.value = '这次到访尚无锚地停留记录。'; return
      }
      // 所选历史停留可能已经离锚；刷新不能把它替换成另一次到访。
      const nextId = list.locatedStay?.queueEntryId ?? (selectedId.value || result.items[0]?.queueEntryId || result.cancelled[0]?.queueEntryId)
      if (nextId) await select(nextId)
      else { detail.value = null; selectedId.value = ''; detailLoading.value = false }
    } catch (failure) {
      if (controller.signal.aborted) return
      facilities.value = null; queue.value = null; detail.value = null
      error.value = failure instanceof Error ? failure.message : '锚地数据读取失败。'
    } finally { if (request === controller) loading.value = false }
  }
  function clear() {
    request?.abort(); selection?.abort(); request = undefined; selection = undefined
    facilities.value = null; queue.value = null; detail.value = null; error.value = ''; detailError.value = ''
    areaId.value = ''; selectedId.value = ''; loading.value = false; detailLoading.value = false; lastRead = 0
  }
  function suspend() { request?.abort(); selection?.abort(); loading.value = false; detailLoading.value = false }
  onScopeDispose(clear)
  return { facilities, queue, detail, areaId, selectedId, loading, detailLoading, error, detailError, select, load, clear, suspend,
    stale: () => !queue.value || (!!selectedId.value && !detail.value) || Date.now() - lastRead > 30_000 }
}
