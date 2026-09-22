<template>
  <WinScrollViewer class="site-home-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-home">
      <!-- Hero -->
      <header class="site-hero" ref="heroRef">
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
                Background="#FFB6C1"
                Foreground="#2B2B2B"
                Height="40"
                Padding="20,0"
                @Click="goDownload" />
              <WinButton
                class="hero-github-btn"
                Style="SubtleButtonStyle"
                :Content="t('home.hero.view-github')"
                Background="#FFFFFF"
                Foreground="#2B2B2B"
                Height="40"
                Padding="16,0"
                @Click="openGithub" />
              <WinButton
                class="hero-why-btn"
                Style="SubtleButtonStyle"
                :Content="t('home.hero.why-choose')"
                Background="#FFFFFF"
                Foreground="#2B2B2B"
                Height="40"
                Padding="16,0"
                @Click="goWhyChoose" />
            </div>
            <div class="site-hero-badges">
            <div class="site-hero-stats">
              <a href="https://github.com/luolangaga/tubatool" target="_blank" rel="noopener">
                <img src="https://img.shields.io/github/stars/luolangaga/tubatool?style=for-the-badge&logo=github&label=Stars&color=ffd33d&logoColor=white" alt="GitHub Stars" />
              </a>
              <a href="https://gitcode.com/luolangaga/tubatool" target="_blank" rel="noopener">
                <img src="https://gitcode.com/luolangaga/tubatool/star/badge.svg" alt="GitCode Stars" />
              </a>
              <a href="https://github.com/luolangaga/tubatool" target="_blank" rel="noopener">
                <img src="https://img.shields.io/github/forks/luolangaga/tubatool?style=for-the-badge&logo=github&label=Forks&color=8b5cf6&logoColor=white" alt="GitHub Forks" />
              </a>
              <a href="https://github.com/luolangaga/tubatool/releases" target="_blank" rel="noopener">
                <img src="https://img.shields.io/github/downloads/luolangaga/tubatool/total?style=for-the-badge&logo=github&label=Downloads&color=0078d4&logoColor=white" alt="GitHub Downloads" />
              </a>
            </div>
              <div class="site-hero-trendshift">
                <a href="https://trendshift.io/repositories/51042?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-51042" target="_blank" rel="noopener noreferrer">
                  <img src="https://trendshift.io/api/badge/trendshift/repositories/51042/daily?language=C%23" alt="Trendshift Daily" />
                </a>
                <a href="https://trendshift.io/repositories/51042?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-51042" target="_blank" rel="noopener noreferrer">
                  <img src="https://trendshift.io/api/badge/trendshift/repositories/51042/weekly?language=C%23" alt="Trendshift Weekly" />
                </a>
                <a href="https://atomgit.com/luolangaga/tubatool" target="_blank" rel="noopener">
                  <img alt="AtomGit G-Star" src="https://atomgit.com/luolangaga/tubatool/star/new_badge.svg" />
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

      <!-- 特性总览 -->
      <section class="site-section">
        <div class="site-section-inner">
          <h2 class="site-section-title">{{ t('home.features.title') }}</h2>
          <p class="site-section-subtitle">{{ t('home.features.subtitle') }}</p>
        </div>
      </section>

      <!-- Section 1: 开源且完全免费 -->
      <section class="site-feature">
        <div class="site-section-inner site-feature-row">
          <div class="site-feature-img">
            <img :src="screenshotTools" alt="工具目录界面" />
          </div>
          <div class="site-feature-text">
            <h3>{{ t('home.section1.title') }}</h3>
            <p class="site-feature-lead">{{ t('home.section1.text1') }}</p>
            <p class="site-feature-intro">{{ t('home.section1.text2') }}</p>
            <ul class="site-check-list">
              <li v-for="item in section1Items" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <p class="site-feature-intro">{{ t('home.section1.text3') }}</p>
          </div>
        </div>
      </section>

      <!-- Section 2: 硬件信息一目了然 -->
      <section class="site-feature">
        <div class="site-section-inner site-feature-row reverse">
          <div class="site-feature-img">
            <img :src="screenshotHardware" alt="硬件信息界面" />
          </div>
          <div class="site-feature-text">
            <h3>{{ t('home.section2.title') }}</h3>
            <p class="site-feature-lead">{{ t('home.section2.text1') }}</p>
            <ul class="site-check-list">
              <li v-for="item in section2Items" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
            <p class="site-feature-intro">{{ t('home.section2.text2') }}</p>
          </div>
        </div>
      </section>

      <!-- Section 3: WinUI 3 原生体验 -->
      <section class="site-feature">
        <div class="site-section-inner site-feature-row">
          <div class="site-feature-img">
            <img :src="screenshotTools" alt="WinUI 3 原生界面" />
          </div>
          <div class="site-feature-text">
            <h3>{{ t('home.section3.title') }}</h3>
            <p class="site-feature-lead">{{ t('home.section3.text1') }}</p>
            <ul class="site-check-list">
              <li v-for="item in section3Items" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
          </div>
        </div>
      </section>

      <!-- Section 4: 内置工具 -->
      <section class="site-feature">
        <div class="site-section-inner site-feature-row reverse">
          <div class="site-feature-img">
            <img :src="screenshotBuiltin" alt="内置工具界面" />
          </div>
          <div class="site-feature-text">
            <h3>{{ t('home.section4.title') }}</h3>
            <p class="site-feature-lead">{{ t('home.section4.text1') }}</p>
            <ul class="site-check-list">
              <li v-for="item in section4Items" :key="item">
                <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>{{ item }}
              </li>
            </ul>
          </div>
        </div>
      </section>

      <!-- 功能截图画廊 -->
      <section class="site-section">
        <div class="site-section-inner">
          <h2 class="site-section-title">{{ t('home.gallery.title') }}</h2>
          <p class="site-section-subtitle">{{ t('home.gallery.subtitle') }}</p>
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
          <div class="site-gallery-actions">
            <WinButton
              Style="AccentButtonStyle"
              :Content="t('home.gallery.more')"
              Height="40"
              Padding="20,0"
              @Click="goFeatures" />
          </div>
        </div>
      </section>

      <!-- 工具分类网格 -->
      <section class="site-section">
        <div class="site-section-inner">
          <h2 class="site-section-title">{{ t('home.tools.title') }}</h2>
          <div class="site-tool-grid">
            <div v-for="(col, ci) in toolColumns" :key="`col-${ci}`" class="site-tool-col">
              <div v-for="group in col" :key="group.title" class="site-card site-tool-col-card">
                <h4>{{ group.title }}</h4>
                <ul class="site-check-list">
                  <li v-for="tool in group.tools" :key="tool.name">
                    <span class="site-check-glyph" aria-hidden="true">&#xE73D;</span>
                    <router-link v-if="tool.url && !tool.url.startsWith('http')" :to="tool.url">{{ tool.name }}</router-link>
                    <a v-else-if="tool.url" :href="tool.url" target="_blank" rel="noopener">{{ tool.name }}</a>
                    <span v-else>{{ tool.name }}</span>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- 支持我们 -->
      <section class="site-section">
        <div class="site-section-inner">
          <div class="site-card site-support-card">
            <h3>{{ t('home.support.title') }}</h3>
            <p>
              {{ t('home.support.text1') }}
              <a href="https://github.com/luolangaga/tubatool" target="_blank" rel="noopener">{{ t('home.support.star') }}</a>，
              <a href="https://github.com/luolangaga/tubatool/issues/new/choose" target="_blank" rel="noopener">{{ t('home.support.feedback') }}</a>，
              <a href="https://github.com/luolangaga/tubatool/blob/main/CONTRIBUTING.md" target="_blank" rel="noopener">{{ t('home.support.contribute') }}</a>，
              {{ t('home.support.text2') }}
            </p>
          </div>
        </div>
      </section>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed, inject, onMounted, onUnmounted, ref } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import SiteFooter from '../components/SiteFooter.vue';
import screenshotTools from '../../assets/site/screenshot-tools.png';
import screenshotHardware from '../../assets/site/screenshot-hardware.png';
import screenshotBuiltin from '../../assets/site/screenshot-builtin.png';
import { features } from '../services/features';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const navigate = inject('navigate', () => {});
const heroRef = ref(null);

/* 首页画廊精选：覆盖「进阶玩法」的关键功能 */
const galleryIds = ['ai-assistant', 'game-monitor', 'game-tunnel', 'format-converter', 'junk-cleaner', 'stress-test', 'cpu-ranking', 'traffic-monitor'];
const galleryItems = computed(() => galleryIds.map((id) => features.find((item) => item.id === id)).filter(Boolean));

const section1Items = computed(() => t('home.section1.items').split('|'));
const section2Items = computed(() => t('home.section2.items').split('|'));
const section3Items = computed(() => t('home.section3.items').split('|'));
const section4Items = computed(() => t('home.section4.items').split('|'));

const toolColumns = [
  [
    { title: '处理器工具', tools: [
      { name: 'CPU-Z' }, { name: 'Core Temp' }, { name: 'Prime95' }, { name: 'LinX' }, { name: 'SuperPI' } ] },
    { title: '显卡工具', tools: [
      { name: 'GPU-Z' }, { name: 'DDU' }, { name: 'FurMark' }, { name: 'NVFlash' }, { name: 'nvidiaInspector' } ] }
  ],
  [
    { title: '硬盘工具', tools: [
      { name: 'CrystalDiskMark' }, { name: 'CrystalDiskInfo' }, { name: 'AS SSD Benchmark' }, { name: 'DiskGenius' }, { name: 'HDTune' } ] },
    { title: '内存工具', tools: [
      { name: 'MemTest' }, { name: 'TM5' }, { name: 'Thaiphoon' }, { name: 'ZenTimings' }, { name: 'MemTest64' } ] }
  ],
  [
    { title: '综合检测', tools: [
      { name: 'AIDA64' }, { name: 'HWiNFO' }, { name: 'HWMonitor' }, { name: 'Speccy' }, { name: 'FanControl' } ] },
    { title: '显示器工具', tools: [
      { name: '色域检测' }, { name: '屏幕测试' }, { name: 'UFO 测试' } ] },
    { title: '烤鸡工具', tools: [
      { name: 'FurMark' }, { name: 'FurMark 64' }, { name: 'y-cruncher' } ] }
  ],
  [
    { title: '外设工具', tools: [
      { name: '键盘测试' }, { name: 'MouseTester' }, { name: '鼠标回报率' }, { name: 'KeyTweak' }, { name: '鼠标单击变双击' } ] },
    { title: '内置工具', tools: [
      { name: 'AI 助手' }, { name: '游戏联机助手' }, { name: '一键三烤' }, { name: '格式转换' }, { name: '垃圾清理' }, { name: '流氓软件的克星' }, { name: '端口占用' }, { name: '时间同步' }, { name: '启动项管理' }, { name: 'Windows 镜像' }, { name: t('home.tools.more'), url: '/features' } ] }
  ]
];

const goDownload = () => navigate('download');
const goFeatures = () => navigate('features');
const openGithub = () => window.open(t('app.repository'), '_blank', 'noopener');
const goWhyChoose = () => navigate('why');

let parallaxObserver = null;
let reducedMotion = false;

onMounted(() => {
  reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (reducedMotion) return;
  parallaxObserver = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add('site-feature-visible');
      } else {
        entry.target.classList.remove('site-feature-visible');
      }
    });
  }, { threshold: 0.12 });
  document.querySelectorAll('.site-feature-row, .site-tool-col, .site-support-card').forEach((el) => {
    parallaxObserver.observe(el);
  });
});

onUnmounted(() => {
  parallaxObserver?.disconnect();
});
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
</style>
