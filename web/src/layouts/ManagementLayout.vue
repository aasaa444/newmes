<script setup>
/**
 * 管理端经典壳：左侧五菜单 + 顶栏 + 内容区（ADR-0009）。
 * 菜单来自 GET /api/nav/management，按角色裁剪。
 */
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, clearSession, getUser } from '../api'

const route = useRoute()
const router = useRouter()
const user = getUser()
const menus = ref([])
const navError = ref('')
const expanded = ref({})

const roleLabel = {
  Planner: '计划员',
  Leader: '班组长',
  Owner: '经营者',
  Operator: '操作工',
}

async function loadNav() {
  navError.value = ''
  const res = await api('/api/nav/management')
  if (!res.ok) {
    navError.value = '菜单加载失败'
    menus.value = []
    return
  }
  const data = await res.json()
  menus.value = data.menus || []
  // 默认展开含当前路由的分组
  for (const m of menus.value) {
    if (m.children?.length) {
      const hit = m.children.some((c) => route.path === c.path || route.path.startsWith(c.path + '/'))
      if (hit || route.path.startsWith(m.path)) expanded.value[m.key] = true
    }
  }
}

onMounted(loadNav)
watch(() => route.path, loadNav)

const breadcrumb = computed(() => {
  const title = route.meta.title || '管理端'
  return ['管理端', title]
})

function isActive(path) {
  if (!path) return false
  if (path === '/plan') return route.path === '/plan'
  return route.path === path || route.path.startsWith(path + '/')
}

function toggle(key) {
  expanded.value[key] = !expanded.value[key]
}

function logout() {
  clearSession()
  router.push('/login')
}

function goStation() {
  if (user?.canAccessStationShell === false) return
  router.push('/station')
}
</script>

<template>
  <div class="mgmt-root">
    <aside class="mgmt-sidebar">
      <div class="mgmt-brand">
        <div class="brand">无名 MES</div>
        <div class="muted" style="font-size: 0.75rem">单厂试点 · 管理端</div>
      </div>
      <p v-if="navError" class="error" style="padding: 0 0.75rem">{{ navError }}</p>
      <nav class="mgmt-nav">
        <template v-for="m in menus" :key="m.key">
          <div v-if="m.children?.length" class="nav-group">
            <button type="button" class="nav-group-title" @click="toggle(m.key)">
              <span>{{ m.title }}</span>
              <span class="muted">{{ expanded[m.key] ? '▾' : '▸' }}</span>
            </button>
            <div v-show="expanded[m.key]" class="nav-children">
              <router-link
                v-for="c in m.children"
                :key="c.key"
                :to="c.path"
                class="nav-link"
                :class="{ active: isActive(c.path) }"
              >
                {{ c.title }}
              </router-link>
            </div>
          </div>
          <router-link
            v-else
            :to="m.path"
            class="nav-link"
            :class="{ active: isActive(m.path) }"
          >
            {{ m.title }}
          </router-link>
        </template>
      </nav>
    </aside>

    <div class="mgmt-main">
      <header class="mgmt-topbar">
        <div class="mgmt-crumb">
          <span v-for="(c, i) in breadcrumb" :key="i">
            <span v-if="i"> / </span>{{ c }}
          </span>
        </div>
        <div class="mgmt-top-actions">
          <span class="pill">{{ roleLabel[user?.role] || user?.role }}</span>
          <span class="muted">{{ user?.displayName || user?.userName }}</span>
          <button
            v-if="user?.canAccessStationShell !== false && user?.role !== 'Owner'"
            class="btn ghost"
            type="button"
            @click="goStation"
          >
            过站端
          </button>
          <button class="btn ghost" type="button" @click="logout">退出</button>
        </div>
      </header>
      <main class="mgmt-content">
        <h1 v-if="route.meta.title" class="page-title">{{ route.meta.title }}</h1>
        <p v-if="route.meta.subtitle" class="muted page-sub">{{ route.meta.subtitle }}</p>
        <router-view />
      </main>
    </div>
  </div>
</template>
