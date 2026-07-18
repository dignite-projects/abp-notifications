import { TestBed } from '@angular/core/testing';
import {
  AuthService,
  ConfigStateService,
  EnvironmentService,
  SessionStateService,
} from '@abp/ng.core';
import { Subject, map } from 'rxjs';
import { NotificationsService } from '../proxy/dignite/abp/notification-center';
import {
  NOTIFICATION_CENTER_REALTIME_OPTIONS,
  NOTIFICATION_REALTIME_CONNECTION_FACTORY,
  NotificationRealtimeRefreshReason,
  NotificationRealtimeService,
  calculateNotificationRealtimeRetryDelay,
} from './notification-realtime.service';

describe('NotificationRealtimeService', () => {
  let auth: FakeAuthService;
  let appConfig: FakeApplicationConfiguration;
  let configUpdates: Subject<void>;
  let environmentUpdates: Subject<void>;
  let tenantUpdates: Subject<FakeTenant>;
  let tenant: FakeTenant;
  let apiUrl: string;
  let connections: FakeHubConnection[];
  let service: NotificationRealtimeService;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'));

    auth = new FakeAuthService();
    appConfig = {
      currentUser: {
        isAuthenticated: true,
        id: 'user-a',
        tenantId: 'tenant-a',
      },
    };
    configUpdates = new Subject<void>();
    environmentUpdates = new Subject<void>();
    tenant = { id: 'tenant-a', name: 'Tenant A' };
    tenantUpdates = new Subject<FakeTenant>();
    apiUrl = 'https://api.example.test/';
    connections = [];

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        {
          provide: ConfigStateService,
          useValue: {
            getOne: (key: keyof FakeApplicationConfiguration) => appConfig[key],
            createOnUpdateStream: <T>(selector: (state: FakeApplicationConfiguration) => T) =>
              configUpdates.pipe(map(() => selector(appConfig))),
          },
        },
        {
          provide: EnvironmentService,
          useValue: {
            getApiUrl: () => apiUrl,
            createOnUpdateStream: <T>(selector: (state: FakeEnvironment) => T) =>
              environmentUpdates.pipe(map(() => selector({
                apis: {
                  NotificationCenter: { url: apiUrl },
                },
              }))),
          },
        },
        {
          provide: SessionStateService,
          useValue: {
            getTenant: () => tenant,
            onTenantChange$: () => tenantUpdates.asObservable(),
          },
        },
        { provide: NotificationsService, useValue: { apiName: 'NotificationCenter' } },
        {
          provide: NOTIFICATION_REALTIME_CONNECTION_FACTORY,
          useValue: {
            create: (hubUrl: string, accessTokenFactory: () => string) => {
              const connection = new FakeHubConnection(hubUrl, accessTokenFactory);
              connections.push(connection);
              return connection;
            },
          },
        },
        {
          provide: NOTIFICATION_CENTER_REALTIME_OPTIONS,
          useValue: {
            hubPath: '/signalr-hubs/notifications',
            accessTokenRenewalSkewMs: 60_000,
          },
        },
      ],
    });

    service = TestBed.inject(NotificationRealtimeService);
  });

  afterEach(() => {
    service.ngOnDestroy();
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('shares one SignalR connection and one handler across duplicate components', async () => {
    const releaseFirst = service.retain();
    const releaseSecond = service.retain();
    await flushAsync();

    expect(connections.length).toBe(1);
    expect(connections[0].hubUrl).toBe('https://api.example.test/signalr-hubs/notifications');
    expect(connections[0].handlerCount('ReceiveNotification')).toBe(1);

    const reasons: NotificationRealtimeRefreshReason[] = [];
    const subscription = service.refreshRequested$.subscribe(event => reasons.push(event.reason));

    connections[0].emit('ReceiveNotification');
    expect(reasons).toEqual(['received']);

    releaseFirst();
    await flushAsync();
    expect(connections[0].stop).not.toHaveBeenCalled();

    releaseSecond();
    await flushAsync();
    expect(connections[0].stop).toHaveBeenCalledTimes(1);

    subscription.unsubscribe();
  });

  it('resynchronizes after SignalR reconnect', async () => {
    const release = service.retain();
    await flushAsync();

    const reasons: NotificationRealtimeRefreshReason[] = [];
    const subscription = service.refreshRequested$.subscribe(event => reasons.push(event.reason));

    connections[0].emitReconnected();

    expect(reasons).toEqual(['reconnected']);

    subscription.unsubscribe();
    release();
  });

  it('stops on logout and reconnects with the new user token on login', async () => {
    const release = service.retain();
    await flushAsync();
    const firstConnection = connections[0];
    const reasons: NotificationRealtimeRefreshReason[] = [];
    const subscription = service.refreshRequested$.subscribe(event => reasons.push(event.reason));

    auth.authenticated = false;
    appConfig.currentUser = {
      isAuthenticated: false,
      id: undefined,
      tenantId: undefined,
    };
    configUpdates.next();
    await flushAsync();

    expect(firstConnection.stop).toHaveBeenCalledTimes(1);

    reasons.length = 0;
    firstConnection.emit('ReceiveNotification');
    expect(reasons).toEqual([]);

    auth.authenticated = true;
    auth.token = 'token-b';
    appConfig.currentUser = {
      isAuthenticated: true,
      id: 'user-b',
      tenantId: 'tenant-a',
    };
    configUpdates.next();
    await flushAsync();

    expect(connections.length).toBe(2);
    expect(connections[1].accessTokenFactory()).toBe('token-b');

    subscription.unsubscribe();
    release();
  });

  it('reconnects when the tenant context changes', async () => {
    const release = service.retain();
    await flushAsync();
    const firstConnection = connections[0];

    tenant = { id: 'tenant-b', name: 'Tenant B' };
    tenantUpdates.next(tenant);
    await flushAsync();

    expect(firstConnection.stop).toHaveBeenCalledTimes(1);
    expect(connections.length).toBe(2);

    release();
  });

  it('restarts before token expiration so the new access token is used', async () => {
    auth.expiration = Date.now() + 90_000;
    const release = service.retain();
    await flushAsync();
    const firstConnection = connections[0];

    auth.token = 'token-b';
    auth.expiration = Date.now() + 600_000;
    await vi.advanceTimersByTimeAsync(30_000);
    await flushAsync();

    expect(firstConnection.stop).toHaveBeenCalledTimes(1);
    expect(connections.length).toBe(2);
    expect(connections[1].accessTokenFactory()).toBe('token-b');

    release();
  });

  it('keeps reconnect delays bounded with jitter', () => {
    expect(calculateNotificationRealtimeRetryDelay(0, () => 0.5)).toBe(0);
    expect(calculateNotificationRealtimeRetryDelay(2, () => 0)).toBe(4000);
    expect(calculateNotificationRealtimeRetryDelay(10, () => 0.999)).toBeLessThanOrEqual(31_000);
  });
});

class FakeAuthService {
  authenticated = true;
  token = 'token-a';
  expiration = Date.now() + 600_000;

  get isAuthenticated(): boolean {
    return this.authenticated;
  }

  getAccessToken = vi.fn(() => this.token);

  getAccessTokenExpiration = vi.fn(() => this.expiration);
}

class FakeHubConnection {
  readonly handlers = new Map<string, Array<() => void>>();
  private reconnectedHandler?: () => void;

  start = vi.fn(() => Promise.resolve());

  stop = vi.fn(() => Promise.resolve());

  constructor(
    readonly hubUrl: string,
    readonly accessTokenFactory: () => string,
  ) {
  }

  on(methodName: string, handler: () => void): void {
    const handlers = this.handlers.get(methodName) ?? [];
    handlers.push(handler);
    this.handlers.set(methodName, handlers);
  }

  onreconnecting(): void {
  }

  onreconnected(handler: () => void): void {
    this.reconnectedHandler = handler;
  }

  onclose(): void {
  }

  emit(methodName: string): void {
    for (const handler of this.handlers.get(methodName) ?? []) {
      handler();
    }
  }

  emitReconnected(): void {
    this.reconnectedHandler?.();
  }

  handlerCount(methodName: string): number {
    return this.handlers.get(methodName)?.length ?? 0;
  }
}

interface FakeApplicationConfiguration {
  currentUser?: {
    isAuthenticated?: boolean;
    id?: string;
    tenantId?: string;
  };
}

interface FakeEnvironment {
  apis?: {
    [key: string]: {
      url?: string;
    };
  };
}

interface FakeTenant {
  id?: string;
  name?: string;
}

async function flushAsync(): Promise<void> {
  await Promise.resolve();
  await vi.advanceTimersByTimeAsync(0);
  await Promise.resolve();
}
