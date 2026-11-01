<template>
  <footer class="site-footer">
    <div class="site-footer-inner">
      <div class="site-footer-left">
        <img class="site-footer-logo" :src="logoUrl" alt="Logo" />
        <span class="site-footer-copyright">{{ t('app.copyright') }}</span>
      </div>
      <div class="site-footer-right">
        <RouterLink to="/sponsors" class="sponsors-link">赞助名单</RouterLink>
        <a href="https://github.com/luolangaga/tubatool" target="_blank" rel="noopener" :title="t('nav.github')">
          <span class="github-mark" aria-hidden="true"></span>
        </a>
        <a href="https://gitcode.com/gcw_uDDNaqJw/tubatool" target="_blank" rel="noopener" title="GitCode">
          <span class="icon" aria-hidden="true">&#xE943;</span>
        </a>
      </div>
      <div class="site-footer-credit">
        {{ t('home.footer.credit1') }}
        <a href="https://devtoys.app" target="_blank" rel="noopener">DevToys</a>
        · {{ t('home.footer.credit2') }}
        <a href="https://github.com/Furry-Xiyi/WinUIonWeb" target="_blank" rel="noopener">{{ t('home.footer.credit-link') }}</a>
      </div>
    </div>
  </footer>
</template>

<script setup>
import logoUrl from '../../assets/site/logo.svg';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();

const githubMarkSvg = 'data:image/svg+xml;utf8,' + encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>'
);
</script>

<style scoped>
.site-footer {
  margin-top: 48px;
  padding: 20px 36px;
  border-top: 1px solid var(--stroke-divider);
  color: var(--text-secondary);
  font-size: 13px;
}

.site-footer-inner {
  max-width: 1080px;
  margin: 0 auto;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px 24px;
}

.site-footer-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.site-footer-logo {
  width: 24px;
  height: 24px;
}

.site-footer-right {
  display: flex;
  gap: 12px;
  margin-left: auto;
}

.site-footer-right a {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 6px;
  color: var(--text-secondary);
  text-decoration: none;
  transition: background 150ms ease, color 150ms ease;
}

.site-footer-right a:hover {
  background: var(--subtle-secondary);
  color: var(--text-primary);
}

.site-footer-right .sponsors-link { width: auto; padding: 0 10px; white-space: nowrap; }

.site-footer-right .icon {
  font-size: 18px;
  line-height: 32px;
}

.site-footer-right .github-mark {
  width: 18px;
  height: 18px;
  background: currentColor;
  -webkit-mask: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>') no-repeat center / contain;
  mask: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>') no-repeat center / contain;
}
</style>
