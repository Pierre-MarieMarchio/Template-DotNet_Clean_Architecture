# 0017. External subjects map to internal owners

Status: Accepted

## Context

With an external identity provider, a caller is known by the token's issuer and subject. Storing
that subject on every row a feature owns would spread the provider's identifier format through the
schema, and changing providers would rewrite every table. The API also needs roles, which some
providers put in their tokens and others do not manage at all.

## Decision

- **(issuer, subject) maps to an internal owner id.** The API validates the provider's token against
  `Authentication:External`, then resolves the pair to a `UserId` of its own, provisioned on the
  first authenticated request. Features store that id and never see the subject.
- **Mappings commit on their own.** A mapping is written while the request is being authenticated,
  in a transaction of its own. Two first requests racing on the unique index over issuer and subject
  both end with the winner's owner id.
- **Roles come from one configured source.** `Authentication:External:Roles:Source` is `Local` by
  default, role assignments held by this API and administered through it, or `Claim`, read from a
  path in the token such as `realm_access.roles`.
- **Removing a mapping is final.** A caller who signs in again is provisioned afresh, under a new
  owner id with none of the old one's data.
<!--#if (anyFeature)-->
  The removal publishes the account-deletion fact
  ([ADR 0012](0012-account-deletion-each-feature-decides.md)).
<!--#endif-->

## Consequences

- Changing providers means pointing the mappings' rows at the same owner ids; no feature table
  changes.
- The mapping is the one place that knows a caller's subject; no feature's rows hold it.
- `ExternalIdentityProvisioningTests`, `ExternalPrincipalMapperTests` and `TokenRoleMappingTests`
  hold provisioning and both role sources.

[An external identity provider](../features/authentication.md#an-external-identity-provider)
describes the settings and the administration routes.

## Revisit when

Several providers must resolve to one person, which needs account linking, or the provider's
subject is guaranteed stable and shared with other systems that need to join on it.
