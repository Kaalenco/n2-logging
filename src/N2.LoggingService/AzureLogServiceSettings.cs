namespace N2.LoggingService;

public class AzureLogServiceSettings
{
    public string AppName { get; set; } = "AzureLogService";
    public string ConnectionString { get; set; } = string.Empty;
    public string LogCategory { get; set; } = string.Empty;
    public string TableName { get; set; } = "N2logging";
}
