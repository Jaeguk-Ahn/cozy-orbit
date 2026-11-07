using System.Globalization;

namespace CozyOrbit.Core.Infra;

/// <summary>
/// UI 언어 (한국어·영어). 번역을 문자열 바로 옆에 둔다 — 키 표를 따로 두면 한쪽만 고치는 일이 생긴다.
/// 언어 값의 출처는 설정의 GameSettings.Language.
/// 언어를 더하려면 T 에 인자를 늘리고 Supported 에 추가한다.
/// </summary>
public static class L
{
    public static readonly IReadOnlyList<string> Supported = new[] { "ko", "en" };

    /// <summary>"ko" 또는 "en"</summary>
    public static string Language { get; private set; } = DefaultForSystem();

    public static bool IsKorean => Language == "ko";

    public static event Action? Changed;

    /// <summary>지원하지 않는 언어(원본 세이브의 ja 등)는 영어로</summary>
    public static string Normalize(string? code) => code == "ko" ? "ko" : "en";

    public static void SetLanguage(string? code)
    {
        var next = Normalize(code);
        if (next == Language) return;
        Language = next;
        Changed?.Invoke();
    }

    /// <summary>새 설치의 기본값: OS 표시 언어가 한국어면 ko, 아니면 en</summary>
    public static string DefaultForSystem() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";

    public static string T(string ko, string en) => IsKorean ? ko : en;

    /// <summary>날짜·요일 표시용 문화권</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsKorean ? "ko-KR" : "en-US");

    public static string DisplayName(string code) => code == "ko" ? "한국어" : "English";
}
