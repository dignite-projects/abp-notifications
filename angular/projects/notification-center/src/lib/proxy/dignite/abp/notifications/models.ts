
export interface NotificationData {
  // The stable wire discriminator from the server's [NotificationDataType] (notifications-invariants.md §1).
  // It is a JSON-only discriminator, not a property on the C# NotificationData base class, so generate-proxy
  // can't see it — added by hand. Re-apply this block if you regenerate this file.
  type?: string;
  schemaVersion?: number;
  extensionData?: Record<string, any> | null;
  // Concrete subclasses serialize their own fields at the top level (e.g. imageUrl on IHasNotificationImageUrl);
  // the index signature exposes them structurally for duck-typed rendering, without a switch over discriminators.
  [key: string]: unknown;
}
