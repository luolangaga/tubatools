<template>
  <WinScrollViewer class="site-page-scroll" VerticalScrollBarVisibility="Auto" VerticalScrollMode="Auto">
    <main class="sponsors-page">
      <section class="support-stage" aria-labelledby="sponsors-title">
        
        
        <header class="support-intro">
          <p class="support-label">赞助名单</p>
          <h1 id="sponsors-title">感谢每一位支持者</h1>
          <p class="support-description">谢谢你，让免费、开源的工具箱一路向前。</p>
          <dl class="support-totals">
            <div><dt>已收录赞助</dt><dd><small>¥</small>{{ totalAmount.toFixed(2) }}</dd></div>
            <div><dt>赞助记录</dt><dd>{{ sponsors.length }}<small> 笔</small></dd></div>
          </dl>
        </header>

        <section class="wall-section" aria-labelledby="wall-title">
          <div class="wall-toolbar">
            <h2 id="wall-title">所有赞助</h2>
            <label class="wall-sort">排列
              <select v-model="sortOrder" aria-label="赞助名单排列顺序">
                <option value="newest">最近赞助</option>
                <option value="oldest">最早赞助</option>
              </select>
            </label>
          </div>
          <div class="support-wall">
            <article v-for="sponsor in sortedSponsors" :key="sponsor.id"
              class="supporter" >
              <div class="supporter-top"><span class="supporter-mark" aria-hidden="true">{{ sponsor.name.replaceAll('*', '').slice(0, 1) }}</span><span class="supporter-caption">感谢支持</span></div>
              <h3>{{ sponsor.name }}</h3>
              <p class="supporter-amount"><span>¥</span>{{ sponsor.amount.toFixed(2) }}</p>
              <blockquote v-if="sponsor.message">{{ sponsor.message }}</blockquote>
              <p v-else class="silent-thanks">一份无言的鼓励，<br>也是继续前行的力量。</p>
              <footer><time :datetime="sponsor.date">{{ sponsor.date.replaceAll('-', '.') }}</time><span>微信赞助</span></footer>
            </article>
          </div>
        </section>
        <p class="wall-note">每一份心意，同样珍贵。昵称按收款记录展示，部分昵称由支付平台隐去。</p>
      </section>
      <section class="support-outro" aria-labelledby="support-outro-title">
        <div>
          <h2 id="support-outro-title">支持，也可以是一个 Star。</h2>
          <p>使用、分享、反馈与代码贡献，都在帮助工具箱变得更好。</p>
        </div>
        <div class="support-actions">
          <a href="https://github.com/luolangaga/tubatool" target="_blank" rel="noopener noreferrer">GitHub <span aria-hidden="true">↗</span></a>
          <a href="https://gitcode.com/luolangaga/tubatool" target="_blank" rel="noopener noreferrer">GitCode <span aria-hidden="true">↗</span></a>
        </div>
      </section>
    </main>
    <SiteFooter />
  </WinScrollViewer>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import WinScrollViewer from '../../components/WinScrollViewer.vue';
import SiteFooter from '../components/SiteFooter.vue';
import { sponsors } from '../services/sponsors';

const sortOrder = ref('newest');
const totalAmount = sponsors.reduce((sum, sponsor) => sum + sponsor.amount, 0);
const sortedSponsors = computed(() => [...sponsors].sort((a, b) =>
  sortOrder.value === 'oldest' ? a.date.localeCompare(b.date) : b.date.localeCompare(a.date)));
</script>

<style scoped>
.sponsors-page { max-width: 1120px; margin: 0 auto; padding: 36px 36px 0; color: var(--text-primary); }
.support-label { font-size: 14px; color: var(--text-secondary); margin: 0 0 12px; }
.support-intro h1 { font-size: 32px; font-weight: 600; line-height: 1.35; margin: 0; }
.support-description { color: var(--text-secondary); margin: 12px 0 28px; line-height: 1.7; }
.support-totals { display: flex; gap: 48px; margin: 0; padding: 24px; border: 1px solid var(--card-stroke); background: var(--card-bg); border-radius: 8px; }
.support-totals div { display: flex; flex-direction: column; gap: 6px; }
.support-totals dt { font-size: 13px; color: var(--text-secondary); }
.support-totals dd { font-size: 28px; font-weight: 600; font-variant-numeric: tabular-nums; margin: 0; }
.support-totals small { font-size: 16px; font-weight: 400; margin-right: 4px; }
.wall-section { margin-top: 32px; }
.wall-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 16px; }
.wall-toolbar h2 { font-size: 20px; font-weight: 600; margin: 0; }
.wall-sort { display: flex; align-items: center; gap: 10px; font-size: 13px; color: var(--text-secondary); }
.wall-sort select { background: var(--ctrl-fill-default); color: var(--text-primary); border: 1px solid var(--ctrl-border); border-bottom: 1px solid var(--ctrl-strong-stroke); border-radius: 4px; padding: 7px 28px 7px 12px; font: inherit; }
.support-wall { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr)); gap: 12px; }
.supporter { display: flex; flex-direction: column; min-width: 0; padding: 24px; border: 1px solid var(--card-stroke); border-radius: 8px; background: var(--card-bg); }
.supporter-top { display: flex; align-items: center; justify-content: space-between; }
.supporter-mark { display: grid; place-items: center; width: 40px; height: 40px; border-radius: 50%; background: var(--subtle-secondary); color: var(--accent-text-fill-color-primary); font-size: 18px; }
.supporter-caption { color: var(--text-secondary); font-size: 12px; }
.supporter h3 { margin: 18px 0 8px; font-size: 20px; font-weight: 600; }
.supporter-amount { margin: 0 0 20px; font-size: 28px; font-weight: 600; font-variant-numeric: tabular-nums; }
.supporter-amount span { font-size: 16px; margin-right: 4px; }
.supporter blockquote, .silent-thanks { font-size: 14px; line-height: 1.8; margin: 0 0 24px; color: var(--text-secondary); overflow-wrap: anywhere; }
.supporter footer { margin-top: auto; padding-top: 16px; border-top: 1px solid var(--stroke-divider); display: flex; justify-content: space-between; gap: 8px; font-size: 12px; color: var(--text-secondary); }
.wall-note { font-size: 12px; color: var(--text-secondary); line-height: 1.8; margin: 16px 0 0; }
.support-outro { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 20px; margin-top: 32px; padding: 24px; border: 1px solid var(--card-stroke); border-radius: 8px; background: var(--card-bg); }
.support-outro h2 { font-size: 18px; font-weight: 600; margin: 0 0 8px; }
.support-outro p { color: var(--text-secondary); font-size: 14px; line-height: 1.7; margin: 0; }
.support-actions { display: flex; gap: 8px; }
.support-actions a { color: var(--accent-text-fill-color-primary); padding: 8px 12px; border-radius: 4px; text-decoration: none; font-size: 14px; }
.support-actions a:hover { background: var(--subtle-secondary); }
.support-actions a:focus-visible, select:focus-visible { outline: 2px solid var(--text-primary); outline-offset: 3px; }
@media(max-width: 600px) { .sponsors-page { padding: 24px 16px 0; } .support-intro h1 { font-size: 28px; } .support-totals { gap: 32px; padding: 20px; } .wall-toolbar { flex-wrap: wrap; } .supporter { padding: 20px; } }
</style>
