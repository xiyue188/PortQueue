<script setup lang="ts">
import { computed, ref } from 'vue'

// 当前阶段只验证调度总览的信息层级和交互入口，数据仍为演示数据。
// 预约 API 联调完成后，再将此处替换为可复用的 API/composable 层。
type QueueRow = {
  position: number
  ship: string
  appointment: string
  anchorage: string
  status: string
  wait: string
}

const activeMenu = ref('overview')
const queueRows = ref<QueueRow[]>([
  { position: 1, ship: '海星 106', appointment: 'AP-20261027-001', anchorage: '外锚地 A', status: '等待安排', wait: '1 小时 42 分' },
  { position: 2, ship: '远洋宁波', appointment: 'AP-20261027-002', anchorage: '外锚地 A', status: '已分配泊位', wait: '56 分钟' },
  { position: 3, ship: '华东之门', appointment: 'AP-20261027-003', anchorage: '外锚地 A', status: '等待安排', wait: '28 分钟' },
])

const queueCount = computed(() => queueRows.value.length)

const menuItems = [
  { key: 'overview', label: '调度总览' },
  { key: 'appointments', label: '进港预约' },
  { key: 'queue', label: '锚地队列' },
  { key: 'berths', label: '泊位计划' },
  { key: 'reports', label: '执行统计' },
]
</script>

<template>
  <div class="app-shell">
    <aside class="sidebar">
      <div class="brand">
        <div class="brand-mark">PQ</div>
        <div>
          <strong>PortQueue</strong>
          <span>港口调度台</span>
        </div>
      </div>
      <div class="sidebar-label">工作区</div>
      <nav>
        <button
          v-for="item in menuItems"
          :key="item.key"
          class="nav-item"
          :class="{ active: activeMenu === item.key }"
          @click="activeMenu = item.key"
        >
          <span class="nav-dot" />
          {{ item.label }}
        </button>
      </nav>
      <div class="sidebar-footer">
        <span class="status-pulse" />
        <span>演示界面 · 接口待联调</span>
      </div>
    </aside>

    <main class="main-content">
      <header class="topbar">
        <div>
          <span class="eyebrow">PORT OPERATIONS</span>
          <h1>调度总览</h1>
        </div>
        <div class="topbar-meta">
          <span class="live-indicator"><i /> 实时状态</span>
          <span>调度员 · 演示账号</span>
        </div>
      </header>

      <section class="hero-grid">
        <article class="hero-card">
          <div class="hero-kicker">TODAY · 2026.10.27</div>
          <h2>让每一次进港安排<br />都有明确依据。</h2>
          <p>从预约审核到实际离泊，沿着同一条业务时间线查看调度状态。</p>
          <button class="primary-button" @click="activeMenu = 'appointments'">查看待审核预约 <span>→</span></button>
        </article>
        <div class="metric-grid">
          <article class="metric-card"><span class="metric-label">待审核预约</span><strong>06</strong><small>较昨日 <b>+2</b></small></article>
          <article class="metric-card accent"><span class="metric-label">锚地在场</span><strong>{{ queueCount }}</strong><small>外锚地 A · <b>正常</b></small></article>
          <article class="metric-card"><span class="metric-label">今日靠泊</span><strong>12</strong><small>已完成 <b>9</b> 艘</small></article>
          <article class="metric-card"><span class="metric-label">平均等待</span><strong>1.8h</strong><small>较上周 <b class="down">-12%</b></small></article>
        </div>
      </section>

      <section class="content-grid">
        <article class="panel queue-panel">
          <div class="panel-heading">
            <div><span class="eyebrow">ANCHORAGE QUEUE</span><h3>当前锚地队列</h3></div>
            <button class="text-button" @click="activeMenu = 'queue'">查看全部 →</button>
          </div>
          <div class="queue-table">
            <div class="table-row table-head"><span>序号</span><span>船舶</span><span>预约编号</span><span>状态</span><span>等待时长</span></div>
            <div v-for="row in queueRows" :key="row.appointment" class="table-row">
              <span class="position">{{ String(row.position).padStart(2, '0') }}</span>
              <span class="ship-name">{{ row.ship }}<small>{{ row.anchorage }}</small></span>
              <span class="appointment-code">{{ row.appointment }}</span>
              <span><em :class="['status-tag', row.status === '已分配泊位' ? 'assigned' : 'waiting']">{{ row.status }}</em></span>
              <span class="wait-time">{{ row.wait }}</span>
            </div>
          </div>
        </article>

        <article class="panel berth-panel">
          <div class="panel-heading"><div><span class="eyebrow">BERTH WINDOWS</span><h3>泊位时间窗</h3></div><span class="date-label">今日 · 10 月 27 日</span></div>
          <div class="timeline">
            <div class="time-axis"><span>08:00</span><span>10:00</span><span>12:00</span><span>14:00</span><span>16:00</span></div>
            <div class="berth-line"><span class="berth-label">B-01</span><div class="track"><span class="slot slot-blue" style="left: 6%; width: 27%;">远洋宁波</span><span class="slot slot-amber" style="left: 42%; width: 23%;">— 可安排 —</span></div></div>
            <div class="berth-line"><span class="berth-label">B-02</span><div class="track"><span class="slot slot-green" style="left: 15%; width: 34%;">海星 106</span></div></div>
            <div class="berth-line"><span class="berth-label">B-03</span><div class="track"><span class="slot slot-gray" style="left: 4%; width: 18%;">维护</span><span class="slot slot-amber" style="left: 45%; width: 33%;">— 可安排 —</span></div></div>
          </div>
          <div class="legend"><span><i class="dot blue" />已确认</span><span><i class="dot green" />执行中</span><span><i class="dot amber" />可安排</span></div>
        </article>
      </section>
    </main>
  </div>
</template>
