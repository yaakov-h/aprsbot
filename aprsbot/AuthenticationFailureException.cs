using System;
using System.Runtime.Serialization;

namespace AprsBot
{
    [Serializable]
    public class AuthenticationFailureException : Exception
    {
        public AuthenticationFailureException()
        {
        }

        public AuthenticationFailureException(string message)
            : base(message)
        {
        }

        public AuthenticationFailureException(string message, Exception inner)
            : base(message, inner)
        {
        }
        protected AuthenticationFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
