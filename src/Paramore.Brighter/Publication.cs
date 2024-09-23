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
using System.Collections.Generic;

namespace Paramore.Brighter
{
    /// <summary>
    /// Contains configuration that is generic to producers - similar to Subscription for consumers
    /// Unlike <see cref="Subscription"/>, as it is passed to a constructor it is by convention over enforced at compile time
    /// Platform specific configuration goes into a <see cref="IAmGatewayConfiguration"/> derived class
    /// </summary>
    public class Publication
    {
        /// <summary>
        /// OPTIONAL [Cloud Events] REQUIRED [Brighter]
        /// Content type of data value. This attribute enables data to carry any type of content, whereby format and
        /// encoding might differ from that of the chosen event format.
        /// MUST adhere to the format specified in <see href="https://datatracker.ietf.org/doc/html/rfc2046">RFC 2046</see>
        /// Because of the complexity of serializing if you do not know the type, we regard this as required even
        /// though Cloud Events does not.
        /// Default value is text/plain
        /// </summary>
        public string ContentType { get; set; } = "text/plain";
        
        /// <summary>
        /// OPTIONAL
        /// From <see href="https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#context-attributes">Cloud Events Spec</see>
        /// Identifies the schema that data adheres to. Incompatible changes to the schema SHOULD be reflected by a different URI. 
        /// </summary>
        public Uri? DataSchema { get; set; }
        
        /// <summary>
        /// What do we do with infrastructure dependencies for the producer?
        /// </summary>
        public OnMissingChannel MakeChannels { get; set; }
        
        /// <summary>
        /// The type of the request that we expect to publish on this channel
        /// </summary>
        public Type? RequestType { get; set; }
        
        /// <summary>
        /// REQUIRED
        /// From <see href="https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#context-attributes">Cloud Events Spec</see>
        /// Identifies the context in which an event happened. Often this will include information such as the type of
        /// the event source, the organization publishing the event or the process that produced the event.
        /// The exact syntax and semantics behind the data encoded in the URI is defined by the event producer.
        /// Producers MUST ensure that source + id is unique for each distinct event.
        /// Default: "http://goparamore.io" for backward compatibility as required
        /// </summary>
        public Uri Source { get; set; } = new Uri("http://goparamore.io");
        
        /// <summary>
        /// OPTIONAL
        /// From <see href="https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#context-attributes">Cloud Events Spec</see>
        /// This describes the subject of the event in the context of the event producer (identified by source).
        /// In publish-subscribe scenarios, a subscriber will typically subscribe to events emitted by a source,
        /// but the source identifier alone might not be sufficient as a qualifier for any specific event if the
        /// source context has internal sub-structure.
        /// </summary>
        public string? Subject { get; set; }
        
        /// <summary>
        /// The topic this publication is for
        /// </summary>
        public RoutingKey? Topic { get; set; }

        /// <summary>
        /// REQUIRED
        /// From <see href="https://github.com/cloudevents/spec/blob/v1.0.2/cloudevents/spec.md#context-attributes">Clode Events Spec</see>
        /// This attribute contains a value describing the type of event related to the originating occurrence.
        /// Often this attribute is used for routing, observability, policy enforcement, etc.
        /// SHOULD be prefixed with a reverse-DNS name. The prefixed domain dictates the organization which defines the semantics of this event type.
        /// Default: "goparamore.io.Paramore.Brighter.Message" for backward compatibility as required
        /// </summary>
        public string Type { get; set; } = "goparamore.io.Paramore.Brighter.Message";
    }
}
