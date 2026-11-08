<script setup lang="ts">
// 两个页面分别缓存查询与选择，共用稳定工作区；页内查询不重建整个页面。
import { computed, nextTick, onMounted, onScopeDispose, ref } from 'vue'
import PortIcon from '../components/PortIcon.vue'
import AppointmentSearch from '../components/AppointmentSearch.vue'
import AppointmentRecords from '../components/AppointmentRecords.vue'
import AppointmentDetail from '../components/AppointmentDetail.vue'
import AppointmentOverview from '../components/AppointmentOverview.vue'
import { useOverview, eventLabels } from '../composables/useOverview'
import { useAppointments } from '../composables/useAppointments'
import type { AppointmentItem, AppointmentStatus } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/vessel-reference.png'

type View = 'overview' | 'appointments'
const viewFromHash = (): View => location.hash === '#appointments' ? 'appointments' : 'overview'
const activeView = ref<View>(viewFromHash())
const mobileMenuOpen = ref(false)
const keyword = ref('')
const selectedId = ref<string>()
const previewId = ref<string>()
const list = useAppointments()
const preview = useAppointments(5)
const { query, rows, totalCount, totalPages, loading, error, load, search } = list
const selected = computed(() => rows.value.find(row => row.appointmentId === selectedId.value) ?? rows.value[0])
const previewSelected = computed(() => preview.rows.value.find(row => row.appointmentId === previewId.value)
  ?? preview.rows.value.find(row => row.appointmentStatusCode === 'PENDING') ?? preview.rows.value[0])
const { overview, overviewError, overviewLoading, refreshOverview } = useOverview()
const title = computed(() => activeView.value === 'overview' ? '总览' : '进港预约')
const dateLabel = new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
const plannedNavigation = [
  { label: '锚地排队', icon: 'anchor' }, { label: '泊位计划', icon: 'berth' },
  { label: '进出港记录', icon: 'document' }, { label: '查询统计', icon: 'chart' },
]
let listVisited = false
function ensureList() { if (!listVisited) { listVisited = true; void load() } }
function switchView(view: View) {
  if (view !== activeView.value) history.pushState(null, '', `#${view}`)
  activeView.value = view
  mobileMenuOpen.value = false
  if (view === 'appointments') ensureList()
}
function historyChange() { activeView.value = viewFromHash(); mobileMenuOpen.value = false; if (activeView.value === 'appointments') ensureList() }
// 连续操作基于最近已提交的意图合并；尚未提交的输入草稿不影响状态切换。
function submit() { listVisited = true; switchView('appointments'); void search(keyword.value, (list.pendingQuery.value ?? query.value).status) }
function chooseSuggestion(item: AppointmentItem) {
  keyword.value = item.appointmentNo
  selectedId.value = item.appointmentId
  listVisited = true
  switchView('appointments')
  void search(item.appointmentNo, '')
}
function refresh() { void refreshOverview(); void preview.load(); if (listVisited) void load() }
function reset() { keyword.value = ''; void search('', '') }
function filterStatus(status: AppointmentStatus | '') { void search((list.pendingQuery.value ?? query.value).keyword, status) }
function selectRow(item: AppointmentItem) { if (!loading.value) selectedId.value = item.appointmentId }
function cycle(step: number) {
  const current = previewSelected.value
  if (!current || preview.loading.value || preview.rows.value.length < 2) return
  const index = preview.rows.value.findIndex(row => row.appointmentId === current.appointmentId)
  previewId.value = preview.rows.value[(index + step + preview.rows.value.length) % preview.rows.value.length].appointmentId
}
async function openDetails() {
  if (!previewSelected.value) return
  const item = previewSelected.value
  keyword.value = item.appointmentNo; selectedId.value = item.appointmentId
  listVisited = true; switchView('appointments')
  await search(item.appointmentNo, '')
  await nextTick()
  document.getElementById('appointment-detail')?.focus({ preventScroll: true })
}
onMounted(() => { refresh(); if (activeView.value === 'appointments') ensureList(); window.addEventListener('popstate', historyChange) })
onScopeDispose(() => window.removeEventListener('popstate', historyChange))
</script>

<template>
  <a class="skip-link" href="#main-content">跳至主要内容</a>
  <div class="port-workspace" :class="{ 'is-overview': activeView === 'overview' }">
    <aside class="sidebar" :class="{ 'is-open': mobileMenuOpen }">
      <a class="brand" href="#overview" @click.prevent="switchView('overview')" aria-label="PortQueue 总览">
        <svg class="brand-mark" viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M5 13c3-7 8-7 12-3s7 5 10-2M5 20c3-7 8-7 12-3s7 5 10-2M5 27c3-7 8-7 12-3s7 5 10-2" stroke="currentColor" stroke-width="3.4" stroke-linecap="round"/></svg>
        <span>PORTQUEUE</span>
      </a>
      <nav aria-label="业务导航" class="side-nav">
        <button :aria-current="activeView === 'overview' ? 'page' : undefined" @click="switchView('overview')"><PortIcon name="home" /><span>总览</span></button>
        <button :aria-current="activeView === 'appointments' ? 'page' : undefined" @click="switchView('appointments')"><PortIcon name="ship" /><span>进港预约</span></button>
        <button v-for="nav in plannedNavigation" :key="nav.label" disabled><PortIcon :name="nav.icon" /><span>{{ nav.label }}</span><small>待接入</small></button>
      </nav>
      <div class="sidebar-bottom">
        <div class="workspace-account"><span class="account-avatar"><PortIcon name="user" :size="21" /></span><div><strong>未登录</strong></div></div>
      </div>
    </aside>

    <main id="main-content" class="workspace-main" tabindex="-1">
      <header class="topbar">
        <button class="icon-button mobile-toggle" aria-label="展开业务导航" :aria-expanded="mobileMenuOpen" @click="mobileMenuOpen = !mobileMenuOpen"><PortIcon name="menu" /></button>
        <div class="page-title"><h1>{{ title }}</h1><span class="header-date">{{ dateLabel }}</span></div>
        <AppointmentSearch v-model="keyword" @search="submit" @select="chooseSuggestion" />
        <button class="button button-primary refresh-button" :disabled="loading || overviewLoading" @click="refresh"><PortIcon name="refresh" :size="17" /><span>刷新</span></button>
      </header>

      <div class="view-pane overview-pane" :hidden="activeView !== 'overview'" aria-label="港口总览">
        <section class="glass-panel metrics-strip wave-summary" aria-label="港口业务概况" :aria-busy="overviewLoading">
          <div class="metric"><span class="metric-icon"><PortIcon name="document" :size="25" /></span><div><span>待审核预约</span><strong>{{ overview?.pendingAppointments ?? '—' }}</strong></div></div>
          <div class="metric"><span class="metric-icon"><PortIcon name="anchor" :size="25" /></span><div><span>锚地在场</span><strong>{{ overview?.anchorageVessels ?? '—' }}</strong></div></div>
          <div class="metric"><span class="metric-icon"><PortIcon name="berth" :size="25" /></span><div><span>待靠泊计划</span><strong>{{ overview?.confirmedPlans ?? '—' }}</strong></div></div>
          <span v-if="overviewLoading" class="sr-only" role="status">正在更新总览</span>
        </section>
        <div class="focus-host" :aria-busy="preview.loading.value" :inert="preview.loading.value || undefined">
          <AppointmentOverview v-if="previewSelected" :item="previewSelected" :can-cycle="preview.rows.value.length > 1" @cycle="cycle" @details="openDetails" />
          <div v-else class="glass-panel focus-placeholder" aria-hidden="true"><PortIcon name="ship" :size="64" /></div>
        </div>
        <div class="overview-bottom">
          <AppointmentRecords compact :rows="preview.rows.value" :query="preview.query.value" :loading="preview.loading.value" :error="preview.error.value"
            :total-count="preview.totalCount.value" :total-pages="preview.totalPages.value" :selected-id="previewSelected?.appointmentId"
            @select="previewId = $event.appointmentId" @load="preview.load()" @reset="preview.load()" @all="switchView('appointments')" />
          <section class="glass-panel recent-events" aria-labelledby="events-title" :aria-busy="overviewLoading">
            <div class="panel-heading"><h2 id="events-title">港口动态</h2></div>
            <p v-if="overviewError" role="alert">{{ overviewError }} <button class="text-action" @click="refreshOverview">重试</button></p>
            <ol v-else-if="overview?.recentEvents.length">
              <li v-for="event in overview.recentEvents" :key="event.eventId"><PortIcon :name="event.eventTypeCode.includes('ANCHORAGE') ? 'anchor' : 'ship'" :size="19" /><strong>{{ event.shipName }}</strong><span>{{ eventLabels[event.eventTypeCode] }}</span><time>{{ formatDate(event.eventTime) }}</time></li>
            </ol>
            <p v-else role="status">{{ overviewLoading ? '正在读取动态…' : '暂无进出港记录' }}</p>
          </section>
        </div>
      </div>

      <div class="view-pane appointments-pane" :hidden="activeView !== 'appointments'" aria-label="进港预约工作区">
        <section class="glass-panel selection-strip" aria-label="当前选中预约" :aria-busy="loading">
          <template v-if="selected">
            <img :src="vesselImage" alt="" class="summary-vessel" />
            <div class="summary-identity"><strong>{{ selected.shipName }}</strong><span>{{ selected.appointmentNo }}</span></div>
            <div class="summary-time"><PortIcon name="calendar" :size="24" /><div><span>申请到港</span><strong>{{ formatDate(selected.requestedArrivalAt) }}</strong></div></div>
            <div class="summary-status"><span>预约状态</span><span class="status-badge" :class="statusClass(selected.appointmentStatusCode)"><i></i>{{ statusLabel(selected.appointmentStatusCode) }}</span></div>
          </template>
          <span v-else class="selection-placeholder">{{ loading ? '正在载入预约…' : error ? '预约暂时无法读取' : '暂无选中预约' }}</span>
        </section>
        <div class="records-layout with-detail">
          <AppointmentRecords :rows="rows" :query="query" :loading="loading" :error="error" :total-count="totalCount" :total-pages="totalPages"
            :selected-id="selected?.appointmentId" @select="selectRow" @filter="filterStatus" @load="load" @reset="reset" />
          <div class="detail-host" :aria-busy="loading" :inert="loading || undefined">
            <AppointmentDetail v-if="selected" :item="selected" />
            <aside v-else class="glass-panel detail-panel detail-placeholder"><PortIcon name="document" :size="30" /><p>{{ loading ? '正在载入详情…' : '选择预约查看详情' }}</p></aside>
          </div>
        </div>
      </div>
    </main>
  </div>
</template>
