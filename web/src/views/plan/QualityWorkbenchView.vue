<script setup>
/**
 * 质量异常工作台 — 票 06 完善；此处先挂隔离列表只读，证明菜单可达。
 */
import { onMounted, ref } from 'vue'
import { api } from '../../api'

const rows = ref([])
const error = ref('')
const loading = ref(true)

onMounted(async () => {
  try {
    const res = await api('/api/quality/isolated')
    if (!res.ok) {
      error.value = `加载隔离列表失败 (${res.status})`
      return
    }
    rows.value = await res.json()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="card">
    <p class="muted" style="margin-top: 0">
      异常工作台：隔离中列表（放行/报废完整交互见票 06）。可与过站端现场处置互补。
    </p>
    <p v-if="loading" class="muted">加载中…</p>
    <p v-else-if="error" class="error">{{ error }}</p>
    <table v-else class="table">
      <thead>
        <tr>
          <th>序列号</th>
          <th>工单</th>
          <th>当前工序</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(r, i) in rows" :key="i">
          <td>{{ r.serialNo }}</td>
          <td>{{ r.workOrderNo }}</td>
          <td>{{ r.currentStepCode || '—' }}</td>
        </tr>
      </tbody>
    </table>
    <p v-if="!loading && !error && !rows.length" class="muted">当前无隔离中的序列号</p>
  </div>
</template>
