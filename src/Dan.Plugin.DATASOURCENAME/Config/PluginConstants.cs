namespace Dan.Plugin.DATASOURCENAME.Config;

public static class PluginConstants
{
    // These are not mandatory, but there should be a distinct error code (any integer) for all types of errors that can occur. The error codes does not have to be globally
    // unique. These should be used within either transient or permanent exceptions, see Plugin.cs for examples.
    public const int ErrorUpstreamUnavailble = 1001;
    public const int ErrorInvalidInput = 1002;
    public const int ErrorNotFound = 1003;
    public const int ErrorUnableToParseResponse = 1004;

    // The datasets must supply a human-readable source description from which they originate. Individual fields might come from different sources, and this string should reflect that (ie. name all possible sources).
    public const string SourceName = "Digitaliseringsdirektoratet";

    // The function names (ie. HTTP endpoint names) and the dataset names must match. Using constants to avoid errors.
    public const string SimpleDatasetName = "SimpleDataset";
    public const string RichDatasetName = "RichDataset";
}
