const noop = async () => {};

export function getCurrentWindow() {
  return {
    minimize: noop,
    maximize: noop,
    unmaximize: noop,
    isMaximized: async () => false,
    close: noop,
    startDragging: noop,
    onResized: async () => () => {},
    onCloseRequested: async () => () => {},
  };
}
