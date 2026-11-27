<script setup lang="ts">
// 受控办理窗口：保留原操作目标，区分写入结果和读回结果，不因网络中断重复提交。
import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import type { SessionClient } from '../composables/useSession'
import type { AnchorageCandidate, AnchorageCandidates, AnchorageDetail, AnchorageSummary } from '../types/anchorage'
import { AnchorageWriteError, fetchAnchorageCandidates, fetchAnchorages, fetchAnchorageStay, writeAnchorage } from '../api/anchorages'
import { formatDate } from '../utils/appointmentPresentation'
import PortIcon from './PortIcon.vue'
const props = defineProps<{ accountSession: SessionClient; active: boolean }>()
const emit = defineEmits<{ saved: [id: string, message: string]; refresh: [] }>()
type Mode = 'enter' | 'leave' | 'estimate'
const dialog = ref<HTMLDialogElement>(), form = ref<HTMLFormElement>()
const mode = ref<Mode>('enter'), target = ref<AnchorageDetail>(), area = ref<AnchorageSummary>()
const candidates = ref<AnchorageCandidates>(), chosen = ref<AnchorageCandidate>(), keyword = ref(''), page = ref(1)
const candidatesLoading = ref(false), candidateError = ref(''), busy = ref(false), uncertain = ref(false), conflict = ref(false)
const opened = ref(false), composing = ref(false)
const value = ref(''), reason = ref(''), error = ref(''), checked = ref(''), checkedDone = ref(false)
const title = computed(() => ({ enter: '登记入锚', leave: '登记离锚', estimate: '预计靠泊' }[mode.value]))
const identity = computed(() => { const user = props.accountSession.session.value?.user; return user ? `${user.userId}:${user.roleCode}` : '' })
const role = computed(() => props.accountSession.session.value?.user?.roleCode ?? '')
let searchRequest: AbortController | undefined, searchTimer: ReturnType<typeof setTimeout> | undefined
let returnFocus: HTMLElement | null = null, generation = 0
let pending: { mode: Mode; areaId?: string; stayId?: string; appointmentId: string; time: string | null; version?: string } | undefined
const local = (time: string | null) => time ? new Date(Date.parse(time) + 8 * 3600_000).toISOString().slice(0,19) : ''
const iso = (time: string) => `${time.length === 16 ? time + ':00' : time}+08:00`
function permitted(kind: Mode) { return (kind === 'estimate' ? ['ADMIN','DISPATCHER'] : ['ADMIN','DISPATCHER','RECORDER']).includes(role.value) }
async function loadCandidates(number = 1) {
  searchRequest?.abort(); const controller = new AbortController(); searchRequest = controller
  page.value = number; candidatesLoading.value = true; candidateError.value = ''; candidates.value = undefined
  try { const result = await fetchAnchorageCandidates(keyword.value.trim(),number,controller.signal); if (searchRequest === controller) candidates.value = result }
  catch (failure) { if (!controller.signal.aborted) candidateError.value = failure instanceof Error ? failure.message : '候选读取失败。' }
  finally { if (searchRequest === controller) candidatesLoading.value = false }
}
function searchCandidates(event: Event) {
  if (composing.value || (event as InputEvent).isComposing) return
  searchRequest?.abort(); searchRequest = undefined; candidates.value = undefined; candidatesLoading.value = true
  clearTimeout(searchTimer); searchTimer = setTimeout(() => void loadCandidates(),250)
}
function searchEnter(event: KeyboardEvent) { if (!event.isComposing && !composing.value) { clearTimeout(searchTimer); void loadCandidates() } }
function startComposition() { composing.value = true; clearTimeout(searchTimer); searchRequest?.abort() }
function resume() { opened.value = true; dialog.value?.showModal() }
async function open(kind: Mode, selectedArea: AnchorageSummary, stay?: AnchorageDetail) {
  if (!permitted(kind) || busy.value) return
  if (uncertain.value && pending) { resume(); return }
  mode.value = kind; area.value = selectedArea; target.value = stay; chosen.value = undefined
  error.value = ''; reason.value = ''; checked.value = ''; uncertain.value = false; conflict.value = false; checkedDone.value = false; pending = undefined
  value.value = local(kind === 'estimate' ? stay?.manualEstimatedBerthingAt ?? null : new Date().toISOString())
  keyword.value = ''; returnFocus = document.activeElement as HTMLElement; resume()
  if (kind === 'enter') await loadCandidates()
  await nextTick(); dialog.value?.querySelector<HTMLInputElement>('input')?.focus()
}
function close(force = false) {
  if (!force && busy.value) return
  if (!force && uncertain.value && checkedDone.value) { uncertain.value = false; pending = undefined }
  opened.value = false; dialog.value?.close(); searchRequest?.abort(); clearTimeout(searchTimer)
  returnFocus?.focus({preventScroll:true})
}
async function submit() {
  if (busy.value || uncertain.value || conflict.value || !permitted(mode.value) || !form.value?.reportValidity()) return
  if (mode.value === 'enter' && !chosen.value) { error.value = '请选择本次入锚的已批准预约。'; return }
  const time = value.value ? iso(value.value) : null
  if (time && !Number.isFinite(Date.parse(time))) { error.value = '请输入有效时间。'; return }
  if (mode.value !== 'estimate' && (!time || Date.parse(time) > Date.now())) { error.value = '实际时间不能晚于现在。'; return }
  if (mode.value === 'leave' && target.value?.enteredAt && Date.parse(time!) < Date.parse(target.value.enteredAt)) { error.value = '离锚时间不能早于本次入锚。'; return }
  if (mode.value === 'estimate' && !reason.value.trim()) { error.value = '请填写调整原因。'; return }
  pending = { mode: mode.value, areaId: area.value?.anchorageId, stayId: target.value?.queueEntryId,
    appointmentId: chosen.value?.appointmentId ?? target.value!.appointmentId, time, version: target.value?.estimateVersion }
  const path = mode.value === 'enter' ? `/api/anchorages/${area.value!.anchorageId}/entries`
    : `/api/anchorage-stays/${target.value!.queueEntryId}/${mode.value === 'leave' ? 'leave' : 'estimate'}`
  const body = mode.value === 'estimate' ? { manualEstimatedBerthingAt: time, expectedVersion: target.value!.estimateVersion, reason: reason.value.trim() }
    : { ...(mode.value === 'enter' ? {appointmentId: chosen.value!.appointmentId} : {}), eventTime: time, remark: reason.value.trim() || null }
  busy.value = true; error.value = ''; const current = generation
  try {
    const result = await writeAnchorage(path, mode.value === 'estimate' ? 'PUT' : 'POST', body, props.accountSession.session.value?.csrfToken ?? '')
    if (current !== generation) return
    close(true); emit('saved', result.queueEntryId, mode.value === 'enter' ? '已登记入锚。' : mode.value === 'leave' ? '已登记离锚。' : '已更新预计靠泊。')
  } catch (failure) {
    if (current !== generation) return
    error.value = failure instanceof Error ? failure.message : '保存结果未确认。'
    uncertain.value = !(failure instanceof AnchorageWriteError) || failure.uncertain
    conflict.value = failure instanceof AnchorageWriteError && failure.status === 409
    if (failure instanceof AnchorageWriteError && ([401,403].includes(failure.status) || failure.code === 'INVALID_CSRF_TOKEN')) await props.accountSession.refresh()
  } finally { if (current === generation) busy.value = false }
}
async function reconcile() {
  if (!pending || busy.value) return
  busy.value = true; checked.value = ''; const current = generation
  try {
    const controller = new AbortController()
    let id = pending.stayId
    if (!id) {
      const list = await fetchAnchorages(controller.signal,pending.appointmentId)
      id = list.locatedStay?.queueEntryId
    }
    const fact = id ? await fetchAnchorageStay(id,controller.signal) : null
    if (current !== generation) return
    const matched = pending.mode === 'estimate'
      ? fact && BigInt(fact.estimateVersion) > BigInt(pending.version!) && (fact.manualEstimatedBerthingAt === null) === (pending.time === null)
        && (pending.time === null || Date.parse(fact.manualEstimatedBerthingAt!) === Date.parse(pending.time))
      : fact && fact.anchorageId === pending.areaId && fact.events.some(e => e.queueEntryId === fact.queueEntryId && e.eventTypeCode === (pending!.mode === 'enter' ? 'ANCHORAGE_ENTER' : 'ANCHORAGE_LEAVE') && Date.parse(e.eventTime) === Date.parse(pending!.time!))
    checked.value = matched ? '已读到与原操作一致的记录。' : '未读到与原操作完全一致的记录，请核对当前停留后再办理。'
    if (fact) checked.value += ` 当前入锚：${formatDate(fact.enteredAt)}；离锚：${formatDate(fact.leftAt)}；预计：${formatDate(fact.manualEstimatedBerthingAt)}。`
    checkedDone.value = true; emit('refresh')
    // 409是确定拒绝；读取新版本后保留原输入与原因供用户再次确认。未知结果只核对，不提供原请求重发。
    if (conflict.value && fact) { target.value = fact; conflict.value = false; error.value = '已读取最新记录，请核对后重新确认。' }
  } catch { if (current === generation) checked.value = '最新记录仍无法读取，请稍后再核对。' }
  finally { if (current === generation) busy.value = false }
}
function resetOnContext() { generation++; busy.value = false; uncertain.value = false; close(true); pending = undefined; chosen.value = undefined; candidates.value = undefined }
watch(identity,resetOnContext)
watch(() => props.active, active => { if (!active) close(true) })
onScopeDispose(resetOnContext)
defineExpose({ open })
</script>

<template>
  <div v-if="uncertain && !opened && active" class="aq-pending-check" role="status"><span>{{ target?.shipName ?? chosen?.shipName }}的{{ title }}结果待核对</span><button class="button" @click="resume">核对原操作</button></div>
  <dialog ref="dialog" class="aq-dialog aq-operation" aria-labelledby="aq-operation-title" @cancel.prevent="close()">
    <header><h2 id="aq-operation-title">{{ title }}</h2><button class="icon-button" :disabled="busy" aria-label="关闭办理窗口" @click="close()"><PortIcon name="close"/></button></header>
    <p class="aq-operation-context"><strong>{{ target?.shipName ?? chosen?.shipName ?? area?.name }}</strong><span v-if="target || chosen">{{ area?.name }} · {{ target?.callNo ?? chosen?.callNo }}</span></p>
    <form ref="form" @submit.prevent="submit">
      <fieldset :disabled="busy || uncertain || conflict">
        <div v-if="mode === 'enter'" class="aq-candidates">
          <label for="aq-candidate-search">已批准预约</label><input id="aq-candidate-search" v-model="keyword" type="search" maxlength="100" placeholder="搜索船名、预约或到访编号" @input="searchCandidates" @compositionstart="startComposition" @compositionend="composing = false; searchCandidates($event)" @keydown.enter.prevent="searchEnter"/>
          <div class="aq-candidate-list" :aria-busy="candidatesLoading">
            <p v-if="candidateError" role="alert">{{ candidateError }} <button type="button" class="aq-link" @click="loadCandidates()">重新读取</button></p>
            <p v-else-if="candidatesLoading">正在读取…</p>
            <p v-else-if="!candidates?.items.length">没有符合条件的已批准预约。</p>
            <label v-for="item in candidates?.items" :key="item.appointmentId" class="aq-candidate"><input v-model="chosen" type="radio" name="entry-candidate" :value="item"/><span><strong>{{ item.shipName }}</strong><small>{{ item.callNo }}</small></span><time>{{ formatDate(item.requestedArrivalAt) }}</time></label>
          </div>
          <nav v-if="candidates" class="aq-candidate-pages" aria-label="入锚候选分页"><span>共 {{ candidates.totalCount }} 条</span><button type="button" class="aq-link" :disabled="page <= 1 || candidatesLoading" @click="loadCandidates(page-1)">上一页</button><span>{{ page }}</span><button type="button" class="aq-link" :disabled="page*8 >= candidates.totalCount || candidatesLoading" @click="loadCandidates(page+1)">下一页</button></nav>
          <p v-if="chosen" class="aq-chosen">已选 {{ chosen.shipName }} · {{ chosen.callNo }}</p>
        </div>
        <label for="aq-operation-time">{{ mode === 'estimate' ? '人工预计靠泊' : mode === 'enter' ? '实际入锚时间' : '实际离锚时间' }}（北京时间）</label>
        <div class="aq-time-input"><input id="aq-operation-time" v-model="value" type="datetime-local" step="1" :required="mode !== 'estimate'"/><button v-if="mode === 'estimate' && value" type="button" class="aq-link" @click="value = ''">清除预计</button></div>
        <label for="aq-operation-reason">{{ mode === 'estimate' ? '调整原因' : '备注（选填）' }}</label><textarea id="aq-operation-reason" v-model="reason" rows="2" maxlength="500" :required="mode === 'estimate'"></textarea>
      </fieldset>
      <p v-if="error" class="aq-form-error" role="alert">{{ error }}</p>
      <p v-if="(uncertain || conflict) && pending" class="aq-check-original">原操作：{{ title }} · {{ pending.time ? formatDate(pending.time) : '清除预计' }}</p>
      <p v-if="checked" class="aq-check-result" role="status">{{ checked }}</p>
      <footer><button type="button" class="button" :disabled="busy" @click="close()">{{ uncertain ? (checkedDone ? '结束核对' : '稍后核对') : '取消' }}</button><button v-if="uncertain || conflict" type="button" class="button button-primary" :disabled="busy" @click="reconcile">{{ busy ? '正在核对…' : '读取最新记录' }}</button><button v-else type="submit" class="button button-primary" :disabled="busy">{{ busy ? '正在保存…' : '确认保存' }}</button></footer>
    </form>
  </dialog>
</template>
