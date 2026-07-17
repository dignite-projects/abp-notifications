using System.Text.Json.Nodes;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Deterministically transforms one payload JSON object from version N to N+1. The object contains payload members
/// only; the reserved <c>type</c> and <c>schemaVersion</c> envelope members are managed by the framework.
/// </summary>
public delegate JsonObject NotificationDataUpcaster(JsonObject payload);
