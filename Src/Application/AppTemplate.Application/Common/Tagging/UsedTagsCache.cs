using AppTemplate.Domain.Core.Common.Primitives;
namespace AppTemplate.Application.Common.Tagging;

/// <summary>
/// Where one owner's used-tag list is kept, and for how long. Shared by every feature that owns
/// tags, so the lifetime is one decision rather than one per feature.
/// </summary>
/// <remarks>
/// <para>
/// This list is a suggestion. It answers "which tags has this owner used before", which a client
/// offers in a picker or a filter, so a list a minute out of date costs a tag typed in full. It
/// decides no authorisation, enforces no bound, becomes no <c>ETag</c> and tells nothing what to
/// delete — the reasons <see cref="Core.Common.Ports.ICacheStore"/> gives for what may not be
/// cached.
/// </para>
/// <para>
/// The entry is dropped by whatever changes an owner's tags, so the lifetime is the ceiling on how
/// long a change made anywhere else — a sweep, another deployment's process — can stay unseen.
/// </para>
/// </remarks>
public static class UsedTagsCache
{
    /// <summary>
    /// How long a list may be handed back without being recomputed. Short, because the entry is
    /// also the answer a client sees immediately after tagging something in another tab.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    /// <summary>The scope of the tags a to-do item carries.</summary>
    public const string TodoItemScope = "todo-items";

    /// <summary>The scope of the tags a stored file carries.</summary>
    public const string FileScope = "files";

    /// <summary>
    /// The key for one owner's list within one feature's <paramref name="scope"/>. The owner is in
    /// the key because the answer is theirs alone, and the scope because two features answer this
    /// question separately.
    /// </summary>
    public static string KeyFor(string scope, UserId ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(ownerId);

        return $"tags:{scope}:{ownerId}";
    }
}
