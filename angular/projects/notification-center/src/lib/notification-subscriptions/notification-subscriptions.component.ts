import { Component, OnInit, inject } from '@angular/core';
import { NotificationsService, NotificationSubscriptionDto } from '../proxy/dignite/abp/notification-center';

/**
 * Lists the notification types available to the current user with subscribe/unsubscribe toggles.
 */
@Component({
  selector: 'nc-notification-subscriptions',
  standalone: true,
  imports: [],
  template: `
    <table class="nc-subs-table">
      <thead>
        <tr>
          <th>Notification type</th>
          <th>Description</th>
          <th>Subscribed</th>
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
      .nc-subs-table { width: 100%; border-collapse: collapse; }
      .nc-subs-table th, .nc-subs-table td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #eee; }
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
