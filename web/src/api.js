/**
 * 前端 API 薄封装。
 * - 开发：VITE_API_BASE 指向 ASP.NET（默认 http://localhost:5101）
 * - Compose：nginx 反代 /api，可留空走同源
 * Token 存在 localStorage；401 时清会话（需路由守卫配合跳转登录）。
 */
const API_BASE = import.meta.env.VITE_API_BASE || ''

export function getToken() {
  return localStorage.getItem('mes_token') || ''
}

export function setSession({ accessToken, userName, role, displayName }) {
  localStorage.setItem('mes_token', accessToken)
  localStorage.setItem('mes_user', JSON.stringify({ userName, role, displayName }))
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
  })
  return data
}
