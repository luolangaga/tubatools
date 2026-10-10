<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner feat-page">
      <header class="site-page-header feat-header">
        <h1>{{ t('features.title') }}</h1>
        <p>{{ t('features.subtitle') }}</p>
      </header>

      <!-- 概览数字 -->
      <section class="feat-stats">
        <div v-for="stat in stats" :key="stat.value" class="site-card feat-stat">
          <span class="feat-stat-icon" aria-hidden="true">{{ stat.icon }}</span>
          <div class="feat-stat-text">
            <div class="feat-stat-num">{{ stat.value }}</div>
            <div class="feat-stat-label">{{ t(stat.key) }}</div>
          </div>
        </div>
      </section>

      <!-- 分类筛选 -->
      <nav class="feat-filters" :aria-label="t('features.filter-label')">
        <button
          v-for="cat in filterCategories"
          :key="cat.id"
          type="button"
          class="feat-filter"
          :class="{ active: activeCat === cat.id }"
          @click="selectCategory(cat.id)">
          <span class="icon" aria-hidden="true">{{ cat.icon }}</span>
          <span>{{ t(`features.cat.${cat.id}`) }}</span>
          <span class="feat-filter-count">{{ cat.count }}</span>
        </button>
      </nav>

      <!-- 截图长廊 -->
      <section class="feat-grid">
        <button
          v-for="(item, index) in visibleFeatures"
          :key="item.id"
          type="button"
          class="site-card feat-card"
          @click="openViewer(index)">
          <span class="feat-card-shot">
            <img :src="item.image" :alt="t(`features.${item.id}.title`)" loading="lazy" decoding="async" />
          </span>
          <span class="feat-card-body">
            <span class="feat-card-cat">{{ t(`features.cat.${item.cat}`) }}</span>
            <span class="feat-card-title">{{ t(`features.${item.id}.title`) }}</span>
            <span class="feat-card-desc">{{ t(`features.${item.id}.desc`) }}</span>
            <span class="feat-tags">
              <span v-for="tag in tagsOf(item.id)" :key="tag" class="feat-tag">{{ tag }}</span>
            </span>
          </span>
        </button>
      </section>

      <p class="feat-empty" v-if="!visibleFeatures.length">{{ t('features.empty') }}</p>

      <SiteFooter />
    </div>

    <!-- 大图查看 -->
    <Teleport to="body">
      <div v-if="viewerIndex >= 0" class="feat-viewer">
        <div class="feat-viewer-stage" @click.self="closeViewer">
          <img :src="current.image" :alt="t(`features.${current.id}.title`)" />
        </div>
        <div class="feat-viewer-foot">
          <div class="feat-viewer-meta">
            <span class="feat-viewer-cat">{{ t(`features.cat.${current.cat}`) }}</span>
            <span class="feat-viewer-title">{{ t(`features.${current.id}.title`) }}</span>
            <span class="feat-viewer-desc">{{ t(`features.${current.id}.desc`) }}</span>
            <span class="feat-viewer-tags">
              <span v-for="tag in tagsOf(current.id)" :key="tag" class="feat-tag">{{ tag }}</span>
              <router-link v-if="current.doc" class="feat-viewer-doc" :to="current.doc" @click="closeViewer">
                {{ t('features.view-doc') }} &rsaquo;
              </router-link>
            </span>
          </div>
          <div class="feat-viewer-actions">
            <span class="feat-viewer-index">{{ viewerIndex + 1 }} / {{ visibleFeatures.length }}</span>
            <button type="button" class="feat-viewer-btn" :title="t('features.prev')" @click="step(-1)">
              <span class="icon" aria-hidden="true">&#xE76B;</span>
            </button>
            <button type="button" class="feat-viewer-btn" :title="t('features.next')" @click="step(1)">
              <span class="icon" aria-hidden="true">&#xE76C;</span>
            </button>
            <button type="button" class="feat-viewer-btn" :title="t('features.close')" @click="closeViewer">
              <span class="icon" aria-hidden="true">&#xE711;</span>
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </WinScrollViewer>
</template>

<script setup>
import { computed, onUnmounted, ref, watch } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import SiteFooter from '../components/SiteFooter.vue';
import { featureCategories, features } from '../services/features';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();

const stats = [
  { value: '134', key: 'features.stat.tools', icon: '\uE8F1' },
  { value: '47', key: 'features.stat.builtin', icon: '\uE90F' },
  { value: '26', key: 'features.stat.community', icon: '\uE902' },
  { value: 'x86 / x64 / ARM64', key: 'features.stat.arch', icon: '\uE950' }
];

const countOf = (id) => (id === 'all' ? features.length : features.filter((item) => item.cat === id).length);

const filterCategories = computed(() =>
  featureCategories.map((cat) => ({ ...cat, count: countOf(cat.id) }))
);

const activeCat = ref('all');
const selectCategory = (id) => {
  activeCat.value = id;
  closeViewer();
};

const visibleFeatures = computed(() =>
  activeCat.value === 'all' ? features : features.filter((item) => item.cat === activeCat.value)
);

const tagsOf = (id) => t(`features.${id}.tags`).split('|').filter(Boolean);

/* --- 大图查看 --- */

const viewerIndex = ref(-1);
const current = computed(() => visibleFeatures.value[viewerIndex.value] ?? visibleFeatures.value[0]);

const openViewer = (index) => {
  viewerIndex.value = index;
};

const closeViewer = () => {
  viewerIndex.value = -1;
};

const step = (delta) => {
  const total = visibleFeatures.value.length;
  if (!total) return;
  viewerIndex.value = (viewerIndex.value + delta + total) % total;
};

const onKeydown = (event) => {
  if (viewerIndex.value < 0) return;
  if (event.key === 'Escape') closeViewer();
  else if (event.key === 'ArrowRight') step(1);
  else if (event.key === 'ArrowLeft') step(-1);
};

watch(viewerIndex, (index) => {
  if (index >= 0) window.addEventListener('keydown', onKeydown);
  else window.removeEventListener('keydown', onKeydown);
});

onUnmounted(() => window.removeEventListener('keydown', onKeydown));
</script>

<style scoped>
.feat-page {
  max-width: 1180px;
}

.feat-header h1 {
  font-size: 30px;
  line-height: 40px;
}

/* ---------- 概览数字 ---------- */

.feat-stats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.feat-stat {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px 18px;
}

.feat-stat-icon {
  flex: 0 0 auto;
  width: 40px;
  height: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  background: var(--subtle-secondary);
  color: var(--accent-base);
  font-family: 'WinUIOnWebIcons';
  font-size: 20px;
}

.feat-stat-text {
  min-width: 0;
}

.feat-stat-num {
  font-size: 20px;
  font-weight: 600;
  line-height: 26px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.feat-stat-label {
  font-size: 12.5px;
  line-height: 18px;
  color: var(--text-secondary);
}

/* ---------- 分类筛选 ---------- */

.feat-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 20px;
}

.feat-filter {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 7px 14px;
  border-radius: 999px;
  border: 1px solid var(--card-stroke);
  background: var(--card-bg);
  color: var(--text-secondary);
  font: inherit;
  font-size: 13.5px;
  cursor: pointer;
  transition: background 150ms ease, color 150ms ease, border-color 150ms ease;
}

.feat-filter .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 14px;
}

.feat-filter:hover {
  background: var(--subtle-secondary);
  color: var(--text-primary);
}

.feat-filter.active {
  background: var(--accent-base);
  border-color: transparent;
  color: var(--accent-text);
  font-weight: 600;
}

.feat-filter-count {
  min-width: 20px;
  padding: 0 6px;
  border-radius: 999px;
  background: var(--subtle-secondary);
  color: var(--text-secondary);
  font-size: 11.5px;
  line-height: 18px;
  text-align: center;
}

.feat-filter.active .feat-filter-count {
  background: rgba(255, 255, 255, 0.24);
  color: inherit;
}

/* ---------- 截图画廊 ---------- */

.feat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(330px, 1fr));
  gap: 16px;
}

.feat-card {
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--card-stroke);
  background: var(--card-bg);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: transform 200ms ease, box-shadow 200ms ease, border-color 200ms ease;
}

.feat-card:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--accent-base) 38%, transparent);
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.12);
}

.feat-card-shot {
  display: block;
  aspect-ratio: 16 / 10;
  overflow: hidden;
  background: var(--card-bg-secondary);
  border-bottom: 1px solid var(--stroke-divider);
}

.feat-card-shot img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top center;
  transition: transform 400ms ease;
}

.feat-card:hover .feat-card-shot img {
  transform: scale(1.02);
}

.feat-card-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 16px 18px 18px 18px;
}

.feat-card-cat {
  font-size: 11.5px;
  font-weight: 600;
  letter-spacing: 0.4px;
  color: var(--accent-base);
}

.feat-card-title {
  font-size: 17px;
  font-weight: 600;
  line-height: 24px;
  color: var(--text-primary);
}

.feat-card-desc {
  font-size: 13.5px;
  line-height: 21px;
  color: var(--text-secondary);
}

.feat-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 4px;
}

.feat-tag {
  padding: 2px 9px;
  border-radius: 999px;
  background: var(--subtle-secondary);
  color: var(--text-secondary);
  font-size: 11.5px;
  line-height: 18px;
}

.feat-empty {
  padding: 40px 0;
  text-align: center;
  color: var(--text-tertiary);
}

/* ---------- 大图查看 ---------- */
/* z-index 与应用弹窗（WinContentDialog, 10000）同级：
   标题栏是 fixed + z-index 9999，低于它时灯箱顶部会被标题栏截获点击。 */

.feat-viewer {
  position: fixed;
  inset: 0;
  z-index: 10000;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 20px 24px 18px 24px;
  background: rgba(0, 0, 0, 0.72);
  backdrop-filter: blur(12px);
}

.feat-viewer-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 5px;
  min-width: 0;
}

.feat-viewer-cat {
  flex: 0 0 auto;
  padding: 2px 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.18);
  font-size: 12px;
  line-height: 20px;
  color: rgba(255, 255, 255, 0.86);
}

.feat-viewer-title {
  font-size: 17px;
  font-weight: 600;
  line-height: 24px;
  color: #ffffff;
}

.feat-viewer-desc {
  font-size: 13px;
  line-height: 19px;
  color: rgba(255, 255, 255, 0.78);
}

.feat-viewer-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
  margin-left: 16px;
}

.feat-viewer-index {
  margin-right: 4px;
  font-size: 12.5px;
  color: rgba(255, 255, 255, 0.72);
  white-space: nowrap;
}

.feat-viewer-btn {
  width: 36px;
  height: 36px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  border: 1px solid rgba(255, 255, 255, 0.24);
  background: rgba(255, 255, 255, 0.12);
  color: #ffffff;
  cursor: pointer;
  transition: background 150ms ease;
}

.feat-viewer-btn:hover {
  background: rgba(255, 255, 255, 0.26);
}

.feat-viewer-btn .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 15px;
}

.feat-viewer-stage {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.feat-viewer-stage img {
  max-width: 100%;
  max-height: 100%;
  border-radius: 8px;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
}

/* 控制条固定在底部：顶部是应用标题栏与窗口按钮的区域，
   把按钮放上去会被全局 fixed 标题栏截获点击。 */
.feat-viewer-foot {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  padding: 12px 16px;
  border-radius: 10px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: rgba(255, 255, 255, 0.08);
}

.feat-viewer-tags {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.feat-viewer-foot .feat-tag {
  background: rgba(255, 255, 255, 0.16);
  color: rgba(255, 255, 255, 0.9);
}

.feat-viewer-doc {
  margin-left: 4px;
  color: #ffffff;
  font-size: 13px;
  font-weight: 600;
  text-decoration: none;
}

.feat-viewer-doc:hover {
  text-decoration: underline;
}

/* ---------- 响应式 ---------- */

@media (max-width: 980px) {
  .feat-stats {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 640px) {
  .feat-stats {
    grid-template-columns: minmax(0, 1fr);
  }

  .feat-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .feat-viewer {
    padding: 12px;
  }
}
</style>
