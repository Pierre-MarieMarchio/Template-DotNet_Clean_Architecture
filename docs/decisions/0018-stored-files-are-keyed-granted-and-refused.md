# 0018. Stored files are keyed, granted and refused

Status: Accepted

## Context

Files are bytes a caller chose, kept in an object store and fetched by browsers. Two questions
decide whether that is safe: where the bytes live and who may reach them, and what is let in at
all. The common answers, a key derived from the file's id, a sanitised name, a type taken from the
extension, each trust something the caller controls.

## Decision

**Where bytes live.**

- **A key is minted data.** `t0/<yyyyMM>/<128 random bits in hex>`, generated once and persisted,
  never a formula over the file's id, which is ordered and guessable. The month slice keeps the
  orphan sweep ordered; `t0` is a reserved partition.
- **The store binds an object to its digest and size.** An upload carries its declared SHA-256 and
  length, and the store keeps the whole object or nothing; the file-system store hashes as it writes
  and publishes only a match.
- **A grant is a narrow bearer right.** A presigned URL is signed for the host the client uses
  (`Files:Storage:S3:PublicEndpoint`), for exactly the declared bytes, with every header signed, and
  for no longer than `MaxGrantLifetime`.

**What is let through.**

- **Names, keys and media types are refused on the character, never sanitised.**
- **The type comes from the head's signatures.** Bytes naming another format than the declared one
  are quarantined, and markup is refused in every category, since nothing here sanitises it. A type
  with no signature, such as CSV or JSON, is released: that is the limit of what a head decides.
- **No verdict is never clean.** A scanner outage defers the file, and an unparseable answer is no
  verdict. Malware scanning is off by default; the signature check runs regardless.

## Consequences

- A refused file keeps its bytes for an investigation; deletion is the one path that reclaims
  them, and the orphan sweep can trust that a key no row names is garbage.
- A project that must accept XML or SVG changes the tests that refuse them, and owes a sanitiser.
- Turning scanning on is a deployment's duty.
- `ObjectKeyTests`, `StoredFileNameTests`, `StoredFileContentPolicyTests`, `ClamAvScannerTests`,
  `SignedGrantTests` and `PublicEndpointTests` hold the rules; `StoredFileMapperObjectKeyTests`
  keeps the key from being altered between row and aggregate.

[Files](../features/files.md) describes uploads, downloads, storage and the background passes.

## Revisit when

Files must be served through the API itself, a markup format must be accepted, or a tenant
partition replaces `t0`.
