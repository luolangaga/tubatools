export type UnlistenFn = () => void;

type Handler = (event: { payload: unknown }) => void;
const handlers = new Map<string, Set<Handler>>();

export async function listen<T = unknown>(
  event: string,
  handler: (event: { payload: T }) => void,
): Promise<UnlistenFn> {
  const set = handlers.get(event) ?? new Set<Handler>();
  set.add(handler as Handler);
  handlers.set(event, set);
  return () => {
    set.delete(handler as Handler);
  };
}

export async function emit(event: string, payload?: unknown): Promise<void> {
  const set = handlers.get(event);
  if (!set) return;
  for (const handler of set) {
    try {
      handler({ payload });
    } catch {
      /* ignore */
    }
  }
}

export async function once<T = unknown>(
  event: string,
  handler: (event: { payload: T }) => void,
): Promise<UnlistenFn> {
  let unlisten: UnlistenFn = () => {};
  unlisten = await listen<T>(event, (e) => {
    unlisten();
    handler(e);
  });
  return unlisten;
}
