using System.Windows.Markup;
using Recorder.Core;

namespace Recorder.UI;

/// <summary>XAML から文言を引く。例: <c>Text="{ui:T settings.sleep}"</c>。キーは Lang/ja.ini（Core/Loc）。</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
