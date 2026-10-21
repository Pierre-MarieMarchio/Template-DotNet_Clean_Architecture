# Email

How this project sends mail, and how it picks the language a mail is written in.

## Sending

Every mail goes through one port, `IEmailSender`
(`Src/Application/AppTemplate.Application.Core/Common/Ports/IEmailSender.cs`), and one adapter
chosen by `Email:Transport` at start-up:

| `Email:Transport` | Adapter | Settings it reads |
|---|---|---|
| `Smtp` (default) | MailKit over SMTP | `Email:Host`, `Email:Port`, `Email:Security`, credentials |
| `Postmark` | Postmark's HTTP API | `Postmark:ServerToken`, `Postmark:MessageStream` |

Pick `Postmark` where the host blocks outbound SMTP, as most do. `Email:FromAddress` and
`Email:FromName` apply to both. An unknown transport stops start-up. SMTP refuses, against any
host other than loopback, a mode that could send in clear, unless `Email:AllowInsecureTransport`
says so explicitly.

There is one transport and no fallback, and a failed send is not retried: sending is a `POST`, and
the outbound policy retries safe verbs only.
<!--#if (localAuth)-->
A caller who missed an account mail asks for it again; registration answers the same whether or
not the mail left.
<!--#endif-->

## Templates and languages

A mail is one HTML template per language, `<Mail>EmailTemplate.<tag>.html`, embedded in the
module that sends it. Its subject is the template's `<title>`, so a subject and its body are
always in the same language. English and French ship, and English is required for every mail.

| The language comes from | |
|---|---|
| A request | its `Accept-Language`: the well-formed tag with the highest `q` |
| A request naming none | `Localization:DefaultCulture` |
| A background job | `Localization:DefaultCulture`, always |
| A language with no template | its parent (`fr-CA` reaches `fr`), then English |

The language is a BCP-47 tag in `CurrentLanguage`, not a `CultureInfo`: this project builds with
invariant globalization, so it chooses the text and nothing else.

Adding a language is adding one template per mail, in the folders below; there is no list to
update. An embedded `*.fr.html` needs `WithCulture="false"` in its `.csproj`, already set for these
folders, or MSBuild compiles it into a satellite assembly.
<!--#if (localAuth)-->
`EmailTemplateCoverageTests` refuses a language that one mail has and another lacks, and two
languages of one mail sharing a subject.
<!--#endif-->

## The mails

<!--#if (localAuth)-->
| Mail | Sent when | Template folder |
|---|---|---|
| `Register` | a new or unconfirmed address registers | `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Features/Accounts/Resources/Templates/` |
| `AlreadyRegistered` | a confirmed address registers again, or is named by an address change | same |
| `PasswordReset` | `forgot-password` names an account | same |
| `EmailChange` | `change-email` names a free address | same |
<!--#endif-->
<!--#if (reminders)-->

| Mail | Sent when | Template folder |
|---|---|---|
| `Reminder` | a reminder falls due, from the reminder job | `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/` |
<!--#endif-->
<!--#if (externalAuth)-->

The identity provider writes its own account mails.
<!--#endif-->
<!--#if (!localAuth && !reminders)-->
No mail ships with this project yet: a feature that sends one embeds its templates and calls
`IEmailSender`.
<!--#endif-->

## Mailpit locally

Compose runs Mailpit as `mailpit`: every mail the API sends lands at <http://localhost:8025>,
none leaves the machine. The API reaches it at `mailpit:1025` with `Email:Security` = `None` and
`Email:AllowInsecureTransport` = `true`; on the host, `appsettings.Development.json` points at
`localhost:1025`, where loopback needs no opt-in.

## Configure

| Section | What it sets |
|---|---|
| [`Email`](../reference/configuration.md#email) | the transport, the SMTP server, the sender |
| [`Postmark`](../reference/configuration.md#postmark) | the Postmark token, base URL and stream |
| [`Localization`](../reference/configuration.md#localization) | the default language |

## Decisions

- [0001 Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): SMTP or Postmark, Mailpit locally, invariant globalization.
