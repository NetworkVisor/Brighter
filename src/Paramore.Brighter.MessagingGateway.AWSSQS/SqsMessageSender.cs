﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Paramore.Brighter.Extensions;
using Paramore.Brighter.Logging;

namespace Paramore.Brighter.MessagingGateway.AWSSQS;

/// <summary>
/// Class responsible for sending a message to a SQS
/// </summary>
public class SqsMessageSender
{
    private static readonly ILogger s_logger = ApplicationLogging.CreateLogger<SqsMessageSender>();
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromSeconds(900);
    
    private readonly string _queueUrl;
    private readonly SnsSqsType _queueType;
    private readonly AmazonSQSClient _client;

    /// <summary>
    /// Initialize the <see cref="SqsMessageSender"/>
    /// </summary>
    /// <param name="queueUrl">The queue ARN</param>
    /// <param name="queueType">The queue type</param>
    /// <param name="client">The SQS Client</param>
    public SqsMessageSender(string queueUrl, SnsSqsType queueType, AmazonSQSClient client)
    {
        _queueUrl = queueUrl;
        _queueType = queueType;
        _client = client;
    }
    
    /// <summary>
    /// Sending message via SQS
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="delay">The delay in ms. 0 is no delay. Defaults to 0</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> that cancels the Publish operation</param>
    /// <returns>The message id.</returns>
    public async Task<string?> SendAsync(Message message, TimeSpan? delay, CancellationToken cancellationToken)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl, 
            MessageBody = message.Body.Value
        };

        delay ??= TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            // SQS has a hard limit of 15min for Delay in Seconds
            if (delay.Value > s_maxDelay)
            {
                delay = s_maxDelay;
                s_logger.LogWarning("Set delay from {CurrentDelay} to 15min (SQS support up to 15min)", delay);
            }

            request.DelaySeconds = (int)delay.Value.TotalSeconds;
        }

        if (_queueType == SnsSqsType.Fifo)
        {
            request.MessageGroupId = message.Header.PartitionKey;
            if (message.Header.Bag.TryGetValue(HeaderNames.DeduplicationId, out var deduplicationId))
            {
                request.MessageDeduplicationId = (string)deduplicationId;
            }
        }

        var messageAttributes = new Dictionary<string, MessageAttributeValue>
        { 
            // Cloud event
            [HeaderNames.Id] = new() { StringValue = Convert.ToString(message.Header.MessageId), DataType = "String" },
            [HeaderNames.DataContentType] = new() { StringValue = message.Header.ContentType, DataType = "String" },
            [HeaderNames.SpecVersion] = new() { StringValue = message.Header.SpecVersion, DataType = "String" },
            [HeaderNames.Type] = new() { StringValue = message.Header.Type, DataType = "String" },
            [HeaderNames.Source] = new() { StringValue = message.Header.Source.ToString(), DataType = "String" },
            [HeaderNames.Time] = new() { StringValue = message.Header.TimeStamp.ToRcf3339(), DataType = "String" },
                        
             // Brighter custom headers
             [HeaderNames.Topic] = new() { StringValue = _queueUrl, DataType = "String" },
             [HeaderNames.HandledCount] = new() { StringValue = Convert.ToString(message.Header.HandledCount), DataType = "String" },
             [HeaderNames.MessageType] = new() { StringValue = message.Header.MessageType.ToString(), DataType = "String" },
                        
             // Backward compatibility with old brighter version
             [HeaderNames.ContentType] = new() { StringValue = message.Header.ContentType, DataType = "String" },
             [HeaderNames.Timestamp] = new() { StringValue = Convert.ToString(message.Header.TimeStamp), DataType = "String" }
        };

        if (!string.IsNullOrEmpty(message.Header.ReplyTo))
        {
            messageAttributes.Add(HeaderNames.ReplyTo,
                new MessageAttributeValue { StringValue = message.Header.ReplyTo, DataType = "String" });
        }

        if (!string.IsNullOrEmpty(message.Header.Subject))
        {
            messageAttributes.Add(HeaderNames.Subject,
                new MessageAttributeValue { StringValue = message.Header.Subject, DataType = "String" });
        }

        if (!string.IsNullOrEmpty(message.Header.CorrelationId))
        {
            messageAttributes.Add(HeaderNames.CorrelationId,
                new MessageAttributeValue { StringValue = message.Header.CorrelationId, DataType = "String" });
        }
        
        if (message.Header.DataSchema != null)
        {
            messageAttributes.Add(HeaderNames.DataSchema, new MessageAttributeValue { StringValue = message.Header.DataSchema.ToString() });
        }
        
        // we can set up to 10 attributes; we have set 6 above, so use a single JSON object as the bag
        var bagJson = JsonSerializer.Serialize(message.Header.Bag, JsonSerialisationOptions.Options);
        messageAttributes[HeaderNames.Bag] = new() { StringValue = bagJson, DataType = "String" };
        request.MessageAttributes = messageAttributes;

        var response = await _client.SendMessageAsync(request, cancellationToken);
        if (response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.Created
            or HttpStatusCode.Accepted)
        {
            return response.MessageId;
        }

        return null;
    }
}
