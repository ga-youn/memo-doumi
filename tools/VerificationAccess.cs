using System.Collections;
using System.Drawing;
using System.Windows.Forms;
namespace MemoDoumi
{
    internal partial class App
    {
        internal NoteForm VerifyForm(NoteData note) { return FormOf(note); }
        internal NoteForm VerifyFind(NoteData note) { return FindForm(note); }
        internal ICollection VerifyForms { get { return notes; } }
        internal SearchForm VerifySearch { get { return search; } }
    }
    internal partial class NoteForm
    {
        internal RichTextBox VerifyEditor { get { return box; } }
        internal ContextMenuStrip VerifyColorMenu { get { return colorMenu; } }
        internal static Font[] VerifyFonts { get { return new[] { NoteFont, PosFont, TimeFont }; } }
    }
    internal partial class IconButton { internal static Font VerifyFont { get { return IconFont; } } }
    internal partial class SearchForm
    {
        internal TextBox VerifyInput { get { return input; } }
        internal IList VerifyHits { get { return hits; } }
        internal VirtualList VerifyList { get { return list; } }
        internal NoteData VerifyNoteAt(int index) { return hits[index].Note; }
        internal double VerifyCost { set { lastCostMs = value; } }
        internal void VerifyKey(Keys key) { OnInputKeyDown(input, new KeyEventArgs(key)); }
    }
}
