(function () {
    'use strict';

    var digniteRoot = window.dignite = window.dignite || {};
    var notificationCenterRoot = digniteRoot.notificationCenter = digniteRoot.notificationCenter || {};

    if (notificationCenterRoot.webInitialized) {
        return;
    }
    notificationCenterRoot.webInitialized = true;

    // ABP's dynamic JavaScript service proxy for the NotificationCenter API. It's generated at
    // /Abp/ServiceProxyScript and included on every ABP MVC page by default, so we call the application
    // service directly (dignite.abp.notificationCenter.notifications.*) instead of hand-writing fetch().
    function api() {
        return dignite.abp.notificationCenter.notifications;
    }

    function refreshDropdown(bell) {
        if (!bell || bell.getAttribute('data-refreshing-dropdown') === 'true') {
            return;
        }

        var url = bell.getAttribute('data-dropdown-url');
        var list = bell.querySelector('.dignite-notification-list');
        if (!url || !list || typeof abp === 'undefined' || !abp.ajax) {
            return;
        }

        var currentContent = list.querySelector('.dignite-notification-list-content');
        if (currentContent) {
            currentContent.setAttribute('aria-busy', 'true');
        }
        bell.setAttribute('data-refreshing-dropdown', 'true');

        var clearRefreshing = function () {
            bell.removeAttribute('data-refreshing-dropdown');
            var content = list.querySelector('.dignite-notification-list-content');
            if (content) {
                content.removeAttribute('aria-busy');
            }
        };

        abp.ajax({
            url: url,
            type: 'GET',
            dataType: 'html'
        }).then(function (html) {
            var temp = document.createElement('div');
            temp.innerHTML = (html || '').trim();

            var nextContent = temp.querySelector('.dignite-notification-list-content') || temp.firstElementChild;
            if (!nextContent) {
                clearRefreshing();
                return;
            }

            currentContent = list.querySelector('.dignite-notification-list-content');
            if (currentContent) {
                currentContent.replaceWith(nextContent);
            } else {
                list.innerHTML = '';
                list.appendChild(nextContent);
            }

            var unreadCount = parseInt(nextContent.getAttribute('data-unread-count') || '0', 10);
            if (!isNaN(unreadCount)) {
                setAllBadgeCounts(unreadCount);
            }

            clearRefreshing();
        }, clearRefreshing);
    }

    document.addEventListener('show.bs.dropdown', function (e) {
        refreshDropdown(e.target.closest('.dignite-notification-bell'));
    });

    // ---- click handling (refresh dropdown, mark-as-read, mark-all, follow entity link) ----
    document.addEventListener('click', function (e) {
        var toggle = e.target.closest('.dignite-notification-toggle');
        if (toggle) {
            refreshDropdown(toggle.closest('.dignite-notification-bell'));
        }

        var item = e.target.closest('.dignite-notification-item');
        if (item) {
            var notificationId = item.getAttribute('data-notification-id');
            var hasLink = item.getAttribute('data-has-link') === 'true';
            var href = item.getAttribute('href');
            e.preventDefault();
            if (notificationId) {
                api().markAsRead(notificationId).then(function () {
                    setAllBadgeCounts(getFirstBadgeCount() - 1);
                    item.classList.remove('dignite-notification-unread');
                    if (hasLink && href) {
                        window.location.href = href;
                    }
                }, function () {
                    if (hasLink && href) {
                        window.location.href = href;
                    }
                });
                return;
            }
            if (hasLink && href) {
                window.location.href = href;
            }
            return;
        }

        var markAllLink = e.target.closest('.dignite-notification-mark-all-read');
        if (markAllLink) {
            e.preventDefault();
            api().markAllAsRead().then(function () {
                setAllBadgeCounts(0);
                var bell = markAllLink.closest('.dignite-notification-bell');
                if (bell) {
                    bell.querySelectorAll('.dignite-notification-item').forEach(function (item) {
                        item.classList.remove('dignite-notification-unread');
                    });
                }
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
        var entityTypeName = toggle.getAttribute('data-entity-type-name') || null;
        var entityId = toggle.getAttribute('data-entity-id') || null;
        var scope = {
            notificationName: name,
            entityTypeName: entityTypeName,
            entityId: entityId
        };
        var request = toggle.checked ? api().subscribeScoped(scope) : api().unsubscribeScoped(scope);
        // abp.ajax already surfaces the error to the user; just revert the checkbox if the call failed.
        request.then(null, function () {
            toggle.checked = !toggle.checked;
        });
    });

    // ---- unread badge helpers ----
    function badgeEls() {
        return document.querySelectorAll('.dignite-notification-badge');
    }

    function getFirstBadgeCount() {
        var el = badgeEls()[0];
        return el ? parseInt(el.getAttribute('data-count') || '0', 10) : 0;
    }

    function setAllBadgeCounts(count) {
        count = Math.max(0, count);
        badgeEls().forEach(function (el) {
            el.setAttribute('data-count', count);
            el.textContent = count;
            if (count > 0) {
                el.removeAttribute('hidden');
            } else {
                el.setAttribute('hidden', '');
            }
        });
    }

    // ---- application-scoped real-time runtime ----
    function createRealtimeRuntime() {
        var consumers = [];
        var refreshHandlers = [];
        var connection = null;
        var hubUrl = null;
        var retryTimer = null;
        var manualRetryCount = 0;

        function retain(element) {
            if (element && consumers.indexOf(element) < 0) {
                consumers.push(element);
                configure(element);
            }

            start('consumer-start');

            return function () {
                var index = consumers.indexOf(element);
                if (index >= 0) {
                    consumers.splice(index, 1);
                }
                if (consumers.length === 0) {
                    stop('no-consumers');
                }
            };
        }

        function configure(element) {
            var nextHubUrl = element ? element.getAttribute('data-signalr-hub-url') : null;
            if (!nextHubUrl || nextHubUrl === hubUrl) {
                return;
            }

            hubUrl = nextHubUrl;
            if (connection) {
                restart('hub-url-changed');
            }
        }

        function onRefresh(handler) {
            if (refreshHandlers.indexOf(handler) < 0) {
                refreshHandlers.push(handler);
            }

            return function () {
                var index = refreshHandlers.indexOf(handler);
                if (index >= 0) {
                    refreshHandlers.splice(index, 1);
                }
            };
        }

        function emitRefresh(reason) {
            refreshHandlers.slice().forEach(function (handler) {
                handler({ reason: reason });
            });
        }

        function start(reason) {
            clearRetryTimer();
            if (connection || !hubUrl || typeof signalR === 'undefined') {
                return;
            }

            var accessTokenFactory = resolveAccessTokenFactory();
            var builder = new signalR.HubConnectionBuilder();
            var withUrlOptions = accessTokenFactory ? { accessTokenFactory: accessTokenFactory } : undefined;
            var nextConnection = builder
                .withUrl(hubUrl, withUrlOptions)
                .withAutomaticReconnect(createRetryPolicy())
                .build();

            connection = nextConnection;

            nextConnection.on('ReceiveNotification', function () {
                if (connection !== nextConnection) {
                    return;
                }
                emitRefresh('received');
            });

            nextConnection.onreconnected(function () {
                if (connection !== nextConnection) {
                    return;
                }
                manualRetryCount = 0;
                emitRefresh('reconnected');
            });

            nextConnection.onclose(function () {
                if (connection !== nextConnection) {
                    return;
                }
                connection = null;
                if (consumers.length > 0) {
                    scheduleReconnect('closed');
                }
            });

            nextConnection.start().then(function () {
                if (connection === nextConnection) {
                    manualRetryCount = 0;
                }
            }).catch(function (err) {
                if (connection !== nextConnection) {
                    return;
                }

                connection = null;
                if (window.console) {
                    console.warn('Notification hub connection failed; bell will not update live.', err);
                }
                if (consumers.length > 0) {
                    scheduleReconnect(reason);
                }
            });
        }

        function stop() {
            clearRetryTimer();
            var currentConnection = connection;
            connection = null;
            if (currentConnection) {
                currentConnection.stop().catch(function () { });
            }
        }

        function restart(reason) {
            stop(reason);
            start(reason);
        }

        function sessionChanged() {
            emitRefresh('context-changed');
            restart('context-changed');
        }

        function scheduleReconnect(reason) {
            clearRetryTimer();
            var retryCount = manualRetryCount++;
            var delay = calculateRetryDelay(retryCount);
            retryTimer = setTimeout(function () {
                retryTimer = null;
                start(reason);
            }, delay);
        }

        function clearRetryTimer() {
            if (retryTimer) {
                clearTimeout(retryTimer);
                retryTimer = null;
            }
        }

        return {
            retain: retain,
            configure: configure,
            onRefresh: onRefresh,
            requestRefresh: emitRefresh,
            sessionChanged: sessionChanged,
            stop: stop
        };
    }

    function resolveAccessTokenFactory() {
        var options = notificationCenterRoot.realtimeOptions || {};
        if (typeof options.accessTokenFactory === 'function') {
            return options.accessTokenFactory;
        }

        if (typeof abp !== 'undefined' &&
            abp.auth &&
            typeof abp.auth.getToken === 'function') {
            return function () {
                return abp.auth.getToken();
            };
        }

        return null;
    }

    function createRetryPolicy() {
        return {
            nextRetryDelayInMilliseconds: function (retryContext) {
                return calculateRetryDelay(retryContext.previousRetryCount);
            }
        };
    }

    function calculateRetryDelay(previousRetryCount) {
        if (previousRetryCount <= 0) {
            return 0;
        }

        var cappedRetryCount = Math.min(previousRetryCount, 5);
        var baseDelay = Math.min(30000, Math.pow(2, cappedRetryCount) * 1000);
        var jitter = Math.floor(Math.random() * Math.min(1000, Math.max(1, baseDelay * 0.2)));
        return baseDelay + jitter;
    }

    var realtime = notificationCenterRoot.realtime =
        notificationCenterRoot.realtime || createRealtimeRuntime();
    var bellRegistrations = [];

    realtime.onRefresh(function (event) {
        refreshAllBells(event.reason);
    });

    function syncBells() {
        cleanupRemovedBells();
        document.querySelectorAll('.dignite-notification-bell').forEach(function (bell) {
            if (bell.getAttribute('data-notification-realtime-bound') === 'true') {
                return;
            }

            bell.setAttribute('data-notification-realtime-bound', 'true');
            bellRegistrations.push({
                bell: bell,
                release: realtime.retain(bell)
            });
        });
    }

    function cleanupRemovedBells() {
        for (var i = bellRegistrations.length - 1; i >= 0; i--) {
            if (document.documentElement.contains(bellRegistrations[i].bell)) {
                continue;
            }

            bellRegistrations[i].release();
            bellRegistrations.splice(i, 1);
        }
    }

    function refreshAllBells(reason) {
        refreshUnreadCount();
        document.querySelectorAll('.dignite-notification-bell').forEach(function (bell) {
            if (reason === 'received') {
                bell.classList.add('dignite-notification-pulse');
                setTimeout(function () { bell.classList.remove('dignite-notification-pulse'); }, 1000);
            }

            if (isDropdownOpen(bell)) {
                refreshDropdown(bell);
            }
        });
    }

    function refreshUnreadCount() {
        var service = api();
        if (!service || typeof service.getCount !== 'function') {
            return;
        }

        service.getCount(0).then(function (count) {
            var unreadCount = parseInt(count || '0', 10);
            if (!isNaN(unreadCount)) {
                setAllBadgeCounts(unreadCount);
            }
        });
    }

    function isDropdownOpen(bell) {
        var toggle = bell ? bell.querySelector('.dignite-notification-toggle') : null;
        return toggle && toggle.getAttribute('aria-expanded') === 'true';
    }

    function bindAbpEvent(name, handler) {
        if (typeof abp !== 'undefined' &&
            abp.event &&
            typeof abp.event.on === 'function') {
            abp.event.on(name, handler);
        }
    }

    bindAbpEvent('abp.auth.login', realtime.sessionChanged);
    bindAbpEvent('abp.auth.tokenChanged', realtime.sessionChanged);
    bindAbpEvent('abp.multiTenancy.tenantChanged', realtime.sessionChanged);
    bindAbpEvent('abp.auth.logout', function () {
        realtime.stop('logout');
        refreshAllBells('context-changed');
    });
    bindAbpEvent('dignite.notificationCenter.sessionChanged', realtime.sessionChanged);

    document.addEventListener('dignite.notificationCenter.sessionChanged', realtime.sessionChanged);
    window.addEventListener('storage', function (e) {
        var key = e.key || '';
        if (key.indexOf('token') >= 0 || key.indexOf('tenant') >= 0 || key.indexOf('Abp') >= 0) {
            realtime.sessionChanged();
        }
    });
    window.addEventListener('beforeunload', function () {
        realtime.stop('beforeunload');
    });

    if (typeof MutationObserver !== 'undefined') {
        new MutationObserver(syncBells).observe(document.documentElement, {
            childList: true,
            subtree: true
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', syncBells);
    } else {
        syncBells();
    }
})();
