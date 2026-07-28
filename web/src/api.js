/**
 * 前端 API 薄封装。
 * - 开发：VITE_API_BASE 指向 ASP.NET（默认 http://localhost:5101）
 * - Compose：nginx 反代 /api，可留空走同源
 * Token 存在 localStorage；401 时清会话（需路由守卫配合跳转登录）。
 * 二期：会话含 defaultShell / defaultPath / 能力位。
 */
const API_BASE = import.meta.env.VITE_API_BASE || ''

export function getToken() {
  return localStorage.getItem('mes_token') || ''
}

export function setSession(session) {
  localStorage.setItem('mes_token', session.accessToken)
  localStorage.setItem(
    'mes_user',
    JSON.stringify({
      userName: session.userName,
      role: session.role,
      displayName: session.displayName,
      defaultShell: session.defaultShell,
      defaultPath: session.defaultPath,
      canViewOpsOverview: session.canViewOpsOverview,
      canAccessManagementShell: session.canAccessManagementShell,
      canAccessStationShell: session.canAccessStationShell,
      canWriteExecution: session.canWriteExecution,
      canWriteStation: session.canWriteStation,
      canWriteMasterData: session.canWriteMasterData,
    })
  )
}

export function clearSession() {
  localStorage.removeItem('mes_token')
  localStorage.removeItem('mes_user')
}

export function getUser() {
  const raw = localStorage.getItem('mes_user')
  if (!raw) return null
  try {
    return JSON.parse(raw)
  } catch {
    return null
  }
}

/** 按角色/能力返回登录后应去的路径 */
export function resolveHomePath(user) {
  if (!user) return '/login'
  if (user.defaultPath) return user.defaultPath
  if (user.role === 'Operator') return '/station'
  if (user.role === 'Owner') return '/plan'
  if (user.role === 'Planner') return '/plan/work-orders'
  if (user.role === 'Leader') return '/plan/wip'
  return '/plan'
}

export async function api(path, options = {}) {
  const headers = {
    'Content-Type': 'application/json',
    ...(options.headers || {}),
  }
  const token = getToken()
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }
  const res = await fetch(`${API_BASE}${path}`, { ...options, headers })
  if (res.status === 401) {
    clearSession()
  }
  return res
}

export async function login(userName, password) {
  const res = await api('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, password }),
  })
  if (!res.ok) {
    throw new Error('登录失败：用户名或密码错误')
  }
  const data = await res.json()
  setSession({
    accessToken: data.accessToken,
    userName: data.userName,
    role: data.role,
    displayName: data.displayName,
    defaultShell: data.defaultShell,
    defaultPath: data.defaultPath,
    canViewOpsOverview: data.canViewOpsOverview,
    canAccessManagementShell: data.canAccessManagementShell,
    canAccessStationShell: data.canAccessStationShell,
    canWriteExecution: data.canWriteExecution,
    canWriteStation: data.canWriteStation,
    canWriteMasterData: data.canWriteMasterData,
  })
  return data
}
