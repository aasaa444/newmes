<script setup>
/** 主数据浏览（壳内：物料 / BOM / 工艺路线 / 工位） */
import { onMounted, ref } from 'vue'
import { api } from '../../api'

const materials = ref([])
const boms = ref([])
const routes = ref([])
const stations = ref([])
const error = ref('')
const loading = ref(true)

onMounted(async () => {
  try {
    const [m, b, r, s] = await Promise.all([
      api('/api/materials'),
      api('/api/boms'),
      api('/api/process-routes'),
      api('/api/work-stations'),
    ])
    if (!m.ok || !b.ok || !r.ok || !s.ok) {
      error.value = '加载主数据失败（需要已登录且 API 可用）'
      return
    }
    materials.value = await m.json()
    boms.value = await b.json()
    routes.value = await r.json()
    stations.value = await s.json()
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="muted">加载中…</p>
    <template v-else>
      <div class="card" style="margin-bottom: 1rem">
        <h3 style="margin-top: 0">物料</h3>
        <table class="table">
          <thead>
            <tr>
              <th>编码</th>
              <th>名称</th>
              <th>成品</th>
              <th>关键件</th>
              <th>采 SN</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in materials" :key="row.id">
              <td>{{ row.code }}</td>
              <td>{{ row.name }}</td>
              <td>{{ row.isFinishedGood ? '是' : '' }}</td>
              <td>{{ row.isKeyComponent ? '是' : '' }}</td>
              <td>{{ row.requiresSerialNumber ? '是' : '' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="card" style="margin-bottom: 1rem">
        <h3 style="margin-top: 0">BOM</h3>
        <div v-for="bom in boms" :key="bom.id" style="margin-bottom: 0.75rem">
          <strong>{{ bom.finishedMaterialCode }}</strong>
          <span class="muted"> v{{ bom.version }}</span>
          <ul>
            <li v-for="(line, i) in bom.lines" :key="i">
              {{ line.componentCode }} × {{ line.quantityPer }}
            </li>
          </ul>
        </div>
      </div>

      <div class="card" style="margin-bottom: 1rem">
        <h3 style="margin-top: 0">工艺路线</h3>
        <div v-for="rt in routes" :key="rt.id">
          <strong>{{ rt.code }}</strong> — {{ rt.name }}
          <ol>
            <li v-for="st in rt.steps" :key="st.id">{{ st.sequence }} {{ st.code }} {{ st.name }}</li>
          </ol>
        </div>
      </div>

      <div class="card">
        <h3 style="margin-top: 0">工位</h3>
        <table class="table">
          <thead>
            <tr>
              <th>编码</th>
              <th>名称</th>
              <th>绑定工序</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="st in stations" :key="st.id">
              <td>{{ st.code }}</td>
              <td>{{ st.name }}</td>
              <td>{{ st.boundProcessStepCode }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
