<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner">
      <header class="site-page-header">
        <h1>{{ t('download.title') }}</h1>
        <p>{{ t('download.subtitle') }}</p>
      </header>

      <!-- 元信息栏 -->
      <div class="site-card dl-meta-bar">
        <div class="dl-meta-item dl-version">
          <span class="dl-meta-label">{{ t('download.version') }}</span>
          <span v-if="version" class="dl-version-num">{{ version }}</span>
          <span v-else class="dl-version-num dl-muted">{{ t('download.loading') }}</span>
          <WinProgressRing v-if="!version" Width="16" Height="16" IsActive="True" />
        </div>
        <div class="dl-meta-item dl-arch-detect">
          <span class="dl-meta-label">{{ t('download.detect-arch') }}</span>
          <strong>{{ detectedArchLabel }}</strong>
          <span v-if="detectedArch !== 'unknown'" class="dl-recommend-badge">{{ t('download.recommended') }}</span>
        </div>
        <div class="dl-meta-item dl-arch-picker">
          <span class="dl-meta-label">CPU</span>
          <WinComboBox
            v-model:SelectedValue="selectedArch"
            :ItemsSource="archOptions"
            DisplayMemberPath="label"
            SelectedValuePath="value"
            Width="140"
            HorizontalAlignment="Left" />
        </div>
      </div>

      <!-- 下载卡片 -->
      <div class="dl-cards">
        <div class="site-card dl-card">
          <div class="dl-card-header">
            <span class="icon" aria-hidden="true">&#xE7B8;</span>
            <h3>{{ t('download.setup') }}</h3>
          </div>
          <p class="dl-card-desc">{{ t('download.setup-desc') }}</p>
          <div class="dl-card-section">
            <h5>{{ t('download.gitcode') }} <span class="dl-badge">{{ t('download.gitcode-badge') }}</span></h5>
            <div class="dl-btns">
              <template v-if="gcLoaded">
                <WinButton
                  v-for="s in setupSources.filter(x => x.group === 'gc')"
                  :key="s.id"
                  Style="AccentButtonStyle"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
          <div class="dl-card-section">
            <h5>{{ t('download.github') }}</h5>
            <div class="dl-btns">
              <template v-if="ghLoaded">
                <WinButton
                  v-for="s in setupSources.filter(x => x.group === 'gh')"
                  :key="s.id"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
        </div>
        <div class="site-card dl-card">
          <div class="dl-card-header">
            <span class="icon" aria-hidden="true">&#xE8B7;</span>
            <h3>{{ t('download.portable') }}</h3>
          </div>
          <p class="dl-card-desc">{{ t('download.portable-desc') }}</p>
          <p class="dl-wd-warning" role="note">⚠ {{ t('download.wd-warning') }}</p>
          <div class="dl-card-section">
            <h5>{{ t('download.gitcode') }} <span class="dl-badge">{{ t('download.gitcode-badge') }}</span></h5>
            <div class="dl-btns">
              <template v-if="gcLoaded">
                <WinButton
                  v-for="s in portableSources.filter(x => x.group === 'gc')"
                  :key="s.id"
                  Style="AccentButtonStyle"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
          <div class="dl-card-section">
            <h5>{{ t('download.github') }}</h5>
            <div class="dl-btns">
              <template v-if="ghLoaded">
                <WinButton
                  v-for="s in portableSources.filter(x => x.group === 'gh')"
                  :key="s.id"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
          <div class="dl-card-section">
            <h5>{{ t('download.cloud') }} <span class="dl-badge dl-badge-sub">{{ t('download.cloud-sub') }}</span></h5>
            <div class="dl-btns">
              <WinButton
                :Content="t('download.quark')"
                Height="36"
                HorizontalContentAlignment="Stretch"
                @Click="handleCloudDownload($event, 'https://pan.quark.cn/s/e593f9c60aa9')" />
              <WinButton
                :Content="t('download.baidu')"
                Height="36"
                HorizontalContentAlignment="Stretch"
                @Click="handleCloudDownload($event, 'https://pan.baidu.com/s/1bEZ2aDgPGgfBRtHMwQ_ilg?pwd=twgm')" />
            </div>
          </div>
        </div>

        <div class="site-card dl-card">
          <div class="dl-card-header">
            <span class="icon" aria-hidden="true">&#xE81E;</span>
            <h3>{{ t('download.lite') }}</h3>
            <span class="dl-badge">{{ t('download.lite-badge') }}</span>
          </div>
          <p class="dl-card-desc">{{ t('download.lite-desc') }}</p>
          <div class="dl-card-section">
            <h5>{{ t('download.gitcode') }} <span class="dl-badge">{{ t('download.gitcode-badge') }}</span></h5>
            <div class="dl-btns">
              <template v-if="gcLoaded">
                <WinButton
                  v-for="s in liteSources.filter(x => x.group === 'gc')"
                  :key="s.id"
                  Style="AccentButtonStyle"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
          <div class="dl-card-section">
            <h5>{{ t('download.github') }}</h5>
            <div class="dl-btns">
              <template v-if="ghLoaded">
                <WinButton
                  v-for="s in liteSources.filter(x => x.group === 'gh')"
                  :key="s.id"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <template v-else>
                <span class="dl-muted">{{ t('download.loading') }}</span>
              </template>
            </div>
          </div>
        </div>


        <div class="site-card dl-card">
          <div class="dl-card-header">
            <span class="icon" aria-hidden="true">&#xE9D9;</span>
            <h3>{{ t('download.store') }}</h3>
          </div>
          <p class="dl-card-desc">{{ t('download.store-desc') }}</p>
          <div class="dl-card-section dl-store-section">
            <a
              href="https://apps.microsoft.com/detail/9P15095X7MGB?referrer=appbadge&mode=full"
              target="_blank"
              rel="noopener noreferrer"
              class="dl-store-badge">
              <img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="186" alt="Microsoft Store" />
            </a>
          </div>
        </div>

        <div class="site-card dl-card dl-preview-card">
          <div class="dl-card-header">
            <span class="icon" aria-hidden="true">&#xE945;</span>
            <h3>{{ t('download.preview') }}</h3>
            <span v-if="previewVersion" class="dl-badge dl-preview-version">v{{ previewVersion }}</span>
          </div>
          <p class="dl-card-desc">{{ t('download.preview-desc') }}</p>
          <p class="dl-preview-warning" role="note">⚠ {{ t('download.preview-warning') }}</p>
          <div class="dl-card-section">
            <h5>
              {{ t('download.gitcode') }}
              <span class="dl-badge">{{ t('download.gitcode-badge') }}</span>
              <span v-if="gcPreviewTag" class="dl-preview-tag">v{{ gcPreviewTag }}</span>
              <span v-if="gcPreviewIsPrerelease" class="dl-badge dl-badge-pre">{{ t('download.prerelease') }}</span>
            </h5>
            <div class="dl-btns">
              <template v-if="gcPreviewLoaded">
                <WinButton
                  v-for="s in previewSources.filter(x => x.group === 'gc')"
                  :key="s.id"
                  Style="AccentButtonStyle"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <span v-else class="dl-muted">{{ t('download.loading') }}</span>
            </div>
          </div>
          <div class="dl-card-section">
            <h5>
              {{ t('download.github') }}
              <span v-if="ghPreviewTag" class="dl-preview-tag">v{{ ghPreviewTag }}</span>
              <span v-if="ghPreviewIsPrerelease" class="dl-badge dl-badge-pre">{{ t('download.prerelease') }}</span>
            </h5>
            <div class="dl-btns">
              <template v-if="ghPreviewLoaded">
                <WinButton
                  v-for="s in previewSources.filter(x => x.group === 'gh')"
                  :key="s.id"
                  :Content="s.label"
                  Height="36"
                  HorizontalContentAlignment="Stretch"
                  @Click="handleDownload($event, s)" />
              </template>
              <span v-else class="dl-muted">{{ t('download.loading') }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- 命令行安装 -->
      <div class="site-card dl-cli-card">
        <div class="dl-card-header">
          <span class="icon" aria-hidden="true">&#xE756;</span>
          <h3>{{ t('download.cli') }}</h3>
        </div>
        <p class="dl-card-desc">{{ t('download.cli-desc') }}</p>
        <div class="dl-cli-grid">
          <div class="dl-cli-item">
            <div class="dl-cli-label">{{ t('download.winget') }}</div>
            <div class="dl-cli-cmd">
              <code>winget install luolangaga.tubatools</code>
              <WinButton
                class="dl-cli-copy"
                :Content="copiedWinget ? '✓' : '📋'"
                @Click="copyCmd('winget install luolangaga.tubatools', 'winget')" />
            </div>
          </div>
          <div class="dl-cli-item">
            <div class="dl-cli-label">{{ t('download.scoop') }}</div>
            <div class="dl-cli-cmd">
              <code>scoop bucket add tubatools https://github.com/luolangaga/scoop-tubatools</code>
              <WinButton
                class="dl-cli-copy"
                :Content="copiedScoop1 ? '✓' : '📋'"
                @Click="copyCmd('scoop bucket add tubatools https://github.com/luolangaga/scoop-tubatools', 'scoop1')" />
            </div>
            <div class="dl-cli-cmd dl-cli-cmd-follow">
              <code>scoop install tubatools/tubatool</code>
              <WinButton
                class="dl-cli-copy"
                :Content="copiedScoop2 ? '✓' : '📋'"
                @Click="copyCmd('scoop install tubatools/tubatool', 'scoop2')" />
            </div>
          </div>
        </div>
      </div>

      <!-- 旧版本 -->
      <div class="dl-older">
        <h3>{{ t('download.older-title') }}</h3>
        <div class="dl-btns dl-center">
          <WinHyperlinkButton
            NavigateUri="https://github.com/luolangaga/tubatool/releases"
            TargetName="_blank"
            :Content="t('download.gh-releases')" />
          <WinHyperlinkButton
            NavigateUri="https://gitcode.com/gcw_uDDNaqJw/tubatool/releases"
            TargetName="_blank"
            :Content="t('download.gc-releases')" />
        </div>
      </div>

      <!-- 系统要求 -->
      <div class="dl-info-row">
        <div class="site-card dl-info-card">
          <h3>{{ t('download.sys-req') }}</h3>
          <table class="dl-sys-table">
            <tbody>
              <tr>
                <td>{{ t('download.sys-os') }}</td>
                <td>{{ t('download.sys-os-value') }}</td>
              </tr>
              <tr>
                <td>{{ t('download.sys-arch') }}</td>
                <td>{{ t('download.sys-arch-value') }}</td>
              </tr>
              <tr>
                <td>{{ t('download.sys-runtime') }}</td>
                <td>{{ t('download.sys-runtime-value') }}</td>
              </tr>
              <tr>
                <td>{{ t('download.sys-disk') }}</td>
                <td>{{ t('download.sys-disk-value') }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import WinHyperlinkButton from '../../components/WinHyperlinkButton.vue';
import WinComboBox from '../../components/WinComboBox.vue';
import WinProgressRing from '../../components/WinProgressRing.vue';
import SiteFooter from '../components/SiteFooter.vue';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();

const GH_OWNER = 'luolangaga';
const GH_REPO = 'tubatool';
const GC_OWNER = 'gcw_uDDNaqJw';
const GC_REPO = 'tubatool';

const version = ref('');
const ghAssets = ref([]);
const gcAssets = ref([]);
const ghLoaded = ref(false);
const gcLoaded = ref(false);
const selectedArch = ref('x64');

const archOptions = [
  { value: 'x64', label: 'x64' },
  { value: 'arm64', label: 'ARM64' }
];

const detectedArch = computed(() => {
  if (typeof navigator === 'undefined') return 'x64';
  const ua = navigator.userAgent.toLowerCase();
  const platform = (navigator.platform || '').toLowerCase();
  if (!ua.includes('mobile') && !ua.includes('android') && !ua.includes('iphone') && !ua.includes('ipad')) {
    if (ua.includes('arm64') || ua.includes('aarch64') || platform.includes('arm')) return 'arm64';
  }
  return 'x64';
});

const detectedArchLabel = computed(() => {
  const m = { x64: 'x64', arm64: 'ARM64', unknown: 'x64' };
  return m[detectedArch.value] || m.unknown;
});

async function fetchJson(url) {
  try {
    const r = await fetch(url);
    return r.ok ? await r.json() : null;
  } catch {
    return null;
  }
}

function pickNewestRelease(list, { stableOnly = false } = {}) {
  if (!Array.isArray(list)) return null;
  const usable = list.filter(r =>
    r && r.tag_name && !r.draft && (!stableOnly || r.prerelease !== true));
  if (!usable.length) return null;
  const timeOf = r => Date.parse(r.published_at || r.created_at || '') || 0;
  return usable.reduce((newest, r) => (timeOf(r) > timeOf(newest) ? r : newest));
}

const previewTag = release => (release?.tag_name || release?.name || '').replace(/^v/, '');

/* 正式版渠道：只用正式版（GitCode 的 /latest 不保证跳过预发布，改从列表挑最新正式版） */
async function fetchGitCode() {
  let release = pickNewestRelease(
    await fetchJson(`https://api.gitcode.com/api/v5/repos/${GC_OWNER}/${GC_REPO}/releases?per_page=30&direction=desc`),
    { stableOnly: true });

  if (!release) {
    const latest = await fetchJson(`https://api.gitcode.com/api/v5/repos/${GC_OWNER}/${GC_REPO}/releases/latest`);
    if (latest && latest.prerelease !== true) release = latest;
  }

  if (release) {
    if (!version.value) version.value = previewTag(release);
    gcAssets.value = (release.assets || []).filter(a => a.type !== 'source');
  }
  gcLoaded.value = true;
}

async function fetchGitHub() {
  const release = await fetchJson(`https://api.github.com/repos/${GH_OWNER}/${GH_REPO}/releases/latest`);
  if (release && release.prerelease !== true) {
    if (!version.value) version.value = previewTag(release);
    ghAssets.value = (release.assets || []).filter(a => a.type !== 'source');
  }
  ghLoaded.value = true;
}

/* 预览版：两平台各自时间最新的已发布版本（GitCode 列表默认升序，需 direction=desc） */
const ghPreview = ref(null);
const gcPreview = ref(null);
const ghPreviewLoaded = ref(false);
const gcPreviewLoaded = ref(false);

async function fetchGitHubPreview() {
  ghPreview.value = pickNewestRelease(
    await fetchJson(`https://api.github.com/repos/${GH_OWNER}/${GH_REPO}/releases?per_page=30`));
  ghPreviewLoaded.value = true;
}

async function fetchGitCodePreview() {
  gcPreview.value = pickNewestRelease(
    await fetchJson(`https://api.gitcode.com/api/v5/repos/${GC_OWNER}/${GC_REPO}/releases?per_page=30&direction=desc`));
  gcPreviewLoaded.value = true;
}

const gcPreviewTag = computed(() => previewTag(gcPreview.value));
const ghPreviewTag = computed(() => previewTag(ghPreview.value));
const previewVersion = computed(() => gcPreviewTag.value || ghPreviewTag.value);
const gcPreviewIsPrerelease = computed(() => gcPreview.value?.prerelease === true);
const ghPreviewIsPrerelease = computed(() => ghPreview.value?.prerelease === true);

function findAssetUrl(list, arch, type) {
  const kind = { portable: 'Portable', setup: 'Setup', lite: 'Lite' }[type] || 'Portable';
  const pattern = type === 'setup'
    ? new RegExp(`${kind}.*${arch}\\.exe$`)
    : new RegExp(`${kind}.*${arch}\\.zip$`);
  const a = list.find(x => pattern.test(x.name));
  return a?.browser_download_url || '';
}

const portableSources = computed(() => {
  const arch = selectedArch.value;
  const tag = version.value ? `v${version.value}` : '';
  const gcUrl = findAssetUrl(gcAssets.value, arch, 'portable')
    || (tag ? `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases/${tag}` : `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases`);
  const ghUrl = findAssetUrl(ghAssets.value, arch, 'portable')
    || `https://github.com/${GH_OWNER}/${GH_REPO}/releases`;

  return [
    { id: 'gc-portable', label: `${t('download.now')} (${arch})`, url: gcUrl, group: 'gc', dlType: 'portable' },
    { id: 'gh-portable', label: `${t('download.github')} ${arch}`, url: ghUrl, group: 'gh', dlType: 'portable' },
    { id: 'gh-mirror-portable', label: `${t('download.mirror')} ${arch}`, url: `https://hub.tubawinui3.cn/${GH_OWNER}/${GH_REPO}/releases/`, group: 'gh', dlType: 'portable' }
  ];
});

const setupSources = computed(() => {
  const arch = selectedArch.value;
  const tag = version.value ? `v${version.value}` : '';
  const gcUrl = findAssetUrl(gcAssets.value, arch, 'setup')
    || (tag ? `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases/${tag}` : `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases`);
  const ghUrl = findAssetUrl(ghAssets.value, arch, 'setup')
    || `https://github.com/${GH_OWNER}/${GH_REPO}/releases`;

  return [
    { id: 'gc-setup', label: `${t('download.now')} (${arch})`, url: gcUrl, group: 'gc', dlType: 'setup' },
    { id: 'gh-setup', label: `${t('download.github')} ${arch}`, url: ghUrl, group: 'gh', dlType: 'setup' },
    { id: 'gh-mirror-setup', label: `${t('download.mirror')} ${arch}`, url: `https://hub.tubawinui3.cn/${GH_OWNER}/${GH_REPO}/releases/`, group: 'gh', dlType: 'setup' }
  ];
});

/* 精简版：只内置必要工具，官方仅提供 x64 便携包 */
const liteSources = computed(() => {
  const arch = 'x64';
  const tag = version.value ? `v${version.value}` : '';
  const gcUrl = findAssetUrl(gcAssets.value, arch, 'lite')
    || (tag ? `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases/${tag}` : `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases`);
  const ghUrl = findAssetUrl(ghAssets.value, arch, 'lite')
    || `https://github.com/${GH_OWNER}/${GH_REPO}/releases`;

  return [
    { id: 'gc-lite', label: `${t('download.now')} (${arch})`, url: gcUrl, group: 'gc', dlType: 'lite' },
    { id: 'gh-lite', label: `${t('download.github')} ${arch}`, url: ghUrl, group: 'gh', dlType: 'lite' },
    { id: 'gh-mirror-lite', label: `${t('download.mirror')} ${arch}`, url: `https://hub.tubawinui3.cn/${GH_OWNER}/${GH_REPO}/releases/`, group: 'gh', dlType: 'lite' }
  ];
});

function buildPreviewSources(release, group) {
  const arch = selectedArch.value;
  const assets = release?.assets || [];
  const tag = release?.tag_name || '';
  const fallback = group === 'gc'
    ? `https://gitcode.com/${GC_OWNER}/${GC_REPO}/releases${tag ? `/${tag}` : ''}`
    : `https://github.com/${GH_OWNER}/${GH_REPO}/releases${tag ? `/tag/${tag}` : ''}`;
  const releaseVersion = previewTag(release);

  return [
    {
      id: `${group}-preview-portable`,
      label: `${t('download.portable')} (${arch})`,
      url: findAssetUrl(assets, arch, 'portable') || fallback,
      group,
      dlType: 'portable',
      version: releaseVersion
    },
    {
      id: `${group}-preview-lite`,
      label: `${t('download.lite')} (x64)`,
      url: findAssetUrl(assets, 'x64', 'lite') || fallback,
      group,
      dlType: 'lite',
      version: releaseVersion
    },
    {
      id: `${group}-preview-setup`,
      label: `${t('download.setup')} (${arch})`,
      url: findAssetUrl(assets, arch, 'setup') || fallback,
      group,
      dlType: 'setup',
      version: releaseVersion
    }
  ];
}

const previewSources = computed(() => [
  ...buildPreviewSources(gcPreview.value, 'gc'),
  ...buildPreviewSources(ghPreview.value, 'gh')
]);

function handleDownload(e, source) {
  e.preventDefault();
  window.open(source.url, '_blank', 'noopener');
  const params = new URLSearchParams();
  const downloadVersion = source.version || version.value;
  if (downloadVersion) params.set('version', downloadVersion);
  if (selectedArch.value) params.set('arch', selectedArch.value);
  if (source.dlType) params.set('type', source.dlType);
  params.set('url', source.url);
  window.location.href = `/download/thanks?${params.toString()}`;
}

function handleCloudDownload(e, url) {
  e.preventDefault();
  window.open(url, '_blank', 'noopener');
  const params = new URLSearchParams();
  if (version.value) params.set('version', version.value);
  if (selectedArch.value) params.set('arch', selectedArch.value);
  params.set('type', 'portable');
  params.set('url', url);
  window.location.href = `/download/thanks?${params.toString()}`;
}

/* 命令行复制 */
const copiedWinget = ref(false);
const copiedScoop1 = ref(false);
const copiedScoop2 = ref(false);

async function copyCmd(text, key) {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // 回退方案
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.cssText = 'position:fixed;opacity:0';
    document.body.appendChild(ta);
    ta.select();
    document.execCommand('copy');
    document.body.removeChild(ta);
  }
  const target = key === 'winget' ? copiedWinget : key === 'scoop1' ? copiedScoop1 : copiedScoop2;
  target.value = true;
  setTimeout(() => { target.value = false; }, 1500);
}

onMounted(() => {
  selectedArch.value = detectedArch.value === 'unknown' ? 'x64' : detectedArch.value;
  fetchGitCode();
  fetchGitHub();
  fetchGitCodePreview();
  fetchGitHubPreview();
});
</script>

<style scoped>
.dl-meta-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 32px;
  padding: 16px 24px;
  margin-bottom: 20px;
}

.dl-meta-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  line-height: 20px;
  color: var(--text-primary);
}

.dl-meta-label {
  color: var(--text-secondary);
}

.dl-version-num {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.dl-muted {
  color: var(--text-tertiary);
}

.dl-recommend-badge {
  padding: 1px 8px;
  border-radius: 999px;
  background: var(--SystemFillColorSuccessBackgroundBrush, rgba(15, 123, 15, 0.15));
  color: var(--SystemFillColorSuccessBrush, #0F7B0F);
  font-size: 12px;
  font-weight: 600;
  line-height: 18px;
}

.dl-arch-picker {
  margin-left: auto;
}

.dl-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 16px;
  margin-bottom: 20px;
}

.dl-card {
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.dl-card-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.dl-card-header .icon {
  font-family: 'WinUIOnWebIcons';
  font-size: 20px;
  color: var(--accent-base);
}

.dl-card-header h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  line-height: 24px;
  color: var(--text-primary);
}

.dl-card-desc {
  margin: 0;
  font-size: 13.5px;
  line-height: 20px;
  color: var(--text-secondary);
}

.dl-wd-warning {
  margin: 0;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--SystemFillColorCriticalBackgroundBrush, rgba(196, 43, 28, 0.12));
  border: 1px solid var(--SystemFillColorCriticalBrush, rgba(196, 43, 28, 0.5));
  font-size: 13px;
  font-weight: 600;
  line-height: 19px;
  color: var(--SystemFillColorCriticalBrush, #C42B1C);
}

.dl-preview-card {
  border-color: var(--SystemFillColorCautionBrush, rgba(157, 93, 0, 0.45));
}

.dl-preview-card .dl-card-header .icon {
  color: var(--SystemFillColorCautionBrush, #9D5D00);
}

.dl-preview-warning {
  margin: 0;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--SystemFillColorCautionBackgroundBrush, rgba(157, 93, 0, 0.14));
  border: 1px solid var(--SystemFillColorCautionBrush, rgba(157, 93, 0, 0.5));
  font-size: 13px;
  font-weight: 600;
  line-height: 19px;
  color: var(--SystemFillColorCautionBrush, #9D5D00);
}

.dl-preview-version,
.dl-badge-pre {
  background: var(--SystemFillColorCautionBackgroundBrush, rgba(157, 93, 0, 0.14));
  color: var(--SystemFillColorCautionBrush, #9D5D00);
}

.dl-preview-version {
  margin-left: auto;
  font-variant-numeric: tabular-nums;
}

.dl-preview-tag {
  color: var(--text-tertiary);
  font-weight: 400;
  font-variant-numeric: tabular-nums;
}

.dl-card-section h5 {
  margin: 0 0 8px 0;
  font-size: 13px;
  font-weight: 600;
  line-height: 18px;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  gap: 6px;
}

.dl-badge {
  padding: 0 8px;
  border-radius: 999px;
  background: var(--subtle-secondary);
  color: var(--text-secondary);
  font-size: 11.5px;
  font-weight: 600;
  line-height: 18px;
}

.dl-badge-sub {
  font-weight: 400;
}

.dl-btns {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.dl-btns .win-btn {
  width: 100%;
}

.dl-center {
  flex-direction: row;
  justify-content: center;
  gap: 16px;
}

.dl-store-section {
  display: flex;
  justify-content: center;
}

.dl-older {
  text-align: center;
  margin: 8px 0 20px 0;
}

.dl-older h3 {
  margin: 0 0 10px 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
}

.dl-info-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 16px;
}

.dl-info-card {
  padding: 20px 24px;
}

.dl-info-card h3 {
  margin: 0 0 12px 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
}

.dl-sys-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13.5px;
}

.dl-sys-table td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--stroke-divider);
  color: var(--text-secondary);
}

.dl-sys-table td:first-child {
  color: var(--text-primary);
  font-weight: 600;
  width: 40%;
}

.dl-sys-table tr:last-child td {
  border-bottom: 0;
}

/* 命令行安装卡片 */
.dl-cli-card {
  padding: 20px 24px;
  margin-bottom: 20px;
}

.dl-cli-grid {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-top: 12px;
}

.dl-cli-item {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.dl-cli-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.dl-cli-cmd {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: var(--ctrl-fill-default);
  border: 1px solid var(--card-stroke);
  border-radius: 6px;
}

.dl-cli-cmd code {
  flex: 1;
  font-family: 'Cascadia Code', 'Consolas', 'SF Mono', monospace;
  font-size: 13px;
  color: var(--text-primary);
  word-break: break-all;
  user-select: text;
  -webkit-user-select: text;
}

.dl-cli-cmd-follow {
  margin-top: -4px;
}

.dl-cli-copy {
  flex: 0 0 auto;
  min-width: 36px !important;
  height: 28px !important;
  padding: 0 6px !important;
  font-size: 14px;
  border-radius: 4px;
}
</style>
