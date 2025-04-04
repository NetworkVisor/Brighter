﻿#region Licence
/* The MIT License (MIT)
Copyright © 2022 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.Actions;
using Paramore.Brighter.Observability;
using Polly.CircuitBreaker;

namespace Paramore.Brighter.ServiceActivator
{
    /// <summary>
    /// Used when the message pump should block for I/O
    /// Will guarantee strict ordering of the messages on the queue
    /// Predictable performance as only one thread, allows you to configure number of performers for number of threads to use
    /// Lower throughput than async
    /// See <a href="https://www.dre.vanderbilt.edu/~schmidt/PDF/reactor-siemens.pdf">Reactor Pattern</a> for more on this approach
    /// </summary>
    /// <typeparam name="TRequest"></typeparam>
    public class Reactor<TRequest> : MessagePump<TRequest>, IAmAMessagePump where TRequest : class, IRequest
    {
        private readonly UnwrapPipeline<TRequest> _unwrapPipeline;

        /// <summary>
        /// Constructs a message pump 
        /// </summary>
        /// <param name="commandProcessor">Provides a way to grab a command processor correctly scoped</param>
        /// <param name="messageMapperRegistry">The registry of mappers</param>
        /// <param name="messageTransformerFactory">The factory that lets us create instances of transforms</param>
        /// <param name="requestContextFactory">A factory to create instances of request synchronizationHelper, used to add synchronizationHelper to a pipeline</param>
        /// <param name="channel">The channel from which to read messages</param>
        /// <param name="tracer">What is the tracer we will use for telemetry</param>
        /// <param name="instrumentationOptions">When creating a span for <see cref="CommandProcessor"/> operations how noisy should the attributes be</param>
        public Reactor(
            IAmACommandProcessor commandProcessor,
            IAmAMessageMapperRegistry messageMapperRegistry, 
            IAmAMessageTransformerFactory messageTransformerFactory,
            IAmARequestContextFactory requestContextFactory,
            IAmAChannelSync channel,
            IAmABrighterTracer? tracer = null,
            InstrumentationOptions instrumentationOptions = InstrumentationOptions.All) 
            : base(commandProcessor, requestContextFactory, tracer,  instrumentationOptions)
        {
            var transformPipelineBuilder = new TransformPipelineBuilder(messageMapperRegistry, messageTransformerFactory);
            _unwrapPipeline = transformPipelineBuilder.BuildUnwrapPipeline<TRequest>();
            Channel = channel;
        }

        /// <summary>
        /// The channel to receive messages from
        /// </summary>
        public IAmAChannelSync Channel { get; set; }
        
        /// <summary>
        /// The <see cref="MessagePumpType"/> of this message pump; indicates Reactor or Proactor
        /// </summary>
        public override MessagePumpType MessagePumpType => MessagePumpType.Reactor;

        /// <summary>
        /// Runs the message pump, performing the following:
        /// - Gets a message from a queue/stream
        /// - Translates the message to the local type system
        /// - Dispatches the message to waiting handlers
        /// - Handles any exceptions that occur during the dispatch and tries to keep the pump alive  
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Run()
        {
            var pumpSpan = Tracer?.CreateMessagePumpSpan(MessagePumpSpanOperation.Begin, Channel.RoutingKey, MessagingSystem.InternalBus, InstrumentationOptions);
            do
            {
                if (UnacceptableMessageLimitReached())
                {
                    Channel.Dispose();
                    break;
                }

                s_logger.LogDebug("MessagePump: Receiving messages from channel {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);

                Activity? span = null;
                Message? message = null;
                try
                {
                    message = Channel.Receive(TimeOut);
                    span = Tracer?.CreateSpan(MessagePumpSpanOperation.Receive, message, MessagingSystem.InternalBus, InstrumentationOptions);
                }
                catch (ChannelFailureException ex) when (ex.InnerException is BrokenCircuitException)
                {
                    s_logger.LogWarning("MessagePump: BrokenCircuitException messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    var errorSpan = Tracer?.CreateMessagePumpExceptionSpan(ex, Channel.RoutingKey, MessagePumpSpanOperation.Receive, MessagingSystem.InternalBus, InstrumentationOptions);
                    Tracer?.EndSpan(errorSpan);
                    Task.Delay(ChannelFailureDelay).GetAwaiter().GetResult(); //-- pause pump; blocks consuming thread on empty queue; 
                    continue;
                }
                catch (ChannelFailureException ex)
                {
                    s_logger.LogWarning("MessagePump: ChannelFailureException messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    var errorSpan = Tracer?.CreateMessagePumpExceptionSpan(ex, Channel.RoutingKey, MessagePumpSpanOperation.Receive, MessagingSystem.InternalBus, InstrumentationOptions);
                    Tracer?.EndSpan(errorSpan );
                    Task.Delay(ChannelFailureDelay).GetAwaiter().GetResult(); //-- pause pump; blocks consuming thread on empty queue; 
                    continue;
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "MessagePump: Exception receiving messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    var errorSpan = Tracer?.CreateMessagePumpExceptionSpan(ex, Channel.RoutingKey, MessagePumpSpanOperation.Receive, MessagingSystem.InternalBus, InstrumentationOptions);
                    Tracer?.EndSpan(errorSpan );
                }

                if (message is null)
                {
                    Channel.Dispose();
                    span?.SetStatus(ActivityStatusCode.Error, "Could not receive message. Note that should return an MT_NONE from an empty queue on timeout");
                    Tracer?.EndSpan(span);
                    throw new Exception("Could not receive message. Note that should return an MT_NONE from an empty queue on timeout");
                }

                // empty queue
                if (message.Header.MessageType == MessageType.MT_NONE)
                {
                    span?.SetStatus(ActivityStatusCode.Ok);
                    Tracer?.EndSpan(span);
                    Task.Delay(EmptyChannelDelay).GetAwaiter().GetResult();  //-- pause pump; blocks consuming thread on empty queue; 
                    continue;
                }

                // failed to parse a message from the incoming data
                if (message.Header.MessageType == MessageType.MT_UNACCEPTABLE)
                {
                    s_logger.LogWarning("MessagePump: Failed to parse a message from the incoming message with id {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    span?.SetStatus(ActivityStatusCode.Error, $"MessagePump: Failed to parse a message from the incoming message with id {message.Id} from {Channel.Name} on thread # {Environment.CurrentManagedThreadId}");
                    Tracer?.EndSpan(span);
                    IncrementUnacceptableMessageLimit();
                    AcknowledgeMessage(message);

                    continue;
                }
 
                // QUIT command
                if (message.Header.MessageType == MessageType.MT_QUIT)
                {
                    s_logger.LogInformation("MessagePump: Quit receiving messages from {ChannelName} on thread #{ManagementThreadId}", Channel.Name, Environment.CurrentManagedThreadId);
                    span?.SetStatus(ActivityStatusCode.Ok);
                    Tracer?.EndSpan(span);
                    Channel.Dispose();
                    break;
                }

                // Serviceable message
                try
                {
                    RequestContext context = InitRequestContext(span, message);

                    var request = TranslateMessage(message, context);
                    
                    DispatchRequest(message.Header, request, context);

                    span?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (AggregateException aggregateException)
                {
                    var stop = false;
                    var defer = false;
  
                    foreach (var exception in aggregateException.InnerExceptions)
                    {
                        if (exception is ConfigurationException configurationException)
                        {
                            s_logger.LogCritical(configurationException, "MessagePump: Stopping receiving of messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                            stop = true;
                            break;
                        }

                        if (exception is DeferMessageAction)
                        {
                            defer = true;
                            continue;
                        }

                        s_logger.LogError(exception, "MessagePump: Failed to dispatch message {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    }

                    if (defer)
                    {
                        s_logger.LogDebug("MessagePump: Deferring message {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                        span?.SetStatus(ActivityStatusCode.Error, $"Deferring message {message.Id} for later action");
                        if (RequeueMessage(message))
                            continue;
                    }

                    if (stop)
                    {
                        RejectMessage(message);
                        span?.SetStatus(ActivityStatusCode.Error, $"MessagePump: Stopping receiving of messages from {Channel.Name} with {Channel.RoutingKey} on thread # {Environment.CurrentManagedThreadId}");
                        Channel.Dispose();
                        break;
                    }

                    span?.SetStatus(ActivityStatusCode.Error, $"MessagePump: Failed to dispatch message {message.Id} from {Channel.Name} with {Channel.RoutingKey}  on thread # {Environment.CurrentManagedThreadId}");
                }
                catch (ConfigurationException configurationException)
                {
                    s_logger.LogCritical(configurationException,"MessagePump: Stopping receiving of messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    RejectMessage(message);
                    span?.SetStatus(ActivityStatusCode.Error, $"MessagePump: Stopping receiving of messages from {Channel.Name} on thread # {Environment.CurrentManagedThreadId}");
                    Channel.Dispose();
                    break;
                }
                catch (DeferMessageAction)
                {
                    s_logger.LogDebug("MessagePump: Deferring message {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);
                    
                    span?.SetStatus(ActivityStatusCode.Error, $"Deferring message {message.Id} for later action");
                    
                    if (RequeueMessage(message)) continue;
                }
                catch (MessageMappingException messageMappingException)
                {
                    s_logger.LogWarning(messageMappingException, "MessagePump: Failed to map message {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);

                    IncrementUnacceptableMessageLimit();
                    
                    span?.SetStatus(ActivityStatusCode.Error, $"MessagePump: Failed to map message {message.Id} from {Channel.Name} with {Channel.RoutingKey} on thread # {Thread.CurrentThread.ManagedThreadId}");
                }
                catch (Exception e)
                {
                    s_logger.LogError(e,
                        "MessagePump: Failed to dispatch message '{Id}' from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}",
                        message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);

                    span?.SetStatus(ActivityStatusCode.Error,$"MessagePump: Failed to dispatch message '{message.Id}' from {Channel.Name} with {Channel.RoutingKey} on thread # {Environment.CurrentManagedThreadId}");
                }
                finally
                {
                    Tracer?.EndSpan(span);
                }

                AcknowledgeMessage(message);

            } while (true);

            s_logger.LogInformation(
                "MessagePump0: Finished running message loop, no longer receiving messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}",
                Channel.Name, Channel.RoutingKey, Thread.CurrentThread.ManagedThreadId);
            Tracer?.EndSpan(pumpSpan);

        }

        private void AcknowledgeMessage(Message message)
        {
            s_logger.LogDebug(
                "MessagePump: Acknowledge message {Id} read from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}",
                message.Id, Channel.Name, Channel.RoutingKey, Environment.CurrentManagedThreadId);

            Channel.Acknowledge(message);
        }
        
        private void DispatchRequest(MessageHeader messageHeader, TRequest request, RequestContext requestContext)
        {
            s_logger.LogDebug("MessagePump: Dispatching message {Id} from {ChannelName} on thread # {ManagementThreadId}", request.Id, Thread.CurrentThread.ManagedThreadId, Channel.Name);
            requestContext.Span?.AddEvent(new ActivityEvent("Dispatch Message"));

            var messageType = messageHeader.MessageType;

            ValidateMessageType(messageType, request);

            switch (messageType)
            {
                case MessageType.MT_COMMAND:
                {
                    CommandProcessor.Send(request, requestContext);
                    break;
                }
                case MessageType.MT_DOCUMENT:
                case MessageType.MT_EVENT:
                {
                    CommandProcessor.Publish(request, requestContext);
                    break;
                }
            }
        }

        private RequestContext InitRequestContext(Activity? span, Message message)
        {
            var context = RequestContextFactory.Create();
            context.Span = span;
            context.OriginatingMessage = message;
            context.Bag.AddOrUpdate("ChannelName", Channel.Name, (_, _) => Channel.Name);
            context.Bag.AddOrUpdate("RequestStart", DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            return context;
        }

        private void RejectMessage(Message message)
        {
            s_logger.LogWarning("MessagePump: Rejecting message {Id} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}", message.Id, Channel.Name, Channel.RoutingKey, Thread.CurrentThread.ManagedThreadId);
            IncrementUnacceptableMessageLimit();

            Channel.Reject(message);
        }

        private bool RequeueMessage(Message message)
        {
            message.Header.UpdateHandledCount();

            if (DiscardRequeuedMessagesEnabled())
            {
                if (message.HandledCountReached(RequeueCount))
                {
                    var originalMessageId = message.Header.Bag.TryGetValue(Message.OriginalMessageIdHeaderName, out object? value) ? value.ToString() : null;

                    s_logger.LogError(
                        "MessagePump: Have tried {RequeueCount} times to handle this message {Id}{OriginalMessageId} from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}, dropping message.",
                        RequeueCount,
                        message.Id,
                        string.IsNullOrEmpty(originalMessageId)
                            ? string.Empty
                            : $" (original message id {originalMessageId})",
                        Channel.Name,
                        Channel.RoutingKey,
                        Thread.CurrentThread.ManagedThreadId);

                    RejectMessage(message);
                    return false;
                }
            }

            s_logger.LogDebug(
                "MessagePump: Re-queueing message {Id} from {ManagementThreadId} on thread # {ChannelName} with {RoutingKey}", message.Id,
                Channel.Name, Channel.RoutingKey, Thread.CurrentThread.ManagedThreadId);

            return Channel.Requeue(message, RequeueDelay);
        }
        
        private TRequest TranslateMessage(Message message, RequestContext requestContext)
        {
            s_logger.LogDebug("MessagePump: Translate message {Id} on thread # {ManagementThreadId}", message.Id, Thread.CurrentThread.ManagedThreadId);
            requestContext.Span?.AddEvent(new ActivityEvent("Translate Message"));

            TRequest request;

            try
            {
                request = _unwrapPipeline.Unwrap(message, requestContext);
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MessageMappingException($"Failed to map message {message.Id} using pipeline for type {typeof(TRequest).FullName} ", exception);
            }

            return request;
        }

        private bool UnacceptableMessageLimitReached()
        {
            if (UnacceptableMessageLimit == 0) return false;

            if (UnacceptableMessageCount >= UnacceptableMessageLimit)
            {
                s_logger.LogCritical(
                    "MessagePump: Unacceptable message limit of {UnacceptableMessageLimit} reached, stopping reading messages from {ChannelName} with {RoutingKey} on thread # {ManagementThreadId}",
                    UnacceptableMessageLimit,
                    Channel.Name,
                    Channel.RoutingKey,
                    Environment.CurrentManagedThreadId
                );
                
                return true;
            }
            return false;
        }
    }
}
