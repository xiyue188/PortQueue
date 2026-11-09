<script setup lang="ts">
// 账号入口与登录表单；原生 dialog 提供模态焦点约束，姓名角色来自同源 Cookie 会话。
import { nextTick, onMounted, ref } from 'vue'
import PortIcon from './PortIcon.vue'
import { useSession } from '../composables/useSession'
const { session, busy, error, refresh, login, logout } = useSession()
const dialog = ref<HTMLDialogElement>()
const usernameInput = ref<HTMLInputElement>()
const username = ref('')
const password = ref('')
onMounted(() => void refresh())
async function open() {
  dialog.value?.showModal()
  await refresh()
  await nextTick(); if (dialog.value?.open) usernameInput.value?.focus()
}
function close() { password.value = ''; dialog.value?.close() }
async function submit() {
  const succeeded = await login(username.value.trim(), password.value)
  password.value = ''
  if (succeeded) close()
}
</script>

<template>
  <div class="workspace-account">
    <span class="account-avatar"><PortIcon name="user" :size="21" /></span>
    <div class="account-identity"><strong>{{ session?.user?.displayName ?? (error ? '账号服务不可用' : '未登录') }}</strong><small v-if="session?.user">{{ session.user.roleName }}</small></div>
    <button v-if="session?.user" class="text-action account-action" :disabled="busy" @click="logout">退出</button>
    <button v-else class="text-action account-action" :disabled="busy" @click="open">登录</button>
  </div>
  <p v-if="error && !dialog?.open" class="account-error" role="alert">{{ error }} <button class="text-action" :disabled="busy" @click="refresh">重试</button></p>
  <Teleport to="body">
    <dialog ref="dialog" class="login-dialog" aria-labelledby="login-title" @cancel.prevent="close" @close="password = ''">
      <div class="panel-heading"><h2 id="login-title">登录 PortQueue</h2><button type="button" class="icon-button" aria-label="关闭登录" @click="close"><PortIcon name="reset" :size="18" /></button></div>
      <form @submit.prevent="submit">
        <label for="login-username">用户名</label><input id="login-username" ref="usernameInput" v-model="username" required maxlength="50" autocomplete="username" :disabled="busy" />
        <label for="login-password">密码</label><input id="login-password" v-model="password" type="password" required maxlength="128" autocomplete="current-password" :disabled="busy" />
        <p v-if="error" role="alert" class="login-error">{{ error }} <button type="button" class="text-action" :disabled="busy" @click="refresh">刷新会话</button></p>
        <button class="button button-primary" type="submit" :disabled="busy || !session">{{ busy ? '正在处理…' : '登录' }}</button>
      </form>
    </dialog>
  </Teleport>
</template>
