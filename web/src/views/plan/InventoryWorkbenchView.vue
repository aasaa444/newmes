<script setup>
/** 库存工作台 — 线边 + 成品只读（票 07 可再增强收料 UI） */
import { onMounted, ref } from 'vue'
import { api } from '../../api'

const lineSide = ref([])
const finished = ref([])
const error = ref('')

onMounted(async () => {
  const [a, b] = await Promise.all([
    api('/api/inventory/line-side'),
    api('/api/inventory/finished-goods'),
  ])
  if (!a.ok || !b.ok) {
    error.value = '加载库存失败'
    return
  }
  lineSide.value = await a.json()
  finished.value = await b.json()
})
</script>

<template>
  <div>
    <p v-if="error" class="error">{{ error }}</p>
    <div class="card" style="margin-bottom: 1rem">
      <h3 style="margin-top: 0">线边可用</h3>
      <table class="table">
        <thead>
          <tr>
            <th>物料</th>
            <th>名称</th>
            <th>可用</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in lineSide" :key="r.materialId">
            <td>{{ r.materialCode }}</td>
            <td>{{ r.materialName }}</td>
            <td>{{ r.quantityOnHand }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="card">
      <h3 style="margin-top: 0">成品可用</h3>
      <table class="table">
        <thead>
          <tr>
            <th>物料</th>
            <th>名称</th>
            <th>可用</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in finished" :key="r.materialId">
            <td>{{ r.materialCode }}</td>
            <td>{{ r.materialName }}</td>
            <td>{{ r.quantityOnHand }}</td>
          </tr>
        </tbody>
      </table>
      <p v-if="!finished.length" class="muted">暂无成品入库</p>
    </div>
  </div>
</template>
