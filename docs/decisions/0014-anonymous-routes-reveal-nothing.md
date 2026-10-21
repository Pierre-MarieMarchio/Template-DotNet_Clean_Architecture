# 0014. Anonymous routes reveal nothing

Status: Accepted

## Context

Registration, sign-in, password reset and resending a confirmation are reached by callers with no
token. Each can be made helpful: "this address is taken", "no account with that address", "wrong
password". Each such answer also tells anyone who asks which addresses have accounts here, which is
the first step of credential stuffing and of targeted phishing.

## Decision

- **Registration always answers `202`**, with one body, for every well-formed request. The address
  learns by mail what happened: a confirmation link, a fresh one, or a notice that it already has an
  account. An email change naming a taken address follows the same rule.
- **Refusals of the submitted values stay `400`** and are judged before the address is looked up,
  so their order reveals nothing.
- **A refused sign-in answers one error** whatever the reason, down to the message. Reset and
  resend answer identically whether the address exists.
- **Security events never name an address.** Every event speaks in user ids, with none for an attempt
  against an address that names no account, so a log line cannot leak what the response hides.

## Consequences

- A client cannot say "that address is taken" inline; the mail says it, to the only person entitled
  to know.
- Support investigates by user id, not by the address a person types.
- `RegisterUseCaseTests` and `RegistrationFlowTests` hold registration; `LoginUniformityTests` and
  `LoginUseCaseTests` assert every refusal against a literal and against each other, so a change
  that moves them all and one that splits them both fail.
- Anonymous routes are rate-limited per client address as well, so the uniform answers are slow to
  probe.

[Registration and email confirmation](../features/accounts.md#registration-and-email-confirmation)
and [The security log](../features/accounts.md#the-security-log) describe the flows.

## Revisit when

The product makes addresses or user names public anyway, so hiding them protects nothing.
