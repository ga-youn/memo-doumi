using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MemoDoumi
{
    [DataContract]
    public class NoteData
    {
        [DataMember(Order = 0)] public string Id;
        [DataMember(Order = 1)] public string Text;
        [DataMember(Order = 2)] public int X;
        [DataMember(Order = 3)] public int Y;
        [DataMember(Order = 4)] public int W;
        [DataMember(Order = 5)] public int H;
        [DataMember(Order = 6)] public int Color;
        [DataMember(Order = 7)] public bool Pinned;
        [DataMember(Order = 8)] public bool Anchored;     // true 면 X,Y 는 강아지 기준 상대 위치
        [DataMember(Order = 9)] public bool Closed;
        [DataMember(Order = 10)] public long Sort;
        [DataMember(Order = 11)] public bool Md;
        [DataMember(Order = 12)] public long CreatedUtc;
        [DataMember(Order = 13)] public long ModifiedUtc;

        // 서식 기호를 뺀 본문을 한 번만 계산해 두는 자리 (저장하지 않는다)
        internal string PlainSource, PlainValue;
    }

    [DataContract]
    public class AppData
    {
        [DataMember(Order = 0)] public List<NoteData> Notes;
        [DataMember(Order = 1)] public bool NotesVisible;
        [DataMember(Order = 2)] public int LastColor;
        [DataMember(Order = 3)] public bool HasMascotPos;
        [DataMember(Order = 4)] public int MascotX;
        [DataMember(Order = 5)] public int MascotY;
        [DataMember(Order = 6)] public bool StayPut;
        [DataMember(Order = 7)] public string CurrentId;
    }

    internal class SaveException : Exception
    {
        public readonly List<string> Failures;

        public SaveException(List<string> failures, Exception first)
            : base(failures.Count == 1 ? failures[0] : failures[0] + " 외 " + (failures.Count - 1) + "건", first)
        {
            Failures = failures;
        }
    }

}
