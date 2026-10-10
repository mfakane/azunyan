namespace Azunote;

public static class ApplicationIdentity
{
#if AZUNOTE_DEV
    public const string DisplayName = "Azunote (Dev)";
    public const string InstanceName = "Azunote.Dev";
#else
    public const string DisplayName = "Azunote";
    public const string InstanceName = "Azunote";
#endif
}
