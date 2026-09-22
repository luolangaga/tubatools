import { createRouter, createWebHistory } from 'vue-router';
import type { RouteRecordRaw } from 'vue-router';

const pageLoaders = {
  home: () => import('./pages/HomePage.vue'),
  download: () => import('./pages/DownloadPage.vue'),
  features: () => import('./pages/FeaturesPage.vue'),
  why: () => import('./pages/WhyChoosePage.vue'),
  about: () => import('./pages/AboutPage.vue'),
  docs: () => import('./pages/DocsPage.vue'),
  thanks: () => import('./pages/ThanksPage.vue'),
  ranking: () => import('./pages/RankingPage.vue'),
  latency: () => import('./pages/LatencyQueryPage.vue'),
  mushroom: () => import('./pages/MushroomTestPage.vue')
};

export const pageTags = new Set(Object.keys(pageLoaders));

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: pageLoaders.home },
  { path: '/download', name: 'download', component: pageLoaders.download },
  { path: '/download/thanks', name: 'thanks', component: pageLoaders.thanks },
  { path: '/features', name: 'features', component: pageLoaders.features },
  { path: '/why', name: 'why', component: pageLoaders.why },
  { path: '/about', name: 'about', component: pageLoaders.about },
  { path: '/ranking', name: 'ranking', component: pageLoaders.ranking },
  { path: '/latency', name: 'latency', component: pageLoaders.latency },
  { path: '/mushroom', name: 'mushroom', component: pageLoaders.mushroom },
  // 文档路由延续原官网 clean URL 格式：/guide/x、/tools/x、/tutorials/x、/dev/x
  { path: '/:cat(guide|tools|tutorials|dev)/:file?', name: 'docs', component: pageLoaders.docs },
  { path: '/:pathMatch(.*)*', redirect: '/' }
];

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes
});

export default router;
