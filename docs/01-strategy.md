# 01 · 定位与战略

## 1. 产品定位

> 面向 ABP 的可扩展通知框架,以及可选的完整通知中心实现。

产品由三部分组成,可分层安装:

- **Dignite Abp Notifications Framework**:通知定义、发布、订阅抽象、分发、Store 抽象、分布式事件、Notifier 扩展机制。
- **Dignite Abp Notification Notifiers**:具体通道——SignalR(现有)、Web Push、Email、FCM、APNs、SMS、Webhook(规划)。
- **Dignite Abp Notification Center**:持久化、用户订阅、收件箱、已读未读、REST API(headless)、EF Core、MongoDB。

## 2. 差异化

对外**不要**讲"ABP 没有通知所以我做了一个"——ABP 一旦出模块、或用户发现其他开源方案,这句话就失效。真正的结构性差异:

- **事件驱动 vs provider 内置**:主要开源竞品 `EasyAbp.NotificationService` 是 provider 内置耦合式;本项目是分布式事件驱动式,Notifier 通过订阅 `RealTimeNotifyEto` 扩展,可跨进程、多消费者并行、独立部署。更适合"分布式 / 微服务 + 想自定义通道"的团队。
- **两种模式一套框架**:无状态转发(只发不存,`NullNotificationStore`)与完整收件箱模式,是同一框架的两个使用级别,不是两个产品。
- **双持久化**:EF Core 与 MongoDB。
- **Headless + 契约驱动渲染**:Notification Center 只暴露 REST API;每条 NotificationData 带稳定类型判别名 + 结构化内容 + 展示提示(图标 key / 标题模板),任意前端(JS/TS 或使用方自建 Blazor)据此渲染,不绑定 UI 技术栈。
  > 注意:这一点在现有实现里对 **远程 HttpApi.Client 客户端是坏的**(见 `03-roadmap.md` 问题 A),必须在 P0 修好,这条契约驱动的渲染才名副其实。
- **开源(LGPL)**:与商业许可证解耦。

> 关于 EasyAbp:其架构不作为参考对象;此处仅将它作为"市场存在 / 中文圈心智占用"的数据点。主要开源竞品架构偏弱,反而是本项目差异化的空间。

## 3. 与 ABP 官方的关系:为什么现在继续做

### 3.1 判断

ABP 官方**短期没有**服务端通知模块的计划,且其任何可能的方向都不会让本项目失去价值。**结论:绿灯继续做。**

### 3.2 证据(2026-07 核实)

- **路线图**:ABP 官方路线图把内容分成 *Next Versions*(近期主攻)和 *Backlog Items*(明确声明非主攻)。"New module: User notification" 位于 **Backlog** 的"应用模块"清单,与 Dynamic dashboard / User guiding / Keycloak integration 并列;近期真正投入的应用模块是 AI Management、Chat with your data、Admin Console 低代码。**没有 issue、milestone、负责人、PR。**
- **历史**:这套"持久化通知系统"需求从 ABP Boilerplate 时代就有,vNext 的 #633 于 2021 年被关闭标记 `canceled`,维护者认为"更像应用功能"。到 2026 年它仍只是 backlog 里的一个想法——**五年多未落地**。
- **官方态度信号**:有人在官方支持区(#5907)直接问"是否有 ABP Boilerplate/ASP.NET Zero 那种持久化通知服务",官方回答是指向 Blazor UI toast、一篇社区 SignalR 博客、以及 Chat 模块——等于明说"没有服务端通知模块,且不把它当核心件"。
- **商业模块**:ABP 商业模块清单(Account、Chat、CMS Kit、File Management、Payment、SaaS、AI Management…)**没有通知模块**。
- **版本节奏**:ABP 已推进到 10.x 线(10.6 preview 已出,跑 .NET 10),精力在 AI 与低代码。

### 3.3 三种未来情形与应对

| ABP 未来走向 | 冲突程度 | 应对 |
|---|---|---|
| 只出 framework 层抽象接口 | 不冲突,甚至协同 | 让本项目去实现/适配官方抽象,成为其主流实现 |
| 出商业通知模块 | 低 | 继续占据开源这一档 |
| 出免费 + 完整 + framework 级全家桶 | 高,但可能性最低且不临近 | 已有数年先发优势与存量用户;靠事件驱动模型 + 双持久化 + 可扩展 UI 差异化 |

### 3.4 监控信号(出现任一,回头重新评估;否则按计划推进)

- ABP 出现带 **milestone / assignee** 的通知专属 issue;
- NuGet/packages 出现 `Volo.Abp.Notifications.*` 之类官方包;
- 路线图把 "User notification" 从 Backlog 挪进 Next Versions;
- ABP 社区放出通知模块设计稿。

### 3.5 一条便宜的设计保险

设计公开契约时,别和内部实现耦太死,留出"万一 ABP 出了抽象,我能低成本写个 adapter 去实现它"的余地。无论 ABP 走哪条路都进可攻退可守。

## 4. 命名决策

**框架核心叫 `Notifications`,不叫 `UserNotifications`。**

1. **框架级模块按"能力"命名,不按"记录对象"命名**。ABP 框架层都是 `Volo.Abp.Identity`/`Emailing`/`Sms`/`BackgroundJobs`。`Notifications` 是能力;`UserNotifications` 是应用层特征名,与"框架级模块"的定位不一致。
2. **UserNotification 只是域内一个实体**。`Notification`(定义/已发布)、`UserNotification`(每用户已读态)、`NotificationSubscription`、`INotificationPublisher`、各 Notifier 并列,拿其一命名整体是以偏概全。
3. **无状态转发模式根本不产生 UserNotification 记录**,`UserNotifications` 会把这个卖点描述错。
4. **"用户通知"已有更准的落点** `Dignite.Abp.NotificationCenter`(收件箱/订阅/REST API);框架核心 `Notifications` + 用户中心 `NotificationCenter` 的分层已经很干净。
5. **ABP 自己把 "User notification" 归为应用模块**;框架核心若也叫 UserNotifications 会与其未来应用模块撞名、显得像跟随,且 `Volo.Abp.UserNotifications` 一旦出现更易混。

> 唯一支持 UserNotifications 的点:ABP 已用 "Notification" 指过客户端 UI toast,想彻底避歧义。但那在 UI 层命名空间(`Volo.Abp.AspNetCore.Components` 一带),没占顶层 `Notifications`;加上 `Dignite.Abp.` 前缀与服务端语境,实际混淆很小,不值得为它牺牲上面四条。

### 4.1 契约层命名:`Abstractions` 而非 `Shared`

跨包共享的契约(`NotificationData` 基类、`RealTimeNotifyEto`、各接口)放在 **`Dignite.Abp.Notifications.Abstractions`**,不叫 `.Shared`:

1. 框架层惯例是 `X` + `X.Abstractions`(`Volo.Abp.BackgroundJobs.Abstractions`、`Volo.Abp.EventBus.Abstractions`、`Volo.Abp.Caching.Abstractions`),与本项目"对齐 `Volo.Abp.*` 框架命名"的定位一致。
2. `.Shared` 在 ABP 里特指 `Domain.Shared`(应用模块层的常量/枚举/本地化/错误码),语义不符;这里装的是被 Core / Notifier / HttpApi.Client 共同依赖的**抽象契约**。
3. Notifier "只依赖抽象",包名叫 `.Abstractions` 让依赖倒置自解释。

> 注意:可选的 NotificationCenter 应用模块仍按标准 ABP 分层用 `...NotificationCenter.Domain.Shared` 等——`.Shared` 依旧合理地出现在其应用层,与框架契约层各司其职。

## 5. 推广表达(建议话术)

> Dignite Abp Notifications 是一套面向 ABP 的可扩展通知框架。它支持可插拔 Notifier,并提供可选的持久化 Notification Center、用户订阅、实时推送、headless REST API、EF Core 与 MongoDB。

可突出:核心与实现解耦、无状态/持久化两种模式、Notifier 可扩展、分布式事件、多租户、双持久化、headless REST API、契约驱动的可扩展通知内容(前端不限技术栈)、开源。
