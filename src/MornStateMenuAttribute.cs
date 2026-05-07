using System;
namespace MornLib {
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class MornStateMenuAttribute : Attribute {
        private readonly string _path;
        public MornStateMenuAttribute(string path) { _path = path; }
        public string Path => _path;
    }
}
