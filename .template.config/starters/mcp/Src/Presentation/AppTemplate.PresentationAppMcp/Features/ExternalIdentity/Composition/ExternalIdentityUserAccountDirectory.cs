﻿using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;

internal sealed class ExternalIdentityUserAccountDirectory(IExternalIdentityService externalIdentities) : IUserAccountDirectory
{
    public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await externalIdentities.FindProfileAsync(userId, cancellationToken) is not null;
    }
}
