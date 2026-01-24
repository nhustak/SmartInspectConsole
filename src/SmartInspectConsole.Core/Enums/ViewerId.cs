namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Specifies the viewer for displaying the title or data of a log entry.
/// </summary>
public enum ViewerId
{
    /// <summary>No viewer</summary>
    None = -1,

    /// <summary>Display title in read-only text field</summary>
    Title = 0,

    /// <summary>Display data in read-only text field</summary>
    Data = 1,

    /// <summary>Display data as a list</summary>
    List = 2,

    /// <summary>Display data as key/value pairs</summary>
    ValueList = 3,

    /// <summary>Display data using object inspector</summary>
    Inspector = 4,

    /// <summary>Display data as a table</summary>
    Table = 5,

    /// <summary>Display data as a website</summary>
    Web = 100,

    /// <summary>Display data as binary hex dump</summary>
    Binary = 200,

    /// <summary>Display as HTML source with syntax highlighting</summary>
    HtmlSource = 300,

    /// <summary>Display as JavaScript source with syntax highlighting</summary>
    JavaScriptSource = 301,

    /// <summary>Display as VBScript source with syntax highlighting</summary>
    VbScriptSource = 302,

    /// <summary>Display as Perl source with syntax highlighting</summary>
    PerlSource = 303,

    /// <summary>Display as SQL source with syntax highlighting</summary>
    SqlSource = 304,

    /// <summary>Display as INI source with syntax highlighting</summary>
    IniSource = 305,

    /// <summary>Display as Python source with syntax highlighting</summary>
    PythonSource = 306,

    /// <summary>Display as XML source with syntax highlighting</summary>
    XmlSource = 307,

    /// <summary>Display as bitmap image</summary>
    Bitmap = 400,

    /// <summary>Display as JPEG image</summary>
    Jpeg = 401,

    /// <summary>Display as Windows icon</summary>
    Icon = 402,

    /// <summary>Display as Windows Metafile image</summary>
    Metafile = 403
}
