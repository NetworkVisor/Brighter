﻿using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Core.Tests.Logging.TestDoubles;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.Logging.Handlers;
using Xunit;
using Polly.Registry;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using Xunit.Abstractions;

namespace Paramore.Brighter.Core.Tests.Logging
{
    public class CommandProcessorWithLoggingInPipelineAsyncTests : IDisposable
    {

        private readonly ITestOutputHelper _output;

        public CommandProcessorWithLoggingInPipelineAsyncTests(ITestOutputHelper output)
        {
            _output = output;
        }

        //TODO: Because we use a global logger with Serilog, this won't run in parallel
        //[Fact]
        public async Task When_A_Request_Logger_Is_In_The_Pipeline_Async()
        {
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.TestCorrelator().CreateLogger();
            using var context = TestCorrelator.CreateContext();
            var myCommand = new MyCommand();

            var registry = new SubscriberRegistry();
            registry.RegisterAsync<MyCommand, MyLoggedHandlerAsync>();

            var container = new ServiceCollection();
            container.AddTransient<MyLoggedHandlerAsync, MyLoggedHandlerAsync>();
            container.AddTransient(typeof(RequestLoggingHandlerAsync<>), typeof(RequestLoggingHandlerAsync<>));

            var handlerFactory = new ServiceProviderHandlerFactory(container.BuildServiceProvider());

            var commandProcessor = new CommandProcessor(registry, handlerFactory,
                new InMemoryRequestContextFactory(), new PolicyRegistry(), new InMemorySchedulerFactory());

            await commandProcessor.SendAsync(myCommand);

            TestCorrelator.GetLogEventsFromContextId(context.Id)
                .Should().Contain(x => x.MessageTemplate.Text.StartsWith("Logging handler pipeline call"))
                .Which.Properties["1"].ToString().Should().Be($"\"{typeof(MyCommand)}\"");
        }

        public void Dispose()
        {
            CommandProcessor.ClearServiceBus();
        }
    }
}
