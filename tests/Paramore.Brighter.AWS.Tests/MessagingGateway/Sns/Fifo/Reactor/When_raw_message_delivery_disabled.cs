﻿using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using Paramore.Brighter.AWS.Tests.Helpers;
using Paramore.Brighter.AWS.Tests.TestDoubles;
using Paramore.Brighter.MessagingGateway.AWSSQS;
using Xunit;

namespace Paramore.Brighter.AWS.Tests.MessagingGateway.Sns.Fifo.Reactor;

[Trait("Category", "AWS")]
public class SqsRawMessageDeliveryTests : IDisposable, IAsyncDisposable
{
    private readonly SnsMessageProducer _messageProducer;
    private readonly ChannelFactory _channelFactory;
    private readonly IAmAChannelSync _channel;
    private readonly RoutingKey _routingKey;

    public SqsRawMessageDeliveryTests()
    {
        var awsConnection = GatewayFactory.CreateFactory();

        _channelFactory = new ChannelFactory(awsConnection);
        var channelName = $"Raw-Msg-Delivery-Tests-{Guid.NewGuid().ToString()}".Truncate(45);
        _routingKey = new RoutingKey($"Raw-Msg-Delivery-Tests-{Guid.NewGuid().ToString()}".Truncate(45));
        var topicAttributes = new SnsAttributes { Type = SqsType.Fifo };

        var bufferSize = 10;

        //Set rawMessageDelivery to false
        _channel = _channelFactory.CreateSyncChannel(new SqsSubscription<MyCommand>(
            subscriptionName: new SubscriptionName(channelName),
            channelName: new ChannelName(channelName),
            channelType: ChannelType.PubSub,
            routingKey: _routingKey,
            bufferSize: bufferSize,
            messagePumpType: MessagePumpType.Reactor,
            queueAttributes: new SqsAttributes(
                rawMessageDelivery: false,
                type: SqsType.Fifo), 
            topicAttributes: topicAttributes,
            makeChannels: OnMissingChannel.Create));

        _messageProducer = new SnsMessageProducer(awsConnection,
            new SnsPublication
            {
                MakeChannels = OnMissingChannel.Create, 
                TopicAttributes = topicAttributes
            });
    }

    [Fact]
    public void When_raw_message_delivery_disabled()
    {
        //arrange
        var messageGroupId = $"MessageGroupId{Guid.NewGuid():N}";
        var deduplicationId = $"DeduplicationId{Guid.NewGuid():N}";
        var messageHeader = new MessageHeader(
            Guid.NewGuid().ToString(),
            _routingKey,
            MessageType.MT_COMMAND,
            correlationId: Guid.NewGuid().ToString(),
            replyTo: RoutingKey.Empty,
            contentType: new ContentType(MediaTypeNames.Text.Plain),
            partitionKey: messageGroupId) { Bag = { [HeaderNames.DeduplicationId] = deduplicationId } };

        var customHeaderItem = new KeyValuePair<string, object>("custom-header-item", "custom-header-item-value");
        messageHeader.Bag.Add(customHeaderItem.Key, customHeaderItem.Value);

        var messageToSent = new Message(messageHeader, new MessageBody("test content one"));

        //act
        _messageProducer.Send(messageToSent);

        var messageReceived = _channel.Receive(TimeSpan.FromMilliseconds(10000));

        _channel.Acknowledge(messageReceived);

        //assert
        Assert.Equal(messageToSent.Id, messageReceived.Id);
        Assert.Equal(messageToSent.Header.Topic.ToValidSNSTopicName(true), messageReceived.Header.Topic);
        Assert.Equal(messageToSent.Header.MessageType, messageReceived.Header.MessageType);
        Assert.Equal(messageToSent.Header.CorrelationId, messageReceived.Header.CorrelationId);
        Assert.Equal(messageToSent.Header.ReplyTo, messageReceived.Header.ReplyTo);
        Assert.Equal(messageToSent.Header.ContentType, messageReceived.Header.ContentType);
        Assert.Contains(customHeaderItem.Key, messageReceived.Header.Bag);
        Assert.Equal(customHeaderItem.Value, messageReceived.Header.Bag[customHeaderItem.Key]);
        Assert.Equal(messageToSent.Body.Value, messageReceived.Body.Value);

        Assert.Equal(messageGroupId, messageReceived.Header.PartitionKey);
        Assert.Contains(HeaderNames.DeduplicationId, messageReceived.Header.Bag);
        Assert.Equal(deduplicationId, messageReceived.Header.Bag[HeaderNames.DeduplicationId]);
    }

    public void Dispose()
    {
        _channelFactory.DeleteTopicAsync().Wait();
        _channelFactory.DeleteQueueAsync().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        await _channelFactory.DeleteTopicAsync();
        await _channelFactory.DeleteQueueAsync();
    }
}
