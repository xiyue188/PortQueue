<script setup lang="ts">
// 全量船舶队列与调序确认。拖动和精确输入共用同一快照、请求及失败恢复。
import { computed, nextTick, ref, watch } from 'vue'
import type { AnchorageQueue, AnchorageStay } from '../types/anchorage'
import type { SessionClient } from '../composables/useSession'
import { AnchorageWriteError, fetchAnchorageQueue, writeAnchorage } from '../api/anchorages'
import { moveQueue, useQueueDrag } from '../composables/useQueueDrag'
import PortIcon from './PortIcon.vue'
import QueueShip from './QueueShip.vue'
const props = defineProps<{ queue: AnchorageQueue | null; selectedId: string; query: string; active: boolean; accountSession: SessionClient }>()
const emit = defineEmits<{ select: [id: string]; saved: [id: string, message: string]; refresh: [] }>()
const lane = ref<HTMLElement>(), dialog = ref<HTMLDialogElement>(), form = ref<HTMLFormElement>()
const preview = ref<string[] | null>(null), destination = ref(1), reason = ref(''), error = ref('')
const busy = ref(false), uncertain = ref(false), conflict = ref(false), opened = ref(false), checked = ref(''), checkedDone = ref(false)
const pending = ref<{ id: string; anchorageId: string; original: AnchorageStay[]; snapshot: string }>()
const identity = computed(()=>{ const user = props.accountSession.session.value?.user; return user ? `${user.userId}:${user.roleCode}` : '' })
const allowed = computed(()=>['ADMIN','DISPATCHER'].includes(props.accountSession.session.value?.user?.roleCode ?? ''))
const current = computed(()=>pending.value?.original.find(item=>item.queueEntryId === pending.value?.id))
const originalPosition = computed(()=>current.value?.queuePosition ?? 1)
const proposed = computed(()=>pending.value ? moveQueue(pending.value.original.map(item=>item.queueEntryId),pending.value.id,Number(destination.value)-1) : [])
const affected = computed(()=>pending.value?.original.filter(item=>item.queueEntryId !== pending.value?.id && proposed.value.indexOf(item.queueEntryId)+1 !== item.queuePosition) ?? [])
let returnFocus: HTMLElement | null = null, generation = 0
const drag = useQueueDrag(lane,()=>props.queue?.items ?? [],()=>allowed.value && !busy.value && !uncertain.value,
  preview,id=>emit('select',id),openReorder)
const { draggingId } = drag
function order(item: AnchorageStay, index: number) { return draggingId.value === item.queueEntryId ? index : preview.value?.indexOf(item.queueEntryId) ?? index }
function matches(item: AnchorageStay) { return `${item.shipName} ${item.callNo} ${item.appointmentNo}`.toLowerCase().includes(props.query.trim().toLowerCase()) }
function waiting(item: AnchorageStay) {
  if (!item.enteredAt || !props.queue) return '待核对'
  const minutes = Math.floor((Date.parse(item.leftAt ?? props.queue.observedAt)-Date.parse(item.enteredAt))/60_000)
  return minutes >= 1440 ? `${Math.floor(minutes/1440)}天${Math.floor(minutes%1440/60)}小时` : `${Math.floor(minutes/60)}小时${minutes%60}分`
}
function resume() { updatePreview(); opened.value = true; dialog.value?.showModal() }
function openReorder(id: string, position?: number) {
  if (uncertain.value && pending.value) { resume(); return }
  if (!allowed.value || !props.queue || props.queue.items.length < 2 || busy.value) return
  const target = props.queue.items.find(item=>item.queueEntryId===id); if (!target) return
  pending.value = { id, anchorageId:props.queue.anchorageId, original:[...props.queue.items], snapshot:props.queue.snapshot }
  destination.value = position ?? target.queuePosition!; reason.value = ''; error.value = ''; checked.value = ''; checkedDone.value = false; conflict.value = false
  returnFocus = document.activeElement as HTMLElement; updatePreview(); emit('select',id); resume()
}
function updatePreview() {
  if (!pending.value) return
  preview.value = moveQueue(pending.value.original.map(item=>item.queueEntryId),pending.value.id,Math.min(pending.value.original.length,Math.max(1,Number(destination.value)||1))-1)
}
function close(force = false, keepPreview = false) {
  if (!force && busy.value) return
  opened.value = false; dialog.value?.close()
  if (!keepPreview) preview.value = null
  // 浏览器后退可在写请求未决时离页；只清画布，原请求必须保留供晚到结果核对。
  if (!busy.value && (!uncertain.value || (!force && checkedDone.value))) { pending.value = undefined; uncertain.value = false }
  returnFocus?.focus({preventScroll:true})
}
async function save() {
  if (!pending.value || busy.value || uncertain.value || conflict.value || !allowed.value || !form.value?.reportValidity()) return
  if (!reason.value.trim()) { error.value = '请填写调整原因。'; return }
  if (destination.value === originalPosition.value) { close(); return }
  const action = pending.value, target = destination.value, currentGeneration = generation
  const name = current.value?.shipName, old = originalPosition.value
  busy.value = true; error.value = ''
  try {
    await writeAnchorage(`/api/anchorage-stays/${action.id}/reorder`,'POST',
      {newPosition:target,reason:reason.value.trim(),expectedSnapshot:action.snapshot},props.accountSession.session.value?.csrfToken ?? '')
    if (currentGeneration !== generation) return
    close(true,true); pending.value = undefined; emit('saved',action.id,`${name}已从第${old}位调整至第${target}位。`)
  } catch (failure) {
    if (currentGeneration !== generation) return
    error.value = failure instanceof Error ? failure.message : '调序结果尚未确认，请先核对。'
    uncertain.value = !(failure instanceof AnchorageWriteError) || failure.uncertain
    conflict.value = failure instanceof AnchorageWriteError && failure.status === 409
    if (failure instanceof AnchorageWriteError && ([401,403].includes(failure.status) || failure.code === 'INVALID_CSRF_TOKEN')) await props.accountSession.refresh()
  } finally { if (currentGeneration === generation) busy.value = false }
}
async function reconcile() {
  if (!pending.value || busy.value) return
  const action = pending.value, currentGeneration = generation; busy.value = true; checked.value = ''
  try {
    const latest = await fetchAnchorageQueue(action.anchorageId,new AbortController().signal)
    if (currentGeneration !== generation) return
    const target = latest.items.find(item=>item.queueEntryId===action.id)
    const requested = moveQueue(action.original.map(item=>item.queueEntryId),action.id,destination.value-1)
    const equal = latest.items.length === requested.length && latest.items.every((item,index)=>item.queueEntryId===requested[index])
    checked.value = equal ? '最新队列与原调序预览一致。' : target ? `当前为第${target.queuePosition}位，请核对整条队列。` : '该船已不在有效队列中。'
    checkedDone.value = true; emit('refresh')
    if (conflict.value && target) {
      pending.value = {...action,original:latest.items,snapshot:latest.snapshot}
      destination.value = Math.min(destination.value,latest.items.length); conflict.value = false
      error.value = '已读取最新队列，调整原因已保留，请核对新的顺位变化后确认。'; updatePreview()
    }
  } catch { if (currentGeneration === generation) checked.value = '最新队列暂时无法读取，可稍后继续核对。' }
  finally { if (currentGeneration === generation) busy.value = false }
}
async function locateSelected() {
  if (drag.interacting.value || opened.value) return
  await nextTick(); const element = lane.value?.querySelector<HTMLElement>(`[data-stay="${props.selectedId}"]`)
  if (!element || !lane.value || drag.interacting.value || opened.value) return
  const left = element.offsetLeft, right = left+element.offsetWidth
  if (left < lane.value.scrollLeft || right > lane.value.scrollLeft+lane.value.clientWidth)
    lane.value.scrollLeft = Math.max(0,left-(lane.value.clientWidth-element.offsetWidth)/2)
}
watch(()=>props.queue,()=>{ drag.cancel(); if (!opened.value) preview.value = null })
watch(()=>props.active,active=>{ if (!active) { drag.cancel(); close(true) } })
watch(identity,()=>{ generation++; busy.value = false; uncertain.value = false; drag.cancel(); close(true); pending.value = undefined })
defineExpose({ locateSelected, openReorder, scrollStart:()=>{if(lane.value)lane.value.scrollLeft=0} })
</script>

<template>
  <div ref="lane" class="aq-sea" tabindex="0" aria-label="完整候泊队列，可横向浏览">
    <div v-if="queue?.items.length" class="aq-lane" :style="{'--slots':queue.items.length}">
      <div v-for="(ship,index) in queue.items" :key="ship.queueEntryId" class="aq-vessel" :class="{selected:selectedId===ship.queueEntryId,dim:query&&!matches(ship),'is-dragging':draggingId===ship.queueEntryId}" :data-stay="ship.queueEntryId" :style="{'--slot':order(ship,index)}">
        <button v-if="allowed && queue.items.length>1" class="aq-grab"
          :aria-label="`调整${ship.shipName}顺位`" title="拖动调序，或点击输入位置"
          @pointerdown="drag.begin($event,ship.queueEntryId)" @pointermove="drag.move"
          @pointerup="drag.end($event)" @pointercancel="drag.end($event,true)" @lostpointercapture="drag.end($event,true)"
          @click="drag.click($event,()=>openReorder(ship.queueEntryId))">
          <PortIcon name="grip" :size="18"/><span class="aq-rank">{{ preview ? preview.indexOf(ship.queueEntryId)+1 : ship.queuePosition }}</span>
        </button>
        <span v-else class="aq-rank">{{ ship.queuePosition }}</span>
        <button class="aq-select" :class="{'can-drag':allowed && queue.items.length>1}"
          :aria-label="`${ship.shipName}，第${ship.queuePosition}位`" :aria-pressed="selectedId===ship.queueEntryId"
          :title="allowed && queue.items.length>1 ? '点击查看，拖动调整顺位' : '查看本次停留'"
          @pointerdown="drag.begin($event,ship.queueEntryId)" @pointermove="drag.move"
          @pointerup="drag.end($event)" @pointercancel="drag.end($event,true)" @lostpointercapture="drag.end($event,true)"
          @click="drag.click($event,()=>emit('select',ship.queueEntryId))" @dragstart.prevent>
          <QueueShip/><strong>{{ ship.shipName }}</strong>
          <span>{{ draggingId===ship.queueEntryId && preview ? `移至第 ${preview.indexOf(ship.queueEntryId)+1} 位` : `等待 ${waiting(ship)}` }}</span>
        </button>
      </div>
      <div v-if="draggingId && preview" class="aq-drop-position" :style="{'--slot':preview.indexOf(draggingId)}" aria-hidden="true"></div>
    </div>
    <div v-else class="aq-sea-empty"><PortIcon name="anchor" :size="32"/><p>当前没有候泊船舶</p></div>
  </div>
  <div v-if="uncertain&&!opened&&active" class="aq-pending-check" role="status"><span>{{ current?.shipName }}调序结果待核对</span><button class="button" @click="resume">核对原调序</button></div>
  <dialog ref="dialog" class="aq-dialog aq-operation aq-reorder" aria-labelledby="aq-reorder-title" @cancel.prevent="close()">
    <header><h2 id="aq-reorder-title">调整候泊顺位</h2><button class="icon-button" :disabled="busy" aria-label="关闭调序窗口" @click="close()"><PortIcon name="close"/></button></header>
    <p class="aq-operation-context"><strong>{{ current?.shipName }}</strong><span>{{ current?.callNo }}</span></p>
    <form ref="form" @submit.prevent="save">
      <fieldset :disabled="busy||uncertain||conflict">
        <div class="aq-position-change"><span>第 <strong>{{ originalPosition }}</strong> 位</span><PortIcon name="arrow"/><label for="aq-new-position">移至第 <input id="aq-new-position" v-model.number="destination" type="number" min="1" :max="pending?.original.length" step="1" required @input="updatePreview"/> 位</label></div>
        <ul v-if="affected.length" class="aq-affected" aria-label="同时顺移的船舶"><li v-for="ship in affected" :key="ship.queueEntryId"><span>{{ ship.shipName }}</span><span>第 {{ ship.queuePosition }} 位 <PortIcon name="arrow" :size="14"/> 第 {{ proposed.indexOf(ship.queueEntryId)+1 }} 位</span></li></ul>
        <label for="aq-reorder-reason">调整原因</label><textarea id="aq-reorder-reason" v-model="reason" rows="2" maxlength="500" required></textarea>
      </fieldset>
      <p v-if="error" class="aq-form-error" role="alert">{{ error }}</p><p v-if="checked" class="aq-check-result" role="status">{{ checked }}</p>
      <footer><button type="button" class="button" :disabled="busy" @click="close()">{{ uncertain?(checkedDone?'结束核对':'稍后核对'):'取消' }}</button><button v-if="uncertain||conflict" type="button" class="button button-primary" :disabled="busy" @click="reconcile">{{ busy?'正在核对…':'读取最新队列' }}</button><button v-else class="button button-primary" type="submit" :disabled="busy||destination===originalPosition">{{ busy?'正在保存…':'确认调序' }}</button></footer>
    </form>
  </dialog>
</template>
