﻿namespace System
{
    internal static class SR
    {
        public static void GuardNotNull<T>(T value) where T : class
        {
            if (value == null) 
                throw new ArgumentNullException();
        }
    }
}