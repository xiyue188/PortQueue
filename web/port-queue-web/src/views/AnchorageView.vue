<script setup lang="ts">
// 锚地工作区沿用已确认原型；查询、到访选择和业务动作各自保留明确边界。
import { computed, nextTick, onScopeDispose, ref, watch } from 'vue'
import type { SessionClient } from '../composables/useSession'
import { useAnchorages } from '../composables/useAnchorages'
import PortIcon from '../components/PortIcon.vue'
import AnchorageQueueLane from '../components/AnchorageQueueLane.vue'
import AnchorageOperation from '../components/AnchorageOperation.vue'
import { appointmentStatuses } from '../types/appointment'
import { formatDate } from '../utils/appointmentPresentation'
import scene from '../assets/port-scene.png'
const props = defineProps<{ active: boolean; accountSession: SessionClient }>()
const emit = defineEmits<{ appointment: [id: string]; changed: [] }>()
const model = useAnchorages()
const { facilities, queue, detail, areaId, selectedId, loading, detailLoading, error, detailError } = model
const query = ref(''), composing = ref(false), occupancyOpen = ref(false)
const timeline = ref<HTMLDialogElement>(), lane = ref<InstanceType<typeof AnchorageQueueLane>>()
const operation = ref<InstanceType<typeof AnchorageOperation>>(), feedback = ref('')
let timelineTrigger: HTMLElement | null = null
const identity = computed(() => { const user = props.accountSession.session.value?.user; return user ? `${user.userId}:${user.roleCode}` : '' })
const area = computed(() => facilities.value?.items.find(a => a.anchorageId === areaId.value))
const role = computed(() => props.accountSession.session.value?.user?.roleCode ?? '')
const canRecord = computed(() => ['ADMIN','DISPATCHER','RECORDER'].includes(role.value))
const canArrange = computed(() => ['ADMIN','DISPATCHER'].includes(role.value))
const isPresent = computed(() => !!detail.value?.enteredAt && !detail.value.leftAt)
const canReorderSelected = computed(() => canArrange.value && (queue.value?.items.length ?? 0) > 1
  && !!queue.value?.items.some(ship => ship.queueEntryId === detail.value?.queueEntryId))
const matching = computed(() => queue.value?.items.filter(s => `${s.shipName} ${s.callNo} ${s.appointmentNo}`.toLowerCase().includes(query.value.trim().toLowerCase())) ?? [])
const statusName = (status: string) => ({ WAITING: '待安排', ASSIGNED: '已安排', CANCELLED: '已撤销', LEFT: '已离锚' }[status] ?? status)
const eventName = (type: string) => ({ ANCHORAGE_ENTER: '实际入锚', ANCHORAGE_LEAVE: '实际离锚', PORT_ENTER: '实际进港', BERTHING: '实际靠泊', UNBERTHING: '实际离泊' }[type] ?? type)
function waiting(entered: string | null, left: string | null) {
  if (!entered || !queue.value) return '待核对'
  const minutes = Math.floor((Date.parse(left ?? queue.value.observedAt) - Date.parse(entered)) / 60_000)
  if (minutes < 0) return '时间待核对'
  return minutes >= 1440 ? `${Math.floor(minutes / 1440)}天${Math.floor(minutes % 1440 / 60)}小时` : `${Math.floor(minutes / 60)}小时${minutes % 60}分`
}
const steps = computed(() => [
  { title: '预约提交', time: detail.value?.submittedAt }, { title: '审核结果', time: detail.value?.reviewedAt },
  { title: '实际入锚', time: detail.value?.enteredAt }, { title: '确认靠泊', time: detail.value?.confirmedBerthingAt },
  { title: '实际离锚', time: detail.value?.leftAt },
])
async function select(id: string) { await model.select(id); await nextTick(); locateSelected() }
function locateSelected() { void lane.value?.locateSelected() }
async function changeArea(id: string) { occupancyOpen.value = false; query.value = ''; await model.load(id); await nextTick(); lane.value?.scrollStart() }
function search() { if (!composing.value && matching.value[0]) void select(matching.value[0].queueEntryId) }
function openTimeline() { timelineTrigger = document.activeElement as HTMLElement; timeline.value?.showModal() }
function closeTimeline() { timeline.value?.close(); timelineTrigger?.focus({preventScroll:true}) }
async function refresh() { if (identity.value) await model.load(); await nextTick(); locateSelected() }
function reconcileSaved() { emit('changed'); void refresh() }
async function afterSaved(id: string, message: string) {
  const savedIdentity = identity.value, savedArea = areaId.value
  feedback.value = message; emit('changed'); await model.load()
  if (savedIdentity !== identity.value || savedArea !== areaId.value || !props.active) return
  if (!error.value) await model.select(id)
  if (savedIdentity !== identity.value) return
  if (error.value || detailError.value) feedback.value = `${message}最新页面暂未读取成功，请重新读取；无需重复保存。`
  await nextTick(); locateSelected()
}
async function locateAppointment(id: string) { if (identity.value) { await model.load('', id); await nextTick(); locateSelected() } }
watch(identity, () => { model.clear(); closeTimeline(); query.value = ''; feedback.value = ''; occupancyOpen.value = false; if (identity.value && props.active) void model.load() })
watch(() => props.active, active => {
  if (!active) { model.suspend(); closeTimeline(); return }
  if (identity.value && model.stale()) void model.load()
}, { immediate:true })
onScopeDispose(() => timeline.value?.close())
defineExpose({ refresh, locateAppointment })
</script>

<template>
  <section class="anchorage-workspace" :hidden="!active" aria-label="锚地工作区" :aria-busy="loading">
    <p v-if="feedback" class="aq-feedback" role="status">{{ feedback }}<button class="icon-button" aria-label="关闭操作结果" @click="feedback = ''"><PortIcon name="close" :size="15"/></button></p>
    <div v-if="!identity" class="aq-empty glass-panel"><PortIcon name="anchor" :size="32"/><h2>锚地排队</h2><p>请在账号区登录后继续。</p></div>
    <div v-else-if="error" class="aq-empty glass-panel" role="alert"><p>{{ error }}</p><button class="button" @click="refresh">重新读取</button></div>
    <div v-show="identity && !error" class="aq-content">
      <header class="aq-heading"><h2>{{ area?.name ?? (loading ? '正在读取锚地…' : '锚地排队') }}</h2>
        <div class="aq-tabs" role="group" aria-label="选择锚地"><button v-for="a in facilities?.items" :key="a.anchorageId" :aria-pressed="a.anchorageId === areaId" :disabled="loading" @click="changeArea(a.anchorageId)">{{ a.name }} <span>{{ a.occupiedCount }}</span></button></div>
      </header>
      <div class="aq-main" :inert="loading || undefined">
        <section class="aq-board glass-panel" aria-labelledby="aq-title">
          <div class="aq-toolbar"><h3 id="aq-title">候泊队列 <span>{{ queue?.items.length ?? '—' }} 艘</span></h3>
            <label class="aq-search"><PortIcon name="search" :size="17"/><input v-model="query" type="search" placeholder="船名 / 预约编号" aria-label="查找队列船舶" @compositionstart="composing = true" @compositionend="composing = false" @keydown.enter="search"/><span v-if="query">{{ matching.length }}</span></label>
            <button v-if="canRecord && area" class="button button-primary aq-enter" :disabled="area.statusCode !== 'AVAILABLE'" @click="operation?.open('enter',area)">登记入锚</button>
          </div>
          <AnchorageQueueLane ref="lane" :queue="queue" :selected-id="selectedId" :query="query" :active="active" :account-session="accountSession" @select="select" @saved="afterSaved" @refresh="reconcileSaved"/>
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
                <div><dt><button v-if="canArrange && isPresent && detail.statusCode !== 'CANCELLED' && area" class="aq-field-action" @click="operation?.open('estimate',area,detail)">人工预计靠泊 <PortIcon name="edit" :size="13"/></button><span v-else>人工预计靠泊</span></dt><dd>{{ formatDate(detail.manualEstimatedBerthingAt) }}</dd></div><div><dt>已确认靠泊</dt><dd>{{ formatDate(detail.confirmedBerthingAt) }}</dd></div>
              </dl>
              <details v-if="detail.legacyEstimatedBerthingAt" class="aq-legacy"><summary>历史预计</summary><p>{{ formatDate(detail.legacyEstimatedBerthingAt) }} · 来源待核</p></details>
              <div v-if="(canRecord && isPresent) || canReorderSelected" class="aq-actions">
                <button v-if="canRecord && isPresent && area" class="button button-primary" @click="operation?.open('leave',area,detail)"><PortIcon name="anchor" :size="18"/>登记离锚</button>
                <button v-if="canReorderSelected" class="button" @click="lane?.openReorder(detail.queueEntryId)"><PortIcon name="grip" :size="18"/>移至第几位</button>
              </div>
            </div>
          </template>
          <div v-else class="aq-empty"><p :role="detailError ? 'alert' : undefined">{{ detailError || (detailLoading ? '正在读取到访…' : '选择船舶查看本次到访') }}</p><button v-if="detailError && selectedId" class="button" @click="model.select(selectedId)">重试详情</button></div>
        </aside>
      </div>
      <!-- 选择异步加载时保留时间线的占位，避免正在拖动的船随主区高度跳动。 -->
      <section v-if="selectedId" class="aq-journey glass-panel" aria-label="本次到访" :aria-busy="detailLoading">
        <header><h3>本次到访</h3>
          <nav aria-label="到访记录">
            <button class="aq-link" :disabled="!detail" @click="detail && emit('appointment',detail.appointmentId)">查看进港预约 <PortIcon name="document" :size="16"/></button>
            <button class="aq-link" :disabled="!detail" @click="openTimeline">展开时间线 <PortIcon name="arrow" :size="16"/></button>
          </nav>
        </header>
        <div class="aq-steps">
          <button v-for="step in steps" :key="step.title" :class="{ done: !!step.time }" :disabled="!detail" @click="openTimeline">
            <span class="aq-node"><PortIcon :name="step.time ? 'check' : 'clock'" :size="13"/></span>
            <strong>{{ step.title }}</strong>
            <time>{{ !detail ? (detailLoading ? '读取中…' : '未能读取') : step.time ? formatDate(step.time) : '尚未记录' }}</time>
          </button>
        </div>
      </section>
    </div>
  </section>
  <dialog ref="timeline" class="aq-dialog" aria-labelledby="aq-visit-title" @cancel.prevent="closeTimeline">
    <header><h2 id="aq-visit-title">{{ detail?.shipName }} · 本次到访</h2><button class="icon-button" aria-label="关闭到访时间线" @click="closeTimeline"><PortIcon name="close"/></button></header>
    <p class="aq-call">{{ detail?.callNo }}</p>
    <div class="aq-history"><details open><summary>进港预约 <time>{{ formatDate(detail?.submittedAt ?? null) }}</time></summary><p>{{ detail?.appointmentNo }}</p><button class="aq-link" @click="closeTimeline(); detail && emit('appointment', detail.appointmentId)">打开预约 <PortIcon name="arrow" :size="16"/></button></details>
      <details><summary>审核记录 <time>{{ formatDate(detail?.reviewedAt ?? null) }}</time></summary><p>{{ detail?.reviewerName ?? '尚无审核人' }} · {{ appointmentStatuses.find(s => s.code === detail?.appointmentStatusCode)?.label }}</p></details>
      <details v-for="event in detail?.events" :key="event.eventId"><summary>{{ eventName(event.eventTypeCode) }} <time>{{ formatDate(event.eventTime) }}</time></summary><p>记录人：{{ event.recordedByName }}<span v-if="event.queueEntryId === detail?.queueEntryId"> · 本次停留</span></p></details>
      <details v-if="detail?.confirmedBerthingAt"><summary>已确认泊位 <time>{{ formatDate(detail.confirmedBerthingAt) }}</time></summary><p>{{ detail.berthName }} · {{ formatDate(detail.confirmedUnberthingAt) }} 结束</p></details>
    </div>
  </dialog>
  <AnchorageOperation ref="operation" :account-session="accountSession" :active="active" @saved="afterSaved" @refresh="reconcileSaved"/>
</template>

<style src="../styles/anchorage.css"></style>
