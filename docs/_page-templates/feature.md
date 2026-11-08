---
type: template
title: <Feature>
description: <description>
---

# <Feature>

## What you get

What a caller can do with it, in two or three sentences, and who may: <access>.

## How it works

The rules it protects and the path of a request through the layers: <shape>.

## Use it

<!--#if (api)-->
Each endpoint with an example request, the codes it returns and the errors a client handles:
<!--#endif-->
<!--#if (!api)-->
Each operation, the host that offers it and the errors a caller handles:
<!--#endif-->

<endpoints>

## Configure

Each setting it reads, as a row in the configuration reference, or "Nothing to configure."

## Tests

What the tests prove beyond the shape, and where they live: <tests>.

## Decisions

The ADRs this feature rests on, linked here; a new one, numbered from 1000, only as a last resort, as
[when to write one](../decisions/README.md#when-to-write-one) says.
