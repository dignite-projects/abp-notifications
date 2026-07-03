# Dignite.Abp.Notifications

> 面向 ABP 的可扩展通知框架 + 可选的通知中心实现
> *An extensible notification framework and optional notification center for ABP.*

## 这是什么

一套为 ABP vNext 设计的服务端通知框架,源自 ASP.NET Boilerplate 的 Notification System 思路,重新模块化。核心特征:

- **事件驱动、可插拔 Notifier**:核心只发一个分布式事件 `RealTimeNotifyEto`,各 Notifier(SignalR / Email / WebPush / FCM …)订阅并中继到自己的通道。
- **两种使用级别**:无状态转发模式(只发不存)与完整通知中心模式(收件箱、订阅、已读未读、REST API)。
- **多适配**:EF Core 与 MongoDB、多租户、Identity 权限集成。
- **Headless**:Notification Center 只暴露 REST API + Client SDK,UI 交给使用方(JS/TS 前端或其自建 Blazor)。
- **开源**:LGPL-3.0。

## 命名结论

- 框架核心:**`Dignite.Abp.Notifications`**(能力名,和 `Volo.Abp.Emailing`/`Sms`/`BackgroundJobs` 一致的框架级命名)。
- 契约层:**`Dignite.Abp.Notifications.Abstractions`**(跨包共享的数据模型与分布式事件契约;Notifier 只依赖它)。
- 用户侧:**`Dignite.Abp.NotificationCenter`**(收件箱/订阅/REST API,headless)。
- `UserNotification` 只是核心域里的一个实体,不用它命名整个模块。

> 不叫 `UserNotifications` 的完整理由见 [01-strategy.md](./01-strategy.md#命名决策)。

## 战略结论(为什么现在值得做)

ABP 官方短期没有服务端通知模块的计划,且其可能的方向与本项目不冲突——**绿灯继续做**。依据与监控信号见 [01-strategy.md](./01-strategy.md)。

## 文档索引

| 文档 | 内容 |
|---|---|
| [01-strategy.md](./01-strategy.md) | 定位、差异化(vs EasyAbp)、与 ABP 官方的关系与判断+证据、命名决策、监控信号 |
| [02-architecture.md](./02-architecture.md) | 目标架构、分层、依赖方向、两种模式、核心流程、扩展点 |
| [03-roadmap.md](./03-roadmap.md) | 从现有实现发现的问题(A–F)+ 分优先级实现计划(P0–P4)+ 验收要点 |

## 来源

本 docs 目录基于:

1. 对现有实现的通读:`dignite-abp/framework/src/Dignite.Abp.Notifications*` 与 `dignite-abp/modules/notification-center`(即本仓库要重构/独立出来的代码)。
2. 2026-07 对 ABP 官方动向的核实(路线图、GitHub milestone、商业模块清单、官方支持问答)。
3. 一份先行的《产品与架构总结报告》(Codex 生成)作为背景,本目录在其基础上补充了代码级问题与更明确的优先级判断。

> 说明:`03-roadmap.md` 中引用的文件路径指向**现有实现**(参考实现),用于对照"哪里做错了、这次要怎么做对",不是本仓库的既有文件。
