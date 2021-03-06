﻿using System;
using System.Runtime.Serialization;

namespace AldursLab.WurmApi
{
    [Serializable]
    public class InvalidSearchParametersException : WurmApiException
    {
        public InvalidSearchParametersException()
        {
        }

        public InvalidSearchParametersException(string message)
            : base(message)
        {
        }

        public InvalidSearchParametersException(string message, Exception inner)
            : base(message, inner)
        {
        }

        protected InvalidSearchParametersException(SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
        }
    }
}