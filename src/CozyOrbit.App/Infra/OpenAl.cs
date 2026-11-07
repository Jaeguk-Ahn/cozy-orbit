using System.Reflection;
using System.Runtime.InteropServices;

namespace CozyOrbit.App.Infra;

/// <summary>
/// OpenAL Soft 의 필요한 함수만 P/Invoke 로. 네이티브 라이브러리(Windows soft_oal.dll, macOS libopenal.dylib)는
/// Silk.NET.OpenAL.Soft.Native 패키지가 넣어 준다. 단일 파일 배포(자체 압축 해제)에서도 런타임의 기본 탐색이 찾는다.
/// LGPL: 사용자가 바꾼 라이브러리를 쓸 수 있게 실행 파일 옆의 같은 이름 파일을 먼저 찾는다.
/// </summary>
internal static class OpenAl
{
    private const string Lib = "openal";

    public const int FormatMono16 = 0x1101;
    public const int Buffer = 0x1009;
    public const int Gain = 0x100A;
    public const int Looping = 0x1007;
    public const int SourceState = 0x1010;
    public const int Playing = 0x1012;

    private static bool _resolverSet;

    /// <summary>"openal" 을 플랫폼 파일 이름으로 이어 준다 (한 번만)</summary>
    public static void EnsureResolver()
    {
        if (_resolverSet) return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(OpenAl).Assembly, (name, asm, path) =>
        {
            if (name != Lib) return 0;
            string file = OperatingSystem.IsWindows() ? "soft_oal" : "libopenal";
            var beside = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "soft_oal.dll" : "libopenal.dylib");
            if (File.Exists(beside) && NativeLibrary.TryLoad(beside, out var replaced)) return replaced;
            return NativeLibrary.TryLoad(file, asm, path, out var h) ? h : 0;
        });
    }

    [DllImport(Lib)] public static extern nint alcOpenDevice(string? name);
    [DllImport(Lib)] public static extern nint alcCreateContext(nint device, nint attributes);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool alcMakeContextCurrent(nint context);
    [DllImport(Lib)] public static extern void alcDestroyContext(nint context);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool alcCloseDevice(nint device);
    [DllImport(Lib)] public static extern void alGenBuffers(int n, out uint buffer);
    [DllImport(Lib)] public static extern void alDeleteBuffers(int n, ref uint buffer);
    [DllImport(Lib)] public static extern void alGenSources(int n, out uint source);
    [DllImport(Lib)] public static extern void alDeleteSources(int n, ref uint source);
    [DllImport(Lib)] public static extern void alBufferData(uint buffer, int format, short[] data, int size, int frequency);
    [DllImport(Lib)] public static extern void alSourcei(uint source, int param, int value);
    [DllImport(Lib)] public static extern void alSourcef(uint source, int param, float value);
    [DllImport(Lib)] public static extern void alSourcePlay(uint source);
    [DllImport(Lib)] public static extern void alSourceStop(uint source);
    [DllImport(Lib)] public static extern void alGetSourcei(uint source, int param, out int value);
    [DllImport(Lib)] public static extern int alGetError();
}
