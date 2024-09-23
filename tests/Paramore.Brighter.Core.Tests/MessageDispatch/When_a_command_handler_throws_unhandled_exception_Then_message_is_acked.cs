﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE. */

#endregion

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Core.Tests.MessageDispatch.TestDoubles;
using Xunit;
using Paramore.Brighter.ServiceActivator;
using Serilog.Sinks.TestCorrelator;
using Serilog.Events;

namespace Paramore.Brighter.Core.Tests.MessageDispatch
{
    public class MessagePumpCommandProcessingExceptionTests
    {
        private readonly IAmAMessagePump _messagePump;
        private readonly Channel _channel;
        private readonly int _requeueCount = 5;
        private readonly RoutingKey _routingKey = new("MyCommand");
        private readonly FakeTimeProvider _timeProvider = new();

        public MessagePumpCommandProcessingExceptionTests()
        {
            SpyExceptionCommandProcessor commandProcessor = new();
            var provider = new CommandProcessorProvider(commandProcessor);

            InternalBus bus = new(); 
            _channel = new Channel(new("myChannel"),_routingKey, new InMemoryMessageConsumer(_routingKey, bus, _timeProvider, TimeSpan.FromMilliseconds(1000)));
            
            var messageMapperRegistry = new MessageMapperRegistry(
                new SimpleMessageMapperFactory(_ => new MyCommandMessageMapper()),
                null);
            messageMapperRegistry.Register<MyCommand, MyCommandMessageMapper>();
            
            _messagePump = new MessagePumpBlocking<MyCommand>(provider, messageMapperRegistry, null, new InMemoryRequestContextFactory(), _channel)
            {
                Channel = _channel, TimeOut = TimeSpan.FromMilliseconds(5000), RequeueCount = _requeueCount
            };

            var msg = new TransformPipelineBuilder(messageMapperRegistry, null)
                .BuildWrapPipeline<MyCommand>()
                .Wrap(new MyCommand(), new RequestContext(), new Publication{Topic = _routingKey});

            bus.Enqueue(msg);
        }

        [Fact]
        public async Task When_a_command_handler_throws_unhandled_exception_Then_message_is_acked()
        {
            using (TestCorrelator.CreateContext())
            {
                var task = Task.Factory.StartNew(() => _messagePump.Run(), TaskCreationOptions.LongRunning);
                await Task.Delay(1000);
                
                _timeProvider.Advance(TimeSpan.FromSeconds(2)); //This will trigger requeue of not acked/rejected messages

                var quitMessage = new Message(new MessageHeader(string.Empty, RoutingKey.Empty, MessageType.MT_QUIT),
                    new MessageBody(""));
                _channel.Enqueue(quitMessage);

                await Task.WhenAll(task);

                TestCorrelator.GetLogEventsFromCurrentContext()
                    .Should().Contain(x => x.Level == LogEventLevel.Error)
                    .Which.MessageTemplate.Text
                    .Should().Be("MessagePump: Failed to dispatch message '{Id}' from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}");
            }
        }
    }
}
