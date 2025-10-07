using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Raven.Server.Documents;
using Raven.Server.Documents.Sharding;
using Raven.Server.Routing;
using Raven.Server.Utils;

namespace Raven.Server.Web
{
    public sealed class RequestHandlerContext : IDisposable
    {
        private static readonly List<IDisposable> DisposedSentinel = new();
        private List<IDisposable> _disposables;

        public HttpContext HttpContext;
        public RavenServer RavenServer;
        public RouteMatch RouteMatch;
        public DocumentDatabase Database;
        public bool CheckForChanges = true;
        public ShardedDatabaseContext DatabaseContext;

        public string DatabaseName => Database?.Name ?? DatabaseContext?.DatabaseName;
        public MetricCounters DatabaseMetrics => Database?.Metrics ?? DatabaseContext?.Metrics;
        public string ClusterTransactionId => Database?.ClusterTransactionId ?? DatabaseContext?.DatabaseRecord.GetClusterTransactionId();

        public IDisposable Register(IDisposable disposable)
        {
            if (disposable == null)
                return null;

            if (ReferenceEquals(_disposables, DisposedSentinel))
                throw new ObjectDisposedException(nameof(RequestHandlerContext));

            var disposables = _disposables;
            if (disposables == null)
            {
                disposables = new List<IDisposable>();
                _disposables = disposables;
            }

            disposables.Add(disposable);

            return disposable;
        }

        public void Dispose()
        {
            var disposables = _disposables;
            if (ReferenceEquals(disposables, DisposedSentinel))
                return;

            _disposables = DisposedSentinel;

            if (disposables == null)
                return;

            List<Exception> exceptions = null;
            for (var i = disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    disposables[i]?.Dispose();
                }
                catch (Exception e)
                {
                    exceptions ??= new List<Exception>();
                    exceptions.Add(e);
                }
            }

            disposables.Clear();

            if (exceptions != null)
                throw new AggregateException("Failed to dispose request handler context resources.", exceptions);
        }
    }
}
