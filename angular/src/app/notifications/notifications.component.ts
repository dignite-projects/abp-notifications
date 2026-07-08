import { Component, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { OAuthService } from 'angular-oauth2-oidc';
import {
  NotificationBellComponent,
  NotificationSubscriptionsComponent,
} from '@dignite-abp/notification-center';
import { environment } from '../../environments/environment';

/**
 * Demo page consuming the @dignite-abp/notification-center library components against the host API.
 * Wires SignalR: connects to the host's /signalr-hubs/notifications with the OAuth token and, on every
 * "ReceiveNotification" push, calls the bell's refresh() for real-time updates. The "Send test notification"
 * button publishes to the current user so you can watch the bell update live.
 */
@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [NotificationBellComponent, NotificationSubscriptionsComponent],
  template: `
    <div class="container py-4">
      <div class="d-flex align-items-center justify-content-between mb-3">
        <h2 class="mb-0">Notifications</h2>
        <nc-notification-bell></nc-notification-bell>
      </div>
      <p class="text-muted">
        The bell (top-right) shows your unread count and recent notifications, updated in real time over
        SignalR. Click below to publish a notification to yourself and watch the bell update instantly.
      </p>
      <button type="button" class="btn btn-primary" (click)="publishTest()">
        Send myself a test notification
      </button>
      <hr />
      <h4>Subscriptions</h4>
      <nc-notification-subscriptions></nc-notification-subscriptions>
    </div>
  `,
})
export class NotificationsComponent implements OnInit, OnDestroy {
  @ViewChild(NotificationBellComponent) bell?: NotificationBellComponent;

  private oauthService = inject(OAuthService);
  private restService = inject(RestService);
  private hubConnection?: HubConnection;

  ngOnInit(): void {
    const hubUrl = `${environment.apis.default.url}/signalr-hubs/notifications`;

    this.hubConnection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => this.oauthService.getAccessToken() })
      .withAutomaticReconnect()
      .build();

    // Server pushes a per-recipient RealTimeNotification (recipient list already stripped). We just refresh
    // the bell from the API — no need to trust/render the pushed payload directly.
    this.hubConnection.on('ReceiveNotification', () => this.bell?.refresh());

    this.hubConnection
      .start()
      .catch(err => console.warn('Notification hub connection failed; bell falls back to polling.', err));
  }

  ngOnDestroy(): void {
    this.hubConnection?.stop();
  }

  publishTest(): void {
    this.restService
      .request<void, void>(
        { method: 'POST', url: '/api/app/demo-notification/publish-test' },
        { apiName: 'default' },
      )
      .subscribe();
  }
}
