<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="thanks-page">
      <div class="thanks-shell">
        <!-- 面包屑：与 dotnet 下载页一致 -->
        <div class="thanks-crumbs">
          <WinBreadcrumbBar :ItemsSource="breadcrumbItems" @ItemClicked="onBreadcrumbInvoked" />
        </div>

        <header class="thanks-head">
          <h1 class="thanks-title">{{ t('thanks.heading') }}</h1>
          <p class="thanks-product">{{ productLine }}</p>
          <p class="thanks-source" v-if="downloadUrl">{{ t('thanks.source-line', { source: sourceLabel }) }}</p>
          <p class="thanks-lead" v-if="downloadUrl">
            <span class="thanks-started">{{ t('thanks.lead-start') }}</span>
            {{ t('thanks.lead-before', { seconds: 30 }) }}<button
              type="button"
              class="thanks-inline-link"
              @click="manualDownload">{{ t('thanks.lead-link') }}</button>{{ t('thanks.lead-end') }}
          </p>
        </header>

        <WinInfoBar
          v-if="!downloadUrl"
          class="thanks-alert"
          IsOpen
          :IsClosable="false"
          Severity="Error"
          :Title="t('thanks.empty-title')"
          :Message="t('thanks.empty-desc')" />

        <!-- 直接链接 / 校验和 -->
        <section v-if="downloadUrl" class="thanks-links" :aria-label="t('thanks.links-label')">
          <div class="thanks-link-row">
            <div class="thanks-link-label">{{ t('thanks.direct-link') }}</div>
            <div class="thanks-link-main">
              <span class="thanks-link-url" :title="downloadUrl">{{ downloadUrl }}</span>
              <WinButton
                class="thanks-copy"
                Height="32"
                Padding="10,0"
                :IsEnabled="Boolean(downloadUrl)"
                :title="t('thanks.copy')"
                :aria-label="t('thanks.copy')"
                @Click="copyText(downloadUrl, 'link')">
                <span class="thanks-copy-inner">
                  <span class="thanks-glyph" aria-hidden="true">{{ copiedKey === 'link' ? '&#xE73E;' : '&#xE8C8;' }}</span>
                  <span>{{ copiedKey === 'link' ? t('thanks.copied') : t('thanks.copy') }}</span>
                </span>
              </WinButton>
            </div>
          </div>

          <div class="thanks-link-row">
            <div class="thanks-link-label">{{ t('thanks.checksum') }}</div>
            <div class="thanks-link-main">
              <span v-if="checksumState === 'loading'" class="thanks-link-muted thanks-inline-loading">
                <WinProgressRing Width="14" Height="14" IsActive="True" />
                {{ t('thanks.checksum-loading') }}
              </span>
              <span v-else-if="checksum" class="thanks-link-url thanks-mono">{{ checksum }}</span>
              <span v-else class="thanks-link-muted">{{ t('thanks.checksum-unavailable') }}</span>
              <WinButton
                v-if="checksum"
                class="thanks-copy"
                Height="32"
                Padding="10,0"
                :title="t('thanks.copy')"
                :aria-label="t('thanks.copy')"
                @Click="copyText(checksum, 'hash')">
                <span class="thanks-copy-inner">
                  <span class="thanks-glyph" aria-hidden="true">{{ copiedKey === 'hash' ? '&#xE73E;' : '&#xE8C8;' }}</span>
                  <span>{{ copiedKey === 'hash' ? t('thanks.copied') : t('thanks.copy') }}</span>
                </span>
              </WinButton>
            </div>
          </div>
        </section>

        <p v-if="checksum" class="thanks-verify">
          {{ t('thanks.verify') }}<button
            type="button"
            class="thanks-inline-link"
            :aria-expanded="verifyOpen"
            @click="verifyOpen = !verifyOpen">{{ t('thanks.verify-link') }}</button>{{ t('thanks.verify-end') }}
        </p>

        <div v-if="checksum && verifyOpen" class="thanks-verify-panel">
          <h4 class="thanks-verify-title">{{ t('thanks.verify-title') }}</h4>
          <ol class="thanks-verify-steps">
            <li>{{ t('thanks.verify-step1') }}</li>
            <li>
              {{ t('thanks.verify-step2') }}
              <div class="thanks-cmd">
                <code>Get-FileHash .\{{ assetName }} -Algorithm SHA256</code>
                <WinButton
                  class="thanks-copy thanks-cmd-copy"
                  Height="30"
                  Padding="8,0"
                  @Click="copyText('Get-FileHash .' + BACKSLASH + assetName + ' -Algorithm SHA256', 'cmd')">
                  <span class="thanks-copy-inner">
                    <span class="thanks-glyph" aria-hidden="true">{{ copiedKey === 'cmd' ? '&#xE73E;' : '&#xE8C8;' }}</span>
                    <span>{{ copiedKey === 'cmd' ? t('thanks.copied') : t('thanks.copy') }}</span>
                  </span>
                </WinButton>
              </div>
            </li>
            <li>{{ t('thanks.verify-step3') }}</li>
          </ol>
        </div>

        <div class="thanks-actions">
          <WinButton
            v-if="downloadUrl"
            Style="AccentButtonStyle"
            :Content="t('thanks.download-again')"
            Height="38"
            Padding="20,0"
            @Click="manualDownload" />
          <WinButton
            :Content="t('thanks.back-download')"
            Height="38"
            Padding="16,0"
            @Click="goDownload" />
          <WinHyperlinkButton
            NavigateUri="https://github.com/luolangaga/tubatools/issues"
            TargetName="_blank"
            :Content="t('thanks.feedback')" />
        </div>

        <!-- 安装向导 / 首次启动预览 -->
        <figure v-if="downloadUrl" class="thanks-preview">
          <div v-if="type === 'setup'" class="installer-window" role="img" :aria-label="t('thanks.preview-installer')">
            <div class="installer-titlebar">
              <img class="installer-titlebar-icon" :src="logoUrl" alt="" />
              <span class="installer-titlebar-text">{{ t('thanks.installer-title') }}</span>
              <span class="installer-titlebar-controls" aria-hidden="true">
                <span>&#xE921;</span><span>&#xE922;</span><span>&#xE923;</span>
              </span>
            </div>
            <div class="installer-body">
              <div class="installer-head">
                <img class="installer-appicon" :src="logoUrl" alt="" />
                <div class="installer-head-copy">
                  <h4>{{ t('thanks.installer-heading') }}</h4>
                  <p>{{ t('thanks.installer-sub', { version: displayVersion }) }}</p>
                </div>
              </div>

              <div class="installer-field">
                <span class="installer-field-label">{{ t('thanks.installer-folder') }}</span>
                <div class="installer-input">
                  <span>C:\Program Files\TubaWinUi3</span>
                  <span class="installer-browse">{{ t('thanks.installer-browse') }}</span>
                </div>
              </div>

              <div class="installer-tasks">
                <span class="installer-task"><i class="installer-box" />{{ t('thanks.installer-desktop') }}</span>
                <span class="installer-task"><i class="installer-box checked">&#xE73E;</i>{{ t('thanks.installer-startmenu') }}</span>
              </div>

              <div class="installer-footer">
                <span class="installer-hint">{{ t('thanks.installer-hint') }}</span>
                <div class="installer-buttons">
                  <span class="installer-btn">{{ t('thanks.installer-cancel') }}</span>
                  <span class="installer-btn primary">{{ t('thanks.installer-install') }}</span>
                </div>
              </div>
            </div>
          </div>

          <div v-else class="app-window">
            <div class="app-window-titlebar">
              <img :src="logoUrl" alt="" />
              <span>{{ t('thanks.app-window-title') }}</span>
              <span class="app-window-controls" aria-hidden="true">
                <span>&#xE921;</span><span>&#xE922;</span><span>&#xE923;</span>
              </span>
            </div>
            <img class="app-window-shot" src="/screenshots/home.png" :alt="t('thanks.app-shot-alt')" loading="lazy" />
          </div>

          <figcaption class="thanks-caption">
            {{ type === 'setup' ? t('thanks.preview-installer') : t('thanks.preview-portable') }}
          </figcaption>
        </figure>

        <SiteFooter />
      </div>
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import WinHyperlinkButton from '../../components/WinHyperlinkButton.vue';
import WinBreadcrumbBar from '../../components/WinBreadcrumbBar.vue';
import WinInfoBar from '../../components/WinInfoBar.vue';
import WinProgressRing from '../../components/WinProgressRing.vue';
import SiteFooter from '../components/SiteFooter.vue';
import { useI18n } from '../../components/i18n/index';
import logoUrl from '../../assets/site/logo.png';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();

const GH_OWNER = 'luolangaga';
const GH_REPO = 'tubatools';
const GC_OWNER = 'gcw_uDDNaqJw';
const GC_REPO = 'tubatool';

const version = computed(() => String(route.query.version ?? '').replace(/^v/, ''));
const arch = computed(() => String(route.query.arch ?? 'x64').toLowerCase() || 'x64');
const type = computed(() => String(route.query.type ?? 'portable'));
const downloadUrl = computed(() => String(route.query.url ?? ''));

const displayVersion = computed(() => version.value || t('thanks.latest'));
const archLabel = computed(() => (arch.value === 'arm64' ? 'ARM64' : arch.value === 'x86' ? 'x86' : 'x64'));
const typeLabel = computed(() => {
  if (type.value === 'setup') return t('download.setup');
  if (type.value === 'lite') return t('download.lite');
  return t('download.portable');
});

const productLine = computed(() =>
  t('thanks.product', { version: displayVersion.value, arch: archLabel.value, type: typeLabel.value })
);

const sourceLabel = computed(() => {
  const url = downloadUrl.value;
  if (!url) return '—';
  try {
    const host = new URL(url).host.toLowerCase();
    if (host.includes('gitcode')) return 'GitCode';
    if (host.includes('github')) return 'GitHub';
    if (host.includes('quark')) return t('download.quark');
    if (host.includes('baidu')) return t('download.baidu');
    return host;
  } catch {
    return '—';
  }
});

/* ---------- 面包屑 ---------- */
const assetName = computed(() => {
  const url = downloadUrl.value;
  if (url) {
    try {
      const base = decodeURIComponent(new URL(url).pathname.split('/').pop() || '');
      if (/\.(zip|exe)$/i.test(base)) return base;
    } catch {
      /* ignore */
    }
  }
  if (!version.value) return '';
  const v = version.value;
  if (type.value === 'setup') return `TubaWinUi3_Setup_${v}_${archLabel.value.toLowerCase() === 'arm64' ? 'arm64' : 'x64'}.exe`;
  if (type.value === 'lite') return `TubaWinUi3_Lite_${v}_x64.zip`;
  return `TubaWinUi3_Portable_${v}_${archLabel.value.toLowerCase() === 'arm64' ? 'arm64' : 'x64'}.zip`;
});

const breadcrumbItems = computed(() => {
  const items = [t('thanks.crumb-home'), t('thanks.crumb-download'), t('thanks.brand')];
  if (version.value) items.push(`v${version.value}`);
  items.push(assetName.value || typeLabel.value);
  return items;
});

function onBreadcrumbInvoked(_sender, args) {
  const index = args?.Index;
  if (index === 0) router.push({ name: 'home' });
  else if (index === 1) router.push({ name: 'download' });
}

/* ---------- 校验和 ---------- */
const checksum = ref('');
const checksumState = ref('idle');

async function fetchJson(url) {
  try {
    const response = await fetch(url, { headers: { Accept: 'application/json' } });
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

function extractChecksum(body, name) {
  if (!body || !name) return '';
  const rows = body.split(/\r?\n/);
  const row = rows.find(line => line.includes(name));
  if (!row) return '';
  const hash = /\b([a-fA-F0-9]{64})\b/.exec(row);
  return hash ? hash[1] : '';
}

async function loadChecksum() {
  if (!assetName.value || !version.value) {
    checksumState.value = 'unavailable';
    return;
  }

  // 网盘分享页里的文件由网盘托管，官方 Release 的哈希不适用于该副本
  try {
    const host = new URL(downloadUrl.value).host.toLowerCase();
    if (host.includes('pan.quark.cn') || host.includes('pan.baidu.com')) {
      checksumState.value = 'unavailable';
      return;
    }
  } catch {
    checksumState.value = 'unavailable';
    return;
  }

  const tag = `v${version.value}`;
  const cacheKey = `tuba-checksum:${tag}:${assetName.value}`;
  const cached = typeof sessionStorage !== 'undefined' ? sessionStorage.getItem(cacheKey) : null;
  if (cached) {
    checksum.value = cached;
    checksumState.value = 'ok';
    return;
  }

  checksumState.value = 'loading';
  const preferGitHub = sourceLabel.value !== 'GitCode';

  const fromGitHub = async () => {
    const release = await fetchJson(`https://api.github.com/repos/${GH_OWNER}/${GH_REPO}/releases/tags/${tag}`);
    return extractChecksum(release?.body, assetName.value);
  };
  const fromGitCode = async () => {
    const list = await fetchJson(
      `https://api.gitcode.com/api/v5/repos/${GC_OWNER}/${GC_REPO}/releases?per_page=30&direction=desc`
    );
    const release = Array.isArray(list) ? list.find(item => item?.tag_name === tag) : null;
    return extractChecksum(release?.body, assetName.value);
  };

  let hash = '';
  try {
    hash = preferGitHub ? await fromGitHub() : await fromGitCode();
    if (!hash) hash = preferGitHub ? await fromGitCode() : await fromGitHub();
  } catch {
    hash = '';
  }

  if (hash) {
    checksum.value = hash;
    checksumState.value = 'ok';
    try {
      sessionStorage.setItem(cacheKey, hash);
    } catch {
      /* ignore quota */
    }
  } else {
    checksumState.value = 'unavailable';
  }
}

/* ---------- 复制 ---------- */
const BACKSLASH = String.fromCharCode(92);
const copiedKey = ref('');
let copiedTimer = null;

async function copyText(text, key) {
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.cssText = 'position:fixed;opacity:0';
    document.body.appendChild(area);
    area.select();
    document.execCommand('copy');
    document.body.removeChild(area);
  }
  copiedKey.value = key;
  clearTimeout(copiedTimer);
  copiedTimer = setTimeout(() => {
    copiedKey.value = '';
  }, 1600);
}

/* ---------- 下载动作 ---------- */
function manualDownload() {
  if (!downloadUrl.value) return;
  window.open(downloadUrl.value, '_blank', 'noopener');
}

function goDownload() {
  router.push({ name: 'download' });
}

onMounted(() => {
  loadChecksum();
});

onBeforeUnmount(() => {
  clearTimeout(copiedTimer);
});
</script>

<style scoped src="./thanks-page.css"></style>
