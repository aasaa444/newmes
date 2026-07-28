<script setup>
/** 集成页（壳内）：ERP 出站 + 业务审计 */
import { onMounted, ref } from 'vue'
import { api } from '../../api'

const audits = ref([])
const outbox = ref([])
const error = ref('')
const selectedPayload = ref('')

onMounted(async () => {
  const [a, o] = await Promise.all([
    api('/api/audit'),
    api('/api/erp/outbox'),
  ])
  if (!a.ok || !o.ok) {
    error.value = '加载集成/审计失败'
    return
  }
  audits.value = await a.json()
  outbox.value = await o.json()
})

function showPayload(row) {
  selectedPayload.value = row.payloadJson
}
</script>

<template>
  <div>
    <p v-if="error" class="error">{{ error }}</p>

    <div class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">ERP 出站（模拟）</h3>
      <table class="table">
        <thead>
          <tr>
            <th>类型</th>
            <th>业务键</th>
            <th>状态</th>
            <th>时间</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="m in outbox" :key="m.id">
            <td>{{ m.messageType }}</td>
            <td>{{ m.businessKey }}</td>
            <td><span class="pill">{{ m.status }}</span></td>
            <td class="muted">{{ new Date(m.createdAt).toLocaleString() }}</td>
            <td><button class="btn ghost" type="button" @click="showPayload(m)">报文</button></td>
          </tr>
        </tbody>
      </table>
      <pre
        v-if="selectedPayload"
        style="
          margin-top: 1rem;
          padding: 0.75rem;
          background: #020617;
          border-radius: 8px;
          overflow: auto;
          max-height: 240px;
          font-size: 0.8rem;
        "
      >{{ selectedPayload }}</pre>
    </div>

    <div class="card">
      <h3 style="margin-top: 0">业务审计（最近）</h3>
      <table class="table">
        <thead>
          <tr>
            <th>动作</th>
            <th>操作者</th>
            <th>对象</th>
            <th>时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(a, i) in audits.slice(0, 40)" :key="i">
            <td>{{ a.action }}</td>
            <td>{{ a.actorUserName }}</td>
            <td class="muted">{{ a.subjectType }} {{ a.subjectId }}</td>
            <td class="muted">{{ new Date(a.occurredAt).toLocaleString() }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
