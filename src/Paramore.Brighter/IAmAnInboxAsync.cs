﻿#region Licence
/* The MIT License (MIT)
Copyright © 2015 Ian Cooper <ian_hammond_cooper@yahoo.co.uk>

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

using System.Threading;
using System.Threading.Tasks;

namespace Paramore.Brighter
{
    /// <summary>
    /// Interface IAmAnInboxAsync
    /// An Inbox stores <see cref="Request"/>s for diagnostics or de-duplication.
    /// </summary>
    public interface IAmAnInboxAsync : IAmAnInbox
    {
        /// <summary>
        ///   Awaitably adds a command to the store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="command">The command.</param>
        /// <param name="contextKey">An identifier for the context in which the command has been processed (for example, the name of the handler)</param>
        /// <param name="requestContext">What is the context for this request; used to access the Span</param>
        /// <param name="timeoutInMilliseconds">Timeout in milliseconds; -1 for default timeout</param>
        /// <param name="cancellationToken">Allow the sender to cancel the operation, if the parameter is supplied</param>
        /// <returns><see cref="Task"/>.</returns>
        Task AddAsync<T>(T command, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1, CancellationToken cancellationToken = default) where T : class, IRequest;

        /// <summary>
        ///   Awaitably finds a command with the specified identifier.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The identifier.</param>
        /// <param name="contextKey">An identifier for the context in which the command has been processed (for example, the name of the handler)</param>
        /// <param name="requestContext">What is the context for this request; used to access the Span</param>
        /// <param name="timeoutInMilliseconds">Timeout in milliseconds; -1 for default timeout</param>
        /// <param name="cancellationToken">Allow the sender to cancel the operation, if the parameter is supplied</param>
        /// <returns><see cref="Task{T}"/>.</returns>
        Task<T> GetAsync<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1,
            CancellationToken cancellationToken = default) where T : class, IRequest;

        /// <summary>
        ///   Awaitable checks whether a command with the specified identifier exists in the store.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id">The identifier.</param>
        /// <param name="contextKey">An identifier for the context in which the command has been processed (for example, the name of the handler)</param>
        /// <param name="requestContext">What is the context for this request; used to access the Span</param>
        /// <param name="timeoutInMilliseconds">Timeout in milliseconds; -1 for default timeout</param>
        /// <param name="cancellationToken">Allow the sender to cancel the operation, if the parameter is supplied</param>
        /// <returns><see cref="Task{true}"/> if it exists, otherwise <see cref="Task{false}"/>.</returns>
        Task<bool> ExistsAsync<T>(string id, string contextKey, RequestContext? requestContext, int timeoutInMilliseconds = -1,
            CancellationToken cancellationToken = default) where T : class, IRequest;

        /// <summary>
        ///   If false we the default thread synchronization context to run any continuation, if true we re-use the original synchronization context.
        ///   Default to false unless you know that you need true, as you risk deadlocks with the originating thread if you Wait
        ///   or access the Result or otherwise block. You may need the originating synchronization context if you need to access thread specific storage
        ///   such as HTTPContext.
        /// </summary>
        /// <value><see langword="true"/> if [continue on captured context]; otherwise, <see langword="false"/>.</value>
        bool ContinueOnCapturedContext { get; set; }
    }
}
