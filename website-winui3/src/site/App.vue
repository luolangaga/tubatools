<template>
  <WinToolTipService />
  <WinTitleBar
    ref="titleBarRef"
    class="site-titlebar"
    :Title="t('app.title')"
    :Subtitle="t('site.slogan')"
    :IsPaneToggleButtonVisible="!isTopNavMode"
    TitleBarContentHorizontalAlignment="Stretch"
    :IconSource="appIconSource"
    @PaneToggleRequested="onTopBarToggle">
    <WinAutoSuggestBox
      v-model:Text="searchQuery"
      v-model:IsSuggestionListOpen="searchPanelOpen"
      :ItemsSource="searchSuggestions"
      TextMemberPath="title"
      :PlaceholderText="t('search.placeholder')"
      QueryIcon="Find"
      :OpenOnFocus="false"
      class="site-search-box"
      @QuerySubmitted="onSearchQuerySubmitted" />
  </WinTitleBar>
  <div class="site-app-content">
    <div class="site-nav-host">
      <WinNavigationView
        :SelectedItem="selectedNavigationItem"
        :PaneDisplayMode="navPosition"
        :MenuItems="navMenuItems"
        :FooterMenuItems="navFooterItems"
        v-model:IsPaneOpen="isPaneOpen"
        IsBackButtonVisible="Collapsed"
        :IsPaneToggleButtonVisible="false"
        :IsSettingsVisible="false"
        @ItemInvoked="onNavigationItemInvoked">
        <router-view v-slot="{ Component }">
          <Transition
            appear
            :enter-active-class="pageTransitionEnter"
            :leave-active-class="pageTransitionLeave">
            <div
              v-if="Component"
              :key="route.name"
              class="site-page-view active">
              <component :is="Component" />
            </div>
          </Transition>
        </router-view>
      </WinNavigationView>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, provide, computed } from 'vue';
import WinTitleBar from '../components/WinTitleBar.vue';
import WinNavigationView from '../components/WinNavigationView.vue';
import WinToolTipService from '../components/WinToolTipService.vue';
import WinAutoSuggestBox from '../components/WinAutoSuggestBox.vue';
import { useRoute, useRouter } from 'vue-router';
import { pageTags } from './router';
import { searchAll, suggest } from './searchIndex';
import logoUrl from '../assets/site/logo.png';
import { useI18n } from '../components/i18n/index';
import {
  DefaultNavigationTransitionInfo,
  NavigationTrigger_BackNavigatingAway,
  NavigationTrigger_BackNavigatingTo,
  NavigationTrigger_NavigatingAway,
  NavigationTrigger_NavigatingTo,
  getNavigationTransitionInfoClassName
} from '../utils/navigationTransitionInfo';

const { t } = useI18n();

const titleBarRef = ref(null);
const route = useRoute();
const router = useRouter();
const currentPage = computed(() => (typeof route.name === 'string' ? route.name : 'home'));
const navPosition = ref('Top');
const isTopNavMode = computed(() => navPosition.value === 'Top');
const isPaneOpen = ref(localStorage.getItem('winui-pane-open') === 'true');
const themeSetting = ref(readStoredSetting('winui-theme-setting', 'system', ['system', 'light', 'dark']));

/* 官方 WinUI 页面切换过渡（与 WinUI Gallery 一致） */
const navigationTransitionInfo = ref(DefaultNavigationTransitionInfo);
const pageTransitionEnter = ref(getNavigationTransitionInfoClassName(navigationTransitionInfo.value, NavigationTrigger_NavigatingTo));
const pageTransitionLeave = ref(getNavigationTransitionInfoClassName(navigationTransitionInfo.value, NavigationTrigger_NavigatingAway));

router.afterEach((to, from) => {
  const historyState = router.options.history.state;
  const isBack = historyState?.forward === from.fullPath;
  const NavigationTrigger = isBack
    ? NavigationTrigger_BackNavigatingTo
    : NavigationTrigger_NavigatingTo;
  const NavigationLeaveTrigger = isBack
    ? NavigationTrigger_BackNavigatingAway
    : NavigationTrigger_NavigatingAway;
  pageTransitionEnter.value = getNavigationTransitionInfoClassName(navigationTransitionInfo.value, NavigationTrigger);
  pageTransitionLeave.value = getNavigationTransitionInfoClassName(navigationTransitionInfo.value, NavigationLeaveTrigger);
  updateSeo(to);
});

/* 按原官网配置的页面级 SEO（title/description/og/canonical + 可选 JSON-LD 结构化数据） */
const pageSeo = {
  home: {
    title: '图吧工具箱CE——PC硬件检测与系统维护工具集',
    description: '图吧工具箱CE官方下载站。原版图吧工具箱的社区重构版（Community Edition），专业的PC硬件检测与系统维护工具集，收录134个工具条目与47款内置工具，支持CPU-Z、GPU-Z、CrystalDiskMark等一键启动，WinUI 3原生界面，完全免费离线运行，零数据收集。',
    url: 'https://tubawinui3.cn/'
  },
  download: {
    title: '图吧工具箱CE下载——免费PC硬件检测与系统维护工具集',
    description: '图吧工具箱CE官方下载页。下载最新版图吧工具箱CE，完全免费、纯离线运行，支持x64/ARM64全架构，一键安装即可使用134个工具条目与47款内置硬件检测与系统维护工具。',
    url: 'https://tubawinui3.cn/download'
  },
  features: {
    title: '功能展示——图吧工具箱CE 截图与内置工具一览',
    description: '图吧工具箱CE功能展示：134 个工具条目、47 款内置工具的实机截图，涵盖硬件信息、一键三烤、游戏监控覆盖层、游戏联机助手、AI 助手、垃圾清理、格式转换、网速测试、时间同步等。',
    url: 'https://tubawinui3.cn/features'
  },
  about: {
    title: '关于图吧工具箱CE——PC硬件检测与系统维护工具集',
    description: '图吧工具箱CE——原版图吧工具箱的社区重构版（Community Edition），免费、开源、注重隐私的PC硬件检测与系统维护工具集，WinUI 3原生界面，完全免费离线运行。',
    url: 'https://tubawinui3.cn/about'
  },
  why: {
    title: '为什么选择图吧工具箱CE？——全面对比原版工具箱',
    description: '对比图吧工具箱CE与原版：多渠道高速下载、精美WinUI 3界面、完美UTF-8支持、20+FluentUI内置工具、AI智能体驱动。',
    url: 'https://tubawinui3.cn/why'
  },
  ranking: {
    title: '跑分排行——图吧工具箱CE社区跑分排行榜',
    description: '查看图吧工具箱CE社区用户上传的性能跑分排行榜，按游戏性能、办公性能、CPU、GPU、硬盘、浏览器等维度排序，与全球用户对比电脑性能。',
    url: 'https://tubawinui3.cn/ranking'
  },
  latency: {
    title: '核间延迟查询——CPU核心间通信延迟热力图',
    description: '查看社区上传的 CPU 核间延迟热力图，对比不同处理器型号的核心间通信延迟，了解跨核心通信延迟表现。',
    url: 'https://tubawinui3.cn/latency'
  },
  mushroom: {
    title: '毒蘑菇测试 VolumeShader——在线 GPU 分形压力测试',
    description: '毒蘑菇测试（VolumeShader）在线版：与图吧工具箱CE桌面版同款 WebGL 体素分形渲染引擎的 GPU 压力测试，轻松/中等/变态三档压力、超分辨率渲染、实时帧率监控，浏览器打开即测，检验显卡稳定性与散热。',
    url: 'https://tubawinui3.cn/mushroom',
    jsonLd: {
      '@context': 'https://schema.org',
      '@type': 'WebApplication',
      name: '毒蘑菇测试 VolumeShader',
      alternateName: '毒蘑菇测试',
      url: 'https://tubawinui3.cn/mushroom',
      applicationCategory: 'UtilitiesApplication',
      operatingSystem: 'Any',
      browserRequirements: 'WebGL',
      description: '毒蘑菇测试（VolumeShader）在线版：与图吧工具箱CE桌面版同款 WebGL 体素分形渲染引擎的 GPU 压力测试，轻松/中等/变态三档压力、超分辨率渲染、实时帧率监控，检验显卡稳定性与散热。',
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'CNY' }
    }
  },
  thanks: {
    title: '感谢下载图吧工具箱CE——免费PC硬件检测与系统维护工具集',
    description: '感谢下载图吧工具箱CE，完全免费、纯离线运行，支持x64/ARM64全架构。',
    url: 'https://tubawinui3.cn/download/thanks'
  }
};

const siteName = '图吧工具箱CE';

function setMeta(attr, name, content) {
  const selector = attr === 'meta'
    ? `meta[name="${name}"], meta[property="${name}"]`
    : `link[rel="${name}"]`;
  let el = document.head.querySelector(selector);
  if (!el) {
    el = document.createElement(attr === 'meta' ? 'meta' : 'link');
    if (attr === 'meta') {
      el.setAttribute(name.startsWith('og:') || name.startsWith('twitter:') ? 'property' : 'name', name);
    } else {
      el.setAttribute('rel', name);
    }
    document.head.appendChild(el);
  }
  el.setAttribute(attr === 'meta' ? 'content' : 'href', content);
}

function updateSeo(to) {
  const name = typeof to?.name === 'string' ? to.name : 'home';
  const seo = pageSeo[name];
  if (!seo) return;
  document.title = seo.title;
  setMeta('meta', 'description', seo.description);
  setMeta('meta', 'og:title', seo.title);
  setMeta('meta', 'og:description', seo.description);
  setMeta('meta', 'og:site_name', siteName);
  setMeta('meta', 'og:url', seo.url);
  setMeta('link', 'canonical', seo.url);

  /* 页面级 JSON-LD 结构化数据（单页唯一，其他页面不残留） */
  const jsonLd = seo.jsonLd;
  const prevLd = document.getElementById('seo-jsonld');
  if (jsonLd) {
    const el = prevLd ?? document.createElement('script');
    el.id = 'seo-jsonld';
    el.type = 'application/ld+json';
    el.textContent = JSON.stringify(jsonLd);
    if (!prevLd) document.head.appendChild(el);
  } else if (prevLd) {
    prevLd.remove();
  }
}

provide('themeSetting', themeSetting);
provide('navPosition', navPosition);

const appIconSource = { ImageSource: logoUrl };

/* GitHub 品牌图标（官方 octocat mark，mask + currentColor 渲染自动适配主题） */
const githubMarkSvg = 'data:image/svg+xml;utf8,' + encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>'
);

const navMenuItems = [
  { Tag: 'home', Icon: '\uE80F', Content: t('nav.home') },
  { Tag: 'download', Icon: '\uE896', Content: t('nav.download') },
  { Tag: 'features', Icon: '\uE91B', Content: t('nav.features') },
  { Tag: 'docs', Icon: '\uE8A1', Content: t('nav.docs') },
  { Tag: 'ranking', Icon: '\uE9D5', Content: t('nav.ranking') },
  { Tag: 'latency', Icon: '\uE9D9', Content: t('nav.latency') },
  { Tag: 'mushroom', Icon: '\uE9F5', Content: t('nav.mushroom') },
  { Tag: 'about', Icon: '\uE946', Content: t('nav.about') }
];

const navFooterItems = [
  { Tag: 'github', Icon: { ImageSource: githubMarkSvg }, Content: t('nav.github') }
];

/* --- 全站搜索（文档 + 页面） --- */

const searchQuery = ref('');
const searchPanelOpen = ref(false);
const searchSuggestions = computed(() => suggest(searchQuery.value).map((hit) => ({
  title: hit.title,
  subtitle: hit.subtitle,
  url: hit.url,
  cat: hit.cat,
  file: hit.file,
  tag: hit.tag
})));

/* 组件在首次输入时因 prop 异步更新来不及打开面板，这里受控强制打开 */
watch(searchSuggestions, (items) => {
  if (searchQuery.value.trim() && items.length > 0) {
    searchPanelOpen.value = true;
  }
});

const onSearchQuerySubmitted = ({ QueryText, ChosenSuggestion }) => {
  const query = String(QueryText ?? '').trim();
  if (!query) return;
  /* 回车直接跳转到最佳匹配结果（选中建议优先，否则取第一条） */
  const target = ChosenSuggestion ?? searchSuggestions.value[0];
  if (target?.url) {
    if (target.cat && target.file) {
      router.push(`/${target.cat}/${target.file}`);
    } else if (target.tag) {
      router.push({ name: target.tag });
    } else {
      router.push(target.url);
    }
    searchQuery.value = '';
    searchPanelOpen.value = false;
  }
};

const selectedNavigationItem = computed({
  get: () => {
    if (currentPage.value === 'thanks') {
      return navMenuItems.find(entry => entry.Tag === 'download') ?? navMenuItems[0];
    }
    const item = navMenuItems.find(entry => entry.Tag === currentPage.value);
    return item ?? navMenuItems[0];
  },
  set: item => {
    if (item?.Tag) navigate(item.Tag);
  }
});

const navigate = tag => {
  if (!tag || tag === currentPage.value || !pageTags.has(tag)) return;
  if (tag === 'docs') {
    router.push('/guide/getting-started');
    return;
  }
  router.push({ name: tag });
};
provide('navigate', navigate);

const onNavigationItemInvoked = args => {
  const item = args?.InvokedItemContainer;
  if (!item || item.SelectsOnInvoked === false) return;
  if (item.Tag === 'github') {
    window.open(t('app.repository'), '_blank', 'noopener');
    return;
  }
  if (item.Tag) navigate(item.Tag);
};

const onTopBarToggle = () => {
  isPaneOpen.value = !isPaneOpen.value;
};

function readStoredSetting(key, fallback, allowedValues) {
  const value = localStorage.getItem(key);
  return allowedValues.includes(value) ? value : fallback;
}

const persistSetting = (key, source) => {
  watch(source, (value) => {
    localStorage.setItem(key, value);
  }, { immediate: true });
};

function applyTheme(mode) {
  const html = document.documentElement;
  html.classList.remove('theme-light', 'theme-dark');
  if (mode === 'light') html.classList.add('theme-light');
  else if (mode === 'dark') html.classList.add('theme-dark');
}

watch(themeSetting, (val) => applyTheme(val), { immediate: true });
persistSetting('winui-theme-setting', themeSetting);
persistSetting('winui-pane-open', isPaneOpen);

const updateThemeColor = () => {
  const mode = themeSetting.value;
  const isDark = mode === 'dark' || (
    mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches
  );
  const color = isDark ? '#202020' : '#f3f3f3';
  let meta = document.querySelector('meta[name="theme-color"]');
  if (!meta) {
    meta = document.createElement('meta');
    meta.name = 'theme-color';
    document.head.appendChild(meta);
  }
  meta.setAttribute('content', color);
};
const systemThemeQuery = window.matchMedia('(prefers-color-scheme: dark)');
const onSystemThemeChange = () => {
  if (themeSetting.value === 'system') updateThemeColor();
};
watch(themeSetting, () => updateThemeColor(), { immediate: true });
systemThemeQuery.addEventListener('change', onSystemThemeChange);
</script>

<style>
  @import '../styles/theme.css';
  @import '../styles/animations.css';

  @font-face {
    font-family: 'WinUIOnWebIcons';
    src: url('../assets/Fonts/SEGOEICONS.TTF') format('truetype');
    font-display: block;
  }

  html, body, #app {
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    margin: 0;
    padding: 0;
    overflow: hidden;
  }

  body .icon,
  body .icon-btn,
  body .ptr-icon-wrapper,
  body .symbol-icon,
  body .win-symbol-icon,
  body .win-asb-icon,
  body .picker-icon,
  body .checkbox-glyph,
  body .win-combo-chevron,
  body .win-cbf-icon,
  body .win-cbf-overflow-icon,
  body .win-expander-header-icon,
  body .win-expander-arrow,
  body .infobadge-icon,
  body .close-icon,
  body .win-menu-flyout-icon,
  body .win-menu-flyout-check,
  body .win-menu-flyout-check-placeholder,
  body .win-menu-flyout-chevron,
  body .win-number-spin-button span,
  body .win-number-compact-indicator span,
  body .win-number-popup-button span,
  body .win-password-reveal span,
  body .win-rating-glyph,
  body .scrollbar-button,
  body .win-settings-card-icon,
  body .win-settings-card-action-icon,
  body .win-teaching-tip-icon,
  body .win-teaching-tip-close,
  body .win-textbox-delete-glyph,
  body .font-icon,
  body .icon-glyph,
  body .icon-preview-glyph,
  body .group-icon,
  body .tree-icon {
    font-family: 'WinUIOnWebIcons';
  }

  /* 顶栏固定为展开高度（48px），避免组件因无内容插槽进入 32px 紧凑模式产生留白 */
  .site-titlebar.win-titlebar.is-compact-height {
    height: 48px !important;
  }

  .site-search-box {
    width: 100%;
    max-width: 340px;
    justify-self: end;
    margin-right: 16px;
  }

  /* 窄屏时隐藏副标题腾出空间，保留主标题与搜索框 */
  .site-titlebar.is-narrow .win-titlebar-subtitle,
  .site-titlebar.is-compact .win-titlebar-subtitle {
    display: none !important;
  }

  .site-titlebar.is-narrow .site-search-box,
  .site-titlebar.is-compact .site-search-box {
    max-width: 200px;
    margin-right: 8px;
  }

  /* 窄屏下导航栏 footer（在 GitHub 查看源码）只显示图标 */
  @media (max-width: 900px) {
    .win-nav-top-footer-menu .win-nav-item .label {
      display: none !important;
    }

    .win-nav-top-footer-menu .win-nav-item {
      padding-left: 8px;
      padding-right: 8px;
    }
  }

  .site-titlebar .win-titlebar-content {
    display: flex;
    justify-content: flex-end;
    padding-right: 8px;
  }

  .site-app-content {
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    display: flex;
    flex-direction: column;
    box-sizing: border-box;
    padding-top: max(env(titlebar-area-height, 0px), 48px);
  }

  /* NavigationView 内容区改为 flex 列，让页面滚动容器高度受约束（否则内容被撑开无法滚动） */
  .win-nav-content-inner {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }

  /* 文档子导航（Left 模式）去掉内容区左上角圆角与边框 */
  .win-nav-shell.is-left.docs-subnav > .win-nav-content {
    border-radius: 0 !important;
    border-top: 0 !important;
    border-left: 0 !important;
  }

  .site-nav-host {
    flex: 1 1 auto;
    min-width: 0;
    min-height: 0;
    display: flex;
  }

  .site-nav-host > .win-nav-shell {
    width: 100%;
    height: 100%;
  }

  .site-page-view {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    min-width: 0;
    min-height: 0;
    overflow: hidden;
    /* 官网内容允许复制文字（控件按钮等除外）。
       theme.css 的全局 *{user-select:none} 直接作用于每个元素，必须逐元素反覆盖。 */
    user-select: text;
    -webkit-user-select: text;
  }

  .site-page-view * {
    user-select: text;
    -webkit-user-select: text;
  }

  .site-page-view button,
  .site-page-view .win-btn,
  .site-page-view .win-nav-item,
  .site-page-view .docs-file-list a,
  .site-page-view .docs-cat-btn {
    user-select: none;
    -webkit-user-select: none;
  }

  .site-page-view.active {
    display: flex;
    flex-direction: column;
    height: 100%;
    min-height: 0;
  }

  .site-page-view.active > .site-page-scroll,
  .site-page-view.active > .site-home-scroll {
    flex: 1 1 auto;
    min-height: 0;
  }

  /* 页面切换过渡由官方 WinUI 导航过渡动画（animations.css）接管 */

  /* ---------- 站点通用排版 ---------- */

  .site-page-scroll {
    height: 100%;
  }

  .site-page-inner {
    max-width: 1080px;
    margin: 0 auto;
    padding: 24px 36px 48px 36px;
    box-sizing: border-box;
  }

  .site-page-header h1 {
    margin: 0 0 4px 0;
    font-size: 28px;
    font-weight: 600;
    line-height: 36px;
    color: var(--text-primary);
  }

  .site-page-header p {
    margin: 0;
    font-size: 14px;
    line-height: 20px;
    color: var(--text-secondary);
  }

  .site-page-header {
    margin-bottom: 24px;
  }

  .site-card {
    box-sizing: border-box;
    background: var(--card-bg);
    border: 1px solid var(--card-stroke);
    border-radius: var(--ControlCornerRadius, 8px);
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03), 0 4px 16px rgba(0, 0, 0, 0.04);
  }

  .site-section-title {
    margin: 0 0 8px 0;
    font-size: 24px;
    font-weight: 600;
    line-height: 32px;
    color: var(--text-primary);
  }

  .site-section-subtitle {
    margin: 0 0 24px 0;
    font-size: 14px;
    line-height: 20px;
    color: var(--text-secondary);
  }

  .site-check-list {
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .site-check-list li {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    margin: 6px 0;
    font-size: 14px;
    line-height: 20px;
    color: var(--text-primary);
  }

  .site-check-list .site-check-glyph {
    flex: 0 0 auto;
    width: 16px;
    height: 20px;
    font-size: 12px;
    line-height: 20px;
    font-family: 'WinUIOnWebIcons';
    color: var(--SystemFillColorSuccessBrush, #0F7B0F);
  }
</style>
