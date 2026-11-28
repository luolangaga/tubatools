<template>
  <WinScrollViewer class="site-home-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-home">
      <!-- Hero -->
      <header class="site-hero">
        <div class="site-hero-inner">
          <div class="site-hero-copy">
            <span class="site-hero-badge">
              <span class="icon" aria-hidden="true">&#xE7E8;</span>
              {{ t('home.hero.badge') }}
            </span>
            <h1 class="site-hero-title">{{ t('home.hero.title') }}</h1>
            <p class="site-hero-subtitle">{{ t('home.hero.subtitle') }}</p>
            <div class="site-hero-actions">
              <WinButton
                class="hero-download-btn"
                Style="AccentButtonStyle"
                :Content="t('home.hero.download')"
                Height="40"
                Padding="20,0"
                @Click="goDownload" />
              <WinButton
                class="hero-github-btn"
                Style="SubtleButtonStyle"
                :Content="t('home.hero.view-github')"
                Height="40"
                Padding="16,0"
                @Click="openGithub" />
              <WinButton
                class="hero-why-btn"
                Style="SubtleButtonStyle"
                :Content="t('home.hero.why-choose')"
                Height="40"
                Padding="16,0"
                @Click="goWhyChoose" />
            </div>
            <div class="site-hero-badges">
              <div class="site-hero-stats">
                <a class="site-stat-badge site-stat-stars" href="https://github.com/luolangaga/tubatools" target="_blank" rel="noopener">
                  <span class="site-stat-label">STARS</span>
                  <span class="site-stat-value">6.3k</span>
                </a>
                <a class="site-stat-badge site-stat-downloads" href="https://github.com/luolangaga/tubatools/releases" target="_blank" rel="noopener" :title="t('home.hero.downloads-title')">
                  <span class="site-stat-label">DOWNLOADS</span>
                  <span class="site-stat-value">{{ totalDownloads }}</span>
                </a>
              </div>
            </div>
          </div>
          <div class="site-hero-art">
            <img class="site-hero-screenshot" :src="screenshotTools" alt="图吧工具箱CE主界面截图" />
          </div>
          <p class="site-hero-license">
            <span class="icon" aria-hidden="true">&#xE8A1;</span>
            {{ t('home.hero.license') }}
            <a href="https://github.com/luolangaga/tubatool/blob/main/LICENSE" target="_blank" rel="noopener">{{ t('home.hero.license-link') }}</a>
          </p>
        </div>
      </header>

      <!-- 微软应用商店提示（仅宽屏显示） -->
      <p class="site-store-tip">
        {{ t('home.hero.store-tip') }}
        <a href="https://apps.microsoft.com/detail/9P15095X7MGB" target="_blank" rel="noopener noreferrer">{{ t('home.hero.store-name') }}</a>
        {{ t('home.hero.store-tip-end') }}
      </p>

      <!-- 宣传片 -->
      <section class="site-section home-promo">
        <div class="site-section-inner">
          <WinMediaPlayerElement
            :Source="promoVideo"
            :PosterSource="promoPoster"
            :AreTransportControlsEnabled="true" />
        </div>
      </section>

      <!-- 三个核心卖点 -->
      <section class="site-section home-core">
        <div class="site-section-inner">
          <div class="home-section-head">
            <span class="home-eyebrow">{{ t('home.core.eyebrow') }}</span>
            <h2 class="site-section-title">{{ t('home.features.title') }}</h2>
            <p class="site-section-subtitle">{{ t('home.features.subtitle') }}</p>
          </div>
          <div class="home-core-grid">
            <article v-for="card in coreCards" :key="card.titleKey" class="site-card home-core-card">
              <span class="home-core-icon icon" aria-hidden="true">{{ card.icon }}</span>
              <h3>{{ t(card.titleKey) }}</h3>
              <p>{{ t(card.descKey) }}</p>
              <router-link to="/features" class="home-text-link">{{ t('home.core.more') }} <span aria-hidden="true">›</span></router-link>
            </article>
          </div>
        </div>
      </section>

      <!-- 精选实机截图 -->
      <section class="site-section home-shots">
        <div class="site-section-inner">
          <div class="home-section-head home-section-head-row">
            <div>
              <h2 class="site-section-title">{{ t('home.shots.title') }}</h2>
              <p class="site-section-subtitle">{{ t('home.shots.subtitle') }}</p>
            </div>
            <WinButton
              Style="SubtleButtonStyle"
              :Content="t('home.gallery.more')"
              Height="36"
              Padding="16,0"
              @Click="goFeatures" />
          </div>
          <div class="site-gallery">
            <button
              v-for="item in galleryItems"
              :key="item.id"
              type="button"
              class="site-gallery-card"
              @click="goFeatures">
              <img :src="item.image" :alt="t(`features.${item.id}.title`)" loading="lazy" decoding="async" />
              <span class="site-gallery-overlay">
                <span class="site-gallery-cat">{{ t(`features.cat.${item.cat}`) }}</span>
                <span class="site-gallery-title">{{ t(`features.${item.id}.title`) }}</span>
                <span class="site-gallery-desc">{{ t(`features.${item.id}.desc`) }}</span>
              </span>
            </button>
          </div>
        </div>
      </section>

      <!-- 社区跑分排行：保留可见的排行卡片入口 -->
      <section class="site-section home-rank">
        <div class="site-section-inner">
          <div class="home-section-head home-section-head-row">
            <div>
              <h2 class="site-section-title">{{ t('home.rank.title') }}</h2>
              <p class="site-section-subtitle">{{ t('home.rank.subtitle') }}</p>
            </div>
            <WinButton
              Style="SubtleButtonStyle"
              :Content="t('home.rank.view-all')"
              Height="36"
              Padding="16,0"
              @Click="goRanking" />
          </div>
          <div v-if="rankPreview.length" class="home-rank-grid">
            <button
              v-for="(row, i) in rankPreview"
              :key="row.entry.id"
              type="button"
              class="site-card home-rank-card"
              @click="goRanking">
              <span class="home-rank-medal" :class="`home-rank-medal-${i + 1}`">{{ i + 1 }}</span>
              <span class="home-rank-body">
                <span class="home-rank-author">@{{ row.entry.author }}</span>
                <span class="home-rank-hw">{{ row.entry.cpuName }}<template v-if="row.entry.gpuName"> · {{ row.entry.gpuName }}</template></span>
              </span>
              <span class="home-rank-score">
                {{ row.entry.gamingScore }}
                <span>{{ t('rank.game') }}</span>
              </span>
            </button>
          </div>
          <p v-else-if="rankLoading" class="home-rank-empty">{{ t('home.rank.loading') }}</p>
          <p v-else class="home-rank-empty">{{ t('home.rank.empty') }}</p>
        </div>
      </section>

      <!-- 最终下载 CTA -->
      <section class="site-section home-cta-wrap">
        <div class="site-section-inner">
          <div class="site-card home-cta">
            <div>
              <h2>{{ t('home.cta.title') }}</h2>
              <p>{{ t('home.cta.desc') }}</p>
            </div>
            <div class="home-cta-actions">
              <WinButton
                Style="AccentButtonStyle"
                :Content="t('home.cta.download')"
                Height="40"
                Padding="20,0"
                @Click="goDownload" />
              <WinButton
                Style="SubtleButtonStyle"
                :Content="t('home.cta.docs')"
                Height="40"
                Padding="16,0"
                @Click="goDocs" />
            </div>
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed, inject, onMounted, ref } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import WinMediaPlayerElement from '../../components/WinMediaPlayerElement.vue';
import SiteFooter from '../components/SiteFooter.vue';
import screenshotTools from '../../assets/site/screenshot-tools.webp';
import { features } from '../services/features';
import { buildLeaderboard, getLeaderboardData } from '../services/benchmarkData';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const navigate = inject('navigate', () => {});

// 宣传片：4.7 MB / 720p60 的 Web 版（由 23 MB 的 v12 母版转码，faststart）。
// 视频放 GitHub raw：支持 Range、无 HTTP/3 通告；Cloudflare 的媒体请求在部分网络下
// 会卡在不可用的 HTTP/3 连接上（readyState 一直为 0）。
const promoVideo = 'https://raw.githubusercontent.com/luolangaga/tubatools-media/main/tuba-promo-web.mp4';
const promoPoster = '/videos/tuba-promo-poster-web.jpg';

const coreCards = [
  { icon: '\uE8B7', titleKey: 'home.core.free.title', descKey: 'home.core.free.desc' },
  { icon: '\uE950', titleKey: 'home.core.hardware.title', descKey: 'home.core.hardware.desc' },
  { icon: '\uE8F1', titleKey: 'home.core.tools.title', descKey: 'home.core.tools.desc' }
];

const galleryIds = ['ai-playground', 'game-monitor', 'junk-cleaner', 'cpu-ranking'];
const galleryItems = computed(() => galleryIds.map((id) => features.find((item) => item.id === id)).filter(Boolean));

const rankPreview = ref([]);
const rankLoading = ref(true);
const totalDownloads = ref('3.75M');

const GITCODE_DOWNLOADS_FALLBACK = 3585950;
const GITHUB_DOWNLOADS_FALLBACK = 165258;

function formatCount(value) {
  const count = Number(value);
  if (!Number.isFinite(count) || count <= 0) return null;
  if (count >= 1000000) {
    return `${(count / 1000000).toFixed(2).replace(/\.?0+$/, '')}M`;
  }
  if (count >= 1000) {
    return `${(count / 1000).toFixed(1).replace(/\.0$/, '')}k`;
  }
  return String(count);
}

async function loadTotalDownloads() {
  let gitcodeDownloads = GITCODE_DOWNLOADS_FALLBACK;
  let githubDownloads = GITHUB_DOWNLOADS_FALLBACK;

  try {
    // GitCode 与 AtomGit 是同一平台，这里只取一份，避免重复累计
    const response = await fetch(
      'https://api.gitcode.com/api/v5/repos/luolangaga/tubatool/download_statistics?direction=desc',
      { headers: { Accept: 'application/json' } }
    );
    if (response.ok) {
      const data = await response.json();
      const count = Number(data?.download_statistics_history_total);
      if (Number.isFinite(count) && count > 0) gitcodeDownloads = count;
    }
  } catch {
    /* 接口不可用时使用快照值 */
  }

  try {
    const response = await fetch(
      'https://api.github.com/repos/luolangaga/tubatools/releases?per_page=100',
      { headers: { Accept: 'application/vnd.github+json' } }
    );
    if (response.ok) {
      const releases = await response.json();
      if (Array.isArray(releases)) {
        const count = releases.reduce((sum, release) => {
          const assets = Array.isArray(release?.assets) ? release.assets : [];
          return sum + assets.reduce((assetSum, asset) => assetSum + Number(asset?.download_count ?? 0), 0);
        }, 0);
        if (Number.isFinite(count) && count > 0) githubDownloads = count;
      }
    }
  } catch {
    /* 接口不可用时使用快照值 */
  }

  const total = formatCount(gitcodeDownloads + githubDownloads);
  if (total) totalDownloads.value = total;
}

async function loadRankPreview() {
  try {
    const data = await getLeaderboardData(false);
    rankPreview.value = buildLeaderboard(data, 'gaming', '').slice(0, 3);
  } catch {
    rankPreview.value = [];
  } finally {
    rankLoading.value = false;
  }
}

onMounted(() => {
  void loadTotalDownloads();
  void loadRankPreview();
});

const goDownload = () => navigate('download');
const goFeatures = () => navigate('features');
const goDocs = () => navigate('docs');
const goWhyChoose = () => navigate('why');
const goRanking = () => navigate('ranking');
const openGithub = () => window.open(t('app.repository'), '_blank', 'noopener');
</script>
<style scoped>
.site-home {
  width: 100%;
  min-width: 0;
}

/* ---------- Hero ---------- */

@keyframes site-hero-bg-scrolling {
  0% { background-position: 0px 196px; }
  100% { background-position: 0px 0px; }
}

.site-hero {
  position: relative;
  overflow: hidden;
  background-color: rgba(0, 90, 158, 0.95);
}

.site-hero::before {
  content: '';
  position: absolute;
  width: 2000%;
  height: 2000%;
  top: -1000%;
  left: -1000%;
  z-index: 0;
  background: url('../../assets/site/hero-tile.png') repeat 0 0;
  background-color: rgba(0, 90, 158, 0.95);
  overflow: hidden;
  transform: rotateX(15deg) rotateZ(-15deg) skewX(15deg);
  transform-style: preserve-3d;
  animation: site-hero-bg-scrolling 20s infinite linear;
  pointer-events: none;
}

.site-hero-inner {
  position: relative;
  z-index: 1;
  max-width: 1080px;
  margin: 0 auto;
  padding: 56px 36px 40px 36px;
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(0, 0.95fr);
  gap: 48px;
  align-items: center;
}

.site-hero-copy {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 16px;
}

.site-hero-badge {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 14px;
  border: 1px solid rgba(255, 255, 255, 0.35);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.12);
  color: #ffffff;
  font-size: 13px;
  font-weight: 600;
  line-height: 24px;
}

.site-hero-badge .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 14px;
}

.site-hero-title {
  margin: 0;
  font-size: 44px;
  font-weight: 700;
  line-height: 56px;
  letter-spacing: -0.5px;
  background: linear-gradient(to right bottom, rgb(255, 255, 255) 30%, rgba(255, 255, 255, 0.30)) text;
  -webkit-box-decoration-break: clone;
  -webkit-text-fill-color: transparent;
  text-wrap: balance;
}

.site-hero-subtitle {
  margin: 0;
  font-size: 16px;
  line-height: 24px;
  color: rgba(255, 255, 255, 0.82);
  max-width: 520px;
}

.site-hero-actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.site-hero-actions :deep(.hero-download-btn.win-btn) {
  border-color: rgba(0, 0, 0, 0.08);
}

.site-hero-actions :deep(.hero-download-btn.win-btn:hover) {
  background: #FFC9D4;
}

.site-hero-actions :deep(.hero-github-btn.win-btn:hover) {
  background: #F0F0F0;
}

.site-hero-badges {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
  max-width: 100%;
}

.site-hero-badges a {
  display: inline-flex;
}

.site-hero-badges img {
  border-radius: 6px;
}

.site-hero-stats img {
  height: 20px;
}

.site-hero-trendshift img {
  height: 34px;
}

.site-hero-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  max-width: 100%;
}

/* 站内统计徽章：不依赖 shields.io，避免第三方图片加载失败 */
.site-stat-badge {
  display: inline-flex;
  align-items: stretch;
  height: 24px;
  overflow: hidden;
  border-radius: 4px;
  font-size: 10px;
  line-height: 1;
  text-decoration: none;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.16);
}

.site-stat-label,
.site-stat-value {
  display: inline-flex;
  align-items: center;
  padding: 0 8px;
  font-weight: 600;
  letter-spacing: 0.03em;
}

.site-stat-value {
  font-size: 11px;
  font-weight: 700;
}

.site-stat-stars .site-stat-label {
  background: #3a3f45;
  color: #f2f4f7;
}

.site-stat-stars .site-stat-value {
  background: #ffd33d;
  color: #24292f;
}

.site-stat-downloads .site-stat-label {
  background: #0078d4;
  color: #fff;
}

.site-stat-downloads .site-stat-value {
  background: #005a9e;
  color: #fff;
}

.site-stat-badge:hover {
  opacity: 0.9;
}

.site-hero-trendshift {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  max-width: 100%;
}

.site-hero-license {
  grid-column: 1 / -1;
  margin: 2px 0 0 0;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 12.5px;
  line-height: 18px;
  color: rgba(255, 255, 255, 0.62);
  max-width: none;
  width: 100%;
}

.site-hero-license .icon {
  flex: 0 0 auto;
  margin-top: 1px;
  font-family: 'WinUIOnWebIcons';
  font-size: 13px;
}

.site-hero-license a {
  color: rgba(255, 255, 255, 0.9);
  text-decoration: none;
}

.site-hero-license a:hover {
  text-decoration: underline;
}

.site-store-tip {
  margin: 0;
  padding: 16px 24px;
  text-align: center;
  font-size: 22px;
  font-weight: 600;
  line-height: 30px;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--accent-base) 8%, transparent);
}

.site-store-tip a {
  color: var(--accent-base);
  text-decoration: none;
  font-weight: 600;
}

.site-store-tip a:hover {
  text-decoration: underline;
}

.site-hero-art {
  position: relative;
}

.site-hero-screenshot {
  width: 100%;
  border-radius: 12px;
  border: 1px solid rgba(255, 255, 255, 0.25);
  box-shadow: 0 24px 64px rgba(0, 0, 0, 0.35);
  transform: perspective(1200px) rotateY(-6deg) rotateX(2deg);
  transition: transform 400ms ease;
}

.site-hero-screenshot:hover {
  transform: perspective(1200px) rotateY(0deg) rotateX(0deg);
}

/* ---------- 通用 section ---------- */

.site-section-inner {
  max-width: 1080px;
  margin: 0 auto;
  padding: 0 36px;
}

.site-section {
  margin-top: 48px;
}

/* ---------- 特性行 ---------- */

.site-feature {
  margin-top: 40px;
}

.site-feature-row {
  display: grid;
  grid-template-columns: minmax(0, 0.95fr) minmax(0, 1.05fr);
  gap: 48px;
  align-items: center;
  opacity: 0;
  transform: translateY(24px);
  transition: opacity 500ms ease, transform 500ms ease;
}

.site-feature-row.reverse .site-feature-img {
  order: 2;
}

.site-feature-row.site-feature-visible {
  opacity: 1;
  transform: translateY(0);
}

.site-feature-img img {
  width: 100%;
  border-radius: 10px;
  border: 1px solid var(--card-stroke);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.12);
}

.site-feature-text h3 {
  margin: 0 0 12px 0;
  font-size: 26px;
  font-weight: 600;
  line-height: 34px;
  color: var(--text-primary);
}

.site-feature-lead {
  margin: 0 0 10px 0;
  font-size: 15px;
  line-height: 22px;
  color: var(--text-primary);
}

.site-feature-intro {
  margin: 8px 0 0 0;
  font-size: 13.5px;
  line-height: 20px;
  color: var(--text-secondary);
}

.site-feature-text :deep(.site-check-list) {
  margin-top: 10px;
}

/* ---------- 功能截图画廊 ---------- */

.site-gallery {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.site-gallery-card {
  position: relative;
  display: block;
  width: 100%;
  aspect-ratio: 16 / 10;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--card-stroke);
  border-radius: 10px;
  background: var(--card-bg-secondary);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: transform 200ms ease, box-shadow 200ms ease, border-color 200ms ease;
}

.site-gallery-card:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--accent-base) 38%, transparent);
  box-shadow: 0 10px 28px rgba(0, 0, 0, 0.16);
}

.site-gallery-card img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top center;
  transition: transform 400ms ease;
}

.site-gallery-card:hover img {
  transform: scale(1.04);
}

.site-gallery-overlay {
  position: absolute;
  inset: auto 0 0 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 34px 14px 12px 14px;
  background: linear-gradient(to top, rgba(0, 0, 0, 0.82) 12%, rgba(0, 0, 0, 0.45) 52%, rgba(0, 0, 0, 0) 100%);
  color: #ffffff;
}

.site-gallery-cat {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.4px;
  color: rgba(255, 255, 255, 0.72);
}

.site-gallery-title {
  font-size: 15px;
  font-weight: 600;
  line-height: 21px;
}

.site-gallery-desc {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  max-height: 0;
  opacity: 0;
  font-size: 12px;
  line-height: 17px;
  color: rgba(255, 255, 255, 0.82);
  transition: max-height 240ms ease, opacity 240ms ease, margin-top 240ms ease;
}

.site-gallery-card:hover .site-gallery-desc {
  max-height: 40px;
  opacity: 1;
  margin-top: 3px;
}

.site-gallery-actions {
  display: flex;
  justify-content: center;
  margin-top: 20px;
}

@media (max-width: 900px) {
  .site-gallery {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 560px) {
  .site-gallery {
    grid-template-columns: minmax(0, 1fr);
  }

  .site-gallery-desc {
    max-height: 40px;
    opacity: 1;
    margin-top: 3px;
  }
}

/* ---------- 工具网格 ---------- */

.site-tool-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
  align-items: start;
}

.site-tool-col {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  opacity: 0;
  transform: translateY(16px);
  transition: opacity 400ms ease, transform 400ms ease;
}

.site-tool-col.site-feature-visible {
  opacity: 1;
  transform: translateY(0);
}

.site-tool-col-card {
  padding: 22px 26px;
}

.site-tool-col h4 {
  margin: 0 0 10px 0;
  font-size: 17px;
  font-weight: 600;
  line-height: 22px;
  color: var(--text-primary);
}

.site-tool-col :deep(.site-check-list li) {
  margin: 7px 0;
  font-size: 14.5px;
  line-height: 21px;
}

.site-tool-col :deep(.site-check-list .site-check-glyph) {
  width: 18px;
  height: 21px;
  font-size: 13px;
  line-height: 21px;
}

.site-tool-col a {
  color: var(--accent-base);
  text-decoration: none;
}

.site-tool-col a:hover {
  text-decoration: underline;
}

@media (max-width: 900px) {
  .site-tool-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 560px) {
  .site-tool-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

/* ---------- 支持 ---------- */

.site-support-card {
  padding: 28px 32px;
  text-align: center;
  opacity: 0;
  transform: translateY(16px);
  transition: opacity 400ms ease, transform 400ms ease;
}

.site-support-card.site-feature-visible {
  opacity: 1;
  transform: translateY(0);
}

.site-support-card h3 {
  margin: 0 0 10px 0;
  font-size: 22px;
  font-weight: 600;
  color: var(--text-primary);
}

.site-support-card p {
  margin: 0;
  font-size: 14.5px;
  line-height: 24px;
  color: var(--text-secondary);
}

.site-support-card a {
  color: var(--accent-base);
  text-decoration: none;
  font-weight: 600;
}

.site-support-card a:hover {
  text-decoration: underline;
}

/* ---------- 响应式 ---------- */

@media (max-width: 900px) {
  .site-hero-inner,
  .site-feature-row {
    grid-template-columns: minmax(0, 1fr);
    gap: 32px;
  }

  .site-feature-row.reverse .site-feature-img {
    order: 0;
  }

  .site-hero-title {
    font-size: 34px;
    line-height: 44px;
  }

  .site-store-tip {
    display: none;
  }
}

@media (max-width: 640px) {
  .site-hero-inner,
  .site-section-inner {
    padding-left: 20px;
    padding-right: 20px;
  }
}
/* ---------- 精简后的首页 ---------- */

.home-promo {
  margin-top: 40px;
}

.home-promo .site-section-inner {
  max-width: 960px;
}

.home-promo :deep(.win-media-player-surface) {
  border-radius: 10px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.12);
}

.home-core {
  margin-top: 40px;
}

.home-section-head {
  max-width: 760px;
  margin-bottom: 20px;
}

.home-eyebrow {
  display: block;
  margin-bottom: 6px;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.8px;
  text-transform: uppercase;
  color: var(--accent-base);
}

.home-section-head .site-section-title {
  margin: 0 0 6px;
}

.home-section-head .site-section-subtitle {
  margin: 0;
}

.home-section-head-row {
  max-width: none;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 20px;
}

.home-core-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.home-core-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 190px;
  padding: 22px 24px;
}

.home-core-icon {
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  border-radius: 8px;
  background: var(--subtle-secondary);
  color: var(--accent-base);
  font-family: 'WinUIOnWebIcons';
  font-size: 20px;
}

.home-core-card h3 {
  margin: 4px 0 0;
  font-size: 18px;
  font-weight: 600;
  line-height: 24px;
  color: var(--text-primary);
}

.home-core-card p {
  flex: 1;
  margin: 0;
  font-size: 14px;
  line-height: 21px;
  color: var(--text-secondary);
}

.home-text-link {
  margin-top: auto;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 13px;
  font-weight: 600;
  color: var(--accent-base);
  text-decoration: none;
}

.home-text-link:hover {
  text-decoration: underline;
}

.home-shots,
.home-rank,
.home-cta-wrap {
  margin-top: 52px;
}

/* ---------- 社区排行 ---------- */

.home-rank-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.home-rank-card {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
  padding: 18px 20px;
  font: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition: transform 200ms ease, box-shadow 200ms ease, border-color 200ms ease;
}

.home-rank-card:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--accent-base) 34%, transparent);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
}

.home-rank-medal {
  display: grid;
  place-items: center;
  flex: 0 0 32px;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: var(--subtle-secondary);
  color: var(--text-secondary);
  font-size: 14px;
  font-weight: 700;
}

.home-rank-medal-1 {
  background: #ffd33d;
  color: #5a4100;
}

.home-rank-medal-2 {
  background: #e8e8e8;
  color: #4b4b4b;
}

.home-rank-medal-3 {
  background: #f0c7a5;
  color: #5d3517;
}

.home-rank-body {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-width: 0;
  gap: 3px;
}

.home-rank-author,
.home-rank-hw {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.home-rank-author {
  font-size: 13px;
  font-weight: 600;
  line-height: 18px;
  color: var(--text-primary);
}

.home-rank-hw {
  font-size: 11px;
  line-height: 16px;
  color: var(--text-secondary);
}

.home-rank-score {
  flex: 0 0 auto;
  text-align: right;
  font-size: 20px;
  font-weight: 700;
  line-height: 24px;
  color: var(--text-primary);
}

.home-rank-score span {
  display: block;
  margin-top: 1px;
  font-size: 10px;
  font-weight: 500;
  line-height: 14px;
  color: var(--text-secondary);
}

.home-rank-empty {
  margin: 0;
  padding: 18px 20px;
  font-size: 13px;
  color: var(--text-secondary);
}

.home-cta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  padding: 28px 32px;
}

.home-cta h2 {
  margin: 0 0 6px;
  font-size: 24px;
  font-weight: 600;
  line-height: 32px;
  color: var(--text-primary);
}

.home-cta p {
  margin: 0;
  font-size: 14px;
  line-height: 21px;
  color: var(--text-secondary);
}

.home-cta-actions {
  display: flex;
  flex-shrink: 0;
  gap: 10px;
}

@media (max-width: 900px) {
  .home-core-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .home-rank-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .home-section-head-row {
    align-items: flex-start;
    flex-direction: column;
  }

  .home-cta {
    align-items: flex-start;
    flex-direction: column;
  }
}

@media (max-width: 560px) {
  .home-core-card {
    min-height: 0;
  }

  .home-cta {
    padding: 22px 20px;
  }

  .home-cta-actions {
    width: 100%;
    flex-direction: column;
  }

  .home-cta-actions .win-btn {
    width: 100%;
  }
}

</style>
