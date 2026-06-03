import { defineConfig } from 'vitepress'

const jsonLd = JSON.stringify({
  "@context": "https://schema.org",
  "@type": "SoftwareApplication",
  "name": "图吧工具箱",
  "description": "PC 硬件检测与系统维护工具集",
  "url": "https://tubawinui3.cn",
  "applicationCategory": "UtilitiesApplication",
  "operatingSystem": "Windows 10",
  "offers": {
    "@type": "Offer",
    "price": "0",
    "priceCurrency": "CNY"
  },

  "programmingLanguage": "C#",
  "applicationSubCategory": "Hardware Diagnostic"
})

export default defineConfig({
  lang: 'zh-CN',
  title: '图吧工具箱',
  description: 'PC 硬件检测与系统维护工具集',

  outDir: '../../docs',

  sitemap: {
    hostname: 'https://tubawinui3.cn',
  },

  vite: {
    server: {
      host: '127.0.0.1',
    },
  },

  head: [
    ['link', { rel: 'icon', href: '/logo.svg', type: 'image/svg+xml' }],
    ['meta', { name: 'theme-color', content: '#5b5fc7' }],
    ['link', { rel: 'stylesheet', href: 'https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.1/css/all.min.css', integrity: 'sha512-DTOQO9RWCH3ppGqcWaEA1BIZOC6xxalwEsw9c2QQeAIftl+Vegovlnee1c9QX4TctnWMn13TZye+giMm8e2LwA==', crossorigin: 'anonymous', referrerpolicy: 'no-referrer' }],
    ['meta', { name: 'keywords', content: '图吧工具箱, 硬件检测, PC工具, 系统维护, WinUI3, CPU-Z, GPU-Z, 硬件信息' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:locale', content: 'zh_CN' }],
    ['meta', { property: 'og:site_name', content: '图吧工具箱' }],
    ['meta', { property: 'og:image', content: 'https://tubawinui3.cn/logo.svg' }],
    ['meta', { property: 'og:url', content: 'https://tubawinui3.cn/' }],
    ['meta', { name: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { name: 'twitter:image', content: 'https://tubawinui3.cn/logo.svg' }],
    ['link', { rel: 'canonical', href: 'https://tubawinui3.cn/' }],
    ['script', { type: 'application/ld+json' }, jsonLd],
  ],

  cleanUrls: true,

  themeConfig: {
    logo: '/logo.svg',

    nav: [
      { text: '首页', link: '/' },
      { text: '指南', link: '/guide/getting-started' },
      {
        text: '工具大全',
        items: [
          { text: '处理器工具', link: '/tools/cpu' },
          { text: '显卡工具', link: '/tools/gpu' },
          { text: '硬盘工具', link: '/tools/disk' },
          { text: '内存工具', link: '/tools/memory' },
          { text: '综合检测', link: '/tools/diagnostic' },
          { text: '外设工具', link: '/tools/peripheral' },
          { text: '其他工具', link: '/tools/other' },
        ],
      },
      { text: '下载', link: '/download' },
      { text: 'GitHub', link: 'https://github.com/luolangaga/tubatool' },
    ],

    sidebar: {
      '/guide/': [
        {
          text: '功能介绍',
          collapsed: false,
          items: [
            { text: '硬件信息', link: '/guide/hardware' },
            { text: '内置工具', link: '/guide/builtin' },
            { text: '工具目录', link: '/guide/catalog' },
          ],
        },
        {
          text: '开发指南',
          collapsed: false,
          items: [
            { text: '快速开始', link: '/guide/getting-started' },
            { text: '添加内置工具', link: '/guide/add-builtin-tool' },
            { text: '添加外部工具', link: '/guide/add-external-tool' },
          ],
        },
      ],
      '/tools/': [
        {
          text: '工具大全',
          items: [
            { text: '处理器工具', link: '/tools/cpu' },
            { text: '显卡工具', link: '/tools/gpu' },
            { text: '硬盘工具', link: '/tools/disk' },
            { text: '内存工具', link: '/tools/memory' },
            { text: '综合检测', link: '/tools/diagnostic' },
            { text: '外设工具', link: '/tools/peripheral' },
            { text: '其他工具', link: '/tools/other' },
          ],
        },
      ],
    },

    socialLinks: [
      { icon: 'github', link: 'https://github.com/luolangaga/tubatool' },
    ],

    search: {
      provider: 'local',
      options: {
        translations: {
          button: { buttonText: '搜索文档' },
          modal: {
            displayDetails: '显示详情',
            noResultsText: '未找到结果',
          },
        },
      },
    },

    editLink: {
      pattern: 'https://github.com/luolangaga/tubatool/edit/main/src/docs/:path',
      text: '在 GitHub 上编辑此页',
    },

    lastUpdated: {
      text: '最后更新',
      formatOptions: { dateStyle: 'short' },
    },

    footer: {
      message: '基于 <a href="https://www.gnu.org/licenses/gpl-3.0.html" target="_blank" rel="noopener">GPL-3.0</a> 开源协议发布',
      copyright: 'Copyright © 2026 罗澜嘎嘎',
    },

    docFooter: {
      prev: '上一页',
      next: '下一页',
    },

    outline: {
      label: '本页目录',
      level: [2, 3],
    },
  },
})
