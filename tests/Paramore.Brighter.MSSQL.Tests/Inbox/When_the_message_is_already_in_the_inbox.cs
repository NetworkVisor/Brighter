﻿#region Licence
/* The MIT License (MIT)
Copyright © 2014 Francesco Pighi <francesco.pighi@gmail.com>

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
using Paramore.Brighter.Inbox.MsSql;
using Paramore.Brighter.MSSQL.Tests.TestDoubles;
using Xunit;

namespace Paramore.Brighter.MSSQL.Tests.Inbox
{
    [Trait("Category", "MSSQL")]
    public class SqlInboxDuplicateMessageTests : IDisposable
    {
        private readonly MsSqlTestHelper _msSqlTestHelper;
        private readonly MsSqlInbox _sqlInbox;
        private readonly MyCommand _raisedCommand;
        private readonly string _contextKey;
        private Exception _exception;

        public SqlInboxDuplicateMessageTests()
        {
            _msSqlTestHelper = new MsSqlTestHelper();
            _msSqlTestHelper.SetupCommandDb();

            _sqlInbox = new MsSqlInbox(_msSqlTestHelper.InboxConfiguration);
            _raisedCommand = new MyCommand { Value = "Test" };
            _contextKey = "context-key";
            _sqlInbox.Add(_raisedCommand, _contextKey, null, -1);
        }

        [Fact]
        public void When_The_Message_Is_Already_In_The_Inbox()
        {
            _exception = Catch.Exception(() => _sqlInbox.Add(_raisedCommand, _contextKey, null, -1));

            //_should_succeed_even_if_the_message_is_a_duplicate
            Assert.Null(_exception);
            Assert.True(_sqlInbox.Exists<MyCommand>(_raisedCommand.Id, _contextKey, null, -1));
        }

        [Fact]
        public void When_The_Message_Is_Already_In_The_Inbox_Different_Context()
        {
            _sqlInbox.Add(_raisedCommand, "some other key", null, -1);

            var storedCommand = _sqlInbox.Get<MyCommand>(_raisedCommand.Id, "some other key", null, -1);

            //should read the command from the dynamo db inbox
            Assert.NotNull(storedCommand);
            
        }

        public void Dispose()
        {
            _msSqlTestHelper.CleanUpDb();
        }
    }
}
