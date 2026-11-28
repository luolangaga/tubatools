<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <div class="site-page-inner why-page">
      <header class="site-page-header">
        <h1>{{ t('why.title') }}</h1>
        <p>{{ t('why.subtitle') }}</p>
      </header>

      <section class="why-table-section">
        <div class="why-section-intro">
          <span class="why-eyebrow">{{ t('why.table.title') }}</span>
          <p>{{ t('why.table.subtitle') }}</p>
        </div>
        <div class="site-card why-table-card">
          <table class="why-table">
            <thead>
              <tr>
                <th scope="col">{{ t('why.table.dimension') }}</th>
                <th scope="col" class="why-col-ce">{{ t('why.table.ce') }}</th>
                <th scope="col">{{ t('why.table.legacy') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in rows" :key="row.labelKey">
                <th scope="row">{{ t(row.labelKey) }}</th>
                <td class="why-ce-cell"><span class="why-check" aria-hidden="true">&#xE73D;</span>{{ t(row.ceKey) }}</td>
                <td><span class="why-cross" aria-hidden="true">&#xE711;</span>{{ t(row.legacyKey) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section class="why-shots">
        <div class="why-section-intro">
          <h2>{{ t('why.shots.title') }}</h2>
        </div>
        <div class="why-shot-grid">
          <figure class="site-card why-shot">
            <img :src="screenshotTools" alt="图吧工具箱CE WinUI 3 界面" loading="lazy" />
            <figcaption>{{ t('why.shots.ce') }}</figcaption>
          </figure>
          <figure class="site-card why-shot">
            <img src="/comparison/legacy-ui.png" alt="原版图吧工具箱界面" loading="lazy" />
            <figcaption>{{ t('why.shots.legacy') }}</figcaption>
          </figure>
        </div>
      </section>

      <section class="site-card why-summary">
        <div>
          <h2>{{ t('why.summary.title') }}</h2>
          <p>{{ t('why.summary.text') }}</p>
        </div>
        <WinButton
          Style="AccentButtonStyle"
          :Content="t('why.summary.download')"
          Height="40"
          Padding="20,0"
          @Click="goDownload" />
      </section>

      <SiteFooter />
    </div>
  </WinScrollViewer>
</template>

<script setup>
import { inject } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import WinButton from '../../components/WinButton.vue';
import SiteFooter from '../components/SiteFooter.vue';
import screenshotTools from '../../assets/site/screenshot-tools.webp';
import { useI18n } from '../../components/i18n/index';

const { t } = useI18n();
const navigate = inject('navigate', () => {});

const rows = [
  { labelKey: 'why.dl.title', ceKey: 'why.row.download.ce', legacyKey: 'why.row.download.legacy' },
  { labelKey: 'why.ui.title', ceKey: 'why.row.ui.ce', legacyKey: 'why.row.ui.legacy' },
  { labelKey: 'why.tools.title', ceKey: 'why.row.tools.ce', legacyKey: 'why.row.tools.legacy' },
  { labelKey: 'why.ai.title', ceKey: 'why.row.ai.ce', legacyKey: 'why.row.ai.legacy' }
];

const goDownload = () => navigate('download');
</script>

<style scoped>
.why-page {
  padding-bottom: 8px;
}

.why-table-section {
  margin-top: 24px;
}

.why-section-intro {
  margin-bottom: 14px;
}

.why-eyebrow {
  display: block;
  margin-bottom: 6px;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.8px;
  text-transform: uppercase;
  color: var(--accent-base);
}

.why-section-intro p {
  margin: 0;
  font-size: 14px;
  line-height: 21px;
  color: var(--text-secondary);
}

.why-section-intro h2 {
  margin: 0;
  font-size: 24px;
  font-weight: 600;
  line-height: 32px;
  color: var(--text-primary);
}

.why-table-card {
  overflow-x: auto;
  padding: 0;
}

.why-table {
  width: 100%;
  min-width: 720px;
  border-collapse: collapse;
  table-layout: fixed;
}

.why-table th,
.why-table td {
  padding: 15px 20px;
  border-bottom: 1px solid var(--stroke-divider);
  text-align: left;
  vertical-align: top;
}

.why-table thead th {
  background: var(--subtle-secondary);
  font-size: 13px;
  font-weight: 600;
  line-height: 18px;
  color: var(--text-secondary);
}

.why-table thead th:first-child,
.why-table tbody th {
  width: 22%;
}

.why-table tbody th {
  font-size: 14px;
  font-weight: 600;
  line-height: 21px;
  color: var(--text-primary);
}

.why-table td {
  font-size: 14px;
  line-height: 21px;
  color: var(--text-secondary);
}

.why-table tbody tr:last-child th,
.why-table tbody tr:last-child td {
  border-bottom: none;
}

.why-col-ce,
.why-ce-cell {
  color: var(--accent-base);
}

.why-ce-cell {
  font-weight: 500;
}

.why-check,
.why-cross {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  margin-right: 8px;
  border-radius: 50%;
  font-family: 'WinUIOnWebIcons';
  font-size: 12px;
  line-height: 18px;
  vertical-align: -3px;
}

.why-check {
  color: var(--SystemFillColorSuccessBrush, #0F7B0F);
  background: var(--SystemFillColorSuccessBackgroundBrush, rgba(15, 123, 15, 0.14));
}

.why-cross {
  color: var(--SystemFillColorCriticalBrush, #C42B1C);
  background: var(--SystemFillColorCriticalBackgroundBrush, rgba(196, 43, 28, 0.12));
}

.why-shots {
  margin-top: 40px;
}

.why-shot-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.why-shot {
  margin: 0;
  padding: 0;
  overflow: hidden;
}

.why-shot img {
  display: block;
  width: 100%;
  aspect-ratio: 16 / 10;
  object-fit: cover;
  object-position: top center;
  border-bottom: 1px solid var(--stroke-divider);
}

.why-shot figcaption {
  padding: 12px 16px;
  font-size: 13px;
  line-height: 19px;
  color: var(--text-secondary);
}

.why-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  margin-top: 40px;
  padding: 24px 28px;
}

.why-summary h2 {
  margin: 0 0 6px;
  font-size: 20px;
  font-weight: 600;
  line-height: 28px;
  color: var(--text-primary);
}

.why-summary p {
  margin: 0;
  font-size: 14px;
  line-height: 21px;
  color: var(--text-secondary);
}

@media (max-width: 720px) {
  .why-table th,
  .why-table td {
    padding: 13px 16px;
  }

  .why-shot-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .why-summary {
    align-items: flex-start;
    flex-direction: column;
    padding: 22px 20px;
  }
}
</style>
