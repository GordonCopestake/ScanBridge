namespace CS3.ScanBridge;

internal static class AppIcon
{
    private const string IconData =
        "AAABAAEAEBAAAAEAIABoBAAAFgAAACgAAAAQAAAAIAAAAAEAIAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAABuShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF//19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/bkoX/25KF/9uShf/bkoX/25KF/9uShf/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1/25KF/9uShf/bkoX/25KF/9uShf/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/bkoX/25KF/9uShf/bkoX//X19f/19fX/9fX1/25KF/9uShf/bkoX/25KF/9uShf/bkoX//X19f/19fX/9fX1/25KF/9uShf/bkoX/25KF//19fX/9fX1//X19f9uShf/bkoX/25KF/9uShf/bkoX/25KF//19fX/9fX1//X19f9uShf/bkoX/25KF/9uShf/9fX1//X19f/19fX/bkoX/25KF/9uShf/bkoX/25KF/9uShf/9fX1//X19f/19fX/bkoX/25KF/9uShf/bkoX//X19f/19fX/9fX1/25KF/9uShf/bkoX/25KF/9uShf/bkoX//X19f/19fX/9fX1/25KF/9uShf/bkoX/25KF//19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f9uShf/bkoX/25KF/9uShf/bkoX//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f9uShf/bkoX/25KF/9uShf/bkoX/25KF//19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/9fX1//X19f/19fX/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/bkoX/25KF/9uShf/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

    private static readonly MemoryStream Stream = new(Convert.FromBase64String(IconData), false);
    public static Icon Value { get; } = new(Stream);
}
