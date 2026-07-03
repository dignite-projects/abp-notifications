# 03 · 已发现的问题与实现路线

本文件是新实现的核心依据。下面"问题清单"来自对**现有实现**的通读——在新仓库里应从设计上规避,而不是照搬后再修。

> 参考实现路径(用于对照):
> - 核心:`dignite-abp/framework/src/Dignite.Abp.Notifications*`
> - 通知中心:`dignite-abp/modules/notification-center/src/*`
>
> 环境基线(现有):ABP **8.3.1** / .NET **8**;混用 System.Text.Json 与 Newtonsoft.Json 13。新仓库建议直接对齐 ABP **10.5.0** / .NET **10**(见 P3)。

---

## 一、问题清单

### A. 契约 / 序列化不一致 —— 最严重,列为 P0

同一个 `NotificationData` 存在三条不一致的处理路径:

1. **落库**(`NotificationCenter.Domain/Notification.cs`):用 **System.Text.Json** 序列化,类型名存 `data.GetType().AssemblyQualifiedName`。
2. **读库**(`NotificationCenter.Domain/NotificationStore.cs` → `GetUserNotificationsAsync`):用 **Newtonsoft.Json** 反序列化,`Type.GetType(DataTypeName)` 还原类型。
3. **远程客户端**(`NotificationCenter.HttpApi.Client/NotificationDataConverter.cs`,任意 .NET 消费端的 REST 代理):用 System.Text.Json,且**硬编码 switch 只认两个内置类型**,其余一律 `throw new JsonException("Unknown notification data type")`(代码里留着 `// TODO Other types...`)。

叠加出三个后果:

- **(a) 写用 STJ、读用 Newtonsoft**:同一份 JSON 两套引擎,大小写策略、转换器、多态、字典值类型还原都不保证一致。
- **(b) `AssemblyQualifiedName` + `Type.GetType()` 是定时炸弹**:AQN 含 `Version=x.x.x.x`,程序集版本一变,老数据 `Type.GetType()` 解析为 null → 反序列化拿不到正确类型。**升一次版本,历史通知可能读不出来。**
- **(c) 自定义 NotificationData 在远程客户端收不到**:converter 的硬编码 switch 使业务模块自定义子类在**任意 HttpApi.Client 消费端**(其它服务 / 前端 BFF / 使用方自建客户端)抛异常。

同根教训(渲染层):现有实现 `NotificationDataComponentSelector.cs` 用 `NotificationDataType.FullName` 匹配组件——同样把 CLR 全名当契约。新实现走 headless、不带 Blazor 组件,但同一条铁律照旧:**判别名必须是稳定短名,不能是 CLR 全名**,这样 JSON 契约里的类型信息才能被任意前端可靠使用。

> 结论:这不是"未来的兼容性增强",是**当前就存在的功能缺陷 + 潜在数据不可读风险**。新实现必须从第一天就用稳定契约。

### B. DI 生命周期:单例捕获请求级服务

- `Dignite.Abp.Notifications/UserNotificationManager.cs` 标 `ISingletonDependency`,却注入 `INotificationStore`(装 Center 后内部持有仓储/DbContext)。其兄弟 `NotificationSubscriptionManager` 是 `ITransientDependency`——同类服务生命周期不一致,基本是笔误。
- `Notifications.Identity/IdentityNotificationDefinitionManager.cs` 标 `ISingletonDependency`,注入 `IIdentityUserRepository`、`IAuthorizationService` 等请求级服务。

用 Autofac 默认不开 scope validation,启动不报错,但单例把 scoped 服务/DbContext 捕获成进程级实例,并发下会踩 DbContext 非线程安全、或 UoW 解析不到当前请求事务——低负载测不出,上量才炸。

> Definition 缓存本身适合单例(基类用 `Lazy`),但权限检查是请求级的。正确做法:**注册表保持单例,权限校验按需从 `IServiceProvider`/scoped 子服务解析**;`UserNotificationManager` 直接降 transient。

### C. 可靠性:先写库再发事件,无 Outbox / 幂等

`Dignite.Abp.Notifications/DefaultNotificationDistributer.cs` → `DistributeAsync`:先 `SaveUserNotificationsAsync`(写库)再 `NotifyAsync`(发 `RealTimeNotifyEto`)。两步无事务保证——进程在中间崩,就"库里有、没推送"或反之。全仓 grep 无 outbox / inbox / idempotent。

### D. 数据库索引 / 查询与真实访问模式不匹配

见 `NotificationCenter.EntityFrameworkCore/.../NotificationCenterDbContextModelCreatingExtensions.cs`:

- `NotificationSubscription` 只建 `(CreationTime, UserId)` 索引,但高频查询是 `FindAsync(userId, notificationName, entityTypeName, entityId)` 与按 `notificationName` 列表查(每次订阅、每次分发都走)——**该索引支持不了按 name 查找,退化为扫描**。
- `UserNotification` 主键已是 `(UserId, NotificationId)`,又单独建 `UserId` 索引——**与主键最左前缀重复、冗余**;而真正的收件箱查询(UserId + State + 时间排序)反而无匹配索引。
- 收件箱读取(`NotificationStore.GetUserNotificationsAsync`)是"先查 UserNotification、再按 id 批量查 Notification、内存 `.Single()` join"——大收件箱下两趟查询 + 内存拼接;且 Notification 被删而 UserNotification 尚存时 `.Single()` 会抛。

### E. 测试盲区:现有测试给了虚假安全感

- `framework/test/.../NotificationData_Serialization_Tests.cs`:两端都用 System.Text.Json,且反序列化**直接指定目标类型**,完全绕开生产里"靠 Type/AQN 还原 + Newtonsoft"的真实路径——最易坏处恰恰没测。
- `framework/test/.../NotificationDistributer_Tests.cs`:用 `FakeNotificationDistributer`,只断言 `IsDistributeCalled == true`;真正的分发逻辑(订阅解析、排除用户、阈值分支、ETO 发布)一行没测。

### F. 其它较小但真实的点

- **`RealTimeNotifyEto` 泄露其他收件人**:ETO 带完整 `Guid[] UserIds`,`SignalRNotifier/NotifyEventHandler.cs` 把整个 ETO 原样推给每个用户 → 用户 A 能在 payload 里看到 B、C 的 UserId。应按用户裁剪。
- **≤5 直发阈值硬编码**:`NotificationPublisher.MaxUserCountToDirectlyDistributeANotification = 5` 是常量,未进 `NotificationOptions`,不可配。
- **实时推送 DisplayName 多语言错位**:`DefaultNotificationDistributer.NotifyAsync` 在分发时用当时 culture 把 DisplayName 定死塞进 ETO;>5 人时跑在后台任务、无请求 culture,基本恒为默认语言(而收件箱读取路径是按读者 culture 重新本地化的,两条路径不一致)。
- **死代码**:`NullNotificationStore` 有个不在接口里的 `GetUserRoles`,清掉。

---

## 二、实现路线(按优先级)

优先级排序的逻辑:**契约/数据路径是所有 Notifier 和所有远程场景的承重墙,必须先修好,它同时解决现有 bug 并解锁 Notifier 生态。** 然后可靠性,再 Notifier 生态,版本与文档并行。

### P0 —— 契约与数据路径收敛(承重墙)

目标:一份 NotificationData,从发布 → 存储 → 事件 → REST API → 远程 .NET 客户端反序列化,全程一致、跨版本稳定、支持自定义类型。

- **统一序列化器**:全程 System.Text.Json,移除 Newtonsoft 混用。
- **稳定类型判别名**:用自定义 discriminator(如 `"Dignite.Message"` 这类稳定短名 + 类型注册表)取代 CLR `FullName` / `AssemblyQualifiedName`。**禁止**用 `Type.GetType(AssemblyQualifiedName)`。
- **类型注册表贯通**:落库读写、HttpApi.Client converter 全部改为**走同一个注册表按 discriminator 解析**,不再有硬编码 switch、不再用 FullName 匹配。
- **`SchemaVersion`**:NotificationData 带版本号,预留跨版本迁移钩子。
- **验收**:定义一个自定义 NotificationData 子类,能完成"发布 → 持久化 → 通过 SignalR / HttpApi.Client 在**远程 .NET 客户端**正确反序列化";判别名在 JSON 契约里稳定(前端 JS/TS 亦可据此渲染);并有一条"跨程序集版本号后仍能读出历史数据"的测试。

### P1 —— 可靠性底座

- **接 ABP Outbox**:让"写存储 + 发布 RealTimeNotifyEto"原子化,拿到至少一次投递。
- **Notifier 幂等**:引入幂等键(`NotificationId` + Notifier 名),消费端去重(尤其 Email/SMS,重复投递是真金白银)。
- **验收**:模拟"发布后进程崩溃重启"仍能补投;模拟事件重复投递不产生重复的用户可见通知/邮件。

### P2 —— Notifier 生态与路由

- **显式 `INotificationNotifier` 契约**(Name + 能力描述 + `NotifyAsync`),在分布式事件处理之上提供:枚举已装 Notifier、健康检查、按名路由、后台配置。
- **第二个 Notifier**(Email 或 Web Push):用它反证 `RealTimeNotifyEto` 是否够通用——Email 需要用户邮箱,会暴露"缺 UserId→Endpoint 映射"的问题。
- **UserId → Endpoint Registry**:SignalR 直接用 UserId;Web Push/FCM/APNs 需要"用户→设备/端点"映射。设计为**可选共享基础设施**,不提升为新核心。
- **通道路由**:用 `NotificationDefinition.Attributes`(已有)承载"某条通知走哪些通道",增量扩展,不新增核心层。
- **验收**:同一条通知,可配置为只走指定通道;新增 Notifier 无需改核心。

### P3 —— 版本基线(新仓库的红利)

新仓库不必背迁移包袱,**直接对齐 ABP 10.5.0 / .NET 10**(现有实现停在 ABP 8.3.1 / .NET 8,落后约两个大版本)。抽象层(Abstractions / Domain.Shared / HttpApi.Client)保留 `netstandard` 多目标以兼容消费端。

### P4 —— 测试与文档

- **补齐核心测试**:`DefaultNotificationDistributer` 的真实逻辑(订阅解析、排除、阈值分支、ETO 内容);**真实序列化往返**(带 discriminator + 版本,而非绕开)。
- **样板文档**(见 `02-architecture.md` 扩展点):
  - *Creating a Custom Notifier* —— 最小示例。
  - *Custom NotificationData across services / 远程客户端* —— 证明 P0 契约名副其实。
  > 这两篇只有 P0/P2 做完后才写得出真东西,不要提前写。

---

## 三、快速对照表(问题 → 优先级)

| 问题 | 优先级 |
|---|---|
| A 契约/序列化不一致(含远程客户端自定义类型不可用) | **P0** |
| F 阈值可配 / ETO 用户裁剪 / DisplayName 本地化 | P0~P1(随手做) |
| C 可靠性(Outbox + 幂等) | **P1** |
| B DI 生命周期(单例捕获 scoped) | P1(实现时直接做对) |
| D 索引/查询(索引对齐 + DB join) | P1~P2 |
| P2 Notifier 生态 / 路由 / Endpoint Registry | **P2** |
| 版本对齐 ABP 10.5.0 / .NET 10 | **P3** |
| E 测试盲区 + 样板文档 | **P4** |
