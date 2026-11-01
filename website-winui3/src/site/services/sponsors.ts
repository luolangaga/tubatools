// Add one entry per donation with a unique id; totals, sorting and responsive cards update automatically.
// Public display fields only. Keep payment records and transaction IDs out of the site.
export const sponsors = [
  {
    id: '20261029-01',
    name: 'R*y',
    amount: 50,
    date: '2026-10-29',
    message: '',
  },
  {
    id: '20260908-01',
    name: '胖胖',
    amount: 30,
    date: '2026-09-08',
    message: '感谢老哥的新版图吧工具箱 希望以后能在设置里加一个语言切换功能 中英切换 [好的]',
  },
  {
    id: '20260903-01',
    name: '*天',
    amount: 5,
    date: '2026-09-03',
    message: '谢谢你的图吧工具箱WinUi3，加油',
  },
] as const;
