<script setup lang="ts">
// 参考总览的船舶焦点 + 右侧摘要布局；展示当前页选中预约，不宣称实时航运状态。
import PortIcon from './PortIcon.vue'
import type { AppointmentItem } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/port-scene.png'
defineProps<{ item: AppointmentItem; canCycle: boolean }>()
const emit = defineEmits<{ cycle: [step: number]; details: [] }>()
</script>

<template>
  <div class="overview-focus">
    <section class="vessel-spotlight glass-panel" aria-labelledby="spotlight-title">
      <img class="spotlight-photo" :src="vesselImage" alt="" />
      <div class="spotlight-wash"></div>
      <div class="spotlight-top"><h2 id="spotlight-title" class="sr-only">当前预约</h2><div class="cycle-actions">
        <button class="icon-button" aria-label="上一艘船舶" :disabled="!canCycle" @click="emit('cycle', -1)"><PortIcon name="left" :size="18" /></button>
        <button class="icon-button" aria-label="下一艘船舶" :disabled="!canCycle" @click="emit('cycle', 1)"><PortIcon name="right" :size="18" /></button>
      </div></div>
      <div class="spotlight-identity"><h3>{{ item.shipName }}</h3><p>{{ item.appointmentNo }}</p><span class="status-badge" :class="statusClass(item.appointmentStatusCode)"><i></i>{{ statusLabel(item.appointmentStatusCode) }}</span></div>

      <div class="request-window">
        <div class="window-track"><i></i><span><PortIcon name="ship" :size="19" /></span><i></i></div>
        <div class="window-labels"><div><strong>申请到港</strong><time>{{ formatDate(item.requestedArrivalAt) }}</time></div><div><strong>到离港时间窗</strong></div><div><strong>申请离港</strong><time>{{ formatDate(item.requestedDepartureAt) }}</time></div></div>
      </div>
    </section>
    <aside class="glass-panel readiness-panel" aria-labelledby="readiness-title">
      <div class="panel-heading"><h2 id="readiness-title">预约信息</h2><PortIcon name="info" :size="18" /></div>
      <div class="readiness-row"><PortIcon name="ship" :size="26" /><div><strong>船舶识别</strong><small>IMO {{ item.imoNo ?? '—' }}</small></div></div>
      <div class="readiness-row"><PortIcon name="calendar" :size="26" /><div><strong>申请到港</strong><small>{{ formatDate(item.requestedArrivalAt) }}</small></div></div>
      <div class="readiness-row"><PortIcon name="document" :size="26" /><div><strong>审核记录</strong><small>{{ item.reviewerName ?? '尚无审核信息' }}</small></div></div>
      <div class="readiness-status" :class="statusClass(item.appointmentStatusCode)"><PortIcon :name="item.appointmentStatusCode === 'REJECTED' ? 'reset' : item.reviewedAt ? 'check' : 'clock'" :size="20" /><span>{{ statusLabel(item.appointmentStatusCode) }}</span></div>
      <button class="button button-primary" @click="emit('details')">查看预约详情<PortIcon name="arrow" :size="19" /></button>
    </aside>
  </div>
</template>
