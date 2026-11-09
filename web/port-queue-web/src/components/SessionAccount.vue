<script setup lang="ts">
// 快捷选项只填真实凭据；所有登录仍经过服务端，不在浏览器切换权限。
import { nextTick, ref } from 'vue'
import PortIcon from './PortIcon.vue'
import type { AccountOption, SessionClient } from '../composables/useSession'
const props = defineProps<{ accountSession: SessionClient }>()
const { session, busy, error, errorKind, options, defaultPassword, refresh, loadOptions, login, logout, clearError } = props.accountSession
const dialog = ref<HTMLDialogElement>(), usernameInput = ref<HTMLInputElement>(), loginButton = ref<HTMLButtonElement>()
const username = ref(''), password = ref(''), showPassword = ref(false)
async function open() {
  dialog.value?.showModal()
  await Promise.all([refresh(), loadOptions()])
  await nextTick(); if (dialog.value?.open) usernameInput.value?.focus()
}
function close() { password.value = ''; showPassword.value = false; clearError(); dialog.value?.close() }
function choose(option: AccountOption) { username.value = option.username; password.value = defaultPassword.value; clearError(); loginButton.value?.focus() }
async function submit() {
  if (!/^[A-Za-z0-9._-]{1,50}$/.test(username.value.trim())) { error.value = '用户名只支持字母、数字、点、下划线和短横线。'; errorKind.value = 'input'; return }
  const succeeded = await login(username.value.trim(), password.value)
  password.value = ''
  if (succeeded) close()
}
</script>
<template>
  <div class="workspace-account">
    <span class="account-avatar"><PortIcon name="user" :size="21" /></span>
    <div class="account-identity"><strong>{{ session?.user?.displayName ?? '未登录' }}</strong><small v-if="session?.user">{{ session.user.roleName }}</small></div>
    <button v-if="session?.user" class="text-action account-action" :disabled="busy" @click="logout">退出</button>
    <button v-else class="text-action account-action" :disabled="busy" @click="open">登录</button>
  </div>
  <p v-if="error && !dialog?.open" class="account-error" role="alert">{{ error }} <button class="text-action" :disabled="busy" @click="refresh">重试</button></p>
  <Teleport to="body">
    <dialog ref="dialog" class="login-dialog" aria-labelledby="login-title" @cancel.prevent="close" @close="password = ''">
      <div class="panel-heading"><h2 id="login-title">登录 PortQueue</h2><button type="button" class="icon-button" aria-label="关闭登录" :disabled="busy" @click="close"><PortIcon name="reset" :size="18" /></button></div>
      <div v-if="options.length" class="account-options" aria-label="选择账号">
        <button v-for="option in options" :key="option.username" type="button" :disabled="busy" :aria-pressed="username === option.username" @click="choose(option)"><PortIcon name="user" :size="20"/><span><strong>{{ option.roleName }}</strong><small>{{ option.displayName }}</small></span></button>
      </div>
      <p v-if="options.length" class="initial-password">初始密码 <strong>{{ defaultPassword }}</strong></p>
      <form @submit.prevent="submit">
        <label for="login-username">用户名</label><input id="login-username" ref="usernameInput" v-model="username" required maxlength="50" autocomplete="username" :disabled="busy" @input="clearError" />
        <label for="login-password">密码</label><div class="password-field"><input id="login-password" v-model="password" :type="showPassword ? 'text' : 'password'" required maxlength="128" autocomplete="current-password" :disabled="busy" @input="clearError" /><button type="button" class="text-action" :aria-pressed="showPassword" @click="showPassword = !showPassword">{{ showPassword ? '隐藏' : '显示' }}</button></div>
        <p v-if="error" role="alert" class="login-error">{{ error }} <button v-if="errorKind === 'INVALID_CSRF_TOKEN' || errorKind === 'connection'" type="button" class="text-action" :disabled="busy" @click="refresh">刷新会话</button></p>
        <button ref="loginButton" class="button button-primary" type="submit" :disabled="busy || !session">{{ busy ? '正在登录…' : '登录' }}</button>
      </form>
    </dialog>
  </Teleport>
</template>
