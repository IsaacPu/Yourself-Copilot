using System.Runtime.Serialization;

namespace LocalDesktopNote
{
    [DataContract]
    public sealed class MemoPaper
    {
        [DataMember] public string Id;
        [DataMember] public string Text;
        [DataMember] public string StoredAtUtc;
        [DataMember] public string Kind;
    }
}
