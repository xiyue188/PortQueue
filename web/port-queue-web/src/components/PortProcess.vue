<script setup lang="ts">
// 业务路径说明，不把静态流程画成实时船位；业务操作统一从主图入口进入。
import PortIcon from './PortIcon.vue'
</script>

<template>
  <section class="glass-panel port-process" aria-labelledby="port-process-title">
    <div class="panel-heading"><h2 id="port-process-title">进港流程</h2></div>
    <div class="process-route" aria-label="预约审核通过后，可在锚地等待或直接安排泊位；确认泊位后进行进港、靠泊及离泊作业。">
      <div class="direct-route" aria-hidden="true"><span>无需等泊，直接安排</span></div>
      <ol class="process-steps">
        <li class="process-entry"><span class="process-symbol"><PortIcon name="document" :size="25" /></span><h3>预约审核</h3><p>登记 · 提交 · 审核</p></li>
        <li><span class="process-symbol"><PortIcon name="anchor" :size="25" /></span><h3>锚地等泊</h3><p>按需进入队列</p><small>待接入</small></li>
        <li><span class="process-symbol"><PortIcon name="berth" :size="25" /></span><h3>泊位安排</h3><p>选择泊位与时间</p><small>待接入</small></li>
        <li><span class="process-symbol"><PortIcon name="ship" :size="25" /></span><h3>靠离泊作业</h3><p>记录实际时间</p><small>待接入</small></li>
      </ol>
    </div>
  </section>
</template>
