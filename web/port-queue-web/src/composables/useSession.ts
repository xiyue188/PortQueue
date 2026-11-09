// 会话只由后端提供，CSRF 令牌仅保存在内存；密码不进入浏览器存储。
import { onScopeDispose, ref } from 'vue'
export interface SessionIdentity { userId: string; username: string; displayName: string; roleCode: string; roleName: string }
interface SessionResponse { user: SessionIdentity | null; permissions: { canRegisterAppointment: boolean; canReviewAppointment: boolean }; csrfToken: string }
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
  const busy = ref(false)
  const error = ref('')
  let controller: AbortController | undefined
  let sequence = 0
  async function perform(path = '', body?: object) {
    controller?.abort()
    const request = new AbortController(); controller = request
    const current = ++sequence
    busy.value = true; error.value = ''
    const timeout = setTimeout(() => request.abort(), 10_000)
    try {
      const response = await fetch(`/api/session${path}`, {
        method: body ? 'POST' : 'GET', credentials: 'same-origin', signal: request.signal,
        headers: body ? { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.value?.csrfToken ?? '' } : { Accept: 'application/json' },
        body: body ? JSON.stringify(body) : undefined,
      })
      const data: unknown = await response.json()
      if (!response.ok) {
        const code = (data as { code?: string }).code
        throw new Error(code === 'INVALID_CREDENTIALS' ? '用户名或密码错误，或账号不可用。'
          : code === 'LOGIN_RATE_LIMITED' ? '登录尝试过于频繁，请一分钟后重试。'
          : code === 'INVALID_CSRF_TOKEN' ? '会话已变化，请刷新会话后重试。' : '暂时无法连接账号服务，请重试。')
      }
      if (!valid(data)) throw new Error('账号响应异常，请重试。')
      if (current === sequence) session.value = data
      return true
    } catch (failure) {
      if (current === sequence) {
        error.value = failure instanceof Error && failure.name !== 'AbortError' ? failure.message : '账号服务响应超时，请重试。'
        if (!body) session.value = null // 身份重读失败时不能继续展示旧授权。
      }
      return false
    } finally {
      clearTimeout(timeout)
      if (current === sequence) busy.value = false
    }
  }
  onScopeDispose(() => { sequence++; controller?.abort() })
  return { session, busy, error, refresh: () => perform(), login: (username: string, password: string) => perform('/login', { username, password }), logout: () => perform('/logout', {}) }
}
