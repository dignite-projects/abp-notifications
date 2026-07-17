import { Component, Input } from '@angular/core';
import { LocalizationPipe } from '@abp/ng.core';
import { NotificationData } from '../proxy/dignite/abp/notifications';

/** Safe fallback for unknown, future, malformed, or failed-upcast notification payloads. */
@Component({
  selector: 'abp-unsupported-notification-data',
  standalone: true,
  imports: [LocalizationPipe],
  template: `
    <p class="abp-notification-item-message abp-notification-item-unsupported text-muted">
      {{ 'AbpNotificationCenter::UnsupportedNotification' | abpLocalization }}
    </p>
  `,
})
export class UnsupportedNotificationDataComponent {
  @Input() data?: NotificationData | null;
}
