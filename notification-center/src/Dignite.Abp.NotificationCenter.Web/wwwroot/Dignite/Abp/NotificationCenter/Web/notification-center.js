(function () {
    'use strict';

    // ABP's dynamic JavaScript service proxy for the NotificationCenter API. It's generated at
    // /Abp/ServiceProxyScript and included on every ABP MVC page by default, so we call the application
    // service directly (dignite.abp.notificationCenter.notifications.*) instead of hand-writing fetch():
    // abp.ajax handles antiforgery, authentication and error reporting, and this surface auto-tracks the
    // C# INotificationAppService (no URLs to keep in sync).
    function api() {
        return dignite.abp.notificationCenter.notifications;
    }

    // ---- click handling (mark-as-read, mark-all, follow entity link) ----
    document.addEventListener('click', function (e) {
        var item = e.target.closest('.dignite-notification-item');
        if (item) {
            var notificationId = item.getAttribute('data-notification-id');
            if (notificationId) {
                api().markAsRead(notificationId).then(function () {
                    setBadgeCount(getBadgeCount() - 1);
                    item.classList.remove('dignite-notification-unread');
                });
            }
            if (item.getAttribute('data-has-link') !== 'true') {
                e.preventDefault();
            }
            return;
        }

        var markAllLink = e.target.closest('.dignite-notification-mark-all-read');
        if (markAllLink) {
            e.preventDefault();
            api().markAllAsRead().then(function () {
                location.reload();
            });
        }
    });

    // ---- subscription toggles ----
    document.addEventListener('change', function (e) {
        var toggle = e.target.closest('.dignite-subscription-toggle');
        if (!toggle) {
            return;
        }

        var name = toggle.getAttribute('data-notification-name');
        var request = toggle.checked ? api().subscribe(name) : api().unsubscribe(name);
        // abp.ajax already surfaces the error to the user; just revert the checkbox if the call failed.
        request.then(null, function () {
            toggle.checked = !toggle.checked;
        });
    });

    // ---- unread badge helpers ----
    function badgeEl() {
        return document.querySelector('.dignite-notification-badge');
    }

    function getBadgeCount() {
        var el = badgeEl();
        return el ? parseInt(el.getAttribute('data-count') || '0', 10) : 0;
    }

    function setBadgeCount(count) {
        var el = badgeEl();
        if (!el) {
            return;
        }
        count = Math.max(0, count);
        el.setAttribute('data-count', count);
        el.textContent = count;
        if (count > 0) {
            el.removeAttribute('hidden');
        } else {
            el.setAttribute('hidden', '');
        }
    }

    // ---- real-time receive over ABP SignalR (optional, degrades gracefully) ----
    // The server-side Notifier (Dignite.Abp.Notifications.SignalR) pushes a per-recipient RealTimeNotification
    // (recipient list already stripped, per notifications-invariants.md §4) via the strongly-typed client method
    // "ReceiveNotification". We only nudge the unread badge + flag the bell here; the authoritative, fully
    // rendered list (localized display name, custom per-type view components, entity links) is server-rendered
    // on the next open/refresh — so we never duplicate server rendering (or its localization) on the client.
    function connectSignalR() {
        if (typeof signalR === 'undefined') {
            return; // @microsoft/signalr not loaded by the host — bell stays non-live, still fully usable.
        }
        var bell = document.querySelector('.dignite-notification-bell');
        var hubUrl = bell ? bell.getAttribute('data-signalr-hub-url') : null;
        if (!hubUrl) {
            return;
        }

        var connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        connection.on('ReceiveNotification', function () {
            setBadgeCount(getBadgeCount() + 1);
            if (bell) {
                bell.classList.add('dignite-notification-pulse');
                setTimeout(function () { bell.classList.remove('dignite-notification-pulse'); }, 1000);
            }
        });

        connection.start().catch(function (err) {
            if (window.console) {
                console.warn('Notification hub connection failed; bell will not update live.', err);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', connectSignalR);
    } else {
        connectSignalR();
    }
})();
