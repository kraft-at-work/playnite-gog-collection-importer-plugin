using System;

namespace GogCollectionImporter.Exceptions
{
    public class GogGalaxyNotFoundException : Exception
    {
        public GogGalaxyNotFoundException(string message) : base(message)
        {
        }
    }
}
