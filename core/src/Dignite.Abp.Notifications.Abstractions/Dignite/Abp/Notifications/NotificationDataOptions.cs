using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Registers the <see cref="NotificationData"/> types known to the application, keyed by their stable
/// discriminator. Business modules add their own types here so those types can be resolved on any client.
/// Discriminators use ordinal, case-sensitive comparison. Repeating the exact discriminator/type pair is
/// idempotent; every other discriminator or CLR type conflict is rejected during application startup.
/// </summary>
public class NotificationDataOptions
{
    private readonly Dictionary<string, SortedDictionary<int, NotificationDataUpcaster>> _upcasters =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the registered discriminator-to-type mappings. Keys use ordinal, case-sensitive comparison.
    /// Use <see cref="Add(string, Type)"/> to preserve conflict validation.
    /// </summary>
    public IDictionary<string, Type> DataTypes { get; }

    public NotificationDataOptions()
    {
        DataTypes = new NotificationDataTypeDictionary();
    }

    public NotificationDataOptions Add<TData>() where TData : NotificationData
    {
        return Add(typeof(TData));
    }

    public NotificationDataOptions Add(Type dataType)
    {
        Check.NotNull(dataType, nameof(dataType));

        var name = NotificationDataTypeAttribute.GetNameOrNull(dataType)
            ?? throw new ArgumentException(
                $"'{dataType.FullName}' must be annotated with [NotificationDataType(\"...\")] to be registered.",
                nameof(dataType));

        return Add(name, dataType);
    }

    public NotificationDataOptions Add(string discriminator, Type dataType)
    {
        DataTypes.Add(discriminator, dataType);
        return this;
    }

    /// <summary>
    /// Registers one deterministic N→N+1 step for the discriminator declared by <typeparamref name="TData"/>.
    /// Every step from legacy v1 to the type's current declared version must exist at startup.
    /// </summary>
    public NotificationDataOptions AddUpcaster<TData>(
        int fromVersion,
        NotificationDataUpcaster upcaster)
        where TData : NotificationData
    {
        var dataType = typeof(TData);
        var discriminator = NotificationDataTypeAttribute.GetNameOrNull(dataType)
            ?? throw new ArgumentException(
                $"'{dataType.FullName}' must be annotated with [NotificationDataType(\"...\")] " +
                "before an upcaster can be registered.",
                nameof(TData));

        return AddUpcaster(discriminator, fromVersion, upcaster);
    }

    /// <summary>Registers one deterministic N→N+1 step for a stable discriminator.</summary>
    public NotificationDataOptions AddUpcaster(
        string discriminator,
        int fromVersion,
        NotificationDataUpcaster upcaster)
    {
        Check.NotNullOrWhiteSpace(discriminator, nameof(discriminator));
        Check.NotNull(upcaster, nameof(upcaster));
        if (fromVersion < NotificationDataSchema.LegacyVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fromVersion),
                fromVersion,
                $"Upcast source versions start at {NotificationDataSchema.LegacyVersion}.");
        }

        if (!_upcasters.TryGetValue(discriminator, out var byVersion))
        {
            byVersion = new SortedDictionary<int, NotificationDataUpcaster>();
            _upcasters.Add(discriminator, byVersion);
        }

        if (byVersion.ContainsKey(fromVersion))
        {
            throw new InvalidOperationException(
                $"Duplicate notification data upcaster registration for discriminator '{discriminator}' " +
                $"from schema v{fromVersion} to v{fromVersion + 1}.");
        }

        byVersion.Add(fromVersion, upcaster);
        return this;
    }

    internal IReadOnlyDictionary<string, SortedDictionary<int, NotificationDataUpcaster>> Upcasters => _upcasters;

    internal void ValidateEvolution()
    {
        foreach (var upcasterGroup in _upcasters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!DataTypes.TryGetValue(upcasterGroup.Key, out var dataType))
            {
                throw new InvalidOperationException(
                    $"Notification data upcasters are registered for discriminator '{upcasterGroup.Key}', " +
                    "but no payload type is registered for that discriminator.");
            }

            var currentVersion = NotificationDataTypeAttribute.GetSchemaVersionOrDefault(dataType);
            foreach (var fromVersion in upcasterGroup.Value.Keys)
            {
                if (fromVersion >= currentVersion)
                {
                    throw new InvalidOperationException(
                        $"Notification data upcaster '{upcasterGroup.Key}' v{fromVersion}→v{fromVersion + 1} " +
                        $"does not lead toward the registered current schema v{currentVersion}.");
                }
            }
        }

        foreach (var registration in DataTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var currentVersion = NotificationDataTypeAttribute.GetSchemaVersionOrDefault(registration.Value);
            if (currentVersion == NotificationDataSchema.LegacyVersion)
            {
                continue;
            }

            _upcasters.TryGetValue(registration.Key, out var byVersion);
            for (var version = NotificationDataSchema.LegacyVersion; version < currentVersion; version++)
            {
                if (byVersion == null || !byVersion.ContainsKey(version))
                {
                    throw new InvalidOperationException(
                        $"Notification data discriminator '{registration.Key}' declares current schema " +
                        $"v{currentVersion}, but its deterministic upcast chain is missing v{version}→v{version + 1}.");
                }
            }
        }
    }

    private sealed class NotificationDataTypeDictionary : IDictionary<string, Type>
    {
        private readonly Dictionary<string, Type> _items = new(StringComparer.Ordinal);

        public Type this[string key]
        {
            get => _items[key];
            set => Add(key, value);
        }

        public ICollection<string> Keys => _items.Keys;

        public ICollection<Type> Values => _items.Values;

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(string discriminator, Type dataType)
        {
            Check.NotNullOrWhiteSpace(discriminator, nameof(discriminator));
            Check.AssignableTo<NotificationData>(dataType, nameof(dataType));

            if (_items.TryGetValue(discriminator, out var registeredType))
            {
                if (registeredType == dataType)
                {
                    return;
                }

                var typeNames = new[] { GetTypeName(registeredType), GetTypeName(dataType) }
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Notification data discriminator '{discriminator}' is registered for conflicting CLR types " +
                    $"'{typeNames[0]}' and '{typeNames[1]}'. Discriminators use ordinal, case-sensitive comparison.");
            }

            var registeredDiscriminator = _items
                .Where(pair => pair.Value == dataType)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (registeredDiscriminator != null)
            {
                var discriminators = new[] { registeredDiscriminator, discriminator }
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                throw new InvalidOperationException(
                    $"Notification data CLR type '{GetTypeName(dataType)}' is registered with conflicting discriminators " +
                    $"'{discriminators[0]}' and '{discriminators[1]}'. Discriminators use ordinal, case-sensitive comparison.");
            }

            _items.Add(discriminator, dataType);
        }

        public void Add(KeyValuePair<string, Type> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _items.Clear();
        }

        public bool Contains(KeyValuePair<string, Type> item)
        {
            return ((ICollection<KeyValuePair<string, Type>>)_items).Contains(item);
        }

        public bool ContainsKey(string key)
        {
            return _items.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, Type>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, Type>>)_items).CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, Type>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        public bool Remove(string key)
        {
            return _items.Remove(key);
        }

        public bool Remove(KeyValuePair<string, Type> item)
        {
            return ((ICollection<KeyValuePair<string, Type>>)_items).Remove(item);
        }

        public bool TryGetValue(string key, out Type value)
        {
            return _items.TryGetValue(key, out value!);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static string GetTypeName(Type type)
        {
            var assemblyName = type.Assembly.GetName().Name ?? "<unknown assembly>";
            return $"{type.FullName ?? type.Name}, {assemblyName}";
        }
    }
}
