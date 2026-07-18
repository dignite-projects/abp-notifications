// @ts-ignore: test-only Node import resolved by Vitest.
import { readFileSync } from 'node:fs';
// @ts-ignore: jsdom is a dev dependency used only by this spec.
import { JSDOM } from 'jsdom';

const mvcBundle = readFileSync(
  '../notification-center/src/Dignite.Abp.NotificationCenter.Web/wwwroot/Dignite/Abp/NotificationCenter/Web/notification-center.js',
  'utf8',
);

describe('MVC notification-center realtime runtime', () => {
  it('shares one connection and REST-refreshes duplicate bells on receive', async () => {
    const host = createMvcHost();

    await host.start();

    expect(host.connections.length).toBe(1);
    expect(host.connections[0].handlerCount('ReceiveNotification')).toBe(1);

    host.nextUnreadCount = 7;
    host.connections[0].emit('ReceiveNotification');
    await host.flush();

    expect(host.badgeCounts()).toEqual(['7', '7']);
  });

  it('REST-refreshes after manual retry reconnect success', async () => {
    const host = createMvcHost({ bellCount: 1 });

    await host.start();
    host.nextUnreadCount = 11;
    host.connections[0].emitClosed();
    await host.flush();

    expect(host.connections.length).toBe(2);
    expect(host.badgeCounts()).toEqual(['11']);
  });

  it('cleans up removed bells so the same element can be retained after reinsertion', async () => {
    const host = createMvcHost({ bellCount: 1 });

    await host.start();
    const bell = host.bells()[0];
    bell.remove();
    await host.flush();

    expect(host.connections[0].stop).toHaveBeenCalledTimes(1);
    expect(bell.getAttribute('data-notification-realtime-bound')).toBeNull();

    host.window.document.body.appendChild(bell);
    await host.flush();

    expect(host.connections.length).toBe(2);
    expect(host.connections[1].handlerCount('ReceiveNotification')).toBe(1);
  });

  it('uses absolute hub URLs and host-provided access token factories', async () => {
    const accessTokenFactory = vi.fn(() => 'mvc-token');
    const host = createMvcHost({
      bellCount: 1,
      hubUrl: 'https://notifications.example.test/signalr-hubs/notifications',
      accessTokenFactory,
    });

    await host.start();

    expect(host.connections[0].url).toBe('https://notifications.example.test/signalr-hubs/notifications');
    expect(host.connections[0].options.accessTokenFactory()).toBe('mvc-token');
  });

  it('reacts to ABP token and tenant change events by reconnecting and refreshing from REST', async () => {
    const host = createMvcHost({ bellCount: 1 });

    await host.start();
    host.nextUnreadCount = 13;
    host.raiseAbpEvent('abp.auth.tokenChanged');
    await host.flush();

    expect(host.connections[0].stop).toHaveBeenCalledTimes(1);
    expect(host.connections.length).toBe(2);
    expect(host.badgeCounts()).toEqual(['13']);

    host.nextUnreadCount = 17;
    host.raiseAbpEvent('abp.multiTenancy.tenantChanged');
    await host.flush();

    expect(host.connections[1].stop).toHaveBeenCalledTimes(1);
    expect(host.connections.length).toBe(3);
    expect(host.badgeCounts()).toEqual(['17']);
  });
});

function createMvcHost(options: MvcHostOptions = {}) {
  const bellCount = options.bellCount ?? 2;
  const hubUrl = options.hubUrl ?? '/signalr-hubs/notifications';
  const bells = Array.from({ length: bellCount }, () => `
    <div class="dignite-notification-bell"
         data-signalr-hub-url="${hubUrl}"
         data-dropdown-url="/notification-center/notification-bell/dropdown">
      <button class="dignite-notification-toggle" aria-expanded="false"></button>
      <span class="dignite-notification-badge" data-count="0">0</span>
      <div class="dignite-notification-list">
        <div class="dignite-notification-list-content" data-unread-count="0"></div>
      </div>
    </div>
  `).join('');
  const dom = new JSDOM(`<!doctype html><html><body>${bells}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://mvc.example.test/',
  });
  const window = dom.window as unknown as MvcWindow;
  const connections: FakeMvcConnection[] = [];
  const abpEvents = new Map<string, Array<() => void>>();
  let nextUnreadCount = 0;

  class HubConnectionBuilder {
    private url = '';
    private options: { accessTokenFactory?: () => string } = {};

    withUrl(url: string, signalROptions?: { accessTokenFactory?: () => string }) {
      this.url = url;
      this.options = signalROptions ?? {};
      return this;
    }

    withAutomaticReconnect() {
      return this;
    }

    build() {
      const connection = new FakeMvcConnection(this.url, this.options);
      connections.push(connection);
      return connection;
    }
  }

  window.signalR = { HubConnectionBuilder };
  window.dignite = {
    abp: {
      notificationCenter: {
        notifications: {
          getCount: () => Promise.resolve(nextUnreadCount),
          markAsRead: () => Promise.resolve(),
          markAllAsRead: () => Promise.resolve(),
        },
      },
    },
    notificationCenter: options.accessTokenFactory
      ? { realtimeOptions: { accessTokenFactory: options.accessTokenFactory } }
      : {},
  };
  window.abp = {
    ajax: () => Promise.resolve('<div class="dignite-notification-list-content" data-unread-count="0"></div>'),
    event: {
      on: (name: string, handler: () => void) => {
        const handlers = abpEvents.get(name) ?? [];
        handlers.push(handler);
        abpEvents.set(name, handlers);
      },
    },
  };
  window.console = console;
  window.eval(mvcBundle);

  const flush = async () => {
    await Promise.resolve();
    await new Promise(resolve => window.setTimeout(resolve, 0));
    await Promise.resolve();
  };

  return {
    window,
    connections,
    get nextUnreadCount() {
      return nextUnreadCount;
    },
    set nextUnreadCount(value: number) {
      nextUnreadCount = value;
    },
    async start() {
      window.document.dispatchEvent(new window.Event('DOMContentLoaded'));
      await flush();
    },
    flush,
    bells: () => Array.from(window.document.querySelectorAll('.dignite-notification-bell')),
    badgeCounts: () => Array.from(window.document.querySelectorAll('.dignite-notification-badge'))
      .map(element => element.getAttribute('data-count')),
    raiseAbpEvent: (name: string) => {
      for (const handler of abpEvents.get(name) ?? []) {
        handler();
      }
    },
  };
}

class FakeMvcConnection {
  private handlers = new Map<string, Array<() => void>>();
  private reconnectedHandler?: () => void;
  private closeHandler?: () => void;

  start = vi.fn(() => Promise.resolve());

  stop = vi.fn(() => Promise.resolve());

  constructor(
    readonly url: string,
    readonly options: { accessTokenFactory?: () => string },
  ) {
  }

  on(methodName: string, handler: () => void): void {
    const handlers = this.handlers.get(methodName) ?? [];
    handlers.push(handler);
    this.handlers.set(methodName, handlers);
  }

  onreconnected(handler: () => void): void {
    this.reconnectedHandler = handler;
  }

  onclose(handler: () => void): void {
    this.closeHandler = handler;
  }

  emit(methodName: string): void {
    for (const handler of this.handlers.get(methodName) ?? []) {
      handler();
    }
  }

  emitClosed(): void {
    this.closeHandler?.();
  }

  emitReconnected(): void {
    this.reconnectedHandler?.();
  }

  handlerCount(methodName: string): number {
    return this.handlers.get(methodName)?.length ?? 0;
  }
}

interface MvcHostOptions {
  bellCount?: number;
  hubUrl?: string;
  accessTokenFactory?: () => string;
}

type MvcWindow = Window & typeof globalThis & {
  signalR: unknown;
  dignite: unknown;
  abp: unknown;
  eval(script: string): unknown;
};
