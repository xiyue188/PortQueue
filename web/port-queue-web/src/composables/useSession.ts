// 工作区共享真实会话；区分输入/凭据/连接错误，公开演示选项也由后端核实。
import { onScopeDispose, ref } from 'vue'
export interface SessionIdentity { userId: string; username: string; displayName: string; roleCode: string; roleName: string }
interface SessionResponse { user: SessionIdentity | null; permissions: { canRegisterAppointment: boolean; canReviewAppointment: boolean }; csrfToken: string }
export interface AccountOption { username: string; displayName: string; roleCode: string; roleName: string }
function valid(value: unknown): value is SessionResponse {
  if (!value || typeof value !== 'object') return false
  const data = value as SessionResponse
  return typeof data.csrfToken === 'string' && !!data.csrfToken && !!data.permissions
    && typeof data.permissions.canRegisterAppointment === 'boolean' && typeof data.permissions.canReviewAppointment === 'boolean'
    && (data.user === null || (!!data.user && /^\d+$/.test(data.user.userId) && typeof data.user.username === 'string'
      && typeof data.user.displayName === 'string' && typeof data.user.roleName === 'string'
      && ['ADMIN', 'DISPATCHER', 'REVIEWER', 'RECORDER'].includes(data.user.roleCode)))
}
export function useSession() {
  const session = ref<SessionResponse | null>(null)
  const busy = ref(false), error = ref(''), errorKind = ref('')
  const options = ref<AccountOption[]>([]), defaultPassword = ref('')
  let controller: AbortController | undefined
  let optionsController: AbortController | undefined
  let sequence = 0
  async function loadOptions() {
    optionsController?.abort()
    const request = new AbortController(); optionsController = request
    const timer = setTimeout(() => request.abort(), 10_000)
    try {
      const response = await fetch('/api/session/options', { signal: request.signal })
      const data = await response.json() as { accounts?: AccountOption[]; defaultPassword?: string }
      if (optionsController !== request) return
      options.value = response.ok && Array.isArray(data.accounts) && data.accounts.every(a => a && ['username', 'displayName', 'roleCode', 'roleName'].every(k => typeof a[k as keyof AccountOption] === 'string')) ? data.accounts : []
      defaultPassword.value = options.value.length && typeof data.defaultPassword === 'string' ? data.defaultPassword : ''
    } catch { if (optionsController === request) { options.value = []; defaultPassword.value = '' } }
    finally { clearTimeout(timer) }
  }
  function clearError() { error.value = ''; errorKind.value = '' }
  async function perform(path = '', body?: object) {
    controller?.abort()
    const request = new AbortController(); controller = request
    const current = ++sequence
    busy.value = true; clearError()
    const timeout = setTimeout(() => request.abort(), 10_000)
    try {
      const response = await fetch(`/api/session${path}`, {
        method: body ? 'POST' : 'GET', credentials: 'same-origin', signal: request.signal,
        headers: body ? { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.value?.csrfToken ?? '' } : { Accept: 'application/json' },
        body: body ? JSON.stringify(body) : undefined,
      })
      let data: unknown
      try { data = await response.json() } catch { throw new Error('账号服务返回异常，请稍后重试。') }
      if (current !== sequence) return false
      if (!response.ok) {
        const problem = data as { code?: string; errors?: Record<string, string[]> }
        errorKind.value = problem.code ?? (response.status >= 500 ? 'connection' : 'input')
        error.value = problem.code === 'INVALID_CREDENTIALS' ? '用户名或密码错误，或账号不可用。'
          : problem.code === 'INVALID_LOGIN_INPUT' ? Object.values(problem.errors ?? {}).flat()[0] ?? '请填写有效的用户名和密码。'
          : problem.code === 'LOGIN_RATE_LIMITED' ? '登录尝试过于频繁，请一分钟后重试。'
          : problem.code === 'INVALID_CSRF_TOKEN' ? '会话已变化，请刷新会话后重试。'
          : response.status >= 500 ? '无法连接账号服务，请检查后端和数据库后重试。' : '登录内容无效，请检查后重试。'
        if (!body) session.value = null
        return false
      }
      if (!valid(data)) throw new Error('账号响应异常，请重试。')
      session.value = data
      return true
    } catch (failure) {
      if (current === sequence) {
        errorKind.value = 'connection'
        error.value = failure instanceof Error && failure.name === 'AbortError' ? '账号服务响应超时，请重试。' : '无法连接账号服务，请检查后端和数据库后重试。'
        if (!body) session.value = null
      }
      return false
    } finally { clearTimeout(timeout); if (current === sequence) busy.value = false }
  }
  onScopeDispose(() => { sequence++; controller?.abort(); optionsController?.abort() })
  return { session, busy, error, errorKind, options, defaultPassword, loadOptions, clearError,
    refresh: () => perform(), login: (username: string, password: string) => perform('/login', { username, password }), logout: () => perform('/logout', {}) }
}
export type SessionClient = ReturnType<typeof useSession>
