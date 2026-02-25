namespace HyperIMSwitch.Core.Models;

public sealed class SwitchDiagnosticsOptions
{
    public bool EnableRetryChain            { get; set; } = true;
    public bool RetryEnableProfile          { get; set; } = true;
    public bool RetryChangeCurrentLanguage  { get; set; } = true;
    public bool RetrySetDefaultProfile      { get; set; } = true;
    public bool RetryForegroundLangRequest  { get; set; } = true;
    public bool LogForegroundWindowContext  { get; set; } = true;
    public bool LogCurrentLanguageState     { get; set; } = true;
    public bool LogStepElapsedMs            { get; set; } = true;
}
