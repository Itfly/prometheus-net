﻿using Microsoft.AspNetCore.Http;
using Prometheus.Advanced;
using Prometheus.Advanced.DataContracts;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Prometheus
{
    /// <summary>
    /// Prometheus metrics export middleware for ASP.NET Core.
    /// 
    /// You should use IApplicationBuilder.UsePrometheus* extension methods instead of using this class directly.
    /// </summary>
    public sealed class PrometheusMiddleware
    {
        public PrometheusMiddleware(RequestDelegate next, Settings settings)
        {
            _next = next;

            _registry = settings.GetRegistryAndRegisterOnDemandCollectors();
        }

        public sealed class Settings
        {
            public IEnumerable<IOnDemandCollector> OnDemandCollectors { get; set; }
            public ICollectorRegistry Registry { get; set; }

            internal ICollectorRegistry GetRegistryAndRegisterOnDemandCollectors()
            {
                // Copypaste from MetricHandler ctor - see there for rationale.

                var registry = Registry ?? DefaultCollectorRegistry.Instance;

                if (registry == DefaultCollectorRegistry.Instance)
                {
                    if (OnDemandCollectors != null)
                        DefaultCollectorRegistry.Instance.RegisterOnDemandCollectors(OnDemandCollectors);
                    else
                        DefaultCollectorRegistry.Instance.RegisterOnDemandCollectors(new[] { new DotNetStatsCollector() });
                }

                return registry;
            }
        }

        private readonly RequestDelegate _next;

        private readonly ICollectorRegistry _registry;

        public async Task Invoke(HttpContext context)
        {
            // We just handle the root URL (/metrics or whatnot).
            if (!string.IsNullOrWhiteSpace(context.Request.Path.Value))
            {
                await _next(context);
                return;
            }

            var request = context.Request;
            var response = context.Response;

            var acceptHeaders = request.Headers["Accept"];
            var contentType = ScrapeHandler.GetContentType(acceptHeaders);
            response.ContentType = contentType;

            IEnumerable<MetricFamily> metrics;

            try
            {
                metrics = _registry.CollectAll();
            }
            catch (ScrapeFailedException ex)
            {
                response.StatusCode = 503;

                if (!string.IsNullOrWhiteSpace(ex.Message))
                {
                    using (var writer = new StreamWriter(response.Body))
                        await writer.WriteAsync(ex.Message);
                }

                return;
            }

            response.StatusCode = 200;

            using (var outputStream = response.Body)
                ScrapeHandler.ProcessScrapeRequest(metrics, contentType, outputStream);
        }
    }
}
