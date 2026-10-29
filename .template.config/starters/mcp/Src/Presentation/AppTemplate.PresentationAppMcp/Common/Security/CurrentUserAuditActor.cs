﻿using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;

namespace AppTemplate.PresentationApp.Common.Security;

internal sealed class CurrentUserAuditActor(ICurrentUser currentUser) : IAuditActor
{
    public Guid? UserId => currentUser.UserId?.Value;
}
