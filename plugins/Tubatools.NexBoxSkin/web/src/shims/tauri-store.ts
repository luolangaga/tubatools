function storageKey(storeName: string, key: string): string {
  return `gamebox.store.${storeName}.${key}`;
}

export class LazyStore {
  private readonly name: string;

  constructor(name: string) {
    this.name = name;
  }

  async get<T>(key: string): Promise<T | null> {
    try {
      const raw = localStorage.getItem(storageKey(this.name, key));
      if (raw === null) return null;
      return JSON.parse(raw) as T;
    } catch {
      return null;
    }
  }

  async set(key: string, value: unknown): Promise<void> {
    localStorage.setItem(storageKey(this.name, key), JSON.stringify(value));
  }

  async delete(key: string): Promise<boolean> {
    const existed = localStorage.getItem(storageKey(this.name, key)) !== null;
    localStorage.removeItem(storageKey(this.name, key));
    return existed;
  }

  async has(key: string): Promise<boolean> {
    return localStorage.getItem(storageKey(this.name, key)) !== null;
  }

  async keys(): Promise<string[]> {
    const prefix = storageKey(this.name, "");
    const result: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const item = localStorage.key(i);
      if (item?.startsWith(prefix)) result.push(item.slice(prefix.length));
    }
    return result;
  }

  async clear(): Promise<void> {
    const prefix = storageKey(this.name, "");
    const remove: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const item = localStorage.key(i);
      if (item?.startsWith(prefix)) remove.push(item);
    }
    remove.forEach((item) => localStorage.removeItem(item));
  }

  async save(): Promise<void> {}
  async reload(): Promise<void> {}
}

export default LazyStore;
