import { Component, Input, OnDestroy, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { LocalizationPipe } from '@abp/ng.core';
import { Subscription, interval } from 'rxjs';
import { NotificationsService, UserNotificationDto } from '../proxy/dignite/abp/notification-center';
import { UserNotificationState } from '../proxy/dignite/abp/notifications';

/**
 * Notification bell: unread badge + dropdown of recent notifications, with mark-as-read / mark-all-as-read.
 * Polls the unread count on an interval; a host app can also call the public refresh() from a SignalR
 * "ReceiveNotification" handler to update live. Renders generically (display name + time + optional image)
 * — a consuming app can wrap this or project its own per-discriminator template.
 */
@Component({
  selector: 'nc-notification-bell',
  standalone: true,
  imports: [DatePipe, LocalizationPipe],
  template: `
    <div class="nc-bell">
      <button type="button" class="nc-bell-btn" (click)="open = !open" [attr.aria-label]="'AbpNotificationCenter::Notifications' | abpLocalization">
        <span class="nc-bell-icon">&#128276;</span>
        @if (unreadCount > 0) {
          <span class="nc-badge">{{ unreadCount }}</span>
        }
      </button>
      @if (open) {
        <div class="nc-dropdown">
          <div class="nc-dropdown-header">
            <strong>{{ 'AbpNotificationCenter::Notifications' | abpLocalization }}</strong>
            @if (unreadCount > 0) {
              <a href="#" (click)="markAllAsRead(); $event.preventDefault()">{{
                'AbpNotificationCenter::MarkAllAsRead' | abpLocalization
              }}</a>
            }
          </div>
          @if (notifications.length === 0) {
            <div class="nc-empty">{{ 'AbpNotificationCenter::NoNotifications' | abpLocalization }}</div>
          } @else {
            @for (n of notifications; track n.id) {
              <div class="nc-item" [class.nc-unread]="n.state === 0" (click)="markAsRead(n)">
                <div class="nc-item-title">{{ n.notificationDisplayName || n.notificationName }}</div>
                @if (imageUrlOf(n); as img) {
                  <img class="nc-item-image" [src]="img" alt="" />
                }
                <div class="nc-item-time">{{ n.creationTime | date: 'short' }}</div>
              </div>
            }
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .nc-bell { position: relative; display: inline-block; }
      .nc-bell-btn { background: none; border: none; cursor: pointer; position: relative; font-size: 1.2rem; }
      .nc-badge { position: absolute; top: -4px; right: -8px; background: #dc3545; color: #fff; border-radius: 10px; padding: 0 5px; font-size: 0.7rem; }
      .nc-dropdown { position: absolute; right: 0; top: 100%; min-width: 300px; max-height: 400px; overflow-y: auto; background: #fff; border: 1px solid #ddd; border-radius: 4px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); z-index: 1000; }
      .nc-dropdown-header { display: flex; justify-content: space-between; align-items: center; padding: 8px; border-bottom: 1px solid #eee; }
      .nc-empty { padding: 12px; color: #888; }
      .nc-item { padding: 8px; border-bottom: 1px solid #f0f0f0; cursor: pointer; }
      .nc-item.nc-unread { background: rgba(13, 110, 253, 0.05); }
      .nc-item-title { font-size: 0.9rem; }
      .nc-item-time { font-size: 0.75rem; color: #888; }
      .nc-item-image { max-width: 100%; border-radius: 4px; margin-top: 4px; }
    `,
  ],
})
export class NotificationBellComponent implements OnInit, OnDestroy {
  /** Poll interval for the unread count, in ms. Set to 0 to disable polling (e.g. when driving via SignalR). */
  @Input() pollIntervalMs = 30000;

  unreadCount = 0;
  notifications: UserNotificationDto[] = [];
  open = false;

  private notificationService = inject(NotificationsService);
  private pollSub?: Subscription;

  ngOnInit(): void {
    this.refresh();
    if (this.pollIntervalMs > 0) {
      this.pollSub = interval(this.pollIntervalMs).subscribe(() => this.refreshCount());
    }
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  /** Public: a host app can call this from a SignalR "ReceiveNotification" handler to refresh live. */
  refresh(): void {
    this.notificationService.getCount(UserNotificationState.Unread).subscribe(c => (this.unreadCount = c));
    this.notificationService.getList({ maxResultCount: 10 }).subscribe(r => (this.notifications = r.items));
  }

  refreshCount(): void {
    this.notificationService.getCount(UserNotificationState.Unread).subscribe(c => (this.unreadCount = c));
  }

  markAsRead(n: UserNotificationDto): void {
    if (n.state === UserNotificationState.Read) {
      return;
    }
    this.notificationService.markAsRead(n.notificationId).subscribe(() => {
      n.state = UserNotificationState.Read;
      if (this.unreadCount > 0) {
        this.unreadCount--;
      }
    });
  }

  markAllAsRead(): void {
    this.notificationService.markAllAsRead().subscribe(() => this.refresh());
  }

  /** Duck-typed image URL (mirrors IHasNotificationImageUrl's "imageUrl" JSON property). */
  imageUrlOf(n: UserNotificationDto): string | null {
    const url = n.data?.['imageUrl'];
    return typeof url === 'string' ? url : null;
  }
}
