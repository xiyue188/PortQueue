<script setup lang="ts">
// 默认展示已联调的预约页面；旧总览明确作为原型保留，按需加载。
import { defineAsyncComponent, ref } from 'vue'
import AppointmentListView from './views/AppointmentListView.vue'

const activeView = ref<'appointments' | 'prototype'>('appointments')
const OverviewPrototype = defineAsyncComponent(() => import('./views/OverviewPrototype.vue'))
</script>

<template>
  <a class="skip-link" href="#main-content">跳至主要内容</a>
  <header class="workspace-header">
    <div class="workspace-brand"><strong>PortQueue</strong><span>港口进港协同管理</span></div>
    <nav aria-label="工作区导航">
      <button type="button" :aria-current="activeView === 'appointments' ? 'page' : undefined" @click="activeView = 'appointments'">进港预约</button>
      <button type="button" :aria-current="activeView === 'prototype' ? 'page' : undefined" @click="activeView = 'prototype'">总览原型（静态）</button>
    </nav>
    <span class="workspace-context">课程设计演示</span>
  </header>
  <AppointmentListView v-if="activeView === 'appointments'" />
  <main v-else id="main-content" class="prototype-container">
    <p class="prototype-notice">总览原型 · 此页为静态演示，真实查询请进入“进港预约”。</p>
    <OverviewPrototype />
  </main>
</template>
