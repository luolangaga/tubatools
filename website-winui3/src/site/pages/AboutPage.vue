<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner">
      <header class="site-page-header">
        <h1>{{ t('about.title') }}</h1>
        <p>{{ t('about.subtitle') }}</p>
      </header>

      <!-- 关于软件 -->
      <div class="site-card about-card about-app-card">
        <img class="about-app-logo" :src="logoUrl" alt="Logo" />
        <div class="about-app-info">
          <h3>图吧工具箱CE</h3>
          <p class="about-app-version">v{{ t('app.version') }} · {{ t('app.author') }}</p>
          <span class="about-ce-badge">社区重构版（Community Edition）</span>
          <p class="about-app-desc">{{ t('about.subtitle') }}</p>
          <WinButton
            :Content="t('about.open-repo')"
            Height="36"
            Margin="0,8,0,0"
            @Click="openRepository" />
        </div>
      </div>

      <!-- 外观设置已移到顶栏全局主题切换 -->

      <!-- 社区与源码 -->
      <div class="about-section">
        <h2 class="about-section-title">{{ t('about.community') }}</h2>
        <div class="about-links">
          <WinHyperlinkButton
            NavigateUri="https://github.com/luolangaga/tubatool"
            TargetName="_blank"
            HorizontalAlignment="Left"
            :Content="t('nav.github')" />
          <WinHyperlinkButton
            NavigateUri="https://gitcode.com/gcw_uDDNaqJw/tubatool"
            TargetName="_blank"
            HorizontalAlignment="Left"
            Content="GitCode" />
          <WinHyperlinkButton
            NavigateUri="https://atomgit.com/luolangaga/tubatool"
            TargetName="_blank"
            HorizontalAlignment="Left"
            Content="AtomGit" />
          <WinHyperlinkButton
            NavigateUri="https://github.com/luolangaga/tubatool/issues/new/choose"
            TargetName="_blank"
            HorizontalAlignment="Left"
            :Content="t('home.support.feedback')" />
        </div>
      </div>

      <!-- 开源协议 -->
      <div class="about-section">
        <h2 class="about-section-title">{{ t('about.license') }}</h2>
        <WinInfoBar
          IsOpen
          :IsClosable="false"
          Severity="Informational"
          :Title="t('about.license-title')"
          :Message="t('about.license-desc')">
          <template #ActionButton>
            <WinButton
              :Content="t('about.dmca')"
              Style="SubtleButtonStyle"
              Height="32"
              @Click="openDmca" />
          </template>
        </WinInfoBar>
      </div>

      <!-- 特别感谢 -->
      <div class="about-section">
        <h2 class="about-section-title">{{ t('about.thanks') }}</h2>
        <div class="site-card about-card about-thanks-card">
          <p class="about-thanks-desc">{{ t('about.thanks-desc') }}</p>
          <WinHyperlinkButton
            NavigateUri="https://github.com/Furry-Xiyi/WinUIonWeb"
            TargetName="_blank"
            HorizontalAlignment="Left"
            :Content="t('about.winui-on-web')" />
        </div>
      </div>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import WinHyperlinkButton from '../../components/WinHyperlinkButton.vue';
import WinInfoBar from '../../components/WinInfoBar.vue';
import SiteFooter from '../components/SiteFooter.vue';
import logoUrl from '../../assets/site/logo.svg';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();

const openRepository = () => window.open(t('app.repository'), '_blank', 'noopener');
const openDmca = () => window.open('https://tubawinui3.cn/dmca', '_blank', 'noopener');
</script>

<style scoped>
.about-card {
  padding: 20px 24px;
}

.about-app-card {
  display: flex;
  align-items: center;
  gap: 20px;
}

.about-app-logo {
  width: 72px;
  height: 72px;
  flex: 0 0 auto;
}

.about-app-info h3 {
  margin: 0 0 2px 0;
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
}

.about-app-version {
  margin: 0 0 6px 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.about-ce-badge {
  display: inline-block;
  margin: 0 0 8px 0;
  padding: 2px 10px;
  font-size: 12px;
  line-height: 18px;
  border-radius: 10px;
  color: var(--accent-base);
  background: color-mix(in srgb, var(--accent-base) 10%, transparent);
}

.about-app-desc {
  margin: 0;
  font-size: 14px;
  line-height: 20px;
  color: var(--text-secondary);
}

.about-section {
  margin-top: 28px;
}

.about-section-title {
  margin: 0 0 10px 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.about-section > .win-settings-card {
  margin-top: 12px;
}

.about-links {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 24px;
  padding: 8px 0;
}

.about-thanks-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 8px;
}

.about-thanks-desc {
  margin: 0;
  font-size: 14px;
  line-height: 20px;
  color: var(--text-secondary);
}
</style>
