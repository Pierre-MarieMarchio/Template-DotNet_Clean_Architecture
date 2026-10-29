using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Worker.Common.Security;

namespace AppTemplate.Worker.UnitTests.TestSupport;

internal sealed class WorkerHost : IDisposable
{
    private static readonly Lock _building = new();

    private static readonly AsyncLocal<bool> _capturing = new();

    private WorkerHost(IHost host, IReadOnlyList<Type> useCases)
    {
        Host = host;
        UseCases = useCases;
    }

    internal IHost Host { get; }

    internal IServiceProvider Services => Host.Services;

    internal IReadOnlyList<Type> UseCases { get; }

    internal static IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            //#if (files)
            ["Files:Storage:S3:BucketName"] = "worker-tests",
            //#endif
        };

    internal static WorkerHost Build(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var arguments = new List<string>
        {
            "--environment", Environments.Development,
            "--contentRoot", AppContext.BaseDirectory,
        };

        foreach ((string key, string? value) in Settings.Concat(overrides ?? new Dictionary<string, string?>()))
        {
            arguments.Add($"--{key}={value}");
        }

        lock (_building)
        {
            using var observer = new BuiltHostObserver();
            using var subscription = DiagnosticListener.AllListeners.Subscribe(observer);

            try
            {
                _capturing.Value = true;
                typeof(WorkerCallerIdentity).Assembly.EntryPoint!.Invoke(null, [arguments.ToArray()]);
            }
            catch (TargetInvocationException stopped) when (stopped.InnerException is HostCaptured)
            {
                return new WorkerHost(observer.Host!, observer.UseCases);
            }
            finally
            {
                _capturing.Value = false;
            }

            throw new InvalidOperationException("The worker's Program.cs returned without building a host.");
        }
    }

    public void Dispose() => Host.Dispose();

    private sealed class HostCaptured : Exception;

    private sealed class BuiltHostObserver
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];

        internal IHost? Host { get; private set; }

        internal List<Type> UseCases { get; } = [];

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.Extensions.Hosting")
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!_capturing.Value)
            {
                return;
            }

            if (value is { Key: "HostBuilding", Value: IHostBuilder builder })
            {
                builder.ConfigureServices(services => UseCases.AddRange(services
                    .Select(descriptor => descriptor.ServiceType)
                    .Where(type => typeof(IUseCase).IsAssignableFrom(type))
                    .Distinct()));
            }

            if (value is { Key: "HostBuilt", Value: IHost host })
            {
                Host = host;

                throw new HostCaptured();
            }
        }

        public void OnCompleted() => Dispose();

        public void OnError(Exception error) => Dispose();

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
