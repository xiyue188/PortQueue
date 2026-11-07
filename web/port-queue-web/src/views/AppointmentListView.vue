<script setup lang="ts">
// 统一管理真实查询和选中预约，两种参考布局共享结果，切换视图不会丢失筛选。
import { computed, nextTick, onMounted, ref } from 'vue'
import PortIcon from '../components/PortIcon.vue'
import AppointmentTable from '../components/AppointmentTable.vue'
import AppointmentDetail from '../components/AppointmentDetail.vue'
import AppointmentOverview from '../components/AppointmentOverview.vue'
import { useAppointments } from '../composables/useAppointments'
import { appointmentStatuses, type AppointmentItem, type AppointmentStatus } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/vessel-reference.png'

const activeView = ref<'overview' | 'appointments'>('overview')
const mobileMenuOpen = ref(false)
const keyword = ref('')
const selectedId = ref<string>()
const { query, rows, totalCount, totalPages, loading, error, lastUpdated, load, search } = useAppointments()
const selected = computed(() => rows.value.find(row => row.appointmentId === selectedId.value) ?? rows.value[0])
const pendingOnPage = computed(() => rows.value.filter(row => row.appointmentStatusCode === 'PENDING').length)
const approvedOnPage = computed(() => rows.value.filter(row => row.appointmentStatusCode === 'APPROVED').length)
const hasResult = computed(() => !loading.value && !error.value && rows.value.length > 0)
const title = computed(() => activeView.value === 'overview' ? '预约总览' : '进港预约')
const dateLabel = computed(() => new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
}).format(lastUpdated.value ?? new Date()))
const updatedLabel = computed(() => lastUpdated.value ? new Intl.DateTimeFormat('zh-CN', {
  timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
}).format(lastUpdated.value) : '')
const resultAnnouncement = computed(() => loading.value ? '正在查询预约'
  : error.value ? '查询未完成' : `共 ${totalCount.value} 条预约，第 ${query.value.page} 页，共 ${totalPages.value} 页`)
const plannedNavigation = [
  { label: '锚地排队', icon: 'anchor' }, { label: '泊位计划', icon: 'berth' },
  { label: '实际执行', icon: 'document' }, { label: '查询统计', icon: 'chart' },
]
const statusTabs: { label: string; code: AppointmentStatus | '' }[] = [
  { label: '全部', code: '' }, { label: '待审核', code: 'PENDING' },
  { label: '已批准', code: 'APPROVED' }, { label: '已完成', code: 'COMPLETED' },
]

function switchView(view: 'overview' | 'appointments') {
  activeView.value = view
  mobileMenuOpen.value = false
}
function submit() { void search(keyword.value, query.value.status) }
function reset() { keyword.value = ''; void search('', '') }
function filterStatus(status: AppointmentStatus | '') {
  // 状态切换保留已提交的关键词，避免把尚在编辑的搜索草稿隐式提交。
  void search(query.value.keyword, status)
}
function changeStatus(event: Event) { filterStatus((event.target as HTMLSelectElement).value as AppointmentStatus | '') }
function changePageSize(event: Event) {
  void load({ ...query.value, page: 1, pageSize: Number((event.target as HTMLSelectElement).value) })
}
function selectRow(item: AppointmentItem) { selectedId.value = item.appointmentId }
function cycle(step: number) {
  if (!selected.value || rows.value.length < 2) return
  const index = rows.value.findIndex(row => row.appointmentId === selected.value?.appointmentId)
  selectedId.value = rows.value[(index + step + rows.value.length) % rows.value.length].appointmentId
}
async function openDetails() {
  switchView('appointments')
  await nextTick()
  document.getElementById('appointment-detail')?.focus()
}
onMounted(() => { void load() })
</script>

<template>
  <a class="skip-link" href="#main-content">跳至主要内容</a>
  <div class="port-workspace" :class="{ 'is-overview': activeView === 'overview' }">
    <aside class="sidebar" :class="{ 'is-open': mobileMenuOpen }">
      <a class="brand" href="#main-content" @click="switchView('overview')" aria-label="PortQueue 预约总览">
        <svg class="brand-mark" viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M5 13c3-7 8-7 12-3s7 5 10-2M5 20c3-7 8-7 12-3s7 5 10-2M5 27c3-7 8-7 12-3s7 5 10-2" stroke="currentColor" stroke-width="3.4" stroke-linecap="round"/></svg>
        <span>PORTQUEUE</span>
      </a>
      <nav aria-label="业务导航" class="side-nav">
        <button :aria-current="activeView === 'overview' ? 'page' : undefined" @click="switchView('overview')"><PortIcon name="home" /><span>预约总览</span></button>
        <button :aria-current="activeView === 'appointments' ? 'page' : undefined" @click="switchView('appointments')"><PortIcon name="ship" /><span>进港预约</span></button>
        <button v-for="nav in plannedNavigation" :key="nav.label" disabled><PortIcon :name="nav.icon" /><span>{{ nav.label }}</span><small>待接入</small></button>
      </nav>
      <div class="sidebar-bottom">
        <div class="workspace-account"><span class="account-avatar"><PortIcon name="user" :size="21" /></span><div><strong>本地演示</strong><small>港口业务工作台</small></div><PortIcon name="down" :size="13" /></div>
      </div>
    </aside>

    <main id="main-content" class="workspace-main" tabindex="-1">
      <header class="topbar">
        <button class="icon-button mobile-toggle" aria-label="展开业务导航" :aria-expanded="mobileMenuOpen" @click="mobileMenuOpen = !mobileMenuOpen"><PortIcon name="menu" /></button>
        <div class="page-title"><h1>{{ title }}</h1><span class="header-date">{{ dateLabel }}</span></div>
        <form class="header-search" role="search" aria-label="预约查询" @submit.prevent="submit">
          <label class="sr-only" for="appointment-keyword">预约关键词</label>
          <PortIcon name="search" :size="18" />
          <input id="appointment-keyword" v-model="keyword" type="search" maxlength="100" placeholder="搜索船舶、预约编号、IMO…" autocomplete="off" />
          <button type="submit" class="search-submit">查询</button>
        </form>
        <button class="button button-primary refresh-button" :disabled="loading" @click="load()"><PortIcon name="refresh" :size="17" /><span>刷新预约</span></button>
      </header>

      <section v-if="activeView === 'overview'" class="glass-panel metrics-strip" aria-label="预约查询摘要">
        <div class="metric"><span class="metric-icon"><PortIcon name="ship" :size="27" /></span><div><span>匹配预约</span><strong>{{ loading || error ? '—' : totalCount }}</strong></div><small>当前查询条件</small></div>
        <div class="metric"><span class="metric-icon"><PortIcon name="anchor" :size="27" /></span><div><span>本页待审核</span><strong>{{ loading || error ? '—' : pendingOnPage }}</strong></div><small>待处理记录</small></div>
        <div class="metric"><span class="metric-icon"><PortIcon name="clock" :size="27" /></span><div><span>本页已批准</span><strong>{{ loading || error ? '—' : approvedOnPage }}</strong></div><small>审核已通过</small></div>
      </section>

      <section v-else-if="hasResult && selected" class="glass-panel selection-strip" aria-label="当前选中预约">
        <img :src="vesselImage" alt="" class="summary-vessel" />
        <div class="summary-identity"><strong>{{ selected.shipName }}</strong><span>{{ selected.appointmentNo }}</span></div>
        <div class="summary-time"><PortIcon name="calendar" :size="26" /><div><span>申请到港 · 北京时间</span><strong>{{ formatDate(selected.requestedArrivalAt) }}</strong></div></div>
        <div class="summary-status"><span>预约状态</span><span class="status-badge" :class="statusClass(selected.appointmentStatusCode)"><i></i>{{ statusLabel(selected.appointmentStatusCode) }}</span></div>
      </section>

      <div v-if="loading" class="glass-panel page-message" role="status"><span class="loading-line"></span><h2>正在查询预约</h2><p>正在获取最新记录，请稍候。</p></div>
      <div v-else-if="error" class="glass-panel page-message" role="alert"><span class="message-icon"><PortIcon name="info" :size="28" /></span><h2>暂时无法显示预约</h2><p>{{ error }}</p><button class="button button-primary" @click="load()">重新查询</button></div>
      <div v-else-if="rows.length === 0" class="glass-panel page-message" role="status">
        <span class="message-icon"><PortIcon name="search" :size="28" /></span><h2>{{ totalCount ? '这一页没有预约记录' : '没有符合条件的预约' }}</h2>
        <p>{{ totalCount ? '记录数量可能已变化，请返回第一页查看。' : '调整关键词或状态，查看其他预约。' }}</p>
        <button v-if="totalCount" class="button button-primary" @click="load({ ...query, page: 1 })">返回第一页</button>
        <button v-else class="button button-primary" @click="reset">清除筛选</button>
      </div>

      <template v-if="hasResult && selected">
        <AppointmentOverview v-if="activeView === 'overview'" :item="selected" :can-cycle="rows.length > 1" @cycle="cycle" @details="openDetails" />
      </template>

      <div class="records-layout" :class="{ 'with-detail': activeView === 'appointments' && hasResult }">
        <section class="glass-panel records-panel" aria-labelledby="records-title" :aria-busy="loading">
          <div class="panel-heading records-heading"><div><h2 id="records-title">{{ activeView === 'overview' ? '进港预约记录' : '预约清单' }}</h2><p>{{ query.keyword ? '关键词：' + query.keyword : '全部申请日期' }} · 按申请到港时间倒序</p></div>
            <button class="icon-button" aria-label="重置全部筛选" title="重置全部筛选" @click="reset"><PortIcon name="reset" :size="17" /></button>
          </div>
          <div class="filters-bar">
            <div class="filter-tabs" role="group" aria-label="常用预约状态">
              <button v-for="tab in statusTabs" :key="tab.code" :aria-pressed="query.status === tab.code" @click="filterStatus(tab.code)">{{ tab.label }}</button>
            </div>
            <div class="all-statuses"><label class="sr-only" for="appointment-status">预约状态</label><select id="appointment-status" :value="query.status" @change="changeStatus"><option value="">全部状态</option><option v-for="status in appointmentStatuses" :key="status.code" :value="status.code">{{ status.label }}</option></select></div>
          </div>
          <p v-if="hasResult" class="scroll-hint">左右滑动表格查看完整记录，选择船舶查看详情。</p>
          <AppointmentTable v-if="hasResult" :rows="rows" :selected-id="selected?.appointmentId" :compact="activeView === 'overview'" @select="selectRow" />
          <p v-else class="records-placeholder">{{ loading ? '正在载入记录…' : error ? '连接恢复后显示记录' : '当前列表为空' }}</p>
          <footer class="pagination">
            <span role="status" aria-live="polite">{{ resultAnnouncement }}</span>
            <div class="pagination-controls">
              <label class="sr-only" for="page-size">每页条数</label><select id="page-size" :value="query.pageSize" :disabled="loading" @change="changePageSize"><option :value="2">2 条 / 页</option><option :value="10">10 条 / 页</option><option :value="20">20 条 / 页</option><option :value="50">50 条 / 页</option><option :value="100">100 条 / 页</option></select>
              <button class="icon-button" aria-label="上一页" :disabled="loading || !!error || query.page <= 1" @click="load({ ...query, page: query.page - 1 })"><PortIcon name="left" :size="17" /></button>
              <span class="page-number">{{ loading || error ? '—' : query.page }}</span>
              <button class="icon-button" aria-label="下一页" :disabled="loading || !!error || query.page >= totalPages" @click="load({ ...query, page: query.page + 1 })"><PortIcon name="right" :size="17" /></button>
            </div>
          </footer>
        </section>
        <AppointmentDetail v-if="activeView === 'appointments' && hasResult && selected" :item="selected" />
      </div>
      <footer class="workspace-footnote"><span>课程演示数据 · 时间为北京时间（UTC+8）</span><span v-if="updatedLabel">更新于 {{ updatedLabel }}</span></footer>
    </main>
  </div>
</template>
