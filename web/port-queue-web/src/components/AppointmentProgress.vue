<script setup lang="ts">
// 场景图承载预约办理状态；节点色与业务状态一致，不推断船舶位置或实际航程。
import { computed } from 'vue'
import type { AppointmentItem } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import PortIcon from './PortIcon.vue'
import portImage from '../assets/port-scene.png'
const props = defineProps<{ item: AppointmentItem }>()
const ended = computed(() => ['REJECTED', 'CANCELLED'].includes(props.item.appointmentStatusCode))
const nodes = computed(() => props.item.appointmentStatusCode === 'CANCELLED' ? ['预约建立', '已撤销'] : ended.value ? ['草稿', '待审核', '已驳回'] : ['草稿', '待审核', '已批准', '已完成'])
const current = computed(() => ended.value ? nodes.value.length - 1 : ['DRAFT', 'PENDING', 'APPROVED', 'COMPLETED'].indexOf(props.item.appointmentStatusCode))
// 撤销记录的先前步骤不能单靠终态推导；轨道不把未经证实的节点涂为已完成。
const reached = (i: number) => props.item.appointmentStatusCode === 'CANCELLED' ? i === current.value : i <= current.value
</script>
<template>
  <section class="appointment-progress" :class="{ 'is-ended': ended }" :data-status="item.appointmentStatusCode" aria-label="预约办理进度">
    <img :src="portImage" alt=""/><div class="progress-shade"></div>
    <div class="progress-identity"><h3>{{ item.shipName }}</h3><p>{{ item.appointmentNo }}</p><span class="status-badge" :class="statusClass(item.appointmentStatusCode)"><i></i>{{ statusLabel(item.appointmentStatusCode) }}</span></div>
    <div class="progress-content"><h4>预约办理进度</h4>
      <ol class="progress-track" :style="{ '--steps': nodes.length }"><li v-for="(node, i) in nodes" :key="node" :class="{ reached: reached(i), current: i === current }" :aria-current="i === current ? 'step' : undefined"><span class="progress-node"><PortIcon v-if="i === current" :name="ended ? 'reset' : 'ship'" :size="19" /></span><strong>{{ node }}</strong></li></ol>
      <div class="progress-times"><div><span>申请到港</span><time>{{ formatDate(item.requestedArrivalAt) }}</time></div><div><span>申请离港</span><time>{{ formatDate(item.requestedDepartureAt) }}</time></div></div>
    </div>
  </section>
</template>
