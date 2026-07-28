<script setup>
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, clearSession, getUser } from '../../api'

const router = useRouter()
const user = getUser()
const scan = ref('')
const ping = ref(null)
const message = ref('工位过站台脚手架：后续票接入防跳站与 SN 过站。')

onMounted(async () => {
  const res = await api('/api/station/ping')
  if (res.ok) ping.value = await res.json()
})

function onScan() {
  message.value = scan.value
    ? `已接收扫码（演示）：${scan.value} — 业务过站尚未实现（票 04）`
    : '请扫描或输入条码'
  scan.value = ''
}

function logout() {
  clearSession()
  router.push('/login')
}
</script>

<template>
  <div class="shell">
    <div class="topbar">
      <div>
        <div class="brand">过站台</div>
        <div class="muted">{{ user?.displayName }} · {{ user?.role }}</div>
      </div>
      <div class="nav">
        <router-link v-if="user?.role !== 'Operator'" to="/plan">计划端</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>

    <div class="card station-hero">
      <h1>当前工位（待配置）</h1>
      <p class="muted">大字 · 扫码优先 · 与管理后台分离布局</p>
      <form @submit.prevent="onScan">
        <label class="field">
          <span>扫描 SN / 条码</span>
          <input
            v-model="scan"
            class="station-input"
            autofocus
            placeholder="扫码枪输入后回车"
          />
        </label>
        <button class="btn primary" type="submit" style="font-size: 1.1rem; padding: 0.85rem 1.4rem">
          确认
        </button>
      </form>
      <p>{{ message }}</p>
      <p v-if="ping" class="muted">station ping：{{ JSON.stringify(ping) }}</p>
    </div>
  </div>
</template>
