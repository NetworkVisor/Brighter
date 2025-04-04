﻿using System;
using System.Collections.Generic;

namespace Paramore.Brighter.MessagingGateway.AWSSQS;

/// <summary>
/// The SQS Attributes
/// </summary>
public class SqsAttributes
{
    /// <summary>
    /// The routing key type.
    /// </summary>
    public ChannelType ChannelType { get; set; }
    
    /// <summary>
    /// This governs how long, in seconds, a 'lock' is held on a message for one consumer
    /// to process. SQS calls this the VisibilityTimeout
    /// </summary>
    public int LockTimeout { get; set; }

    /// <summary>
    /// The length of time, in seconds, for which the delivery of all messages in the queue is delayed.
    /// </summary>
    public int DelaySeconds { get; set; }

    /// <summary>
    /// The length of time, in seconds, for which Amazon SQS retains a message
    /// </summary>
    public int MessageRetentionPeriod { get; set; }

    /// <summary>
    ///  The JSON serialization of the queue's access control policy.
    /// </summary>
    public string? IAMPolicy { get; set; }

    /// <summary>
    /// Indicate that the Raw Message Delivery setting is enabled or disabled
    /// </summary>
    public bool RawMessageDelivery { get; set; }

    /// <summary>
    /// The policy that controls when we send messages to a DLQ after too many requeue attempts
    /// </summary>
    public RedrivePolicy? RedrivePolicy { get; set; }

    /// <summary>
    /// Gets the timeout that we use to infer that nothing could be read from the channel i.e. is empty
    /// or busy
    /// </summary>
    /// <value>The timeout</value>
    public TimeSpan TimeOut { get; set; }

    /// <summary>
    /// A list of resource tags to use when creating the queue
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// The AWS SQS type.
    /// </summary>
    public SnsSqsType Type { get; set; }

    /// <summary>
    /// Enables or disable content-based deduplication, for Fifo queues.
    /// </summary>
    public bool ContentBasedDeduplication { get; set; } = true;

    /// <summary>
    /// Specifies whether message deduplication occurs at the message group or queue level.
    /// This configuration is used for high throughput for FIFO queues configuration
    /// </summary>
    public DeduplicationScope? DeduplicationScope { get; set; }

    /// <summary>
    /// Specifies whether the FIFO queue throughput quota applies to the entire queue or per message group
    /// This configuration is used for high throughput for FIFO queues configuration
    /// </summary>
    public int? FifoThroughputLimit { get; set; }

    public static SqsAttributes From(SqsSubscription subscription)
    {
        return new SqsAttributes
        {
            ChannelType = subscription.ChannelType,
            LockTimeout = subscription.LockTimeout,
            DelaySeconds = subscription.DelaySeconds,
            MessageRetentionPeriod = subscription.MessageRetentionPeriod,
            IAMPolicy = subscription.IAMPolicy,
            RawMessageDelivery = subscription.RawMessageDelivery,
            RedrivePolicy = subscription.RedrivePolicy,
            Tags = subscription.Tags,
            Type = subscription.SqsType,
            ContentBasedDeduplication = subscription.ContentBasedDeduplication,
            DeduplicationScope = subscription.DeduplicationScope,
            FifoThroughputLimit = subscription.FifoThroughputLimit,
            TimeOut = subscription.TimeOut,
        };
    }
}
