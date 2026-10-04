using System;

namespace Tideward.Helpers.Enumeration;

[AttributeUsage(AttributeTargets.All)]
public class LocalizationKeyAttribute : Attribute
{
    public string Key { get; set; }

    public LocalizationKeyAttribute(string key)
    {
        Key = key;
    }
}