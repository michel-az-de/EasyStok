namespace EasyStock.Application.Tests.Services.Atendimento.Audio;

/// <summary>
/// Gravações reais do MediaRecorder do Chrome (audio/webm;codecs=opus), tom de 440/330 Hz gerado por
/// WebAudio. <see cref="MonoCurto"/>: start() sem fatia, tamanhos conhecidos. <see cref="MonoFatiado"/>:
/// start(100), Segment e Cluster de tamanho desconhecido. <see cref="Estereo"/>: dois canais.
/// Referência independente: <c>ffmpeg -c copy</c> extrai 6, 8 e 6 pacotes Opus de 60 ms, respectivamente.
/// </summary>
internal static class AmostrasWebmChrome
{
    public static byte[] MonoCurto => Convert.FromBase64String(MonoCurtoBase64);
    public static byte[] MonoFatiado => Convert.FromBase64String(MonoFatiadoBase64);
    public static byte[] Estereo => Convert.FromBase64String(EstereoBase64);

    private const string MonoCurtoBase64 =
        "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" +
        "U7trjbuLs4EAt4b3gQHxgdc=";

    private const string MonoFatiadoBase64 =
        "GkXfo59ChoEBQveBAULygQRC84EIQoKEd2VibUKHgQRChYECGFOAZwH/////////FUmpZpkq17GDD0JATYCGQ2hyb21lV0GGQ2hy" +
        "b21lFlSua7+uvdeBAXPFh+zgVL4Vmo2DgQKGhkFfT1BVU2Oik09wdXNIZWFkAQEAAIC7AAAAAADhjbWERzuAAJ+BAWJkgSAfQ7Z1" +
        "Af/////////ngQCj64EAAIB7gycVhCloNBYy4nbKbSaSLN+xB0Yer8hU81ztN6OSyWZNDOhEwui9NADbvKIhbWltyldrCaNRNUzm" +
        "6ao7413Ru+kAFC0+1PN5pTc3f+sAIkPv8I2sPbONXB+YoRjHshSQBLjQ4Udno/SBAD2Ae4MjI7Z9kF2UyU8N2CdJowNe7QbwkZ1Y" +
        "HmJLVOQAOUoKcFuGqC4Wtq1p4gvJTwya+VNMWFJ1tb3gpFiSPxf2R8RLeQdgkjSYsqy1lr0Pup2GEXBxqIX4onjXp9ydjnvBDNVD" +
        "mz/XirvvQ0OTQ+h+Jh9DtnUB/////////+eBd6P7gQAAgHsDtK9v0hHWgxh7kdesH4Vtg1pCAMhULsOgpQk1l1AZQxBdt7NcZhbG" +
        "rgRrLjqSorhOhQHea+YionhdiPnbukkJaUJg1azNCInTuw0sO9Dmq0EWAPEynhQ67G3mOdD9zQmyVGq/HJMYNwnC2KziCRb5wTWn" +
        "TzSfo/yBAD6Ae4MnJqrSIygvFVK5gSDL3J+zKtWUCFre0UWln0f3NwDyoyXj3Kf1aD6fqLSzrfLdRdrFvERUDJlish+m2PWCgEX8" +
        "mnlUFnktsEqX5uqfDb1NtK9E4+S3jNxjAK8xC8vKCcuo59F9qH+OifSBEZciZlotyM9NXx8GH0O2dQH/////////54Hwo/WBAACA" +
        "e4MjJqyEEJMvFVICeR8ytBjEKG5ihGsIuaZWd8Z+LI2YjkMyevXHq0EWAPEiY2mI5PV4wCywB6zqGCzaLHbnJpefCyIZyDkIFeVS" +
        "SO2zWsQpyVGPuwu/Q26PxY+GITbLuEttwJ71K/M/2DMVSZZg8qij+YEAR4B7gyUntK9E4+S3kCWQhO/LUv70D/pAYixeSrBn0S99" +
        "Db6bz7MjXC0xzaorAuI110FsMRlMwvb7egKhUUWGoTNPA3mM51A+daRNbw2/udBXBq2Cl1cmL+CklJ7Fn3UjWHJnXSOpnsykOxPR" +
        "beYMU2ECTGcTbZYfQ7Z1Af/////////nggFxo/WBAACAe4MlJ69WOZFtKHOrrApVHfKxEUHCGfMbzROHSIaprwT4zc4hGsvPNJ6s" +
        "TKVGT/7qqSyzwxDW/r/zURKuuXGVHFqXDFlU/JRdeKZ3mcxysFypjlYA8Tv3CG6pep7nzcxU3bptk9QeJWqCULjnAo8NvU2j+4EA" +
        "PoD7A8pxmTeUkgu/o0QltOxaWlj54DmFzjy2W98uEs/K1GR1SyYQKFMXksXFEAYVyegPetAhNurxjtRoTVpDt0vBz25ntmVrlUBT" +
        "WBabFzDHksMZRQsSy8Ai/zUH03z2ogemLW0YowrZ3M57ABt4CB8f6CW9klSbkg==";

    private const string EstereoBase64 =
        "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" +
        "U7trjbuLs4EAt4b3gQHxgdc=";
}
