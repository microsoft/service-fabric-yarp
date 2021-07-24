// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IslandGateway.FabricDiscovery.Util
{
    internal static class HostToWebHostAdapterExtensions
    {
        // TODO: SF-YARP davidni: remove
        public static IHostBuilder AdaptWebHostBuilder(this IHostBuilder hostBuilder, Action<IWebHostBuilder> configure)
        {
            _ = hostBuilder ?? throw new ArgumentNullException(nameof(hostBuilder));
            _ = configure ?? throw new ArgumentNullException(nameof(configure));
            var webHostBuilder = new WebHostBuilderAdapter(hostBuilder);
            configure(webHostBuilder);
            return hostBuilder;
        }

        private class WebHostBuilderAdapter : IWebHostBuilder
        {
            private readonly IHostBuilder builder;
            private readonly IConfiguration config;

            public WebHostBuilderAdapter(IHostBuilder builder)
            {
                this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
                this.config = new ConfigurationBuilder()
                    .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                    .Build();
            }

            public IWebHost Build()
            {
                throw new NotImplementedException();
            }

            public IWebHostBuilder ConfigureAppConfiguration(Action<WebHostBuilderContext, IConfigurationBuilder> configureDelegate)
            {
                this.builder.ConfigureAppConfiguration((context, configurationBuilder) =>
                {
                    configureDelegate(CreateWebHostContext(context), configurationBuilder);
                });
                return this;
            }

            public IWebHostBuilder ConfigureServices(Action<WebHostBuilderContext, IServiceCollection> configureServices)
            {
                this.builder.ConfigureServices((context, services) =>
                {
                    configureServices(CreateWebHostContext(context), services);
                });
                return this;
            }

            public IWebHostBuilder ConfigureServices(Action<IServiceCollection> configureServices)
            {
                this.builder.ConfigureServices(configureServices);
                return this;
            }

            public string GetSetting(string key)
            {
                return this.config[key];
            }

            public IWebHostBuilder UseSetting(string key, string value)
            {
                this.config[key] = value;
                return this;
            }

            private static WebHostBuilderContext CreateWebHostContext(HostBuilderContext context)
            {
                // NOTE: This is not a complete implementation, but it suffices for current use cases,
                // and is only needed temporarily until this entire class can be removed.
                return new WebHostBuilderContext
                {
                    Configuration = context.Configuration,
                };
            }
        }
    }
}
