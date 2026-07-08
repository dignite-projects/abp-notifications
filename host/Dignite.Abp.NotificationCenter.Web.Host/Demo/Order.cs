namespace Demo;

/// <summary>
/// Placeholder "entity" whose <see cref="System.Type.FullName"/> is exactly "Demo.Order" — so a published
/// notification's EntityTypeName matches the host's <c>EntityLinkResolvers["Demo.Order"]</c> key and the bell
/// item becomes a clickable link. A real app points this at its own aggregate type instead.
/// </summary>
public class Order
{
}
