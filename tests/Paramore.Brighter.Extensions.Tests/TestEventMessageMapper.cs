using Paramore.Brighter;

namespace Tests
{
    public class TestEventMessageMapper : IAmAMessageMapper<TestEvent>
    {
        public Message MapToMessage(TestEvent request, Publication publication)  
        {
            throw new System.NotImplementedException();
        }

        public TestEvent MapToRequest(Message message)
        {
            throw new System.NotImplementedException();
        }
    }
}
