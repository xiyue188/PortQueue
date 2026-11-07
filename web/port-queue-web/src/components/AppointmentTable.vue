<script setup lang="ts">
// 总览与预约页共用一张真实结果表；选择按钮可用键盘操作，行点击只提供快捷方式。
import PortIcon from './PortIcon.vue'
import type { AppointmentItem } from '../types/appointment'
import { formatDate, statusClass, statusLabel } from '../utils/appointmentPresentation'
import vesselImage from '../assets/vessel-reference.png'

defineProps<{ rows: AppointmentItem[]; selectedId?: string; compact?: boolean }>()
const emit = defineEmits<{ select: [item: AppointmentItem] }>()
</script>

<template>
  <div class="table-scroll" tabindex="0" role="region" aria-label="预约记录表，可横向滚动">
    <table class="appointment-table" :class="{ compact }">
      <caption class="sr-only">按申请到港时间和预约主键倒序排列，选择船舶可查看预约详情</caption>
      <thead><tr>
        <th scope="col">船舶 / 预约编号</th><th scope="col">申请到港 <span>UTC+8</span></th>
        <th scope="col">预约状态</th><th scope="col"><span class="sr-only">选择记录</span></th>
      </tr></thead>
      <tbody>
        <tr v-for="row in rows" :key="row.appointmentId" :class="{ selected: row.appointmentId === selectedId }" @click="emit('select', row)">
          <td><button class="vessel-row" :aria-pressed="row.appointmentId === selectedId" :aria-label="`查看 ${row.appointmentNo} 预约详情`" @click.stop="emit('select', row)">
            <img :src="vesselImage" alt="" width="54" height="34" />
            <span><strong>{{ row.shipName }}</strong><small>{{ row.appointmentNo }}</small></span>
          </button></td>
          <td class="tabular">{{ formatDate(row.requestedArrivalAt) }}</td>
          <td><span class="status-text" :class="statusClass(row.appointmentStatusCode)"><i></i>{{ statusLabel(row.appointmentStatusCode) }}</span></td>
          <td><PortIcon name="right" :size="16" /></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
