<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, clearSession, getUser } from '../../api'

const router = useRouter()
const user = getUser()
const me = ref(null)
const planPing = ref(null)
const error = ref('')

onMounted(async () => {
  const res = await api('/api/me')
  if (!res.ok) {
    error.value = '无法加载当前用户'
    return
  }
  me.value = await res.json()
  if (me.value.role === 'Planner') {
    const ping = await api('/api/plan/ping')
    planPing.value = ping.ok ? await ping.json() : null
  }
})

function logout() {
  clearSession()
  router.push('/login')
}
</script>

<template>
  <div class="shell">
    <div class="topbar">
      <div>
        <div class="brand">计划端</div>
        <div class="muted">工单 / 主数据 / 追溯（后续票）· 当前为脚手架</div>
      </div>
      <div class="nav">
        <router-link to="/plan/master-data">执行主数据</router-link>
        <router-link to="/plan/work-orders">生产工单</router-link>
        <router-link to="/plan/audit">业务审计</router-link>
        <router-link to="/station">过站台</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>

    <div class="card">
      <p v-if="error" class="error">{{ error }}</p>
      <template v-else-if="me">
        <p>
          当前用户：<strong>{{ me.displayName }}</strong>
          <span class="pill" style="margin-left: 0.5rem">{{ me.role }}</span>
        </p>
        <p class="muted">账号 {{ me.userName }} · 服务端鉴权已生效</p>
        <p v-if="planPing" class="muted">计划区 ping：{{ JSON.stringify(planPing) }}</p>
        <p v-else-if="user?.role !== 'Planner'" class="muted">
          非计划员不调用 /api/plan/ping（操作工将得到 403）。
        </p>
      </template>
      <p v-else class="muted">加载中…</p>
    </div>
  </div>
</template>
