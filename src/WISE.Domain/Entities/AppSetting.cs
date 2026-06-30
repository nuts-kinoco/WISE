namespace WISE.Domain.Entities;

public class AppSetting
{
    public string Key { get; private set; }
    public string Value { get; private set; }

    private AppSetting() { Key = ""; Value = ""; }

    public AppSetting(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public void SetValue(string value) => Value = value;
}
