using System;

namespace SteamCollectionImporter.Exceptions
{
    public class SteamLibraryNotFoundException : Exception
    {
        public SteamLibraryNotFoundException(string message) : base(message)
        {
        }
    }
}