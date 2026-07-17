import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { NotificationsService } from '../proxy/dignite/abp/notification-center';
import { NotificationSubscriptionsComponent } from './notification-subscriptions.component';

describe('NotificationSubscriptionsComponent', () => {
  const notificationService = {
    getSubscriptions: vi.fn(() => of({ items: [] })),
    subscribeScoped: vi.fn(() => of(undefined)),
    unsubscribeScoped: vi.fn(() => of(undefined)),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.overrideComponent(NotificationSubscriptionsComponent, {
      set: { template: '', imports: [] },
    });
    TestBed.configureTestingModule({
      providers: [{ provide: NotificationsService, useValue: notificationService }],
    });
  });

  it('submits the complete entity scope when subscribing', () => {
    const component = TestBed.createComponent(NotificationSubscriptionsComponent).componentInstance;
    const subscription = {
      notificationName: 'order.shipped',
      entityTypeName: 'Demo.Order',
      entityId: '42',
      isSubscribed: false,
    };

    component.toggle(subscription, true);

    expect(notificationService.subscribeScoped).toHaveBeenCalledWith({
      notificationName: 'order.shipped',
      entityTypeName: 'Demo.Order',
      entityId: '42',
    });
    expect(subscription.isSubscribed).toBe(true);
  });

  it('keeps definition-wide and entity rows distinct and unsubscribes the exact scope', () => {
    const component = TestBed.createComponent(NotificationSubscriptionsComponent).componentInstance;
    const definitionWide = {
      notificationName: 'order.shipped',
      entityTypeName: null,
      entityId: null,
      isSubscribed: true,
    };
    const entitySpecific = {
      notificationName: 'order.shipped',
      entityTypeName: 'Demo.Order',
      entityId: '42',
      isSubscribed: true,
    };

    expect(component.scopeKey(definitionWide)).not.toBe(component.scopeKey(entitySpecific));
    component.toggle(entitySpecific, false);

    expect(notificationService.unsubscribeScoped).toHaveBeenCalledWith({
      notificationName: 'order.shipped',
      entityTypeName: 'Demo.Order',
      entityId: '42',
    });
    expect(entitySpecific.isSubscribed).toBe(false);
    expect(definitionWide.isSubscribed).toBe(true);
  });
});
