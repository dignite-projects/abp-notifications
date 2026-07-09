import { Component, OnInit, inject } from '@angular/core';
import { LocalizationPipe } from '@abp/ng.core';
import { NotificationsService, NotificationSubscriptionDto } from '../proxy/dignite/abp/notification-center';

/**
 * Lists the notification types available to the current user with subscribe/unsubscribe toggles.
 */
@Component({
  selector: 'abp-notification-subscriptions',
  standalone: true,
  imports: [LocalizationPipe],
  template: `
    <table class="abp-notification-subscriptions-table">
      <thead>
        <tr>
          <th>{{ 'AbpNotificationCenter::NotificationType' | abpLocalization }}</th>
          <th>{{ 'AbpNotificationCenter::Description' | abpLocalization }}</th>
          <th>{{ 'AbpNotificationCenter::Subscribed' | abpLocalization }}</th>
        </tr>
      </thead>
      <tbody>
        @for (s of subscriptions; track s.notificationName) {
          <tr>
            <td>{{ s.displayName || s.notificationName }}</td>
            <td>{{ s.description }}</td>
            <td>
              <input
                type="checkbox"
                [checked]="s.isSubscribed"
                (change)="toggle(s, $any($event.target).checked)"
              />
            </td>
          </tr>
        }
      </tbody>
    </table>
  `,
  styles: [
    `
      .abp-notification-subscriptions-table { width: 100%; border-collapse: collapse; }
      .abp-notification-subscriptions-table th, .abp-notification-subscriptions-table td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #eee; }
    `,
  ],
})
export class NotificationSubscriptionsComponent implements OnInit {
  subscriptions: NotificationSubscriptionDto[] = [];

  private notificationService = inject(NotificationsService);

  ngOnInit(): void {
    this.notificationService.getSubscriptions().subscribe(r => (this.subscriptions = r.items));
  }

  toggle(subscription: NotificationSubscriptionDto, subscribe: boolean): void {
    const request = subscribe
      ? this.notificationService.subscribe(subscription.notificationName)
      : this.notificationService.unsubscribe(subscription.notificationName);
    request.subscribe(() => (subscription.isSubscribed = subscribe));
  }
}
