<script setup>
import { onMounted, ref } from 'vue'
import { api, clearSession } from '../../api'
import { useRouter } from 'vue-router'

const router = useRouter()
const rows = ref([])
const error = ref('')

onMounted(async () => {
  const res = await api('/api/audit')
  if (!res.ok) {
    error.value = `加载审计失败 (${res.status})`
    return
  }
  rows.value = await res.json()
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
        <div class="brand">业务审计</div>
        <div class="muted">记录操作者行为（与产品谱系分离）</div>
      </div>
      <div class="nav">
        <router-link to="/plan">计划端</router-link>
        <button class="btn ghost" type="button" @click="logout">退出</button>
      </div>
    </div>
    <div class="card">
      <p v-if="error" class="error">{{ error }}</p>
      <table v-else class="table">
        <thead>
          <tr>
            <th>时间</th>
            <th>动作</th>
            <th>操作者</th>
            <th>对象</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(r, i) in rows" :key="i">
            <td>{{ new Date(r.occurredAt).toLocaleString() }}</td>
            <td>{{ r.action }}</td>
            <td>{{ r.actorUserName }}</td>
            <td>{{ r.subjectType }} {{ r.subjectId }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!error && rows.length === 0" class="muted">暂无审计记录</p>
    </div>
  </div>
</template>
