<script setup lang="ts">
// 锚地工作区沿用已确认原型；查询、到访选择和业务动作各自保留明确边界。
import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import type { SessionClient } from '../composables/useSession'
import { useAnchorages } from '../composables/useAnchorages'
import PortIcon from '../components/PortIcon.vue'
import QueueShip from '../components/QueueShip.vue'
import { formatDate } from '../utils/appointmentPresentation'
import scene from '../assets/port-scene.png'
const props = defineProps<{ active: boolean; accountSession: SessionClient }>()
const emit = defineEmits<{ appointment: [id: string]; changed: [] }>()
const model = useAnchorages()
const { facilities, queue, detail, areaId, selectedId, loading, detailLoading, error, detailError } = model
const query = ref(''), composing = ref(false), occupancyOpen = ref(false)
const timeline = ref<HTMLDialogElement>(), lane = ref<HTMLElement>()
let timelineTrigger: HTMLElement | null = null
const identity = computed(() => { const user = props.accountSession.session.value?.user; return user ? `${user.userId}:${user.roleCode}` : '' })
const area = computed(() => facilities.value?.items.find(a => a.anchorageId === areaId.value))
const matching = computed(() => queue.value?.items.filter(s => `${s.shipName} ${s.callNo} ${s.appointmentNo}`.toLowerCase().includes(query.value.trim().toLowerCase())) ?? [])
const statusName = (status: string) => ({ WAITING: '待安排', ASSIGNED: '已安排', CANCELLED: '已撤销', LEFT: '已离锚' }[status] ?? status)
const eventName = (type: string) => ({ ANCHORAGE_ENTER: '实际入锚', ANCHORAGE_LEAVE: '实际离锚', PORT_ENTER: '实际进港', BERTHING: '实际靠泊', UNBERTHING: '实际离泊' }[type] ?? type)
function waiting(entered: string | null, left: string | null) {
  if (!entered || !queue.value) return '待核对'
  const minutes = Math.floor((Date.parse(left ?? queue.value.observedAt) - Date.parse(entered)) / 60_000)
  if (minutes < 0) return '时间待核对'
  return minutes >= 1440 ? `${Math.floor(minutes / 1440)}天${Math.floor(minutes % 1440 / 60)}小时` : `${Math.floor(minutes / 60)}小时${minutes % 60}分`
}
const steps = computed(() => !detail.value ? [] : [
  { title: '预约提交', time: detail.value.submittedAt }, { title: '审核结果', time: detail.value.reviewedAt },
  { title: '实际入锚', time: detail.value.enteredAt }, { title: '确认靠泊', time: detail.value.confirmedBerthingAt },
  { title: '实际离锚', time: detail.value.leftAt },
])
async function select(id: string) { await model.select(id); await nextTick(); locateSelected() }
function locateSelected() {
  const element = lane.value?.querySelector<HTMLElement>(`[data-stay="${selectedId.value}"]`)
  if (!element || !lane.value) return
  const left = element.offsetLeft, right = left + element.offsetWidth
  if (left < lane.value.scrollLeft || right > lane.value.scrollLeft + lane.value.clientWidth)
    lane.value.scrollLeft = Math.max(0, left - (lane.value.clientWidth-element.offsetWidth)/2)
}
async function changeArea(id: string) { occupancyOpen.value = false; query.value = ''; await model.load(id); await nextTick(); if (lane.value) lane.value.scrollLeft = 0 }
function search() { if (!composing.value && matching.value[0]) void select(matching.value[0].queueEntryId) }
function openTimeline() { timelineTrigger = document.activeElement as HTMLElement; timeline.value?.showModal() }
function closeTimeline() { timeline.value?.close(); timelineTrigger?.focus({preventScroll:true}) }
async function refresh() { if (identity.value) await model.load(); await nextTick(); locateSelected() }
async function locateAppointment(id: string) { if (identity.value) { await model.load('', id); await nextTick(); locateSelected() } }
watch(identity, () => { model.clear(); closeTimeline(); query.value = ''; occupancyOpen.value = false; if (identity.value && props.active) void model.load() })
watch(() => props.active, active => {
  if (!active) { model.suspend(); closeTimeline(); return }
  if (identity.value && model.stale()) void model.load()
}, { immediate:true })
onScopeDispose(() => timeline.value?.close())
defineExpose({ refresh, locateAppointment })
</script>

<template>
  <section class="anchorage-workspace" :hidden="!active" aria-label="锚地工作区" :aria-busy="loading">
    <div v-if="!identity" class="aq-empty glass-panel"><PortIcon name="anchor" :size="32"/><h2>锚地排队</h2><p>请在账号区登录后继续。</p></div>
    <div v-else-if="error" class="aq-empty glass-panel" role="alert"><p>{{ error }}</p><button class="button" @click="refresh">重新读取</button></div>
    <template v-else>
      <header class="aq-heading"><h2>{{ area?.name ?? (loading ? '正在读取锚地…' : '锚地排队') }}</h2>
        <div class="aq-tabs" role="group" aria-label="选择锚地"><button v-for="a in facilities?.items" :key="a.anchorageId" :aria-pressed="a.anchorageId === areaId" :disabled="loading" @click="changeArea(a.anchorageId)">{{ a.name }} <span>{{ a.occupiedCount }}</span></button></div>
      </header>
      <div class="aq-main" :inert="loading || undefined">
        <section class="aq-board glass-panel" aria-labelledby="aq-title">
          <div class="aq-toolbar"><h3 id="aq-title">候泊队列 <span>{{ queue?.items.length ?? '—' }} 艘</span></h3>
            <label class="aq-search"><PortIcon name="search" :size="17"/><input v-model="query" type="search" placeholder="船名 / 预约编号" aria-label="查找队列船舶" @compositionstart="composing = true" @compositionend="composing = false" @keydown.enter="search"/><span v-if="query">{{ matching.length }}</span></label>
          </div>
          <div ref="lane" class="aq-sea" tabindex="0" aria-label="完整候泊队列，可横向浏览">
            <div v-if="queue?.items.length" class="aq-lane" :style="{ '--slots': Math.max(5, queue.items.length) }">
              <div v-for="(ship,index) in queue.items" :key="ship.queueEntryId" class="aq-vessel" :class="{ selected: selectedId === ship.queueEntryId, dim: query && !matching.includes(ship) }" :data-stay="ship.queueEntryId" :style="{ '--slot': index }">
                <span class="aq-rank">{{ ship.queuePosition }}</span>
                <button class="aq-select" :aria-label="`${ship.shipName}，第${ship.queuePosition}位`" :aria-pressed="selectedId === ship.queueEntryId" @click="select(ship.queueEntryId)"><QueueShip/><strong>{{ ship.shipName }}</strong><span>等待 {{ waiting(ship.enteredAt, ship.leftAt) }}</span></button>
              </div>
            </div>
            <div v-else class="aq-sea-empty"><PortIcon name="anchor" :size="32"/><p>{{ loading ? '正在读取队列…' : '当前没有候泊船舶' }}</p></div>
          </div>
          <footer class="aq-occupancy"><span>实际在场 <strong>{{ queue?.occupiedCount ?? '—' }}</strong> / {{ queue?.capacity ?? '未登记容量' }}</span>
            <div class="aq-capacity" aria-hidden="true"><i :style="{ width: queue?.capacity ? `${Math.min(100,queue.occupiedCount / queue.capacity * 100)}%` : '0%' }"></i></div>
            <button v-if="queue?.cancelled.length" class="aq-link" :aria-expanded="occupancyOpen" @click="occupancyOpen = !occupancyOpen">待离锚 {{ queue.cancelled.length }} 艘 <PortIcon name="down" :size="15"/></button>
            <span v-else>{{ queue?.capacity === null ? '容量未登记' : `余位 ${Math.max(0,(queue?.capacity ?? 0)-(queue?.occupiedCount ?? 0))}` }}</span>
            <div v-if="occupancyOpen" class="aq-occupancy-popover"><button v-for="ship in queue?.cancelled" :key="ship.queueEntryId" @click="select(ship.queueEntryId); occupancyOpen = false"><PortIcon name="ship" :size="18"/><strong>{{ ship.shipName }}</strong><span>已撤销 · 待离锚</span><PortIcon name="arrow" :size="16"/></button></div>
          </footer>
        </section>
        <aside class="aq-detail glass-panel" :aria-busy="detailLoading">
          <template v-if="detail">
            <div class="aq-scene" :style="{ backgroundImage: `url(${scene})` }"></div>
            <div class="aq-detail-body"><div class="aq-identity"><h2>{{ detail.shipName }}</h2><span class="aq-state" :class="{assigned:detail.statusCode === 'ASSIGNED'}">{{ statusName(detail.statusCode) }}</span></div><span class="aq-call">{{ detail.callNo }}</span>
              <dl class="aq-facts"><div><dt>实际入锚</dt><dd>{{ formatDate(detail.enteredAt) }}</dd></div><div><dt>{{ detail.leftAt ? '本段停留' : '已等待' }}</dt><dd>{{ waiting(detail.enteredAt,detail.leftAt) }}</dd></div>
                <div><dt>人工预计靠泊</dt><dd>{{ formatDate(detail.manualEstimatedBerthingAt) }}</dd></div><div><dt>已确认靠泊</dt><dd>{{ formatDate(detail.confirmedBerthingAt) }}</dd></div>
              </dl>
              <details v-if="detail.legacyEstimatedBerthingAt" class="aq-legacy"><summary>历史预计</summary><p>{{ formatDate(detail.legacyEstimatedBerthingAt) }} · 来源待核</p></details>
              <button class="button button-primary aq-primary" @click="openTimeline">查看本次到访 <PortIcon name="arrow" :size="17"/></button>
              <button class="aq-link aq-appointment" @click="emit('appointment',detail.appointmentId)">查看进港预约 <PortIcon name="document" :size="16"/></button>
            </div>
          </template>
          <div v-else class="aq-empty"><p :role="detailError ? 'alert' : undefined">{{ detailError || (detailLoading ? '正在读取到访…' : '选择船舶查看本次到访') }}</p><button v-if="detailError && selectedId" class="button" @click="model.select(selectedId)">重试详情</button></div>
        </aside>
      </div>
      <section v-if="detail" class="aq-journey glass-panel" aria-label="本次到访"><header><h3>本次到访</h3><button class="aq-link" @click="openTimeline">展开时间线 <PortIcon name="arrow" :size="16"/></button></header>
        <div class="aq-steps"><button v-for="step in steps" :key="step.title" :class="{ done: !!step.time }" @click="openTimeline"><span class="aq-node"><PortIcon :name="step.time ? 'check' : 'clock'" :size="13"/></span><strong>{{ step.title }}</strong><time>{{ step.time ? formatDate(step.time) : '尚未记录' }}</time></button></div>
      </section>
    </template>
  </section>
  <dialog ref="timeline" class="aq-dialog" aria-labelledby="aq-visit-title" @cancel.prevent="closeTimeline">
    <header><h2 id="aq-visit-title">{{ detail?.shipName }} · 本次到访</h2><button class="icon-button" aria-label="关闭到访时间线" @click="closeTimeline"><PortIcon name="close"/></button></header>
    <p class="aq-call">{{ detail?.callNo }}</p>
    <div class="aq-history"><details open><summary>进港预约 <time>{{ formatDate(detail?.submittedAt ?? null) }}</time></summary><p>{{ detail?.appointmentNo }}</p><button class="aq-link" @click="closeTimeline(); detail && emit('appointment', detail.appointmentId)">打开预约 <PortIcon name="arrow" :size="16"/></button></details>
      <details><summary>审核记录 <time>{{ formatDate(detail?.reviewedAt ?? null) }}</time></summary><p>{{ detail?.reviewerName ?? '尚无审核人' }} · {{ detail?.appointmentStatusCode }}</p></details>
      <details v-for="event in detail?.events" :key="event.eventId"><summary>{{ eventName(event.eventTypeCode) }} <time>{{ formatDate(event.eventTime) }}</time></summary><p>记录人：{{ event.recordedByName }}<span v-if="event.queueEntryId === detail?.queueEntryId"> · 本次停留</span></p></details>
      <details v-if="detail?.confirmedBerthingAt"><summary>已确认泊位 <time>{{ formatDate(detail.confirmedBerthingAt) }}</time></summary><p>{{ detail.berthName }} · {{ formatDate(detail.confirmedUnberthingAt) }} 结束</p></details>
    </div>
  </dialog>
</template>

<style src="../styles/anchorage.css"></style>
