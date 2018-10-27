using System;

namespace Akka.Persistence.EventStore.Common
{
    public class SubscriptionDroppedException : Exception
    {

        public SubscriptionDroppedException() : this("Unknown error", null)
        {
            
        }
        
        public SubscriptionDroppedException(string message, Exception inner) : base(message, inner)
        {
            
        }
    }
}