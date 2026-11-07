using Avalonia.Markup.Xaml;
using CozyOrbit.Core.Infra;

namespace CozyOrbit.App.Infra;

/// <summary>
/// XAML 용 번역: <c>Text="{l:T Ko='연구', En='Research'}"</c>.
/// 창을 만들 때 한 번 정해진다 (언어를 바꾸면 창을 다시 만든다). 코드에서는 <see cref="L.T"/> 를 쓴다.
/// </summary>
public sealed class T : MarkupExtension
{
    public string Ko { get; set; } = "";
    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Ko, En);
}
