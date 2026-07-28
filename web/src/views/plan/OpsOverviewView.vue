<script setup>
/**
 * 经营总览占位（票 03 接真数）。壳内首页。
 */
import { getUser } from '../../api'

const user = getUser()
</script>

<template>
  <div class="card">
    <p>
      欢迎，<strong>{{ user?.displayName }}</strong>
      <span class="pill" style="margin-left: 0.5rem">{{ user?.role }}</span>
    </p>
    <p class="muted">
      此处为<strong>经营总览</strong>入口（二期票 03 将展示：在制分布、今日入库/报废、隔离待处理、工单进度）。
    </p>
    <div class="overview-grid">
      <div class="overview-tile muted">在制</div>
      <div class="overview-tile muted">今日合格入库</div>
      <div class="overview-tile muted">今日报废</div>
      <div class="overview-tile muted">隔离待处理</div>
      <div class="overview-tile muted">工单进行中 / 未开工</div>
    </div>
    <p class="muted" style="margin-top: 1rem; font-size: 0.85rem">
      请从左侧进入「生产执行」「质量异常」「物料库存」「系统」开展日常工作。
    </p>
  </div>
</template>

<style scoped>
.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 0.75rem;
  margin-top: 1rem;
}
.overview-tile {
  border: 1px dashed rgba(148, 163, 184, 0.35);
  border-radius: 12px;
  padding: 1.25rem 0.75rem;
  text-align: center;
  font-size: 0.9rem;
}
</style>
