import { Injectable, InjectionToken, NgZone, OnDestroy, inject } from '@angular/core';
import { AuthService, ConfigStateService, EnvironmentService, SessionStateService } from '@abp/ng.core';
import {
  HubConnection,
  HubConnectionBuilder,
  IRetryPolicy,
} from '@microsoft/signalr';
import { BehaviorSubject, Subject, Subscription, merge } from 'rxjs';
import { debounceTime, distinctUntilChanged, map } from 'rxjs/operators';
import { NotificationsService } from '../proxy/dignite/abp/notification-center';

export type NotificationRealtimeRefreshReason =
  | 'received'
  | 'reconnected'
  | 'context-changed'
  | 'manual';

export interface NotificationRealtimeRefreshEvent {
  reason: NotificationRealtimeRefreshReason;
}

export type NotificationRealtimeStatus =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected';

export interface NotificationRealtimeLifecycleEvent {
  status: NotificationRealtimeStatus;
  reason?: string;
  error?: unknown;
}

export interface NotificationCenterRealtimeOptions {
  /**
   * Fully resolved hub URL. Use this when the SignalR endpoint is not under the configured
   * NotificationCenter remote service URL.
   */
  hubUrl?: string;

  /**
   * Hub path appended to the NotificationCenter remote service URL.
   */
  hubPath?: string;

  /**
   * Milliseconds before access-token expiration when the connection should be restarted.
   */
  accessTokenRenewalSkewMs?: number;
}

export interface NotificationRealtimeConnectionFactory {
  create(hubUrl: string, accessTokenFactory: () => string): HubConnection;
}

export const NOTIFICATION_CENTER_REALTIME_OPTIONS =
  new InjectionToken<NotificationCenterRealtimeOptions>('NOTIFICATION_CENTER_REALTIME_OPTIONS', {
    providedIn: 'root',
    factory: () => ({
      hubPath: '/signalr-hubs/notifications',
      accessTokenRenewalSkewMs: 60_000,
    }),
  });

export const NOTIFICATION_REALTIME_CONNECTION_FACTORY =
  new InjectionToken<NotificationRealtimeConnectionFactory>('NOTIFICATION_REALTIME_CONNECTION_FACTORY', {
    providedIn: 'root',
    factory: () => ({
      create: (hubUrl, accessTokenFactory) =>
        new HubConnectionBuilder()
          .withUrl(hubUrl, { accessTokenFactory })
          .withAutomaticReconnect(createNotificationRealtimeRetryPolicy())
          .build(),
    }),
  });

@Injectable({ providedIn: 'root' })
export class NotificationRealtimeService implements OnDestroy {
  private readonly refreshSubject = new Subject<NotificationRealtimeRefreshEvent>();

  private readonly lifecycleSubject = new BehaviorSubject<NotificationRealtimeLifecycleEvent>({
    status: 'idle',
  });

  readonly refreshRequested$ = this.refreshSubject.asObservable();
  readonly lifecycle$ = this.lifecycleSubject.asObservable();

  private readonly authService = inject(AuthService);
  private readonly configState = inject(ConfigStateService);
  private readonly environmentService = inject(EnvironmentService);
  private readonly sessionState = inject(SessionStateService);
  private readonly notificationService = inject(NotificationsService);
  private readonly ngZone = inject(NgZone);
  private readonly options = inject(NOTIFICATION_CENTER_REALTIME_OPTIONS);
  private readonly connectionFactory = inject(NOTIFICATION_REALTIME_CONNECTION_FACTORY);

  private retainCount = 0;
  private contextSubscription?: Subscription;
  private connection?: HubConnection;
  private connectionContextKey?: string;
  private retryTimer?: ReturnType<typeof setTimeout>;
  private tokenRenewalTimer?: ReturnType<typeof setTimeout>;
  private manualRetryCount = 0;
  private handledExpiredTokenAt?: number;

  retain(): () => void {
    this.retainCount++;
    if (this.retainCount === 1) {
      this.startRuntime();
    }

    let released = false;
    return () => {
      if (released) {
        return;
      }

      released = true;
      this.retainCount = Math.max(0, this.retainCount - 1);
      if (this.retainCount === 0) {
        this.stopRuntime('no-consumers');
      }
    };
  }

  requestRefresh(reason: NotificationRealtimeRefreshReason = 'manual'): void {
    this.emitRefresh(reason);
  }

  ngOnDestroy(): void {
    this.stopRuntime('destroy');
  }

  private startRuntime(): void {
    this.watchContext();
    this.reconnectIfNeeded('consumer-start');
  }

  private stopRuntime(reason: string): void {
    this.contextSubscription?.unsubscribe();
    this.contextSubscription = undefined;
    this.clearRetryTimer();
    this.stopConnection(reason);
  }

  private watchContext(): void {
    if (this.contextSubscription) {
      return;
    }

    this.contextSubscription = merge(
      this.configState.createOnUpdateStream(state =>
        [
          state.currentUser?.isAuthenticated ?? false,
          state.currentUser?.id ?? '',
          state.currentUser?.tenantId ?? '',
        ].join(':'),
      ),
      this.sessionState.onTenantChange$().pipe(
        map(tenant => `tenant:${tenant?.id ?? ''}:${tenant?.name ?? ''}`),
      ),
      this.environmentService.createOnUpdateStream(state => {
        const api = state.apis?.[this.notificationService.apiName] ?? state.apis?.default;
        return api?.url ?? '';
      }),
    )
      .pipe(debounceTime(0), distinctUntilChanged())
      .subscribe(() => {
        this.emitRefresh('context-changed');
        this.reconnectIfNeeded('context-changed');
      });
  }

  private reconnectIfNeeded(reason: string): void {
    if (this.retainCount <= 0) {
      return;
    }

    const context = this.createContext();
    if (!context.isAuthenticated) {
      this.stopConnection('unauthenticated');
      return;
    }

    if (this.connection && this.connectionContextKey === context.key) {
      this.scheduleTokenRenewalReconnect();
      return;
    }

    this.restartConnection(context, reason);
  }

  private restartConnection(context: NotificationRealtimeConnectionContext, reason: string): void {
    this.clearRetryTimer();
    this.stopConnection(reason);
    if (this.retainCount <= 0 || !context.isAuthenticated) {
      return;
    }

    const connection = this.connectionFactory.create(
      context.hubUrl,
      () => this.authService.getAccessToken(),
    );
    this.connection = connection;
    this.connectionContextKey = context.key;

    connection.on('ReceiveNotification', () => {
      this.emitIfCurrent(connection, 'received');
    });
    connection.onreconnecting(error => {
      if (this.connection !== connection) {
        return;
      }

      this.lifecycleSubject.next({ status: 'reconnecting', reason: 'transport', error });
    });
    connection.onreconnected(() => {
      if (this.connection !== connection) {
        return;
      }

      this.manualRetryCount = 0;
      this.lifecycleSubject.next({ status: 'connected', reason: 'reconnected' });
      this.scheduleTokenRenewalReconnect();
      this.emitRefresh('reconnected');
    });
    connection.onclose(error => {
      if (this.connection !== connection) {
        return;
      }

      this.connection = undefined;
      this.connectionContextKey = undefined;
      this.clearTokenRenewalTimer();
      this.lifecycleSubject.next({ status: 'disconnected', reason: 'closed', error });
      if (this.retainCount > 0 && this.authService.isAuthenticated) {
        this.scheduleReconnect('closed');
      }
    });

    this.lifecycleSubject.next({ status: 'connecting', reason });
    connection.start()
      .then(() => {
        if (this.connection !== connection) {
          return;
        }

        this.manualRetryCount = 0;
        this.lifecycleSubject.next({ status: 'connected', reason });
        this.scheduleTokenRenewalReconnect();
        if (this.shouldRefreshAfterStart(reason)) {
          this.emitRefresh('reconnected');
        }
      })
      .catch(error => {
        if (this.connection !== connection) {
          return;
        }

        this.connection = undefined;
        this.connectionContextKey = undefined;
        this.clearTokenRenewalTimer();
        this.lifecycleSubject.next({ status: 'disconnected', reason, error });
        if (this.retainCount > 0 && this.authService.isAuthenticated) {
          this.scheduleReconnect(reason);
        }
      });
  }

  private stopConnection(reason: string): void {
    const connection = this.connection;
    this.connection = undefined;
    this.connectionContextKey = undefined;
    this.clearTokenRenewalTimer();
    if (connection) {
      void connection.stop().catch(() => undefined);
    }

    this.lifecycleSubject.next({ status: 'disconnected', reason });
  }

  private emitIfCurrent(
    connection: HubConnection,
    reason: NotificationRealtimeRefreshReason,
  ): void {
    if (this.connection !== connection) {
      return;
    }

    this.emitRefresh(reason);
  }

  private emitRefresh(reason: NotificationRealtimeRefreshReason): void {
    this.ngZone.run(() => {
      this.refreshSubject.next({ reason });
    });
  }

  private scheduleReconnect(reason: string): void {
    this.clearRetryTimer();
    const retryCount = this.manualRetryCount++;
    const delay = calculateNotificationRealtimeRetryDelay(retryCount);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = undefined;
      this.reconnectIfNeeded(`${reason}-retry`);
    }, delay);
  }

  private scheduleTokenRenewalReconnect(): void {
    this.clearTokenRenewalTimer();
    const expiration = this.authService.getAccessTokenExpiration();
    const expiresAt = normalizeExpirationToMilliseconds(expiration);
    if (!expiresAt) {
      this.handledExpiredTokenAt = undefined;
      return;
    }

    const delay = expiresAt - Date.now() - (this.options.accessTokenRenewalSkewMs ?? 60_000);
    if (delay <= 0) {
      if (this.handledExpiredTokenAt === expiresAt) {
        return;
      }

      this.handledExpiredTokenAt = expiresAt;
      const context = this.createContext();
      if (context.isAuthenticated) {
        this.restartConnection(context, 'token-renewal');
      }
      return;
    }

    this.handledExpiredTokenAt = undefined;
    this.tokenRenewalTimer = setTimeout(() => {
      this.tokenRenewalTimer = undefined;
      const context = this.createContext();
      if (context.isAuthenticated) {
        this.restartConnection(context, 'token-renewal');
      }
    }, delay);
  }

  private clearRetryTimer(): void {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = undefined;
    }
  }

  private clearTokenRenewalTimer(): void {
    if (this.tokenRenewalTimer) {
      clearTimeout(this.tokenRenewalTimer);
      this.tokenRenewalTimer = undefined;
    }
  }

  private createContext(): NotificationRealtimeConnectionContext {
    const currentUser = this.configState.getOne('currentUser');
    const tenant = this.sessionState.getTenant();
    const hubUrl = this.resolveHubUrl();
    const isAuthenticated = this.authService.isAuthenticated ||
      currentUser?.isAuthenticated === true;

    return {
      isAuthenticated,
      hubUrl,
      key: [
        hubUrl,
        isAuthenticated ? 'auth' : 'anonymous',
        currentUser?.id ?? '',
        currentUser?.tenantId ?? tenant?.id ?? '',
        tenant?.name ?? '',
      ].join('|'),
    };
  }

  private shouldRefreshAfterStart(reason: string): boolean {
    return reason !== 'consumer-start';
  }

  private resolveHubUrl(): string {
    if (this.options.hubUrl?.trim()) {
      return this.options.hubUrl.trim();
    }

    const apiUrl = this.environmentService
      .getApiUrl(this.notificationService.apiName)
      .replace(/\/$/, '');
    const hubPath = this.options.hubPath || '/signalr-hubs/notifications';
    return `${apiUrl}${hubPath.startsWith('/') ? hubPath : `/${hubPath}`}`;
  }
}

interface NotificationRealtimeConnectionContext {
  isAuthenticated: boolean;
  hubUrl: string;
  key: string;
}

export function createNotificationRealtimeRetryPolicy(): IRetryPolicy {
  return {
    nextRetryDelayInMilliseconds: retryContext =>
      calculateNotificationRealtimeRetryDelay(retryContext.previousRetryCount),
  };
}

export function calculateNotificationRealtimeRetryDelay(
  previousRetryCount: number,
  random: () => number = Math.random,
): number {
  if (previousRetryCount <= 0) {
    return 0;
  }

  const cappedRetryCount = Math.min(previousRetryCount, 5);
  const baseDelay = Math.min(30_000, 1_000 * 2 ** cappedRetryCount);
  const jitter = Math.floor(random() * Math.min(1_000, Math.max(1, baseDelay * 0.2)));
  return baseDelay + jitter;
}

function normalizeExpirationToMilliseconds(expiration: number): number | null {
  if (!Number.isFinite(expiration) || expiration <= 0) {
    return null;
  }

  return expiration < 10_000_000_000
    ? expiration * 1000
    : expiration;
}
