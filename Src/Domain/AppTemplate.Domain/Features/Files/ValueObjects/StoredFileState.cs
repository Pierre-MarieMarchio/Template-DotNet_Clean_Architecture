namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// Where a stored file is in its one-way life. The states exist because half of this aggregate is a
/// row in a database and the other half is a blob in an object store, and nothing makes those two
/// writes one transaction: the first three below each name a moment when the two are known to
/// disagree about something, and the fourth names the one file that will never agree.
/// <para>
/// <b>No member here may mean "deleted".</b> A deleted file is a removed row — see
/// <c>CONTRIBUTING.md</c> — and a state meaning "gone" would put a predicate in every query, where
/// the one that forgets it serves a file that was meant to be unreachable. <see cref="Quarantined"/>
/// costs no such predicate: the only thing in this feature that hands out a file's bytes asks for
/// <see cref="Available"/> by name, so a state added here is refused by default rather than served
/// by default.
/// </para>
/// <para>
/// <b>The numbers are the column and the order is the life, and they disagree on purpose.</b> The
/// value is persisted as an integer, so <see cref="Pending"/> and <see cref="Available"/> keep the
/// numbers they were written to existing rows with; the two members added afterwards take the next
/// free ones. Reordering the members is free, renumbering them silently reinterprets every row in
/// the table.
/// </para>
/// </summary>
public enum StoredFileState
{
    /// <summary>
    /// Registered, with an object key reserved and nothing written to it yet. A client that never
    /// completes its deposit leaves the file here for ever, which is why
    /// <c>StoredFile.RegisteredAt</c> exists: it is what lets a sweep say "this one has been waiting
    /// too long" and remove it.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The bytes are present and are the ones that were declared — the size and the digest the store
    /// reports match what was promised — and nothing has yet read them.
    /// <para>
    /// <b>Not servable, and this is the state that pays for that guarantee.</b> Confirming a deposit
    /// says the bytes arrived intact; it says nothing about what they are. A file waits here until
    /// something has looked at its content and reached a verdict, and until then no download grant
    /// can be minted for it.
    /// </para>
    /// <para>
    /// It is also the state the abandonment sweep must not touch, and does not: <c>IsAbandoned</c>
    /// reads <see cref="Pending"/> alone. A deposit that arrived is not a registration that was
    /// given up on, however long it then waits for a verdict — an inspection backlog or an
    /// unreachable scanner must never turn into deleted uploads.
    /// </para>
    /// </summary>
    Deposited = 2,

    /// <summary>
    /// The bytes are present, were confirmed to be the ones that were declared, and have been
    /// examined. The only state in which this file may be served.
    /// </summary>
    Available = 1,

    /// <summary>
    /// The content was examined and refused. Terminal: nothing moves a file out of here, because the
    /// verdict is about bytes that can no longer change — the object key is minted once, the digest
    /// was checked on the way in, and a client wanting a different answer uploads a different file.
    /// <para>
    /// The row survives so that the owner is told their upload was refused rather than left to
    /// wonder why a file they can see never becomes readable. <b>Why it was refused is deliberately
    /// not recorded here</b>: the detail belongs in the operator's log, where it can name a malware
    /// signature without ever reaching the person who uploaded it.
    /// </para>
    /// </summary>
    Quarantined = 3,
}
