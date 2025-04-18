using System;
using Microsoft.EntityFrameworkCore;

namespace CA.Presentation.WebApi.Commons.Services;
public static class DatabaseHelper
{
    public static async Task EnsureDatabaseReadyAsync(DbContext context, int retries = 10, int delayMs = 2000)
    {
        var dbName = context.GetType().Name;

        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (await context.Database.CanConnectAsync())
                {
                    return;
                }
            }
            catch
            {
                // Ignore les exceptions, la logique est de réessayer
            }

            await Task.Delay(delayMs);
        }

        throw new InvalidOperationException($"Impossible de se connecter à la base de données : {dbName}");
    }
}